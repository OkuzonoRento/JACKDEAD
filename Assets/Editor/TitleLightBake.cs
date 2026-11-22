using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルのスロット台のライト演出（スポットライト・背後の赤い光・周りを暗くする）を、シーンのデータとして保存する。
/// これで、再生していないときも、再生したときと同じ見た目になる（ちらつき・揺れは再生中だけ動く）。
/// 台に EerieSpotPivot が無いときだけ、スクリプト再読込のあとに一度行う。
/// 調整したいときは、TitleSlotMachine の値を変えて、メニュー JackDead/Title/スロットのライトをシーンに反映 を実行する。
/// </summary>
[InitializeOnLoad]
public static class TitleLightBake
{
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    static TitleLightBake()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Title/スロットのライトをシーンに反映")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(TitleScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[TitleLightBake] TitleScene に未保存の変更があります。保存してから再実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);

        try
        {
            SlotMachineController machine = null;
            TitleSlotMachine title = null;
            Vector3 pos = new Vector3(2f, -1f, 5f);
            Quaternion rot = Quaternion.identity;
            Vector3 scl = Vector3.one * 0.7f;
            bool restored = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (title == null) title = root.GetComponentInChildren<TitleSlotMachine>(true);
                foreach (SlotMachineController c in root.GetComponentsInChildren<SlotMachineController>(true))
                {
                    // 台の本体は、リール（SlotReelScroller）を子に持つ。持たないものは、壊れた残骸
                    if (c.GetComponentInChildren<SlotReelScroller>(true) != null) { if (machine == null) machine = c; continue; }
                    pos = c.transform.position;
                    rot = c.transform.rotation;
                    scl = c.transform.localScale;
                    Object.DestroyImmediate(c);
                    restored = true;
                }
            }

            // 台がシーンに無い（壊れて消えた）ときは、プレハブから置き直す
            if (machine == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Slots/SlotMachine-DEMO.prefab");
                if (prefab == null) return;
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.transform.SetPositionAndRotation(pos, rot);
                inst.transform.localScale = scl;
                machine = inst.GetComponent<SlotMachineController>();
                if (machine == null) return;
                restored = true;
                Debug.Log("[TitleLightBake] スロット台をプレハブから置き直しました。");
            }
            bool baked = machine.transform.Find("EerieSpotPivot") != null;
            if (baked && !force && !restored) return;

            // 1. TitleSlotMachine をシーンに置く（実行時の自動生成をやめる）
            if (title == null)
            {
                GameObject go = new GameObject("TitleSlotMachine");
                SceneManager.MoveGameObjectToScene(go, scene);
                title = go.AddComponent<TitleSlotMachine>();
            }
            title.machine = machine;
            if (!title.eerieLight) return;

            // 2. 周りを暗くする（再実行で二重に暗くならないよう、まだ反映していないときだけ）
            title.DimSceneLights(); // 反映済みなら何もしない（二重に暗くならない）

            // 3. スポットと赤い光を作る
            title.BuildLightsInScene();

            // 4. タイトルの飾り用に、ボタンとゲーム用の文字を切る（実行時の PrepareForTitle と同じ）
            foreach (SlotButtonInteractable b in machine.GetComponentsInChildren<SlotButtonInteractable>(true))
                b.enabled = false;
            foreach (TMPro.TextMeshPro t in machine.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                t.gameObject.SetActive(false);

            EditorUtility.SetDirty(title);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TitleLightBake] スロット台のライトと周りの暗さを、タイトルシーンに保存しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
