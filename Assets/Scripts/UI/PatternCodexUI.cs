using TMPro;
using UnityEngine;

/// <summary>
/// The Pattern Codex -- the expandable HUD panel formerly used for Materials.
/// Collapsed face: a chip counting discoveries ("Patterns 3 / 18"). Expanded
/// body: one row per catalog entry. Undiscovered rows render the icon near-
/// black with the atmospheric source hint; discovered rows show the name,
/// flavour and learned-from note.
///
/// PANEL SETUP (script on the panel root, left ENABLED in the Inspector --
/// the expand/collapse you built is untouched; this only manages content):
///   - catalog:       the PatternCatalog asset
///   - chipLabel:     TMP text on the collapsed face
///   - contentParent: the layout group the rows spawn under
///   - rowPrefab:     prefab carrying PatternCodexRow
/// </summary>
public class PatternCodexUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private PatternCatalog catalog;

    [Header("Wiring")]
    [SerializeField] private TMP_Text chipLabel;
    [SerializeField] private Transform contentParent;
    [SerializeField] private PatternCodexRow rowPrefab;

    [Header("Silhouette")]
    [SerializeField] private Color silhouetteTint = new Color(0.06f, 0.06f, 0.10f, 1f);

    private void OnEnable()
    {
        UnlockState.OnChanged += HandleUnlockChanged;
        Rebuild();
    }

    private void OnDisable()
    {
        UnlockState.OnChanged -= HandleUnlockChanged;
    }

    private void HandleUnlockChanged(string key)
    {
        // Any unlock (or a reset firing null) refreshes; cheap at 18 rows.
        Rebuild();
    }

    public void Rebuild()
    {
        if (catalog == null) return;

        if (chipLabel != null)
            chipLabel.text = $"Patterns {catalog.DiscoveredCount()} / {catalog.TotalCount}";

        if (contentParent == null || rowPrefab == null) return;

        for (int i = contentParent.childCount - 1; i >= 0; i--)
            Destroy(contentParent.GetChild(i).gameObject);

        foreach (var def in catalog.Patterns)
        {
            if (def == null) continue;
            var row = Instantiate(rowPrefab, contentParent);
            bool known = UnlockState.IsUnlocked(def.Key);
            row.Bind(def, known, silhouetteTint,
                known ? PatternDiscovery.LearnedFromNote(def.Key) : null);
        }
    }
}