using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 敵（Enemy_01）と Box を使えるようにする初期設定。
/// ・Box プレハブに時限爆弾（BombBox）を付ける
/// ・Box のアイテムデータ（Resources/BoxItemData）とインベントリ用アイコンを作る
/// ・Enemy_01 プレハブに EnemyBrain を付け、Box と動きのクリップを割り当てる
/// ・敵の出現設定（Resources/EnemySpawnConfig）を作る
/// プロジェクトを開いたときに、未設定なら自動で実行される。メニュー「JackDead/Setup Enemy」から再実行もできる（何度実行しても安全）。
/// </summary>
[InitializeOnLoad]
public static class EnemySetup
{
    // 設定内容を変えたら上げる（プロジェクトを開いたときに、古い設定だと自動で再実行される）
    private const int SetupVersion = 3;
    private const string VersionKey = "JackDead.EnemySetupVersion";
    private const string BoxPrefabPath = "Assets/Prefabs/Box.prefab";
    private const string EnemyPrefabPath = "Assets/Prefabs/Enemy/Enemy_01.prefab";
    private const string ResourcesDir = "Assets/Resources";
    private const string BoxItemPath = "Assets/Data/BoxItemData.asset";
    private const string ConfigPath = "Assets/Resources/EnemySpawnConfig.asset";
    private const string IconPath = "Assets/Images/Sprites/BoxIcon.png";
    private const string AnimDir = "Assets/Animations/Enemy_01/Animation/";

    static EnemySetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(ConfigPath) && File.Exists(BoxItemPath) && EditorPrefs.GetInt(VersionKey, 0) >= SetupVersion) return;
            Run();
            EditorPrefs.SetInt(VersionKey, SetupVersion);
        };
    }

    [MenuItem("JackDead/Setup Enemy")]
    public static void Run()
    {
        GameObject boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (boxPrefab == null || enemyPrefab == null)
        {
            Debug.LogWarning("[EnemySetup] Box.prefab または Enemy_01.prefab が見つかりません。");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ResourcesDir)) AssetDatabase.CreateFolder("Assets", "Resources");

        AddComponentToPrefab<BombBox>(BoxPrefabPath);
        ConfigureBombBox();
        boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);

        Sprite icon = EnsureIcon();
        ItemDataAsset boxData = EnsureBoxItemData(boxPrefab, icon);

        AddComponentToPrefab<EnemyBrain>(EnemyPrefabPath);
        ConfigureEnemyPrefab(boxPrefab, boxData);
        enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);

        EnsureConfig(enemyPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EnemySetup] 敵と Box の設定が完了しました。");
    }

    private static void AddComponentToPrefab<T>(string path) where T : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<T>() == null)
            {
                root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>爆発までの時間と跳ね具合（プレハブに保存されている値）を設定する</summary>
    private static void ConfigureBombBox()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BoxPrefabPath);
        try
        {
            BombBox bomb = root.GetComponent<BombBox>();
            if (bomb == null) return;

            SerializedObject so = new SerializedObject(bomb);
            so.FindProperty("fuseTime").floatValue = 5f;
            so.FindProperty("bounciness").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BoxPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureEnemyPrefab(GameObject boxPrefab, ItemDataAsset boxData)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        try
        {
            EnemyBrain brain = root.GetComponent<EnemyBrain>();
            if (brain == null) return;

            brain.boxPrefab = boxPrefab;
            brain.boxItemData = boxData;
            if (brain.idleClip == null) brain.idleClip = LoadClip("HumanoidIdle.fbx");
            if (brain.walkClip == null) brain.walkClip = LoadClip("HumanoidWalk.fbx");
            if (brain.runClip == null) brain.runClip = LoadClip("HumanoidRun.fbx");

            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static AnimationClip LoadClip(string fbxName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(AnimDir + fbxName)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    private static ItemDataAsset EnsureBoxItemData(GameObject boxPrefab, Sprite icon)
    {
        ItemDataAsset data = AssetDatabase.LoadAssetAtPath<ItemDataAsset>(BoxItemPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemDataAsset>();
            AssetDatabase.CreateAsset(data, BoxItemPath);
        }

        SerializedObject so = new SerializedObject(data);
        so.FindProperty("amount").intValue = 0;
        so.FindProperty("itemName").stringValue = "Box";
        so.FindProperty("itemWeight").floatValue = 0.1f; // 投げる力は重さに反比例するので、最小（0.1）にして一番遠くへ飛ぶようにする
        so.FindProperty("itemPrefab").objectReferenceValue = boxPrefab;
        so.FindProperty("requiredHoldTime").floatValue = 0.5f;
        so.FindProperty("isBomb").boolValue = true;
        if (icon != null) so.FindProperty("itemIcon").objectReferenceValue = icon;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        return data;
    }

    private static void EnsureConfig(GameObject enemyPrefab)
    {
        EnemySpawnConfig config = AssetDatabase.LoadAssetAtPath<EnemySpawnConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<EnemySpawnConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        // Enemy_01 は Tier 1 に登録する（まだ入っていなければ）
        if (config.tiers != null && config.tiers.Length > 0 && config.tiers[0] != null)
        {
            if (config.tiers[0].prefabs == null) config.tiers[0].prefabs = new System.Collections.Generic.List<GameObject>();
            if (!config.tiers[0].prefabs.Contains(enemyPrefab)) config.tiers[0].prefabs.Add(enemyPrefab);
        }
        EditorUtility.SetDirty(config);
    }

    /// <summary>インベントリに表示する Box のアイコン（茶色い箱に赤い「!」）を作る</summary>
    private static Sprite EnsureIcon()
    {
        if (!File.Exists(IconPath))
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color brown = new Color(0.62f, 0.42f, 0.2f);
            Color dark = new Color(0.35f, 0.22f, 0.1f);
            Color red = new Color(0.9f, 0.1f, 0.1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inBox = x >= 6 && x < size - 6 && y >= 6 && y < size - 6;
                    bool edge = inBox && (x < 10 || x >= size - 10 || y < 10 || y >= size - 10);
                    bool bang = (x >= 29 && x < 35) && ((y >= 24 && y < 48) || (y >= 14 && y < 20));
                    tex.SetPixel(x, y, bang ? red : edge ? dark : inBox ? brown : clear);
                }
            }
            tex.Apply();
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath);

            TextureImporter importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }
        // 作った直後は読み込めないことがあるので、インポートし直してから探す
        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (sprite == null) sprite = AssetDatabase.LoadAllAssetsAtPath(IconPath).OfType<Sprite>().FirstOrDefault();
        return sprite;
    }
}
