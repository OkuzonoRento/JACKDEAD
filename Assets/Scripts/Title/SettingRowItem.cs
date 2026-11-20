using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class SettingRowItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image highlightBackground;
    public Color normalBgColor = new Color(1f, 1f, 1f, 0f);
    public Color hoverBgColor = new Color(1f, 1f, 1f, 0.14f);

    public TextMeshProUGUI descriptionTargetText;
    [TextArea(2, 3)]
    public string descriptionMessage = "";

    void Start()
    {
        if (highlightBackground != null)
            highlightBackground.color = normalBgColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (highlightBackground != null)
            highlightBackground.color = hoverBgColor;

        if (descriptionTargetText != null)
            descriptionTargetText.text = descriptionMessage;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (highlightBackground != null)
            highlightBackground.color = normalBgColor;
    }
}