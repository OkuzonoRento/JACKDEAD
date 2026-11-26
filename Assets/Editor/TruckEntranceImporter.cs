using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// トラックにテクスチャを貼り、トラックとマップの間を「旅館の玄関前の通路」に差し替える（Truck.prefab を書き換える）。
/// ・Truck.blend は変えない。TruckMesh と Lever は、同じ形のまま UV と材質を付けたメッシュに差し替える
/// ・元のつなぎ部分（Plane）は非表示にして、Assets/Models/TruckEntrance/approach.json の通路を「Approach」として足す
/// まだ作っていなければ、スクリプトの読み込み時に自動で一度だけ実行する。作り直すときはメニューから。
/// 元に戻すときは、Approach を消して Plane を表示し、TruckMesh と Lever のメッシュと材質を Truck.blend のものへ戻す。
/// 通路のモデルとテクスチャは Docs/MapPrototype/Truck/build_truck.py で作る。
/// </summary>
[InitializeOnLoad]
public static class TruckEntranceImporter
{
    private const string Root = "Assets/Models/TruckEntrance";
    private const string TextureDir = Root + "/textures";
    private const string MaterialDir = Root + "/Materials";
    private const string MeshDir = Root + "/Generated";
    private const string JsonPath = Root + "/approach.json";
    private const string RyokanTextureDir = "Assets/Models/Map/Ryokan/textures";
    private const string RyokanMaterialDir = "Assets/Models/Map/Ryokan/Materials";
    private const string PrefabPath = "Assets/Prefabs/Truck.prefab";
    private const string BlendPath = "Assets/Models/Truck.blend";
    private const string ApproachName = "Approach";
    private const string Tag = "[TruckEntranceImporter] ";

    // TruckMesh の材質の順番（サブメッシュの順）
    private static readonly string[] TruckMaterials = { "Truck_Metal", "Truck_Floor", "Truck_Board" };

    [System.Serializable]
    private class ApproachData { public MeshData[] meshes; public LightData[] lights; }

    [System.Serializable]
    private class MeshData { public string name; public PartData[] parts; }

    [System.Serializable]
    private class PartData { public string material; public float[] vertices; public float[] uvs; public int[] triangles; }

    [System.Serializable]
    private class LightData { public float x, y, z, intensity, range; } // 位置はトラックの中の座標（m）

    static TruckEntranceImporter()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Map/トラックと玄関前の通路を作り直す")]
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

        // 作成済みなら、自動では何もしない（手で変えた内容を上書きしない）
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null) return;
        if (!force && asset.transform.Find(ApproachName) != null) return;

        AssetDatabase.Refresh();
        ApproachData data = JsonUtility.FromJson<ApproachData>(File.ReadAllText(JsonPath));
        if (data == null || data.meshes == null || data.meshes.Length == 0)
        {
            Debug.LogError(Tag + "approach.json を読めませんでした。");
            return;
        }

        Dictionary<string, Material> materials = BuildMaterials(data);
        if (materials == null) return;

        EnsureFolder(MeshDir);
        GameObject truck = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!TextureTruck(truck.transform, materials)) return;
            if (!BuildApproach(truck.transform, data, materials)) return;
            PrefabUtility.SaveAsPrefabAsset(truck, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(Tag + "完了：トラックにテクスチャを貼り、玄関前の通路を作りました。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(truck);
        }
    }

    // ───────── マテリアル ─────────

    private static Dictionary<string, Material> BuildMaterials(ApproachData data)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError(Tag + "URP の Lit シェーダーが見つかりません。");
            return null;
        }

        IEnumerable<string> names = TruckMaterials.Concat(new[] { "Truck_Red" })
            .Concat(data.meshes.Where(m => m.name == "Approach_Visual").SelectMany(m => m.parts).Select(p => p.material))
            .Distinct();

        EnsureFolder(MaterialDir);
        Dictionary<string, Material> result = new Dictionary<string, Material>();
        foreach (string name in names)
        {
            // 旅館マップと同じ素材は、旅館マップのマテリアルをそのまま使う
            if (name.StartsWith("Ryokan_"))
            {
                Material shared = AssetDatabase.LoadAssetAtPath<Material>($"{RyokanMaterialDir}/{name}.mat");
                if (shared == null)
                {
                    Debug.LogError(Tag + $"旅館マップのマテリアルがありません: {name}（先に「旅館マップを取り込む」を実行してください。テクスチャは {RyokanTextureDir}）");
                    return null;
                }
                result[name] = shared;
                continue;
            }

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
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            if (name == "Entrance_Lantern")
            {
                // 提灯と電球は、暗い中でも光って見えるようにする
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", texture);
                material.SetColor("_EmissionColor", new Color(1.4f, 1.0f, 0.6f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(material);
            result[name] = material;
        }
        return result;
    }

    // ───────── トラック ─────────

    private static bool TextureTruck(Transform root, Dictionary<string, Material> materials)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BlendPath);
        if (model == null)
        {
            Debug.LogError(Tag + "Truck.blend を読めません。");
            return false;
        }

        foreach (string partName in new[] { "TruckMesh", "Lever" })
        {
            MeshFilter target = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == partName);
            MeshFilter source = model.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == partName);
            if (target == null || source == null || source.sharedMesh == null)
            {
                Debug.LogError(Tag + $"トラックの部品が見つかりません: {partName}");
                return false;
            }

            bool isLever = partName == "Lever";
            string[] slots = isLever ? new[] { "Truck_Red" } : TruckMaterials;
            Mesh mesh = BuildTexturedMesh(root, target.transform, source.sharedMesh, slots, partName + "_Textured");
            if (mesh == null) return false;

            // 当たり判定のメッシュは、元のまま使う（形は同じ）
            target.sharedMesh = SaveMesh(mesh);
            target.GetComponent<MeshRenderer>().sharedMaterials = slots.Select(n => materials[n]).ToArray();
        }
        return true;
    }

    /// <summary>
    /// 元のメッシュの三角形ごとに、場所と向きで材質を決め、メートル基準の UV を付ける。
    /// 決まりは build_truck.py の確認画像と同じ。座標はトラックの中の位置（m）で、X はトラックの奥が +
    /// </summary>
    private static Mesh BuildTexturedMesh(Transform root, Transform part, Mesh source, string[] slots, string meshName)
    {
        Vector3[] srcVertices = source.vertices;
        Vector3[] srcNormals = source.normals;
        int[] srcTriangles = source.triangles;

        Vector3[] inTruck = new Vector3[srcVertices.Length];
        for (int i = 0; i < srcVertices.Length; i++)
            inTruck[i] = Vector3.Scale(root.InverseTransformPoint(part.TransformPoint(srcVertices[i])), root.localScale);

        if (slots.Length > 1)
        {
            Bounds bounds = new Bounds(inTruck[0], Vector3.zero);
            foreach (Vector3 p in inTruck) bounds.Encapsulate(p);
            if (bounds.min.x < -0.2f || bounds.max.x > 10.6f || bounds.min.y < -0.2f || bounds.max.y > 4.6f ||
                bounds.min.z < -2.8f || bounds.max.z > 2.8f || bounds.size.x < 9f)
            {
                Debug.LogError(Tag + $"トラックの大きさか向きが想定と違います: min={bounds.min} max={bounds.max}");
                return null;
            }
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int>[] submeshes = slots.Select(_ => new List<int>()).ToArray();
        int quotaBoard = 0, levelBoard = 0;

        for (int t = 0; t < srcTriangles.Length; t += 3)
        {
            Vector3 a = inTruck[srcTriangles[t]], b = inTruck[srcTriangles[t + 1]], c = inTruck[srcTriangles[t + 2]];
            Vector3 center = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

            int slot = 0;
            bool onBoard = false;
            Vector2 boardMin = Vector2.zero, boardMax = Vector2.one; // x: 横（Z 座標）、y: 高さ
            if (slots.Length > 1)
            {
                if (normal.x < -0.8f && Mathf.Abs(center.x - 9.58f) < 0.05f && center.y > 1.45f && center.y < 3.05f &&
                    center.z > -1.25f && center.z < 2.5f)
                {
                    // ノルマ・収入・残額を出す板（黒い文字が読めるよう、明るい板にする）
                    slot = 2; onBoard = true; quotaBoard++;
                    boardMin = new Vector2(-1.2f, 1.49f); boardMax = new Vector2(2.48f, 3.03f);
                }
                else if (Vector3.Dot(normal, new Vector3(0.91f, 0f, -0.42f)) > 0.95f && center.x > 1.34f && center.x < 1.76f &&
                         center.y > 2.58f && center.y < 3.10f && center.z > 1.45f && center.z < 2.45f)
                {
                    // レベルを出す板
                    slot = 2; onBoard = true; levelBoard++;
                    boardMin = new Vector2(1.40f, 2.55f); boardMax = new Vector2(2.50f, 3.13f);
                }
                else if (normal.y > 0.7f && center.y < 1.35f) slot = 1; // 床と台の上
            }

            for (int k = 0; k < 3; k++)
            {
                int index = srcTriangles[t + k];
                Vector3 p = inTruck[index];
                Vector2 uv;
                if (onBoard)
                {
                    float side = Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? p.z : p.x;
                    uv = new Vector2((side - boardMin.x) / (boardMax.x - boardMin.x), (p.y - boardMin.y) / (boardMax.y - boardMin.y));
                }
                else
                {
                    float tile = slots.Length > 1 ? 2.5f : 0.6f;
                    float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
                    if (ax >= ay && ax >= az) uv = new Vector2(p.z, p.y) / tile;
                    else if (ay >= az) uv = new Vector2(p.x, p.z) / tile;
                    else uv = new Vector2(p.x, p.y) / tile;
                }

                submeshes[slot].Add(vertices.Count);
                vertices.Add(srcVertices[index]);
                normals.Add(srcNormals[index]);
                uvs.Add(uv);
            }
        }

        if (slots.Length > 1 && (quotaBoard == 0 || levelBoard == 0))
            Debug.LogWarning(Tag + $"文字の後ろの板が見つかりません（金額の板 {quotaBoard} 面、レベルの板 {levelBoard} 面）。文字が読みにくくないか確認してください。");

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = submeshes.Length;
        for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ───────── 玄関前の通路 ─────────

    private static bool BuildApproach(Transform root, ApproachData data, Dictionary<string, Material> materials)
    {
        MeshData visual = data.meshes.FirstOrDefault(m => m.name == "Approach_Visual");
        MeshData collision = data.meshes.FirstOrDefault(m => m.name == "Approach_Collision");
        if (visual == null || collision == null)
        {
            Debug.LogError(Tag + "approach.json に通路のメッシュがありません。");
            return false;
        }

        // 元のつなぎ部分は、消さずに非表示にする
        Transform oldPlane = root.Find("Plane");
        if (oldPlane != null) oldPlane.gameObject.SetActive(false);
        else Debug.LogWarning(Tag + "元のつなぎ部分（Plane）が見つかりません。");

        Transform old = root.Find(ApproachName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject approach = new GameObject(ApproachName);
        approach.transform.SetParent(root, false);
        approach.transform.localScale = Vector3.one / root.localScale.x; // この下は、1 = 1m

        GameObject body = new GameObject("Visual");
        body.transform.SetParent(approach.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BuildPartsMesh(visual, true));
        body.AddComponent<MeshRenderer>().sharedMaterials = visual.parts.Select(p => materials[p.material]).ToArray();

        // 当たり判定は、床・左右の壁・天井・トラック側の壁だけ（見えない。飾りには付けない）
        GameObject wall = new GameObject("Collision");
        wall.transform.SetParent(approach.transform, false);
        wall.AddComponent<MeshCollider>().sharedMesh = SaveMesh(BuildPartsMesh(collision, false));

        if (data.lights != null)
        {
            for (int i = 0; i < data.lights.Length; i++)
            {
                LightData info = data.lights[i];
                GameObject obj = new GameObject($"Light_{i + 1}");
                obj.transform.SetParent(approach.transform, false);
                obj.transform.localPosition = new Vector3(info.x, info.y, info.z);

                Light light = obj.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = new Color(1f, 0.74f, 0.44f);
                light.intensity = info.intensity;
                light.range = info.range;
            }
        }
        return true;
    }

    /// <summary>材質ごとの部品をまとめて、1つのメッシュにする。useSubmeshes なら材質ごとのサブメッシュに分ける</summary>
    private static Mesh BuildPartsMesh(MeshData data, bool useSubmeshes)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<List<int>> submeshes = new List<List<int>>();

        foreach (PartData part in data.parts)
        {
            if (useSubmeshes || submeshes.Count == 0) submeshes.Add(new List<int>());
            int offset = vertices.Count;
            for (int i = 0; i < part.vertices.Length; i += 3)
                vertices.Add(new Vector3(part.vertices[i], part.vertices[i + 1], part.vertices[i + 2]));
            for (int i = 0; i < part.uvs.Length; i += 2)
                uvs.Add(new Vector2(part.uvs[i], part.uvs[i + 1]));
            foreach (int index in part.triangles) submeshes[submeshes.Count - 1].Add(offset + index);
        }

        Mesh mesh = new Mesh { name = data.name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
        mesh.RecalculateNormals(); // 面ごとに頂点が分かれているので、角は立ったままになる
        mesh.RecalculateBounds();
        return mesh;
    }

    // ───────── 保存 ─────────

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
