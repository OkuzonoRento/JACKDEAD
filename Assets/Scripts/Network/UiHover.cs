using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>マウスが行の範囲に入っている間、背景の色を変えて説明を出す（設定画面の行用）。
/// 上に重なるボタンやスライダーに乗っても行から外れた扱いにならないよう、イベントではなく位置で判定する。
/// シーンに置く部品は、クラス名と同じ名前のファイルに書くこと（別のファイルに書くと、ビルドしたゲームがシーンを読めずに落ちる）</summary>
public class UiHover : MonoBehaviour
{
    public Image bg;
    public Color normal;
    public Color hover;
    public TMP_Text descTarget;
    public string descJa, descEn;
    public string defaultJa, defaultEn;

    private bool inside;

    void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool now = mouse != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, mouse.position.ReadValue(), null);
        if (now == inside) return;
        inside = now;
        Apply();
    }

    void OnEnable()
    {
        inside = false;
        Apply();
    }

    private void Apply()
    {
        if (bg != null) bg.color = inside ? hover : normal;
        if (descTarget != null) descTarget.text = inside ? Localization.T(descJa, descEn) : Localization.T(defaultJa, defaultEn);
    }
}
