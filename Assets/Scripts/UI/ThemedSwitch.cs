using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a Black Hammer "switch" Toggle: swaps the track sprite and slides the
/// knob between off (left) and on (right) when the value changes. Attach to the
/// switch root (the object that has the Toggle), wire the fields, and it keeps the
/// visuals in sync with Toggle.isOn -- including on enable and on load.
/// </summary>
[RequireComponent(typeof(Toggle))]
public class ThemedSwitch : MonoBehaviour
{
    [Header("Track")]
    [SerializeField] private Image trackImage;   // the switch background (the track)
    [SerializeField] private Sprite onSprite;     // switch_orange
    [SerializeField] private Sprite offSprite;    // switch_gray

    [Header("Knob")]
    [SerializeField] private RectTransform knob;  // the sliding knob
    [SerializeField] private float onX = 24f;   // knob anchoredPosition.x when ON
    [SerializeField] private float offX = -24f;   // knob anchoredPosition.x when OFF

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(Apply);
    }

    private void OnEnable()
    {
        Apply(toggle != null && toggle.isOn);
    }

    private void OnDestroy()
    {
        if (toggle != null) toggle.onValueChanged.RemoveListener(Apply);
    }

    private void Apply(bool on)
    {
        if (trackImage != null && onSprite != null && offSprite != null)
            trackImage.sprite = on ? onSprite : offSprite;

        if (knob != null)
        {
            Vector2 p = knob.anchoredPosition;
            p.x = on ? onX : offX;
            knob.anchoredPosition = p;
        }
    }
}