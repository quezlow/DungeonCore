using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pre-discovery HUD pointer to the seeded entrance. Shows a quantized
/// 8-direction bearing in wisp voice with an arrow, computed from the core
/// to the entrance mouth. Hides itself once the cave is discovered, when no
/// seeded cave exists (legacy saves), or while the entrance is absent.
///
/// SCENE SETUP (add to UICanvas_Dungeon, e.g. near the WavePreviewHUD):
///   EntranceCompass (this script)
///   ├── CompassArrow  (Image — arrow sprite authored pointing UP)
///   └── CompassLabel  (TMP_Text)
/// </summary>
public class EntranceCompass : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image arrow;

    private static readonly string[] DirectionNames =
    {
        "east", "north-east", "north", "north-west",
        "west", "south-west", "south", "south-east"
    };

    private void Update()
    {
        var floor0 = FloorManager.Instance?.GetFloor(0);
        var features = floor0 != null ? floor0.FeatureGenerator : null;
        var entrance = DungeonEntrance.Instance;
        var core = DungeonCore.Instance;

        bool visible =
            TutorialDirector.DigPromptGiven &&      // stays hidden until the wisp says to dig
            features != null &&
            features.EntranceCave != null &&
            !features.IsEntranceDiscovered &&
            entrance != null &&
            core != null;

        if (label != null) label.enabled = visible;
        if (arrow != null) arrow.enabled = visible;
        if (!visible) return;

        Vector2 delta = entrance.SpawnPosition - core.transform.position;
        float degrees = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        // Quantize to the nearest of 8 compass points.
        int sector = Mathf.RoundToInt(degrees / 45f);
        sector = ((sector % 8) + 8) % 8;

        if (label != null)
            label.text = $"Air stirs from the {DirectionNames[sector]}. The way in lies there.";
        if (arrow != null)
            arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, sector * 45f - 90f);
    }
}