// The ethereal influence ring — built-in render pipeline, unlit, additive.
//
// Samples the per-floor field texture written by InfluenceRingRenderer:
//   R = signed distance to the claimed boundary (0.5 = the boundary itself,
//       encoded so one cell = 1 / (2 * sdfRangeCells))
//   G = normalized free-growth cost from InfluenceField (1 = unreachable)
//   B = exposed fringe on CLAIMED ground, discovered dwarven holdings on
//       UNCLAIMED ground -- mutually exclusive, each masked by its own side,
//       and the granite fill cuts its hard edge from the unclaimed half
//   A = proximity to dwarven ground (255 on it, easing to 0 at the flare's
//       reach); the ring colour lerps toward _RingColorAlt by it, so the
//       boundary warms as it closes on a hold and is fully bronze where it
//       touches. The fill cannot use it: A is a smooth ramp, and a hard edge
//       cut from it would land out in open ground
//
// Bilinear filtering interpolates the per-cell values into organic curves.
// Two octaves of scrolling value noise perturb the isoline (the waver — purely
// cosmetic; gameplay boundaries never move), the band falls off asymmetrically
// (short into claimed ground, a long soft tail bleeding into the fog), and the
// whole ring breathes with a gentle pulse. The free-growth overlay fills
// unclaimed ground whose cost sits within _EffReach — driven per frame from
// C#, so surges, suppression, and recovery animate with zero texture work.
//
// All distance uniforms arrive pre-converted to encoded units by
// InfluenceRingRenderer; this shader stays unit-dumb.
Shader "DCR/InfluenceRing"
{
    Properties
    {
        _FieldTex ("Field (R=sdf, G=reach)", 2D) = "black" {}
        _RingColor ("Ring Color", Color) = (0.784, 0.565, 0.165, 1)
        _RingColorAlt ("Ring Color Alt (A-weighted)", Color) = (0.75, 0.62, 0.42, 1)
        _Intensity ("Intensity", Float) = 1.15
        _InnerFalloff ("Inner Falloff (encoded)", Float) = 0.044
        _OuterFalloff ("Outer Falloff (encoded)", Float) = 0.2
        _WaverAmp ("Waver Amplitude (encoded)", Float) = 0.022
        _Noise1Scale ("Noise 1 Scale (uv)", Float) = 22
        _Noise2Scale ("Noise 2 Scale (uv)", Float) = 62
        _Noise1Speed ("Noise 1 Speed", Float) = 0.05
        _Noise2Speed ("Noise 2 Speed", Float) = 0.11
        _PulseSpeed ("Pulse Speed", Float) = 1.4
        _PulseAmp ("Pulse Amplitude", Range(0, 1)) = 0.12
        _EffReach ("Effective Reach (normalized)", Range(0, 1)) = 0.2
        _ReachEdge ("Reach Edge Softness", Float) = 0.02
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0
        _OverlayClaimedLevel ("Overlay Inside Level", Range(0, 1)) = 0.45
        _OverlayExposedLevel ("Overlay Exposed Level", Range(0, 1)) = 0.22
        _OverlayDwarvenLevel ("Overlay Dwarven Fill", Range(0, 1)) = 0.55
        _OverlayDwarvenEdge ("Overlay Dwarven Edge", Range(0, 2)) = 0.9
        _HoldingsColor ("Holdings Color", Color) = (0.55, 0.58, 0.64, 1)
        _OverlayHoldPoolLevel ("Overlay Holdings Pool Fill", Range(0, 1)) = 0.62
        _HoldPoolReach ("Holdings Pool Reach (encoded)", Float) = 0.375
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _FieldTex;
            fixed4 _RingColor;
            fixed4 _RingColorAlt;
            float _Intensity;
            float _InnerFalloff;
            float _OuterFalloff;
            float _WaverAmp;
            float _Noise1Scale;
            float _Noise2Scale;
            float _Noise1Speed;
            float _Noise2Speed;
            float _PulseSpeed;
            float _PulseAmp;
            float _EffReach;
            float _ReachEdge;
            float _OverlayStrength;
            float _OverlayClaimedLevel;
            float _OverlayExposedLevel;
            float _OverlayDwarvenLevel;
            float _OverlayDwarvenEdge;
            fixed4 _HoldingsColor;
            float _OverlayHoldPoolLevel;
            float _HoldPoolReach;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Bilinear value noise, 0..1.
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 f4 = tex2D(_FieldTex, i.uv);
                float3 fs = f4.rgb;
                float sdf = fs.r;

                // Two scrolling octaves waver the isoline. Cosmetic only.
                float t = _Time.y;
                float n1 = vnoise(i.uv * _Noise1Scale + float2(t * _Noise1Speed, t * _Noise1Speed * 0.7));
                float n2 = vnoise(i.uv * _Noise2Scale - float2(t * _Noise2Speed * 0.6, t * _Noise2Speed));
                float waver = ((n1 + 0.5 * n2) / 1.5 - 0.5) * 2.0 * _WaverAmp;

                float d = sdf - (0.5 + waver);

                // Asymmetric band: sharp toward claimed ground, long tail into the fog.
                float inside = saturate(1.0 - d / _InnerFalloff);
                float outside = saturate(1.0 + d / _OuterFalloff);
                float band = (d >= 0.0) ? inside : outside;
                band *= band; // soften the shoulders

                float pulse = 1.0 + _PulseAmp * sin(t * _PulseSpeed);
                // Frontier hue: A carries PROXIMITY to dwarven ground, so the
                // band warms toward _RingColorAlt as the boundary closes on a
                // hold and is fully bronze where it touches. It reads A rather
                // than B because this band straddles the boundary, and B means
                // something different on each side of it. The overlay below
                // stays the core hue on purpose -- the wash answers reach, not
                // ownership.
                float3 ringRGB = lerp(_RingColor.rgb, _RingColorAlt.rgb, f4.a);
                float3 ring = ringRGB * band * _Intensity * pulse * _RingColor.a;

                // Free-growth overlay, four tiers:
                //   unclaimed within reach -> brightest (where creep will fill)
                //   unclaimed dwarven      -> flat granite-grey fill + hard edge (theirs)
                //   claimed & safe         -> mid wash (yours, breach-durable)
                //   claimed & exposed      -> dim wash (yours, but a breach reclaims it)
                // Unclaimed beyond reach stays dark. Claimed ground is washed
                // regardless of reach, so pushed territory is no longer a black void.
                float inReach = smoothstep(_EffReach + _ReachEdge, _EffReach - _ReachEdge, fs.g);
                float unclaimedSide = smoothstep(0.5, 0.46, sdf);
                float claimedSide = 1.0 - unclaimedSide;
                float claimedWash = lerp(_OverlayClaimedLevel, _OverlayExposedLevel, fs.b);
                // Dwarven holdings (canon: Granite holdings overlay): a
                // surveyed claim, not a living creep. The fill is
                // hard-thresholded from the bilinear A ramp and the edge band
                // is cut from the same ramp, so the border reads as a crisp
                // drawn line. No waver, no pulse, no reach gate, no fog gate,
                // all on purpose: yours breathes, theirs doesn't, and the
                // core senses rival claim before an accidental 9x push. COOL
                // GREY per canon's colour caution -- gold ring, gold HUD and
                // amber Earth cores leave no room for a bronze AREA; bronze
                // stays a thin accent on the frontier flare above. Claimed
                // cells write A = 0, so conquered holdings dissolve into the
                // ordinary wash.
                // The granite fill reads B, not A. It needs a HARD edge on a
                // BINARY value; A is a smooth proximity ramp and cutting an
                // isoline from it would put the granite's boundary somewhere out
                // in open ground rather than on the hold. unclaimedSide already
                // masks away B's other meaning.
                float hold = smoothstep(0.45, 0.55, fs.b) * unclaimedSide;
                float holdEdge = smoothstep(0.15, 0.5, fs.b) * smoothstep(0.85, 0.5, fs.b) * unclaimedSide;

                // PROXIMITY POOL. The same wash the exposed fringe uses, on the
                // other side of the boundary and driven by A, so the warning is
                // an AREA rather than a tint on a hairline.
                //
                // Clipped to the player's OWN frontier. Unclipped, A is a
                // sixteen-cell dilation of the holding's footprint, so the pool
                // would trace the outline of a hold the player has never found --
                // the exact leak the split signal was chosen to avoid. Clipped,
                // its shape is the frontier's and it says only "near".
                //
                // Suppressed inside a confirmed holding so the two do not stack:
                // the pool is the guess, the fill is the answer, and once you have
                // the answer the guess should be gone.
                float poolClip = smoothstep(0.5 - _HoldPoolReach, 0.5, sdf) * unclaimedSide;
                float pool = saturate(f4.a) * poolClip * (1.0 - hold);

                float unclaimedWash = lerp(inReach, _OverlayDwarvenLevel, hold);
                unclaimedWash = lerp(unclaimedWash, _OverlayHoldPoolLevel, pool);
                float wash = lerp(unclaimedWash, claimedWash, claimedSide);

                // Granite for both. Canon's colour caution rules out a bronze
                // AREA -- gold ring, gold HUD and amber Earth cores leave no room
                // for one -- so the two signals separate by FORM instead: the pool
                // is soft and edgeless, the holding carries the hard surveyed edge.
                float3 washCol = lerp(_RingColor.rgb, _HoldingsColor.rgb, max(hold, pool));
                float3 overlay = washCol * (wash * _OverlayStrength)
                               + _HoldingsColor.rgb * (holdEdge * _OverlayDwarvenEdge * _OverlayStrength);

                return fixed4(ring + overlay, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}