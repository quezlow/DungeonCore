using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MapController_Dynamic : MonoBehaviour
{
    [Header("Ui References")]
    public RectTransform mapParent;
    public GameObject areaPrefab;
    public RectTransform playerIcon;

    [Header("Colors")]
    public Color defaultColor = Color.gray;
    public Color currentAreaColor = Color.green;

    [Header("Map Settings")]
    public GameObject mapBounds;
    public PolygonCollider2D initialArea;
    public float mapScale = 10f; //adjust map size of UI

    private PolygonCollider2D[] mapAreas;
    private Dictionary<string, RectTransform> uiAreas = new Dictionary<string, RectTransform>();
    
    public static MapController_Dynamic Instance { get; set; }

    private void Awake()
    {
        if(Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        mapAreas = mapBounds.GetComponentsInChildren<PolygonCollider2D>();
    } 

    //generate map
    public void GenerateMap(PolygonCollider2D newCurrentArea = null)
    {
        PolygonCollider2D currentArea = newCurrentArea != null ? newCurrentArea : initialArea;

        ClearMap();

        foreach(PolygonCollider2D area in mapAreas)
        {
            CreateAreaUI(area, area == currentArea);
        }

        MovePlayerIcon(currentArea.name);
    }

    //clear map
    private void ClearMap()
    {
        foreach(Transform child in mapParent)
        {
            Destroy(child.gameObject);
        }

        uiAreas.Clear();
    }

    private void CreateAreaUI(PolygonCollider2D area, bool isCurrent)
    {
        //instantiate prefab for image
        GameObject areaImage = Instantiate(areaPrefab, mapParent);
        RectTransform rectTransform = areaImage.GetComponent<RectTransform>();

        //get bounds
        Bounds bounds = area.bounds;

        //scale ui image fit map and bounds
        rectTransform.sizeDelta = new Vector2(bounds.size.x * mapScale, bounds.size.y * mapScale);
        rectTransform.anchoredPosition = bounds.center * mapScale;

        //set color based on current or not
        areaImage.GetComponent<Image>().color = isCurrent ? currentAreaColor : defaultColor;

        //add to dictionary
        uiAreas[area.name] = rectTransform;

    }
    //update current area
    public void UpdateCurrentArea(string newCurrentArea)
    {
        foreach(KeyValuePair<string, RectTransform> area in uiAreas)
        {
            area.Value.GetComponent<Image>().color = area.Key == newCurrentArea ? currentAreaColor : defaultColor;
        }

        MovePlayerIcon(newCurrentArea);
    }

    //move playericon
    private void MovePlayerIcon(string newCurrentArea)
    {
        if(uiAreas.TryGetValue(newCurrentArea, out RectTransform areaUI))
        {
            playerIcon.anchoredPosition = areaUI.anchoredPosition;
        }
    }
}
