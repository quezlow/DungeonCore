using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small panel opened by clicking a housed corpse in a Crypt. Shows the hero's
/// name, the price, and the contract: one life, no return. The Raise button greys
/// out while unaffordable; Esc closes it via the PauseMenuController ladder.
///
/// SCENE SETUP: a small panel under the HUD canvas (inactive by default) with two
/// TMP labels, a Raise button and a Close button; wire all five references plus
/// the panel root. Button clicks are bound in code.
/// </summary>
public class CryptRaiseUI : MonoBehaviour
{
    public static CryptRaiseUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text costLabel;
    [SerializeField] private TMP_Text mortalityLabel;
    [SerializeField] private Button raiseButton;
    [SerializeField] private Button closeButton;

    private FurniturePiece piece;
    private Corpse corpse;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        Instance = this;
        if (raiseButton != null) raiseButton.onClick.AddListener(OnRaiseClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Open(FurniturePiece sarcophagus, Corpse housedCorpse)
    {
        if (sarcophagus == null || housedCorpse == null || panelRoot == null) return;
        piece = sarcophagus;
        corpse = housedCorpse;

        var crypt = CryptController.Instance;
        if (nameLabel != null) nameLabel.text = housedCorpse.HeroName + " lies here.";
        if (costLabel != null && crypt != null)
            costLabel.text = crypt.RaiseManaCost.ToString("0") + " mana. "
                + crypt.RisenCapacityCost + " capacity, held while it walks.";
        if (mortalityLabel != null)
            mortalityLabel.text = "Raised once. When it falls, it falls forever.";

        panelRoot.SetActive(true);
        RefreshAffordability();
    }

    public void Close()
    {
        piece = null;
        corpse = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (corpse == null || corpse.Claimed) { Close(); return; }
        RefreshAffordability();
    }

    private void RefreshAffordability()
    {
        if (raiseButton == null) return;
        var core = DungeonCore.Instance;
        var crypt = CryptController.Instance;
        raiseButton.interactable = core != null && crypt != null
            && !PauseGate.Held
            && core.FreeCapacity >= crypt.RisenCapacityCost
            && core.CurrentMana >= crypt.RaiseManaCost;
    }

    private void OnRaiseClicked()
    {
        if (piece == null) return;
        // Raising spawns a body onto the board, so it is acting, not deciding
        // (canon 39). The panel still opens and reads while held -- pausing
        // mid-raid to conjure defenders was the sharpest case the audit found.
        if (PauseGate.RefuseAt(piece.transform.position)) return;
        if (CryptController.Instance != null && CryptController.Instance.RaiseFromSarcophagus(piece))
            Close();
    }
}