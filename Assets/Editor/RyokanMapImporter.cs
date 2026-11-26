using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 旅館のマップチップ（8形状×3パターン）を取り込む。
/// Assets/Models/Map/Ryokan の FBX とテクスチャから、マテリアル・プレハブ（当たり判定と明かり付き）・MapChipData を作り、
/// SampleScene の MapGenerator の allMapChips を、この 24 個に差し替える。
/// まだ作っていなければ、スクリプトの読み込み時に自動で一度だけ実行する。作り直すときはメニューから。
/// 元の仮置きチップ（Assets/Data/Map/01〜08_MapChips）は残すので、allMapChips に戻せば元通りになる。
/// </summary>
[InitializeOnLoad]
public static class RyokanMapImporter
{
    private const string Root = "Assets/Models/Map/Ryokan";
    private const string FbxDir = Root + "/fbx";
    private const string TextureDir = Root + "/textures";
    private const string MaterialDir = Root + "/Materials";
    private const string JsonPath = Root + "/rooms.json";
    private const string PrefabDir = "Assets/Prefabs/Map/Ryokan";
    private const string DataDir = "Assets/Data/Map/Ryokan";
    private const string BaseDataDir = "Assets/Data/Map";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string Tag = "[RyokanMapImporter] ";

    private static readonly string[] MaterialNames =
    {
        "Ryokan_Tatami", "Ryokan_Plank", "Ryokan_Tile", "Ryokan_Soil",
        "Ryokan_Plaster", "Ryokan_WoodWall", "Ryokan_TileWall", "Ryokan_Ceiling", "Ryokan_Props",
    };

    [System.Serializable]
    private class RoomList { public RoomInfo[] rooms; }

    [System.Serializable]
    private class RoomInfo
    {
        public string name;
        public string title;
        public string chip;
        public bool top, bottom, left, right;
        public LightInfo[] lights;
    }

    [System.Serializable]
    private class LightInfo
    {
        public float x, y, z; // Blender 上の位置（m）。Z が上
        public string kind;   // warm / red / fill
    }

    static RyokanMapImporter()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Map/旅館マップを取り込む（作り直す）")]
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

        RoomList list = JsonUtility.FromJson<RoomList>(File.ReadAllText(JsonPath));
        if (list == null || list.rooms == null || list.rooms.Length == 0)
        {
            Debug.LogError(Tag + "rooms.json を読めませんでした。");
            return;
        }

        // 作成済みなら、自動では何もしない（手で変えた内容を上書きしない）
        string lastData = $"{DataDir}/{list.rooms[list.rooms.Length - 1].name}.asset";
        if (!force && AssetDatabase.LoadAssetAtPath<MapChipData>(lastData) != null) return;

        AssetDatabase.Refresh();
        Dictionary<string, Material> materials = BuildMaterials();
        if (materials == null) return;

        Dictionary<string, MapChipData> baseData = LoadBaseData();
        EnsureFolder(PrefabDir);
        EnsureFolder(DataDir);

        List<MapChipData> chips = new List<MapChipData>();
        int problems = 0;
        foreach (RoomInfo room in list.rooms)
        {
            if (!baseData.TryGetValue(room.chip, out MapChipData source))
            {
                Debug.LogError(Tag + $"元の MapChipData が見つかりません: {room.chip}");
                return;
            }
            if (source.hasTopDoor != room.top || source.hasBottomDoor != room.bottom ||
                source.hasLeftDoor != room.left || source.hasRightDoor != room.right)
            {
                Debug.LogError(Tag + $"扉の向きが元の設定と違います: {room.name}");
                problems++;
            }

            GameObject prefab = BuildPrefab(room, materials, ref problems);
            if (prefab == null) return;

            string dataPath = $"{DataDir}/{room.name}.asset";
            MapChipData data = AssetDatabase.LoadAssetAtPath<MapChipData>(dataPath);
            bool isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<MapChipData>();
            data.chipName = room.title;
            data.chipPrefab = prefab;
            data.hasTopDoor = source.hasTopDoor;
            data.hasBottomDoor = source.hasBottomDoor;
            data.hasLeftDoor = source.hasLeftDoor;
            data.hasRightDoor = source.hasRightDoor;
            data.allowRotation = source.allowRotation;
            if (isNew) AssetDatabase.CreateAsset(data, dataPath);
            else EditorUtility.SetDirty(data);
            chips.Add(data);
        }

        AssetDatabase.SaveAssets();
        RegisterToScene(chips);
        Debug.Log(Tag + $"完了：{chips.Count} 部屋を取り込みました（問題 {problems} 件）。");
    }

    // ───────── マテリアル ─────────

    private static Dictionary<string, Material> BuildMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError(Tag + "URP の Lit シェーダーが見つかりません。");
            return null;
        }

        EnsureFolder(MaterialDir);
        Dictionary<string, Material> result = new Dictionary<string, Material>();
        foreach (string name in MaterialNames)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{name}.png");
            if (texture == null)
            {
                Debug.LogError(Tag + $"テクスチャがありません: {name}.png");
                return null;
            }

            string path = $"{MaterialDir}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white); // 床の判定は、この色の明るさを見る
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            result[name] = material;
        }
        return result;
    }

    // ───────── プレハブ ─────────

    private static GameObject BuildPrefab(RoomInfo room, Dictionary<string, Material> materials, ref int problems)
    {
        string fbxPath = $"{FbxDir}/{room.name}.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError(Tag + $"FBX がありません: {fbxPath}");
            return null;
        }

        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.isReadable = true;        // 実行中の NavMesh 作成で、当たり判定のメッシュを読むため
        importer.addCollider = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        foreach (KeyValuePair<string, Material> pair in materials)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
        importer.SaveAndReimport();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
            instance.transform.localScale = Vector3.one * 10f;

            MeshFilter body = null;
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.EndsWith("_Deco")) { body = filter; break; }
            }
            if (body == null)
            {
                Debug.LogError(Tag + $"本体のメッシュがありません: {room.name}");
                return null;
            }

            // 当たり判定は本体だけ（飾りには付けない。低い飾りに付けると、床の高さを誤って判定する）
            MeshCollider collider = body.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = body.sharedMesh;

            Physics.SyncTransforms();
            Verify(room, body, ref problems);
            AddLights(room, instance.transform);

            return PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabDir}/{room.name}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // 大きさ（20m 四方・高さ 6m）と、向きを確かめる
    private static void Verify(RoomInfo room, MeshFilter body, ref int problems)
    {
        Bounds bounds = body.GetComponent<Renderer>().bounds;
        Vector3 min = bounds.min, max = bounds.max;
        if (min.x < -10.05f || max.x > 10.05f || min.z < -10.05f || max.z > 10.05f || min.y < -0.05f || Mathf.Abs(max.y - 6f) > 0.05f)
        {
            Debug.LogError(Tag + $"大きさが想定と違います: {room.name} min={min} max={max}");
            problems++;
        }

        // 左右非対称の形で、向きを確かめる。
        // -O（行き止まり）は右（+X）に中央の張り出しだけ。_（曲がり角）は上（+Z）と左（-X）にしか伸びない
        if (room.chip != "-O" && room.chip != "_") return;
        foreach (Vector3 vertex in body.sharedMesh.vertices)
        {
            Vector3 world = body.transform.TransformPoint(vertex);
            bool outside = room.chip == "-O"
                ? Mathf.Abs(world.z) > 5f && world.x > 3.4f
                : world.z < -3.4f || world.x > 3.4f;
            if (outside)
            {
                Debug.LogError(Tag + $"向きが想定と違います: {room.name}");
                problems++;
                return;
            }
        }
    }

    // 提灯の下と、暗くなりすぎる所に、影を落とさない弱い明かりを置く
    private static void AddLights(RoomInfo room, Transform root)
    {
        if (room.lights == null) return;

        for (int i = 0; i < room.lights.Length; i++)
        {
            LightInfo info = room.lights[i];
            GameObject obj = new GameObject($"Light_{i + 1}");
            obj.transform.SetParent(root, false);
            // 親は 10 倍なので 0.1 倍。Blender の X は、ゲームのメッシュでは反対向き
            obj.transform.localPosition = new Vector3(-info.x, info.y, info.z) * 0.1f;

            Light light = obj.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            switch (info.kind)
            {
                case "red":
                    light.color = new Color(1f, 0.28f, 0.18f);
                    light.intensity = 6f;
                    light.range = 9f;
                    break;
                case "fill":
                    light.color = new Color(0.55f, 0.65f, 0.85f);
                    light.intensity = 3f;
                    light.range = 9f;
                    break;
                default:
                    light.color = new Color(1f, 0.74f, 0.44f);
                    light.intensity = 7f;
                    light.range = 10f;
                    break;
            }
        }
    }

    // ───────── データとシーン ─────────

    /// <summary>仮置きチップの MapChipData を、形（プレハブ名の先頭）ごとに引けるようにする</summary>
    private static Dictionary<string, MapChipData> LoadBaseData()
    {
        Dictionary<string, MapChipData> result = new Dictionary<string, MapChipData>();
        foreach (string guid in AssetDatabase.FindAssets("t:MapChipData", new[] { BaseDataDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(DataDir + "/")) continue;

            MapChipData data = AssetDatabase.LoadAssetAtPath<MapChipData>(path);
            if (data == null || data.chipPrefab == null) continue;

            // "+_MapChips" → "+"、"__MapChips" → "_"
            string prefabName = data.chipPrefab.name;
            int end = prefabName.LastIndexOf("_MapChips", System.StringComparison.Ordinal);
            if (end > 0) result[prefabName.Substring(0, end)] = data;
        }
        return result;
    }

    private static void RegisterToScene(List<MapChipData> chips)
    {
        Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool wasDirty = scene.isDirty;

        try
        {
            MapGenerator generator = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                generator = root.GetComponentInChildren<MapGenerator>(true);
                if (generator != null) break;
            }
            if (generator == null)
            {
                Debug.LogError(Tag + "SampleScene に MapGenerator が見つかりません。");
                return;
            }

            Undo.RecordObject(generator, "旅館マップを登録");
            generator.allMapChips = new List<MapChipData>(chips);
            EditorUtility.SetDirty(generator);
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
