using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 死亡演出（DeathEffect）の画面の部品を、ゲームシーンの Hierarchy に実物として作って保存する。
/// 置き場所: PlayerStatusUI の Canvas の子「DeathEffectUI」（Canvas は増やさない。表示順は子の Canvas の Override Sorting で、ポーズ画面の下・HUD の上）。
/// 中身: Tint（赤い色味）/ Fade（黒いフェード）/ Title（「死亡」の文字）/ Reason（原因の文字）。
/// それを動かす DeathEffect は、シーンのルートの「DeathEffect」オブジェクトに付ける。
/// シーンに DeathEffect / HitFeedback が無いとき、、スクリプト再読込のあとに自動で一度行う。作り直すときは メニュー JackDead/Game/死亡演出UIをシーンに作成。
/// </summary>
[InitializeOnLoad]
public static class DeathEffectBuilder
{
    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";

    static DeathEffectBuilder()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Game/ダメージ・死亡演出UIをシーンに作成")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(GameScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[DeathEffectBuilder] SampleScene に未保存の変更があります。保存してから、もう一度実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);

        try
        {
            DeathEffect existingDeath = null;
            HitFeedback existingHit = null;
            Canvas hud = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (existingDeath == null) existingDeath = root.GetComponentInChildren<DeathEffect>(true);
                if (existingHit == null) existingHit = root.GetComponentInChildren<HitFeedback>(true);
                if (hud == null)
                    foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                        if (c.name == "PlayerStatusUI") { hud = c; break; }
            }
            if (existingDeath != null && existingHit != null && !force) return;
            if (hud == null)
            {
                Debug.LogWarning("[DeathEffectBuilder] PlayerStatusUI の Canvas が見つかりません。");
                return;
            }

            if (existingDeath == null || force)
            {
                if (existingDeath != null)
                {
                    if (existingDeath.tint != null && existingDeath.tint.transform.parent != null) Object.DestroyImmediate(existingDeath.tint.transform.parent.gameObject);
                    Object.DestroyImmediate(existingDeath.gameObject);
                }
                BuildDeath(scene, hud);
            }
            if (existingHit == null || force)
            {
                if (existingHit != null)
                {
                    if (existingHit.flash != null && existingHit.flash.transform.parent != null) Object.DestroyImmediate(existingHit.flash.transform.parent.gameObject);
                    Object.DestroyImmediate(existingHit.gameObject);
                }
                BuildHit(scene, hud);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DeathEffectBuilder] ダメージ・死亡演出（HitFeedback / DeathEffect と、その UI）をゲームシーンに作成しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BuildDeath(Scene scene, Canvas hud)
    {
        GameObject ui = new GameObject("DeathEffectUI", typeof(RectTransform), typeof(Canvas));
        ui.transform.SetParent(hud.transform, false);
        LobbyUIBuilder.Stretch((RectTransform)ui.transform);
        Canvas canvas = ui.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 90;

        Image tint = LobbyUIBuilder.NewImage(ui.transform, "Tint", new Color(0.55f, 0f, 0f, 0f));
        LobbyUIBuilder.Stretch(tint.rectTransform);
        tint.raycastTarget = false;

        Image fade = LobbyUIBuilder.NewImage(ui.transform, "Fade", new Color(0f, 0f, 0f, 0f));
        LobbyUIBuilder.Stretch(fade.rectTransform);
        fade.raycastTarget = false;

        TextMeshProUGUI title = MakeCenterText(ui.transform, "Title", "死亡", 150f, new Color(0.8f, 0.05f, 0.05f, 0f), 40f, FontStyles.Bold);
        TextMeshProUGUI reason = MakeCenterText(ui.transform, "Reason", "", 44f, new Color(0.8f, 0.8f, 0.8f, 0f), -90f, FontStyles.Normal);

        LobbyUIBuilder.SetLayerRecursive(ui, 5);

        // 動かす本体（UI の外に置く）
        GameObject go = new GameObject("DeathEffect");
        SceneManager.MoveGameObjectToScene(go, scene);
        DeathEffect fx = go.AddComponent<DeathEffect>();
        fx.tint = tint;
        fx.fade = fade;
        fx.title = title;
        fx.reason = reason;
        EditorUtility.SetDirty(fx);
    }

    private static void BuildHit(Scene scene, Canvas hud)
    {
        // 被弾の赤い閃光（HUD の上、死亡演出の下）
        GameObject ui = new GameObject("HitEffectUI", typeof(RectTransform), typeof(Canvas));
        ui.transform.SetParent(hud.transform, false);
        LobbyUIBuilder.Stretch((RectTransform)ui.transform);
        Canvas canvas = ui.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 80;

        Image flash = LobbyUIBuilder.NewImage(ui.transform, "Flash", new Color(0.8f, 0f, 0f, 0f));
        LobbyUIBuilder.Stretch(flash.rectTransform);
        flash.raycastTarget = false;
        LobbyUIBuilder.SetLayerRecursive(ui, 5);

        GameObject go = new GameObject("HitFeedback");
        SceneManager.MoveGameObjectToScene(go, scene);
        HitFeedback hit = go.AddComponent<HitFeedback>();
        hit.flash = flash;
        EditorUtility.SetDirty(hit);
    }

    /// <summary>画面の中央に置く文字（中央基準。y は中央からの上下のずれ）</summary>
    private static TextMeshProUGUI MakeCenterText(Transform parent, string name, string text, float size, Color color, float y, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400f, size * 1.3f);
        rt.anchoredPosition = new Vector2(0f, y);

        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }
}
