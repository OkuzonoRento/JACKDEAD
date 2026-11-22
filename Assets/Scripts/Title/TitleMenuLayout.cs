using UnityEngine;
using TMPro;

/// <summary>
/// タイトルのメインメニューの配置（R.E.P.O. のタイトルのような、左端にそろえた文字だけのリスト）。
/// ・ロゴは左上
/// ・項目は上から HOST GAME / JOIN GAME、（空き）、SINGLE PLAY、（空き）、SETTING / QUIT
/// ・ホバーの動き（右へスライド・赤くなる・トランプのマーク）は CasinoButtonEffect のまま
///
/// 配置はエディタ（Assets/Editor/TitleSceneBake.cs）がシーンに保存する。実行時には何も動かさない。
/// 位置や大きさを変えたいときは下の定数を調整して、メニュー JackDead/Title/メニュー配置をシーンに反映 を実行する。
/// </summary>
public static class TitleMenuLayout
{
    private const float LeftEdge = -820f;       // 文字の左端（画面中央が 0。基準解像度 1920x1080）
    private const float ItemWidth = 560f;
    private const float ItemHeight = 60f;
    private const float ItemFontSize = 46f;

    private const float FirstItemY = 150f;      // 最初の項目の Y 座標
    private const float ItemPitch = 72f;        // 同じグループ内の項目の間隔
    private const float GroupGap = 40f;        // グループとグループの間にさらに空ける距離

    private const float LogoY = 433f;           // ロゴの Y 座標（上寄せ）

    private static CasinoButtonEffect Find(CasinoButtonEffect[] all, string keyword)
    {
        foreach (CasinoButtonEffect e in all)
            if (e.name.ToUpperInvariant().Contains(keyword)) return e;
        return null;
    }

    /// <summary>メニューの項目とロゴを並べる。all にはメニューのボタン（HOST / JOIN / SINGLE / SETTING / QUIT）をすべて入れる</summary>
    public static void Arrange(CasinoButtonEffect[] all)
    {
        if (all == null || all.Length == 0) return;

        // 上から順に。null のものは飛ばす。null の位置が「グループの切れ目」
        CasinoButtonEffect[] order =
        {
            Find(all, "HOST"),
            Find(all, "JOIN"),
            null,
            Find(all, "SINGLE"),
            null,
            Find(all, "SETTING"),
            Find(all, "QUIT"),
        };

        float y = FirstItemY;
        foreach (CasinoButtonEffect e in order)
        {
            if (e == null) { y -= GroupGap; continue; }
            PlaceItem(e, y);
            y -= ItemPitch;
        }

        PlaceLogo(all[0].transform.parent as RectTransform);
    }

    private static void PlaceItem(CasinoButtonEffect e, float y)
    {
        RectTransform rt = (RectTransform)e.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // ボタンに Scale（3 倍など）が掛かっているので、画面上の大きさが定数どおりになるよう割り戻す
        float s = Mathf.Max(0.01f, rt.localScale.x);
        rt.sizeDelta = new Vector2(ItemWidth / s, ItemHeight / s);
        rt.anchoredPosition = new Vector2(LeftEdge + ItemWidth * 0.5f, y);

        // 文字は左揃え・やや小さめ
        if (e.buttonText != null)
        {
            e.buttonText.alignment = TextAlignmentOptions.MidlineLeft;
            e.buttonText.enableAutoSizing = false;
            e.buttonText.fontSize = ItemFontSize / s;
            RectTransform tr = (RectTransform)e.buttonText.transform;
            if (tr != rt)
            {
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = tr.offsetMax = Vector2.zero;
            }
        }

        // ホバー時のトランプのマークは、文字の左隣に出す
        if (e.casinoIcon != null)
        {
            RectTransform icon = (RectTransform)e.casinoIcon.transform;
            icon.anchoredPosition = new Vector2((-ItemWidth * 0.5f - 30f) / s, 0f);
        }
    }

    /// <summary>ロゴ（JACKDEAD の大きな文字）を、項目と同じ左端にそろえて左上へ</summary>
    private static void PlaceLogo(RectTransform panel)
    {
        if (panel == null) return;

        foreach (TextMeshProUGUI t in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.transform.parent != panel) continue;
            if (t.text.Trim() != "JACKDEAD") continue;

            RectTransform rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            t.alignment = TextAlignmentOptions.TopLeft;
            rt.anchoredPosition = new Vector2(LeftEdge + rt.sizeDelta.x * 0.5f, LogoY);
            return;
        }
    }
}
