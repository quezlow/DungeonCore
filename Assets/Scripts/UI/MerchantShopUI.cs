using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Wandering Merchant's shop panel. Opened by clicking the docked
/// merchant; rows are built from his current visit's stock, priced buttons
/// grey out live as gold changes (DungeonCore.OnGoldChanged), and a purchase
/// rebuilds the list - sold means gone until his next visit.
///
/// Dismissal is the Close button only: ESC arbitration is centralised in
/// PauseMenuController by house rule, so this panel does not listen for keys.
/// The game keeps running while the wagon is open - the surface stays alive.
/// </summary>
public class MerchantShopUI : MonoBehaviour
{
    public static MerchantShopUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private CanvasGroup panelRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text goldLabel;
    [SerializeField] private Button closeButton;

    [Header("Rows")]
    [SerializeField] private Transform contentParent;
    [Tooltip("Row prefab: a Button with two TMP_Texts named Name and Price, and a TMP_Text named Flavour.")]
    [SerializeField] private GameObject rowPrefab;

    // Held as the INTERFACE, not the merchant. Canon 19 asked for this before a
    // second vendor existed rather than after, and this is that moment: the
    // dwarven outpost is the second, and a third would otherwise have added a
    // branch to each of the four places this used to name a concrete type.
    private IShopVendor vendor;
    private readonly List<GameObject> rows = new();
    private bool open;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(IShopVendor from)
    {
        if (open || from == null || panelRoot == null) return;
        vendor = from;
        open = true;

        if (titleLabel != null) titleLabel.text = from.ShopTitle;
        panelRoot.gameObject.SetActive(true);
        panelRoot.alpha = 1f;
        panelRoot.blocksRaycasts = true;

        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnGoldChanged += HandleGoldChanged;

        Rebuild();
        RefreshGold();
    }

    public void Close()
    {
        if (!open) return;
        open = false;

        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnGoldChanged -= HandleGoldChanged;

        ClearRows();
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
        vendor = null;
    }

    /// <summary>Read by PauseMenuController's central ESC chain.</summary>
    public bool IsOpen => open;

    /// <summary>Dusk pulls the wagon out from under an open panel.</summary>
    public void CloseIfOpen() { if (open) Close(); }

    // -- Internals -----------------------------------------------------------

    private void HandleGoldChanged(int _) { RefreshGold(); RefreshAffordability(); }

    private void RefreshGold()
    {
        if (goldLabel != null && DungeonCore.Instance != null)
            goldLabel.text = DungeonCore.Instance.Gold + "g";
    }

    private void Rebuild()
    {
        ClearRows();
        if (vendor == null || contentParent == null || rowPrefab == null) return;

        foreach (var entry in vendor.CurrentStock)
        {
            var e = entry;   // capture per row
            GameObject row = Instantiate(rowPrefab, contentParent);
            rows.Add(row);

            foreach (var label in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name == "Name") label.text = e.displayName;
                // PriceOf, not e.price: the Deep Holds discount by regard and the
                // shelf must show what will actually be taken.
                else if (label.name == "Price") label.text = vendor.PriceOf(e) + "g";
                else if (label.name == "Flavour") label.text = e.flavour;
            }

            Button buy = row.GetComponentInChildren<Button>(true);
            if (buy != null)
                buy.onClick.AddListener(() =>
                {
                    if (vendor != null && vendor.TryPurchase(e))
                    {
                        Rebuild();
                        RefreshGold();
                    }
                });
        }
        RefreshAffordability();
    }

    private void RefreshAffordability()
    {
        if (vendor == null || DungeonCore.Instance == null) return;
        int gold = DungeonCore.Instance.Gold;
        int i = 0;
        foreach (var entry in vendor.CurrentStock)
        {
            if (i >= rows.Count) break;
            Button buy = rows[i].GetComponentInChildren<Button>(true);
            if (buy != null) buy.interactable = gold >= vendor.PriceOf(entry);
            i++;
        }
    }

    private void ClearRows()
    {
        foreach (GameObject row in rows)
            if (row != null) Destroy(row);
        rows.Clear();
    }
}