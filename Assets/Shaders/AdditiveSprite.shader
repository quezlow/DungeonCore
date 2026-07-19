Shader "DCR/AdditiveSprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        // Screen blend: contribution = src * (1 - dst). Full strength over
        // near-black (the void fill), vanishing over lit art, bounded at 1 so
        // it can never clip to white. Light in DCR is darkness removed, and
        // this is the blend that behaves that way.
        Blend OneMinusDstColor One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // The SpriteRenderer's colour rides the vertex colour: rgb is the
            // light hue, alpha is the intensity. Premultiply so the screen
            // blend receives exactly rgb * a as src.
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                return fixed4(c.rgb * c.a, 0);
            }
            ENDCG
        }
    }
}