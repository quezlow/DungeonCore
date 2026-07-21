using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Directs the core selection ceremony: the slow reveal after death, the
/// wisp's arrival, the four teaching beats that assemble the facsimile HUD
/// (move, breathe, reach, pulse), the read-back of the life the player lived,
/// the affinity choice, and the recolour-and-commit that hands the slot to
/// the dungeon proper.
///
/// Soft cage by design: pan and zoom are live from the first frame - the
/// prompts choreograph discovery, they never disable input. Suggestion, not
/// gate: all six affinities stay selectable; deeds add emphasis, a read-back
/// line, and the dimming of roads not taken.
///
/// The commit is durable the moment the dungeon loads: the pending type is
/// written here, and DungeonSaveController's InitializeNewGame saves and
/// consumes the prologue checkpoint on arrival.
/// </summary>
public class CeremonyController : MonoBehaviour
{
    [System.Serializable]
    public class AffinityOption
    {
        public DungeonType type;
        public CanvasGroup group;
        public Image frame;
        public TMP_Text nameLabel;
        public TMP_Text identityLabel;
        public GameObject leaderMark;
        public Button button;
    }

    [Header("Scene")]
    [Tooltip("Full-screen dark sprite over the world. Starts near-opaque; the ceremony lifts it in stages.")]
    [SerializeField] private SpriteRenderer gloom;
    [SerializeField] private Transform camRig;
    [SerializeField] private Camera cam;
    [SerializeField] private float panSpeed = 8f;
    [SerializeField] private float zoomMin = 3f;
    [SerializeField] private float zoomMax = 7f;
    [Tooltip("How far the rig may drift from the core, in world units.")]
    [SerializeField] private Vector2 panBounds = new Vector2(10f, 7f);

    [Header("Wisp")]
    [SerializeField] private Transform wisp;
    [Tooltip("Where the wisp settles beside the core.")]
    [SerializeField] private Transform wispAnchor;
    [SerializeField] private CanvasGroup wispPanel;
    [SerializeField] private TMP_Text wispText;
    [SerializeField] private float lineHold = 2.8f;

    [Header("Facsimile HUD (fades in per beat: move, breathe, reach, pulse)")]
    [SerializeField] private CanvasGroup[] hudPieces = new CanvasGroup[4];
    [SerializeField] private Image manaOrbFill;

    [Header("Choice")]
    [SerializeField] private CanvasGroup choicePanel;
    [SerializeField] private TMP_Text readBackText;
    [SerializeField] private AffinityOption[] options = new AffinityOption[6];
    [SerializeField] private Button confirmButton;

    [Header("Commit")]
    [Tooltip("World sprites tinted white-to-affinity on commit (core glow, particles).")]
    [SerializeField] private SpriteRenderer[] tintSprites;
    [Tooltip("UI images tinted white-to-affinity on commit (the orb fill, frame accents).")]
    [SerializeField] private Image[] tintImages;
    [SerializeField] private float recolorSeconds = 2f;

    [Header("Data")]
    [SerializeField] private AffinityMapping mapping;
    [SerializeField] private Key senseKey = Key.Space;
    [SerializeField] private float manaHoldSeconds = 1.2f;
    [Tooltip("Core tint image that swells as the ambient-mana hold fills.")]
    [SerializeField] private Image coreTintImage;
    [Tooltip("Scale of the core tint image at a full hold (1 = no growth).")]
    [SerializeField] private float coreTintHoldScale = 1.3f;

    private Vector3 wispRestPosition;
    private bool wispBobbing;
    private bool inputLive;
    private float panAccumulated;
    private float zoomAccumulated;
    private DungeonType selectedType = DungeonType.None;
    private bool committed;

    private void Start()
    {
        if (gloom != null) SetGloomAlpha(0.92f);
        if (wispPanel != null) wispPanel.alpha = 0f;
        if (manaOrbFill != null)
        {
            manaOrbFill.fillAmount = 0f;
            manaOrbFill.color = Color.white;
        }
        foreach (CanvasGroup piece in hudPieces)
            if (piece != null) piece.alpha = 0f;
        if (choicePanel != null)
        {
            choicePanel.alpha = 0f;
            choicePanel.gameObject.SetActive(false);
        }
        if (confirmButton != null)
        {
            confirmButton.interactable = false;
            confirmButton.onClick.AddListener(OnConfirm);
        }
        foreach (AffinityOption option in options)
        {
            if (option == null || option.button == null) continue;
            AffinityOption captured = option;
            option.button.onClick.AddListener(() => OnOptionSelected(captured));
        }

        inputLive = true; // soft cage: nothing is ever actually locked
        StartCoroutine(RunCeremony());
    }

    private void Update()
    {
        if (inputLive && !committed)
        {
            ApplyPan();
            ApplyZoom();
        }

        if (wispBobbing && wisp != null)
            wisp.localPosition = wispRestPosition + Vector3.up * (Mathf.Sin(Time.time * 2.2f) * 0.12f);
    }

    // ------------------------------------------------------------------ input

    private void ApplyPan()
    {
        if (camRig == null) return;

        Vector2 dir = Vector2.zero;
        if (Keybinds.IsHeld(GameAction.PanUp)) dir.y += 1f;
        if (Keybinds.IsHeld(GameAction.PanDown)) dir.y -= 1f;
        if (Keybinds.IsHeld(GameAction.PanLeft)) dir.x -= 1f;
        if (Keybinds.IsHeld(GameAction.PanRight)) dir.x += 1f;
        if (dir == Vector2.zero) return;

        Vector3 delta = (Vector3)(dir.normalized * panSpeed * Time.deltaTime);
        Vector3 next = camRig.position + delta;
        next.x = Mathf.Clamp(next.x, -panBounds.x, panBounds.x);
        next.y = Mathf.Clamp(next.y, -panBounds.y, panBounds.y);

        panAccumulated += (next - camRig.position).magnitude;
        camRig.position = next;
    }

    private void ApplyZoom()
    {
        if (cam == null || Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        // Sign-based step: hardware-independent (scroll magnitudes vary wildly).
        float step = -Mathf.Sign(scroll) * 0.5f;
        float before = cam.orthographicSize;
        cam.orthographicSize = Mathf.Clamp(before + step, zoomMin, zoomMax);
        zoomAccumulated += Mathf.Abs(cam.orthographicSize - before);
    }

    private static bool AnyKeyPressed()
    {
        bool key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        return key || click;
    }

    private bool SensePressed() =>
        Keyboard.current != null && Keyboard.current[senseKey].wasPressedThisFrame;

    private bool SenseHeld() =>
        Keyboard.current != null && Keyboard.current[senseKey].isPressed;

    private string SenseKeyName() => senseKey.ToString().ToUpperInvariant();

    private static string PanKeyNames()
    {
        return $"{Keybinds.KeyFor(GameAction.PanUp)} {Keybinds.KeyFor(GameAction.PanLeft)} " +
               $"{Keybinds.KeyFor(GameAction.PanDown)} {Keybinds.KeyFor(GameAction.PanRight)}";
    }

    // ------------------------------------------------------------------ flow

    private IEnumerator RunCeremony()
    {
        // The dark thins. The world was always there.
        yield return FadeGloom(0.92f, 0.55f, 3f);

        yield return WispArrive();

        yield return Say("There you are. There you are! Forgive me - the dark between deaths is wide, and I am small.");
        yield return Say("You died. I am sorry. That part is over now, and it does not repeat.");
        yield return Say("You are a core now. Or - almost. Let me show you your hands.");

        // Beat one: move.
        yield return Prompt($"Push against the world - the old keys, the walking ones. ({PanKeyNames()})");
        yield return new WaitUntil(() => panAccumulated >= 2f);
        yield return RevealHudPiece(0);
        yield return Say("Good. The world gives.");

        // Beat two: breathe (zoom).
        yield return Prompt("Now breathe. In, and out. (mouse wheel)");
        yield return new WaitUntil(() => zoomAccumulated >= 1f);
        yield return RevealHudPiece(1);
        yield return Say("The world is larger than your view of it. Remember that.");

        // Beat three: reach (sense).
        yield return Prompt($"Reach. Press {SenseKeyName()} and listen with your edges.");
        yield return new WaitUntil(SensePressed);
        yield return FadeGloom(GetGloomAlpha(), 0.25f, 0.8f);
        yield return RevealHudPiece(2);
        yield return Say("There. Stone, and hollow, and quiet. All of it yours to know.");

        // Beat four: pulse (feel the ambient mana).
        yield return Prompt($"Last: the breath beneath the breath. Hold {SenseKeyName()} and feel the ambient mana pool.");
        yield return HoldForMana();
        yield return RevealHudPiece(3);
        yield return Say("There. Eyes, breath, reach, and pulse. Almost a core.");

        // The choice.
        yield return Say("Almost. A core is not a what - it is a which.");
        yield return Say("The dead bring their lives down with them, and the life chooses.");
        yield return OpenChoice();
    }

    private IEnumerator WispArrive()
    {
        if (wisp == null || wispAnchor == null)
        {
            wispBobbing = wisp != null;
            if (wisp != null) wispRestPosition = wisp.localPosition;
            yield break;
        }

        Vector3 from = wispAnchor.position + new Vector3(9f, 3f, 0f);
        wisp.position = from;
        wisp.gameObject.SetActive(true);

        float t = 0f;
        const float duration = 1.4f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float eased = Mathf.SmoothStep(0f, 1f, t / duration);
            wisp.position = Vector3.Lerp(from, wispAnchor.position, eased);
            yield return null;
        }
        wisp.position = wispAnchor.position;
        wispRestPosition = wisp.localPosition;
        wispBobbing = true;
    }

    private IEnumerator HoldForMana()
    {
        float held = 0f;
        while (held < manaHoldSeconds)
        {
            held = SenseHeld() ? held + Time.deltaTime : 0f;
            float fill01 = Mathf.Clamp01(held / manaHoldSeconds);
            if (manaOrbFill != null)
                manaOrbFill.fillAmount = fill01;
            if (coreTintImage != null)
                coreTintImage.transform.localScale =
                    Vector3.one * Mathf.Lerp(1f, coreTintHoldScale, fill01);
            yield return null;
        }
        if (manaOrbFill != null) manaOrbFill.fillAmount = 1f;
        if (coreTintImage != null)
            coreTintImage.transform.localScale = Vector3.one * coreTintHoldScale;
    }

    // ------------------------------------------------------------------ choice

    private IEnumerator OpenChoice()
    {
        AffinityMapping.Tally tally = mapping != null
            ? mapping.Evaluate(Persistence.AllFlags)
            : null;

        BuildReadBack(tally);
        ConfigureOptions(tally);

        if (choicePanel != null)
        {
            choicePanel.gameObject.SetActive(true);
            yield return FadeCanvas(choicePanel, 0f, 1f, 0.6f);
            choicePanel.interactable = true;
            choicePanel.blocksRaycasts = true;
        }
    }

    private void BuildReadBack(AffinityMapping.Tally tally)
    {
        if (readBackText == null) return;
        if (tally == null || mapping == null)
        {
            readBackText.text = "";
            return;
        }

        var parts = new List<string>();

        if (tally.emptyHanded)
        {
            parts.Add(mapping.emptyHandedLine);
        }
        else
        {
            int named = 0;
            foreach (DungeonType leader in tally.leaders)
            {
                AffinityMapping.Row row = mapping.RowFor(leader);
                if (row != null && !string.IsNullOrEmpty(row.readBack))
                {
                    parts.Add(row.readBack);
                    named++;
                }
                if (named >= 2) break;
            }
            if (tally.leaders.Count > 2)
                parts.Add("And more than one road besides.");
        }

        if (tally.prayed) parts.Add(mapping.prayShrineLine);
        if (tally.fossil) parts.Add(mapping.eggFossilLine);
        if (tally.mill) parts.Add(mapping.eggMillLine);

        readBackText.text = string.Join("\n", parts);
    }

    private void ConfigureOptions(AffinityMapping.Tally tally)
    {
        foreach (AffinityOption option in options)
        {
            if (option == null) continue;

            AffinityMapping.Row row = mapping != null ? mapping.RowFor(option.type) : null;

            if (option.nameLabel != null)
                option.nameLabel.text = option.type.ToString();
            if (option.identityLabel != null)
                option.identityLabel.text = row != null ? row.identity : "";
            if (option.frame != null)
                option.frame.color = DungeonCore.ColorFor(option.type);

            float score = 0f;
            bool isLeader = false;
            if (tally != null)
            {
                tally.scores.TryGetValue(option.type, out score);
                isLeader = tally.leaders.Contains(option.type);
            }

            if (option.leaderMark != null)
                option.leaderMark.SetActive(isLeader);

            // Dim the roads not taken - still open, just unlit.
            if (option.group != null)
                option.group.alpha = (tally == null || tally.emptyHanded || score > 0f) ? 1f : 0.55f;
        }
    }

    private void OnOptionSelected(AffinityOption option)
    {
        if (committed || option == null) return;

        selectedType = option.type;

        foreach (AffinityOption other in options)
            if (other != null && other.group != null)
                other.group.transform.localScale =
                    Vector3.one * (other == option ? 1.06f : 1f);

        if (confirmButton != null) confirmButton.interactable = true;

        AffinityMapping.Row row = mapping != null ? mapping.RowFor(option.type) : null;
        if (row != null && !string.IsNullOrEmpty(row.identity))
            StartCoroutine(Say(row.identity));
    }

    private void OnConfirm()
    {
        if (committed || selectedType == DungeonType.None) return;
        committed = true;
        StartCoroutine(Commit(selectedType));
    }

    // ------------------------------------------------------------------ commit

    private IEnumerator Commit(DungeonType chosen)
    {
        if (choicePanel != null)
        {
            choicePanel.interactable = false;
            choicePanel.blocksRaycasts = false;
            yield return FadeCanvas(choicePanel, choicePanel.alpha, 0f, 0.5f);
            choicePanel.gameObject.SetActive(false);
        }

        yield return Recolor(chosen);

        yield return Say("Then it is chosen, and it was always going to be you.");
        yield return Say("Down we go - there is so much to build.");

        // Let the last line breathe before the dark takes the scene.
        yield return new WaitForSeconds(2.0f);

        var manager = SaveSlotManager.Instance;
        if (manager != null && manager.PendingNewGame != null)
        {
            manager.PendingNewGame.dungeonType = chosen;
        }
        else if (manager != null)
        {
            // The prologue path arrives with no pending (LaunchSlot clears it).
            // Build one so the chosen type ALWAYS reaches the core -- this was
            // the dark-became-fire bug -- and the slot gets a stable name.
            manager.SetPendingNewGame($"Dungeon {manager.ActiveSlotId}", chosen);
        }
        else
        {
            Debug.LogWarning("[Ceremony] No SaveSlotManager - chosen type not written " +
                             "(direct scene play?). Loading the dungeon anyway.");
        }

        SceneLoader.FadeToScene("Dungeon_Level_0");
    }

    private IEnumerator Recolor(DungeonType chosen)
    {
        Color target = DungeonCore.ColorFor(chosen);
        float t = 0f;
        while (t < recolorSeconds)
        {
            t += Time.deltaTime;
            Color current = Color.Lerp(Color.white, target, t / recolorSeconds);

            if (manaOrbFill != null) manaOrbFill.color = current;
            if (tintSprites != null)
                foreach (SpriteRenderer sprite in tintSprites)
                    if (sprite != null) sprite.color = current;
            if (tintImages != null)
                foreach (Image image in tintImages)
                    if (image != null) image.color = current;

            yield return null;
        }
    }

    // ------------------------------------------------------------------ speech

    /// <summary>A line that holds, then clears. Any key skips the hold.</summary>
    private IEnumerator Say(string line)
    {
        yield return ShowLine(line);
        yield return WaitForPress();
    }

    /// <summary>A line that stays on screen while the player acts on it.</summary>
    private IEnumerator Prompt(string line)
    {
        yield return ShowLine(line);
    }

    private IEnumerator ShowLine(string line)
    {
        if (wispText != null) wispText.text = line;
        if (wispPanel != null && wispPanel.alpha < 1f)
            yield return FadeCanvas(wispPanel, wispPanel.alpha, 1f, 0.25f);
    }

    // The sprite's lines advance only on a press -- never on a timer -- so the
    // player reads at their own pace.
    private IEnumerator WaitForPress()
    {
        yield return null; // swallow the press that carried us into this line
        while (!AnyKeyPressed()) yield return null;
    }

    private IEnumerator HoldOrSkip(float seconds)
    {
        float t = 0f;
        yield return null; // swallow the press that may have advanced us here
        while (t < seconds)
        {
            if (AnyKeyPressed()) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ fades

    private IEnumerator RevealHudPiece(int index)
    {
        if (index < 0 || index >= hudPieces.Length || hudPieces[index] == null) yield break;
        yield return FadeCanvas(hudPieces[index], 0f, 1f, 0.6f);
    }

    private static IEnumerator FadeCanvas(CanvasGroup group, float from, float to, float seconds)
    {
        if (group == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        group.alpha = to;
    }

    private IEnumerator FadeGloom(float from, float to, float seconds)
    {
        if (gloom == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            SetGloomAlpha(Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        SetGloomAlpha(to);
    }

    private void SetGloomAlpha(float alpha)
    {
        if (gloom == null) return;
        Color color = gloom.color;
        color.a = alpha;
        gloom.color = color;
    }

    private float GetGloomAlpha() => gloom != null ? gloom.color.a : 0f;
}