using TMPro;
using UnityEngine;

/// <summary>
/// 金額が減ったことを、その場に「-$123」と浮かべて見せる。少し上へ昇りながら消える。
/// </summary>
public class ItemValuePopup : MonoBehaviour
{
    private const float Duration = 1.2f;
    private const float RiseHeight = 0.6f;
    private const float StartHeight = 0.35f;
    private const float FontSize = 3f;

    private TextMeshPro label;
    private Vector3 origin;
    private float elapsed;

    public static void Spawn(Vector3 position, int loss)
    {
        GameObject obj = new GameObject("ItemValuePopup");
        obj.transform.position = position + Vector3.up * StartHeight;

        ItemValuePopup popup = obj.AddComponent<ItemValuePopup>();
        popup.origin = obj.transform.position;
        popup.label = obj.AddComponent<TextMeshPro>();
        popup.label.alignment = TextAlignmentOptions.Center;
        popup.label.fontSize = FontSize;
        popup.label.color = new Color(1f, 0.25f, 0.2f, 1f);
        popup.label.text = $"-${loss:N0}";
        popup.label.rectTransform.sizeDelta = new Vector2(4f, 1f);
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Duration);
        transform.position = origin + Vector3.up * (RiseHeight * t);

        Camera cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;

        Color c = label.color;
        c.a = 1f - t * t;
        label.color = c;

        if (t >= 1f) Destroy(gameObject);
    }
}
