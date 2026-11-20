using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class CasinoButtonEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("連動するテキストと飾り")]
    public TextMeshProUGUI buttonText;
    public GameObject casinoIcon; // カーソルが乗ったときだけ出る飾り（チップやなど）

    [Header("色設定")]
    public Color normalColor = new Color(0.8f, 0.8f, 0.8f, 1f); // 通常時：少し暗い白
    public Color hoverColor = new Color(0.9f, 0.1f, 0.1f, 1f);  // ホバー時：不気味な赤

    [Header("動き設定")]
    public float slideOffsetX = 25f; // ホバー時に右へスライドする距離
    public float smoothSpeed = 12f;  // アニメーションの滑らかさ

    [Header("ホバー音（任意）")]
    public Title3DSystem titleSystem; // 音を鳴らす用（未設定でもエラーにならない）

    private Vector2 initialPos;
    private Vector2 targetPos;
    private Color targetColor;
    private RectTransform rectTransform;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        initialPos = rectTransform.anchoredPosition;
        targetPos = initialPos;
        targetColor = normalColor;

        if (buttonText != null) buttonText.color = normalColor;
        if (casinoIcon != null) casinoIcon.SetActive(false);
    }

    void Update()
    {
        // 位置と色を滑らかに変化させる
        rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, targetPos, Time.deltaTime * smoothSpeed);

        if (buttonText != null)
        {
            buttonText.color = Color.Lerp(buttonText.color, targetColor, Time.deltaTime * smoothSpeed);
        }
    }

    // マウスカーソルが乗った瞬間
    public void OnPointerEnter(PointerEventData eventData)
    {
        targetPos = initialPos + new Vector2(slideOffsetX, 0f);
        targetColor = hoverColor;

        if (casinoIcon != null) casinoIcon.SetActive(true);
        if (titleSystem != null) titleSystem.PlayHoverSound();
    }

    // マウスカーソルが離れた瞬間
    public void OnPointerExit(PointerEventData eventData)
    {
        targetPos = initialPos;
        targetColor = normalColor;

        if (casinoIcon != null) casinoIcon.SetActive(false);
    }
}