using TMPro;
using UnityEngine;

/// <summary>
/// 文字を、現在の言語に合わせて日本語 / 英語で表示する。言語を切り替えると、その場で書き換わる。
/// シーンに置いた TextMeshPro の文字に付ける（タイトルのメニューなど）。
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [TextArea(1, 3)] public string japanese;
    [TextArea(1, 3)] public string english;

    private TMP_Text target;

    private void OnEnable()
    {
        Localization.LanguageChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= Apply;
    }

    public void Set(string ja, string en)
    {
        japanese = ja;
        english = en;
        Apply();
    }

    public void Apply()
    {
        if (target == null) target = GetComponent<TMP_Text>();
        if (target == null) return;
        string s = Localization.T(japanese, english);
        if (!string.IsNullOrEmpty(s)) target.text = s;
    }
}
