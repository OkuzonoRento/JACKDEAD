using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// スクラップ（11種類）を取り込む。Assets/Models/Scrap の scrap.json・テクスチャ・アイコンから、
/// マテリアル・メッシュ・プレハブ・ItemDataAsset を作り、SampleScene の MapGenerator の scrapDataList を、この11種類に差し替える。
/// まだ作っていなければ、スクリプトの読み込み時に自動で一度だけ実行する。作り直すときはメニューから。
/// 金額・重さなどは、ItemDataAsset を最初に作るときだけ scrap.json の値を入れる（作り直しても、インスペクターで直した値は残る）。
/// 元の仮アイテム（Assets/Data/Item/DebugItem_*）は残すので、scrapDataList に戻せば元通りになる。
/// モデルは Docs/ScrapModels の build_scrap.py と export_unity.py で作る。
/// </summary>
[InitializeOnLoad]
public static class ScrapImporter
{
    private const string Root = "Assets/Models/Scrap";
    private const string JsonPath = Root + "/scrap.json";
    private const string TextureDir = Root + "/textures";
    private const string IconDir = Root + "/icons";
    private const string MaterialDir = Root + "/Materials";
    private const string MeshDir = Root + "/Generated";
    private const string PrefabDir = "Assets/Prefabs/Scrap";
    private const string DataDir = "Assets/Data/Scrap";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string Tag = "[ScrapImporter] ";

    private const float MinThickness = 0.06f;   // これより薄い物は、すり抜けないよう、当たり判定だけ厚くする
    private const float HoldTime = 0.8f;        // 拾うのに押し続ける時間（秒）

    [System.Serializable]
    private class ScrapList { public ScrapInfo[] items; }

    [System.Serializable]
    private class ScrapInfo
    {
        public string id, displayName;
        public int price;
        public float weight, fragility, playerStun, enemyStun, lightIntensity, lightRange, lightAngle;
        public float[] vertices, normals, uvs; // 三角形ごとに3点ずつ（Unity の座標、m）
    }

    static ScrapImporter()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Item/スクラップを取り込む（作り直す）")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(JsonPath)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += () => Run(force);
            return;
        }

        ScrapList list = JsonUtility.FromJson<ScrapList>(File.ReadAllText(JsonPath));
        if (list == null || list.items == null || list.items.Length == 0)
        {
            Debug.LogError(Tag + "scrap.json を読めませんでした。");
            return;
        }

        // 作成済みなら、自動では何もしない（手で変えた内容を上書きしない）
        ItemDataAsset last = AssetDatabase.LoadAssetAtPath<ItemDataAsset>(DataPath(list.items[list.items.Length - 1]));
        if (!force && last != null && last.ItemPrefab != null) return;

        AssetDatabase.Refresh();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError(Tag + "URP の Lit シェーダーが見つかりません。");
            return;
        }
        int itemsLayer = LayerMask.NameToLayer("Items");
        if (itemsLayer < 0)
        {
            Debug.LogError(Tag + "Items レイヤーがありません。");
            return;
        }

        EnsureFolder(MaterialDir);
        EnsureFolder(MeshDir);
        EnsureFolder(PrefabDir);
        EnsureFolder(DataDir);

        List<ItemDataAsset> created = new List<ItemDataAsset>();
        foreach (ScrapInfo info in list.items)
        {
            ItemDataAsset data = Build(info, shader, itemsLayer);
            if (data == null) return;
            created.Add(data);
        }

        AssetDatabase.SaveAssets();
        RegisterToScene(created);
        Debug.Log(Tag + $"完了：スクラップ {created.Count} 種類を取り込みました。");
    }

    private static string DataPath(ScrapInfo info) => $"{DataDir}/Scrap_{info.id}.asset";

    private static ItemDataAsset Build(ScrapInfo info, Shader shader, int itemsLayer)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/Scrap_{info.id}.png");
        if (texture == null)
        {
            Debug.LogError(Tag + $"テクスチャがありません: Scrap_{info.id}.png");
            return null;
        }

        // マテリアル
        string materialPath = $"{MaterialDir}/Scrap_{info.id}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.15f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);

        Mesh mesh = SaveMesh(BuildMesh(info));
        Sprite icon = LoadIcon($"{IconDir}/Icon_{info.id}.png");

        // アイテムデータ（先に作り、プレハブの Item から参照させる）
        string dataPath = DataPath(info);
        ItemDataAsset data = AssetDatabase.LoadAssetAtPath<ItemDataAsset>(dataPath);
        bool isNew = data == null;
        if (isNew)
        {
            data = ScriptableObject.CreateInstance<ItemDataAsset>();
            AssetDatabase.CreateAsset(data, dataPath);
        }

        SerializedObject serialized = new SerializedObject(data);
        if (isNew)
        {
            serialized.FindProperty("amount").intValue = info.price;
            serialized.FindProperty("itemName").stringValue = info.displayName;
            serialized.FindProperty("itemWeight").floatValue = info.weight;
            serialized.FindProperty("requiredHoldTime").floatValue = HoldTime;
            serialized.FindProperty("fragility").floatValue = info.fragility;
            serialized.FindProperty("playerStunSeconds").floatValue = info.playerStun;
            serialized.FindProperty("enemyStunSeconds").floatValue = info.enemyStun;
            serialized.FindProperty("heldLightIntensity").floatValue = info.lightIntensity;
            serialized.FindProperty("heldLightRange").floatValue = info.lightRange;
            serialized.FindProperty("heldLightAngle").floatValue = info.lightAngle;
        }
        serialized.FindProperty("itemIcon").objectReferenceValue = icon;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = BuildPrefab(info, mesh, material, data, itemsLayer);
        serialized.Update();
        serialized.FindProperty("itemPrefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        return data;
    }

    private static Mesh BuildMesh(ScrapInfo info)
    {
        int count = info.vertices.Length / 3;
        Vector3[] vertices = new Vector3[count];
        Vector3[] normals = new Vector3[count];
        Vector2[] uvs = new Vector2[count];
        int[] triangles = new int[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(info.vertices[i * 3], info.vertices[i * 3 + 1], info.vertices[i * 3 + 2]);
            normals[i] = new Vector3(info.normals[i * 3], info.normals[i * 3 + 1], info.normals[i * 3 + 2]);
            uvs[i] = new Vector2(info.uvs[i * 2], info.uvs[i * 2 + 1]);
            triangles[i] = i;
        }

        Mesh mesh = new Mesh { name = info.id };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static GameObject BuildPrefab(ScrapInfo info, Mesh mesh, Material material, ItemDataAsset data, int itemsLayer)
    {
        GameObject obj = new GameObject($"Scrap_{info.id}");
        try
        {
            obj.layer = itemsLayer;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;

            Bounds bounds = mesh.bounds;
            if (bounds.size.y < MinThickness)
            {
                // 薄い物：下の面は見た目に合わせ、上へ厚みを足した箱にする
                BoxCollider box = obj.AddComponent<BoxCollider>();
                box.size = new Vector3(bounds.size.x, MinThickness, bounds.size.z);
                box.center = new Vector3(bounds.center.x, bounds.min.y + MinThickness * 0.5f, bounds.center.z);
            }
            else
            {
                MeshCollider collider = obj.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
            }

            Rigidbody body = obj.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.1f, info.weight);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Item item = obj.AddComponent<Item>();
            SerializedObject serialized = new SerializedObject(item);
            serialized.FindProperty("itemData").objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return PrefabUtility.SaveAsPrefabAsset(obj, $"{PrefabDir}/Scrap_{info.id}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(obj);
        }
    }

    private static Sprite LoadIcon(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning(Tag + $"アイコンがありません: {path}");
            return null;
        }

        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void RegisterToScene(List<ItemDataAsset> items)
    {
        Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool wasDirty = scene.isDirty;

        try
        {
            MapGenerator generator = scene.GetRootGameObjects()
                .Select(root => root.GetComponentInChildren<MapGenerator>(true))
                .FirstOrDefault(found => found != null);
            if (generator == null)
            {
                Debug.LogError(Tag + "SampleScene に MapGenerator が見つかりません。");
                return;
            }

            SerializedObject serialized = new SerializedObject(generator);
            SerializedProperty list = serialized.FindProperty("scrapDataList");
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);

            if (wasDirty)
                Debug.LogWarning(Tag + "SampleScene に未保存の変更があったため、保存していません。Ctrl+S で保存してください。");
            else
                EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>メッシュをアセットとして保存する。すでにあれば、参照が切れないように中身だけ入れ替える</summary>
    private static Mesh SaveMesh(Mesh mesh)
    {
        string path = $"{MeshDir}/{mesh.name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
