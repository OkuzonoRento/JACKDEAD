using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// PV の場面づくりと進行。Evaluate(秒) を呼ぶと、その時刻のカメラ・光・役者・テロップになる（時刻だけで決まるので、どこからでも描ける）。
/// 場面の秒数は PVAudio の音の時刻とそろえている。映すのは、実装済みの要素だけ。
/// </summary>
public class PVDirector : IDisposable
{
    public const float Length = 60f;

    private const int MapLevel = 8;
    private const int MapSeed = 20261126;
    private static readonly Vector3 StagePosition = new Vector3(0f, 600f, 0f); // タイトル用の何もない舞台

    private readonly PVComposer ui;
    private readonly List<Object> owned = new List<Object>();

    private MapGenerator generator;
    private List<MapGenerator.GeneratedRoom> rooms;
    private readonly List<Transform> roomObjects = new List<Transform>();
    private readonly List<Vector3> roomHomes = new List<Vector3>();
    private readonly List<GameObject> mapObjects = new List<GameObject>();
    private Bounds mapBounds;
    private Transform truck;

    private Camera cam;
    public Camera SceneCamera => cam;

    private Light moon, torch, lampA, lampB, lampC;
    private readonly List<Light> crewTorches = new List<Light>();

    private PVSlot titleSlot, mapSlot;
    private float exploreStart, pickAt, silenceStart, chaseStart, crewStart, caughtStart;
    private PVActor santa;
    private GameObject box;
    private Vector3 boxCenter;       // 原点から見た目の中心まで（回転していないとき）
    private GameObject loot;
    private Vector3 lootPosition;
    private readonly List<Transform> crew = new List<Transform>();

    private PVPath path;
    private string truckTexts = "";
    private Material glowMaterial;
    private Vector3 lootScale = Vector3.one;
    private PVSlotWin slotWin;
    private Transform quotaBoard;
    private int lootValue = 1200;

    public string Summary { get; private set; } = "";

    public PVDirector(PVComposer composer) { ui = composer; }

    // ═════════ 準備 ═════════

    public void Build()
    {
        generator = Object.FindAnyObjectByType<MapGenerator>();
        if (generator == null) throw new Exception("SampleScene に MapGenerator がありません。");

        // ゲーム画面の UI・カメラ・光・後処理は使わない
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
        foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c.name != "PVComposerCamera") c.enabled = false;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;

        BuildMap();
        BuildCameraAndLights();
        BuildActors();
        Physics.SyncTransforms();
        BuildPath();
        BuildSlotWin();
        SetPlaceholderTexts();
        Summary = $"rooms={rooms.Count} slots={generator.GetSpawnedSlots().Count} path={path.Total:F1}m bounds={mapBounds.size} spawn={generator.PlayerSpawnPoint} truck={(truck != null ? truck.position + " bounds=" + BoundsOf(truck.gameObject) + " renderers=" + truck.GetComponentsInChildren<Renderer>(false).Length : "なし")}\nloot={lootPosition} p0={path.At(path.Total * 0.42f)} truckTexts: {truckTexts}\nsantaArm={santa.HasArm}";
    }

    /// <summary>プレハブの仮の文字（再生中にゲームが書き換える所）を、映像用の値にする</summary>
    private void SetPlaceholderTexts()
    {
        List<string> found = new List<string>();
        foreach (TMPro.TMP_Text text in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            if (truck == null || !text.transform.IsChildOf(truck)) continue;
            // ゲームと同じ形（DisplayTextManager の「$000000」）で入れる。桁が増えても折り返さない
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            if (text.name == "QuotaText") { text.text = $"${generator.CurrentMapQuota:D6}"; quotaBoard = text.transform; }
            else if (text.name == "IncomeText") text.text = $"${0:D6}";
            else if (text.name == "RemainingText") { text.text = $"${generator.TotalDebt:D6}"; text.fontSize *= 0.8f; }
            else if (text.name == "MapLevel") text.text = MapLevel.ToString();
            found.Add(text.name + " pos=" + text.transform.position + " fwd=" + text.transform.forward);
        }
        truckTexts = string.Join(", ", found);

        foreach (SlotMachineController slot in Object.FindObjectsByType<SlotMachineController>(FindObjectsSortMode.None))
        {
            TMPro.TMP_Text[] bets = slot.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(x => x.text.Contains("123456")).ToArray();
            for (int i = 0; i < bets.Length; i++) bets[i].text = "$" + generator.ScaleToCurrentPrice(new[] { 100, 500, 1000 }[i % 3]).ToString("N0");
            Transform label = slot.transform.Find("RarityLabel");
            if (label != null) label.gameObject.SetActive(false);
        }
    }

    private void BuildMap()
    {
        UnityEngine.Random.InitState(MapSeed);
        SetField(generator, "doorBlockerMesh", BuildDoorMesh());
        SetField(generator, "currentStageLevel", MapLevel);
        Call(generator, "RollQuotaForCurrentLevel");

        bool made = false;
        int target = generator.GetRoomCountForLevel(MapLevel);
        for (int attempt = 0; attempt < 200 && !made; attempt++)
        {
            for (int i = generator.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(generator.transform.GetChild(i).gameObject);
            SetField(generator, "currentTargetRoomCount", target);
            made = (bool)Call(generator, "TryGenerateMapOnce");
        }
        if (!made) throw new Exception("マップを生成できませんでした。");

        rooms = generator.GetSpawnedRooms();
        foreach (MapGenerator.GeneratedRoom room in rooms)
        {
            roomObjects.Add(room.instance.transform);
            roomHomes.Add(room.instance.transform.position);
        }
        foreach (Transform child in generator.transform)
        {
            if (roomObjects.Contains(child)) continue;
            if (child.GetComponentInChildren<EnemyBrain>() != null) { child.gameObject.SetActive(false); continue; }
            if (child.name.StartsWith("Truck")) truck = child;
            mapObjects.Add(child.gameObject);
            Item item = child.GetComponent<Item>();
            if (item != null) AddGlow(child.gameObject);
        }
        bool first = true;
        foreach (Transform room in roomObjects)
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                if (first) { mapBounds = r.bounds; first = false; }
                else mapBounds.Encapsulate(r.bounds);
            }

        // マップに置かれた台にも、リールの絵を入れる
        foreach (GameObject slot in generator.GetSpawnedSlots())
        {
            PVSlot s = new PVSlot(slot, generator, owned);
            if (mapSlot == null) mapSlot = s;
        }
    }

    /// <summary>MapGenerator.BuildDoorBlockerMesh と同じ形（あちらは Destroy を使うので、編集中は呼ばない）</summary>
    private Mesh BuildDoorMesh()
    {
        const float width = 4f, height = 3.4f, thickness = 0.2f;
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh source = cube.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = Vector3.Scale(vertices[i], new Vector3(width, height, thickness));
            v.y += height * 0.5f;
            vertices[i] = v;
            float x = Mathf.Abs(normals[i].x) > 0.5f ? v.z : v.x;
            uvs[i] = new Vector2((x + width * 0.5f) / (20f / 3f), v.y / 6f);
        }
        Mesh mesh = new Mesh { name = "DoorBlocker", vertices = vertices, normals = normals, uv = uvs, triangles = source.triangles };
        mesh.RecalculateBounds();
        Object.DestroyImmediate(cube);
        owned.Add(mesh);
        return mesh;
    }

    /// <summary>落ちているアイテムの光（ゲームの ItemHighlight と同じ見た目。再生しないと付かないので、ここで足す）</summary>
    private void AddGlow(GameObject item)
    {
        Shader shader = Resources.Load<Shader>("Shaders/ItemGlow");
        if (shader == null) return;
        if (glowMaterial == null)
        {
            ItemGlowSettings g = new ItemGlowSettings();
            glowMaterial = new Material(shader);
            owned.Add(glowMaterial);
            glowMaterial.SetColor("_GlowColor", g.color);
            glowMaterial.SetFloat("_Surface", g.surface);
            glowMaterial.SetFloat("_Rim", g.edge);
            glowMaterial.SetFloat("_Glint", g.glint);
            glowMaterial.SetFloat("_GlintInterval", g.glintInterval);
            glowMaterial.SetFloat("_FadeStart", g.fadeStartDistance);
            glowMaterial.SetFloat("_FadeEnd", g.fadeEndDistance);
        }
        foreach (MeshRenderer r in item.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null) continue;
            r.sharedMaterials = r.sharedMaterials.Concat(new[] { glowMaterial }).ToArray();
        }
    }

    private void BuildCameraAndLights()
    {
        GameObject go = new GameObject("PVCamera");
        cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 800f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.004f, 0.004f, 0.008f);
        cam.cullingMask = ~ui.LayerMaskBit;
        cam.aspect = PVRenderer.Width / (float)PVRenderer.Height;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.volumeLayerMask = ~0;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        owned.Add(profile);
        Tonemapping tone = profile.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.ACES;
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 0.85f;
        bloom.intensity.value = 1.1f;
        bloom.scatter.value = 0.65f;
        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = 0.38f;
        vignette.smoothness.value = 0.5f;
        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.intensity.value = 0.25f;
        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.value = 0.25f;
        color.contrast.value = 12f;
        color.saturation.value = 4f;
        ChromaticAberration aberration = profile.Add<ChromaticAberration>(true);
        aberration.intensity.value = 0.12f;

        GameObject volumeObject = new GameObject("PVVolume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        volume.sharedProfile = profile;

        moon = MakeLight("PVMoon", LightType.Directional);
        moon.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        torch = MakeLight("PVTorch", LightType.Spot);
        lampA = MakeLight("PVLampA", LightType.Point);
        lampB = MakeLight("PVLampB", LightType.Point);
        lampC = MakeLight("PVLampC", LightType.Point);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        // 距離の霧は、この描き方では画面が真っ黒になるので使わない（暗さは、光の届く範囲で出す）
        RenderSettings.fog = false;
    }

    private static Light MakeLight(string name, LightType type)
    {
        Light l = new GameObject(name).AddComponent<Light>();
        l.type = type;
        l.enabled = false;
        l.shadows = type == LightType.Point ? LightShadows.None : LightShadows.Soft;
        return l;
    }

    private void BuildActors()
    {
        // タイトルの舞台：台1つと、暗い床
        GameObject slotPrefab = Load<GameObject>("Assets/Prefabs/Slots/SlotMachine-DEMO.prefab");
        GameObject stageSlot = (GameObject)Object.Instantiate(slotPrefab, StagePosition, slotPrefab.transform.localRotation);
        titleSlot = new PVSlot(stageSlot, generator, owned);
        titleSlot.Face(Vector3.back);

        GameObject enemyPrefab = Load<GameObject>("Assets/Prefabs/Enemy/Enemy_01.prefab");
        santa = new PVActor(enemyPrefab);

        GameObject boxPrefab = Load<GameObject>("Assets/Prefabs/Box.prefab");
        box = (GameObject)Object.Instantiate(boxPrefab);
        box.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        boxCenter = BoundsOf(box).center;

        ItemDataAsset item = Load<ItemDataAsset>("Assets/Data/Scrap/Scrap_GameConsole.asset");
        loot = (GameObject)Object.Instantiate(item.ItemPrefab);
        lootScale = loot.transform.localScale;
        lootValue = generator.GetScrapValue(item);
        AddGlow(loot);
        // 仲間（ゲームの他プレイヤーと同じ、カプセルの姿）
        Color[] colors = { new Color(0.2f, 0.6f, 1f), new Color(1f, 0.75f, 0.15f), new Color(0.35f, 0.9f, 0.4f) };
        foreach (Color c in colors)
        {
            GameObject body = new GameObject("PVCrew");
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.SetParent(body.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            capsule.GetComponent<Renderer>().sharedMaterial = Lit(c, c * 0.12f);
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.35f, 0.34f);
            nose.transform.localScale = new Vector3(0.4f, 0.14f, 0.12f);
            nose.GetComponent<Renderer>().sharedMaterial = Lit(Color.black, new Color(0.7f, 1.4f, 1.6f));
            foreach (Collider col in body.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            crew.Add(body.transform);

            Light l = MakeLight("PVCrewTorch", LightType.Spot);
            l.shadows = LightShadows.None;
            crewTorches.Add(l);
        }
    }

    private Material Lit(Color baseColor, Color emission)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        owned.Add(m);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", 0.55f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        return m;
    }

    /// <summary>出発地点から、いちばん遠い部屋まで歩く道（NavMesh の経路）。取れなければ部屋の中心をつなぐ</summary>
    /// <summary>スロットの場面で使う、当たりの演出</summary>
    private void BuildSlotWin()
    {
        PVSlot slot = mapSlot ?? titleSlot;
        SlotAnchors anchors = (SlotAnchors)Call(slot.Controller, "MeasureAnchors");
        float rarity = slot.Controller.RarityMultiplier;
        int reward = Mathf.RoundToInt(generator.ScaleToCurrentPrice(1000) * rarity * 7f);
        slotWin = new PVSlotWin(anchors, slot.ReelScrollers, reward, rarity, owned);
    }

    private void BuildPath()
    {
        Vector2Int startGrid = generator.GetBusGridPos() ?? Vector2Int.zero;
        MapGenerator.GeneratedRoom start = rooms.First(r => r.gridPos == startGrid);
        Vector3 from = new Vector3(start.worldPos.x, start.walkableFloorY, start.worldPos.z);

        List<Vector3> best = null;
        float bestLength = 0f;
        if (NavMesh.SamplePosition(from, out NavMeshHit a, 8f, NavMesh.AllAreas))
        {
            foreach (MapGenerator.GeneratedRoom room in rooms)
            {
                Vector3 to = new Vector3(room.worldPos.x, room.walkableFloorY, room.worldPos.z);
                if (!NavMesh.SamplePosition(to, out NavMeshHit b, 8f, NavMesh.AllAreas)) continue;
                NavMeshPath nav = new NavMeshPath();
                if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, nav) || nav.status != NavMeshPathStatus.PathComplete) continue;
                float length = 0f;
                for (int i = 1; i < nav.corners.Length; i++) length += Vector3.Distance(nav.corners[i - 1], nav.corners[i]);
                if (length > bestLength) { bestLength = length; best = nav.corners.ToList(); }
            }
        }

        if (best == null || bestLength < 30f)
        {
            Debug.LogWarning("[PV] NavMesh の経路が短いため、部屋の中心をつないだ道を使います。");
            best = rooms.Select(r => new Vector3(r.worldPos.x, r.walkableFloorY, r.worldPos.z)).Take(8).ToList();
        }
        path = new PVPath(AwayFromWalls(new PVPath(best)));

        Physics.SyncTransforms();
        List<Vector2> used = new List<Vector2>();
        chaseStart = FindClearSection(used, 25f, -4f, 7f);
        silenceStart = FindClearSection(used, 2f, 8.5f, 1f);
        crewStart = FindClearSection(used, 13f, 6f, 8f);
        exploreStart = FindClearSection(used, 22f, 5f, 1f);
        pickAt = FindClearSection(used, 1f, 2.5f, 4f);
        caughtStart = FindClearSection(used, 3f, 4f, 1f);

        lootPosition = path.Point(pickAt) + Vector3.up * 0.25f;

        // 道の上のスクラップは、カメラをふさぐので映さない
        foreach (GameObject o in mapObjects.ToList())
        {
            if (o.GetComponent<Item>() == null) continue;
            for (float d = 0f; d <= path.Total; d += 0.5f)
            {
                Vector3 offset = o.transform.position - path.Point(d);
                offset.y = 0f;
                Bounds size = BoundsOf(o);
                float near = Mathf.Max(size.extents.x, size.extents.z) + 0.6f;
                if (offset.sqrMagnitude > near * near) continue;
                o.SetActive(false);
                mapObjects.Remove(o);
                break;
            }
        }
    }

    /// <summary>道の点を少しずつ動かして、壁や家具から離す（カメラがめり込まないように）</summary>
    private static List<Vector3> AwayFromWalls(PVPath source)
    {
        List<Vector3> points = new List<Vector3>();
        for (float d = 0f; d < source.Total; d += 0.5f) points.Add(source.At(d));

        const float keep = 1.5f;
        for (int pass = 0; pass < 6; pass++)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 push = Vector3.zero;
                for (int a = 0; a < 12; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward;
                    foreach (float height in new[] { 0.6f, 1.5f })
                        if (Physics.Raycast(points[i] + Vector3.up * height, dir, out RaycastHit hit, keep, ~0, QueryTriggerInteraction.Ignore))
                            push -= dir * (keep - hit.distance) * 0.07f;
                }
                points[i] += Vector3.ClampMagnitude(push, 0.3f);
            }
            for (int i = 1; i < points.Count - 1; i++) points[i] = Vector3.Lerp(points[i], (points[i - 1] + points[i + 1]) * 0.5f, 0.5f);
        }
        return points;
    }

    /// <summary>
    /// 長さ length の区間のうち、視点が物にめり込まず、見る先（lead だけ先。負なら後ろ）まで見通せる所を選ぶ。
    /// used に入っている区間とは、なるべく重ねない。margin は区間の前後に空ける長さ
    /// </summary>
    private float FindClearSection(List<Vector2> used, float length, float lead, float margin)
    {
        float best = path.Total * 0.2f;
        float bestScore = float.MaxValue;
        float first = Mathf.Max(3f, margin - Mathf.Min(0f, lead));
        for (float start = first; start + length + Mathf.Max(0f, lead) + margin < path.Total; start += 1f)
        {
            float score = 0f;
            int samples = 0;
            for (float d = start; d <= start + length; d += 0.5f, samples++)
            {
                Vector3 eye = path.Point(d) + Vector3.up * 1.6f;
                if (Physics.CheckSphere(eye, 0.55f, ~0, QueryTriggerInteraction.Ignore)) score += 3f;
                for (float k = 0.5f; k <= 1f; k += 0.5f)
                    if (Physics.Linecast(eye, path.Point(d + lead * k) + Vector3.up * 1.15f, ~0, QueryTriggerInteraction.Ignore)) score += 1f;
            }
            score /= Mathf.Max(1, samples);
            foreach (Vector2 range in used)
                if (start < range.y && start + length > range.x) score += 0.6f;
            if (score < bestScore) { bestScore = score; best = start; }
        }
        used.Add(new Vector2(best - 2f, best + length + 2f));
        Debug.Log($"[PV] 区間 start={best:F0} length={length} 障害={bestScore:F2}");
        return best;
    }

    // ═════════ 進行 ═════════

    public void Evaluate(float t)
    {
        ResetWorld();
        ui.Clear();

        if (t < 6f) Opening(t);
        else if (t < 11f) MapBirth(t);
        else if (t < 15f) Arrival(t);
        else if (t < 21.5f) Explore(t);
        else if (t < 25f) PickUp(t);
        else if (t < 35f) Gamble(t);
        else if (t < 38.5f) Silence(t);
        else if (t < 46f) Chase(t);
        else if (t < 51.5f) Crew(t);
        else if (t < 55f) Caught(t);
        else Logo(t);
    }

    private void ResetWorld()
    {
        for (int i = 0; i < roomObjects.Count; i++)
        {
            roomObjects[i].position = roomHomes[i];
            roomObjects[i].gameObject.SetActive(true);
        }
        foreach (GameObject o in mapObjects) o.SetActive(true);
        santa.Hide();
        if (slotWin != null) slotWin.Hide();
        box.SetActive(false);
        loot.SetActive(false);
        foreach (Transform c in crew) c.gameObject.SetActive(false);
        foreach (Light l in crewTorches) l.enabled = false;
        moon.enabled = torch.enabled = lampA.enabled = lampB.enabled = lampC.enabled = false;
        cam.fieldOfView = 62f;
        RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.11f);
        if (mapSlot != null) mapSlot.Idle();
        titleSlot.Idle();
    }

    // 0–6秒：暗闇に浮かぶスロット台
    private void Opening(float t)
    {
        RenderSettings.ambientLight = new Color(0.012f, 0.008f, 0.02f);
        float k = Smooth(t / 6f);
        Vector3 focus = titleSlot.ReelCenter;
        Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(28f, -6f, k), 0f) * titleSlot.Front;
        Look(focus + dir * Mathf.Lerp(4.6f, 2.5f, k) + Vector3.up * Mathf.Lerp(-0.1f, 0.25f, k), focus + Vector3.up * -0.05f, 48f);

        titleSlot.Spin(t, 0.6f, 3.4f, new[] { 0, 1, 2 });
        float on = Flicker(t, 1f) * Smooth((t - 0.3f) / 1.2f);
        Vector3 right = Vector3.Cross(Vector3.up, titleSlot.Front);
        Point(lampA, focus + titleSlot.Front * 1.6f + right * 1.8f + Vector3.up * 1.2f, new Color(1f, 0.12f, 0.1f), 9f * on, 8f);
        Point(lampB, focus + titleSlot.Front * 1.4f - right * 1.9f + Vector3.up * 0.4f, new Color(1f, 0.7f, 0.2f), 7f * on, 8f);
        Point(lampC, focus - titleSlot.Front * 1.5f + Vector3.up * 1.8f, new Color(0.5f, 0.2f, 1f), 10f * on, 7f);

        ui.Fade(1f - Smooth((t - 0.2f) / 1.0f));
        ui.Caption("借金を、返せ。", t, 2.4f, 5.6f);
        if (t > 5.65f) ui.Fade((t - 5.65f) / 0.35f);
    }

    // 6–11秒：マップが組み上がる（上空から）
    private void MapBirth(float t)
    {
        float s = t - 6f;
        RenderSettings.ambientLight = new Color(0.34f, 0.32f, 0.42f);
        moon.enabled = true;
        moon.color = new Color(0.65f, 0.75f, 1f);
        moon.intensity = 2.8f;

        foreach (GameObject o in mapObjects) o.SetActive(s > 3.4f);
        for (int i = 0; i < roomObjects.Count; i++)
        {
            float begin = 0.15f + 3.0f * i / roomObjects.Count;
            float k = Mathf.Clamp01((s - begin) / 0.55f);
            roomObjects[i].gameObject.SetActive(k > 0f);
            float back = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f); // 少し行き過ぎて戻る
            roomObjects[i].position = roomHomes[i] + Vector3.down * 30f * (1f - back);
        }

        Vector3 c = mapBounds.center;
        float size = Mathf.Max(mapBounds.size.x, mapBounds.size.z);
        float angle = Mathf.Lerp(-30f, 25f, s / 5f);
        Vector3 offset = Quaternion.Euler(0f, angle + 40f, 0f) * new Vector3(0f, size * 0.85f, -size * 0.5f);
        Look(c + offset * Mathf.Lerp(1.05f, 0.82f, Smooth(s / 5f)), c, 50f);

        int newest = Mathf.Clamp(Mathf.FloorToInt((s - 0.15f) / 3f * roomObjects.Count), 0, roomObjects.Count - 1);
        Point(lampA, rooms[newest].worldPos + Vector3.up * 8f, new Color(1f, 0.55f, 0.2f), s < 3.5f ? 90f : 0f, 30f);

        ui.Fade(1f - Smooth(s / 0.4f));
        ui.Caption("入るたび、形が変わる。", t, 7.0f, 10.8f);
        if (s > 4.8f) ui.Fade((s - 4.8f) / 0.2f);
    }

    // 11–15秒：回収車の中から、現場へ
    private void Arrival(float t)
    {
        float s = t - 11f;
        Vector3 center = truck != null ? truck.position : roomHomes[0];
        Vector3 toMap = path.At(0f) - center;
        toMap.y = 0f;
        toMap.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toMap);
        float k = Smooth(s / 4f);
        Vector3 eye = center - toMap * Mathf.Lerp(7.5f, 1.5f, k) + Vector3.up * 1.75f;
        float yaw = Mathf.Lerp(-34f, 0f, Smooth((s - 1.4f) / 2.2f));
        Look(eye, eye + Quaternion.Euler(0f, yaw, 0f) * toMap * 10f + Vector3.up * 0.2f, 66f);
        if (quotaBoard != null && s < 1.9f) // 最初に、本日のノルマの表示を見せる
        {
            Vector3 board = quotaBoard.position;
            Look(board - quotaBoard.forward * Mathf.Lerp(5.2f, 3.6f, Smooth(s / 1.9f)) - Vector3.up * 0.2f, board, 52f);
            Point(lampC, board - quotaBoard.forward * 1.8f + Vector3.up * 0.9f, new Color(1f, 0.92f, 0.75f), 15f * Flicker(t, 8f), 7f);
        }

        RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.11f);
        float on = Flicker(t, 3f);
        Point(lampA, center + Vector3.up * 4.5f, new Color(1f, 0.65f, 0.32f), 38f * on, 18f);
        Point(lampB, center + toMap * 14f + Vector3.up * 2.5f, new Color(1f, 0.1f, 0.08f), 30f, 16f);
        Point(lampC, center - toMap * 2f + side * 6f + Vector3.up * 2f, new Color(0.35f, 0.4f, 1f), 12f, 12f);

        ui.Fade(1f - Smooth(s / 0.5f));
        ui.Caption("今夜も、回収の時間だ。", t, 11.4f, 14.6f);
        if (s > 3.7f) ui.Fade((s - 3.7f) / 0.3f);
    }

    // 15–21.5秒：懐中電灯で探索（一人称）
    private void Explore(float t)
    {
        float s = t - 15f;
        float d = exploreStart + 22f * s / 6.5f;
        Walk(d, s, 2.0f, 0.055f, Mathf.Sin(s * 0.9f) * 14f);
        Torch(1f);
        PathLamps(d);
        ui.Fade(1f - Smooth(s / 0.35f));
        ui.Caption("暗闇の中を探れ。", t, 16.0f, 19.0f);
    }

    // 21.5–25秒：スクラップを拾う
    private void PickUp(float t)
    {
        float s = t - 21.5f;
        float dl = pickAt;
        Vector3 back = path.Point(dl - Mathf.Lerp(3.2f, 1.7f, Smooth(s / 1.8f)));
        Vector3 eye = back + Vector3.up * Mathf.Lerp(1.5f, 1.15f, Smooth(s / 1.8f));
        float take = Smooth((s - 2.0f) / 0.45f);
        Look(eye, lootPosition + Vector3.up * take * 0.4f, 58f);

        loot.SetActive(s < 2.45f);
        Vector3 hand = cam.transform.position + cam.transform.forward * 0.55f - cam.transform.up * 0.32f + cam.transform.right * 0.18f;
        loot.transform.position = Vector3.Lerp(lootPosition, hand, take);
        loot.transform.rotation = Quaternion.Euler(20f, s * 110f, 12f);
        loot.transform.localScale = lootScale * Mathf.Lerp(1.25f, 0.45f, take);

        Torch(0.22f);
        Point(lampA, lootPosition + Vector3.up * 0.9f, new Color(1f, 0.75f, 0.25f), s < 2.45f ? 1.2f + 0.3f * Mathf.Sin(s * 8f) : 0f, 4f);
        PathLamps(dl, true);

        if (s > 2.45f)
        {
            float a = Mathf.Clamp01((3.5f - s) / 0.3f);
            float rise = Smooth((s - 2.45f) / 0.5f);
            ui.Flash(new Color(1f, 0.8f, 0.3f), 0.25f * Mathf.Clamp01(1f - (s - 2.45f) / 0.25f));
            ui.Text($"+${lootValue:N0}", new Vector2(0f, Mathf.Lerp(-40f, 110f, rise)), 130f, new Color(1f, 0.82f, 0.25f, a), 4f);
        }
        ui.Caption("拾え。稼げ。", t, 22.2f, 24.9f);
    }

    // 25–35秒：スロット（ボタン → 回る → 777 → ゲームと同じ JACKPOT の演出）
    private void Gamble(float t)
    {
        float s = t - 25f;
        PVSlot slot = mapSlot ?? titleSlot;
        Vector3 focus = slot.ReelCenter;
        float approach = Smooth(s / 1.8f);
        float push = Smooth((s - 3.2f) / 1.8f);
        float pull = Smooth((s - 5.0f - PVSlotWin.FinaleStart) / 0.5f); // 大当たりで、少し引いて全体を見せる
        float distance = Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(3.2f, 2.1f, approach), 1.65f, push), 2.25f, pull);
        Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(22f, 0f, approach), 0f) * slot.Front;
        float w = s - 5.0f - PVSlotWin.FinaleStart;
        float shake = w >= 0f ? 0.025f * Mathf.Clamp01(1f - w / 1.2f) : 0f;
        Look(focus + dir * distance + Vector3.up * 0.14f + Shake(t, shake), focus + Vector3.up * -0.06f, 52f);

        // 2.35秒に回り出して、3.7 / 4.35 / 5.0秒に左から止まる
        slot.Spin(s, 2.35f, 5.0f, new[] { 0, 0, 0 });
        if (s >= 5.0f) slotWin.Show(s - 5.0f);

        RenderSettings.ambientLight = new Color(0.05f, 0.035f, 0.06f);
        Vector3 right = Vector3.Cross(Vector3.up, slot.Front);
        Point(lampA, focus + slot.Front * 1.3f + right * 1.2f + Vector3.up * 0.9f, new Color(1f, 0.15f, 0.12f), 3.5f * Flicker(t, 5f), 6f);
        Point(lampB, focus + slot.Front * 1.3f - right * 1.2f + Vector3.up * 0.7f, new Color(1f, 0.75f, 0.2f), 3f, 6f);
        Point(lampC, focus + slot.Front * 0.8f + Vector3.up * 1.3f, new Color(0.9f, 0.9f, 1f), 1.2f, 4f);

        ui.Fade(1f - Smooth(s / 0.35f));
        ui.Caption("足りなければ、賭けろ。", t, 25.7f, 29.3f);
        if (w >= 0f) ui.Flash(new Color(1f, 0.75f, 0.85f), 0.6f * Mathf.Clamp01(1f - w / 0.35f));
        if (s > 9.75f) ui.Fade((s - 9.75f) / 0.25f);
    }
    // 35–38.5秒：静寂。通路の奥に立つサンタ
    private void Silence(float t)
    {
        float s = t - 35f;
        float d = silenceStart + s * 0.45f;
        Walk(d, s, 0.8f, 0.018f, 0f);
        float santaD = silenceStart + 8.5f;
        Vector3 pos = path.Point(santaD);
        Vector3 toCam = cam.transform.position - pos;
        toCam.y = 0f;
        float turn = Smooth((s - 2.0f) / 0.5f);
        Quaternion facing = Quaternion.LookRotation(toCam.normalized) * Quaternion.Euler(0f, Mathf.Lerp(180f, 0f, turn), 0f);
        santa.Show(pos, facing, PVActor.Idle, s);

        Look(cam.transform.position, pos + Vector3.up * 1.2f, Mathf.Lerp(60f, 42f, Smooth(s / 3.5f)));
        bool dark = (s > 1.45f && s < 1.7f) || (s > 1.82f && s < 1.9f);
        Torch(dark ? 0f : 0.65f);
        Point(lampA, pos + Vector3.up * 2.6f - toCam.normalized * 0.8f, new Color(1f, 0.04f, 0.03f), dark ? 0f : 14f * Flicker(t, 11f), 9f);
        ui.Fade(1f - Smooth(s / 0.5f));
        ui.Caption("音を立てるな。", t, 35.7f, 38.0f);
    }

    // 38.5–46秒：追われる → サンタが立ち止まって箱を投げる → 爆発して吹っ飛ぶ
    private void Chase(float t)
    {
        float s = t - 38.5f;
        const float stop = 3.9f;      // サンタが立ち止まる
        const float windup = 4.05f;   // 腕を振りかぶり始める
        const float release = windup + ThrowWindup; // 投げる（箱が手を離れる）
        const float hit = 5.6f;
        const float runSpeed = 4.2f, slowSpeed = 1.3f;

        // 逃げる側は、投げられる所で足がゆるむ（投げる動きが見える距離に保つ）
        float run = Mathf.Min(s, hit);
        float d = chaseStart + Mathf.Min(run, stop) * runSpeed + Mathf.Max(0f, run - stop) * slowSpeed;
        float santaD = chaseStart + Mathf.Min(run, stop) * runSpeed - Mathf.Lerp(5.8f, 3.6f, run / stop);
        float slowing = Smooth((run - stop) / 0.4f);

        float step = Mathf.Min(run, stop) * 9.5f + Mathf.Max(0f, run - stop) * 3.5f;
        Vector3 eye = path.Point(d) + Vector3.up * (1.55f + Mathf.Abs(Mathf.Sin(step)) * Mathf.Lerp(0.12f, 0.03f, slowing));
        Vector3 sp = path.Point(santaD);
        Vector3 toEye = eye - sp;
        toEye.y = 0f;
        Vector3 forward = path.Point(santaD + 1.2f) - sp;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = toEye;
        Quaternion facing = Quaternion.Slerp(Quaternion.LookRotation(forward.normalized), Quaternion.LookRotation(toEye.normalized), slowing);

        // 構えの間は体を少しひねって、投げる腕が見えるようにする
        float twist = Smooth((s - windup) / 0.35f) * (1f - Smooth((s - release) / 0.2f));
        facing *= Quaternion.Euler(0f, -38f * twist, 0f);

        // ゲームと同じ：走る → 立ち止まる（待機の姿勢）→ 右腕を振りかぶって、振り抜く
        santa.Show(sp, facing, PVActor.Run, s * 1.15f, PVActor.Idle, s, Mathf.Clamp01((s - stop) * 8f));
        santa.RotateThrowArm(ThrowArmAngle(s - windup));

        Vector3 aim = sp + Vector3.up * 1.25f;
        Look(eye + Shake(t, Mathf.Lerp(0.045f, 0.012f, slowing)), aim, Mathf.Lerp(58f, 46f, slowing));

        // 箱を投げる（出る位置は、ゲームと同じ「頭の高さの少し前」）
        if (s >= release && s < hit)
        {
            float k = (s - release) / (hit - release);
            Vector3 from = sp + Vector3.up * 1.7f + facing * Vector3.forward * 0.5f;
            Vector3 to = eye + (aim - eye).normalized * 0.7f - Vector3.up * 0.25f;
            ShowBox(Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.7f,
                Quaternion.Euler(s * 520f, s * 300f, 0f), Mathf.Sin(s * 40f) > 0f);
            Point(lampB, box.transform.position + (eye - sp).normalized * 0.5f + Vector3.up * 0.4f, new Color(1f, 0.6f, 0.35f), 3f, 4f);
        }

        Torch(0.9f);
        Point(lampA, sp + Vector3.up * 2.8f + facing * Vector3.forward * 0.6f, new Color(1f, 0.05f, 0.03f), 16f * (0.65f + 0.35f * Mathf.Sin(t * 13f)), 10f);
        PathLamps(d, true);

        if (s >= hit)
        {
            float w = s - hit;
            // 体が飛んで、視界がぐるぐる回る
            float fly = Smooth(w / 1.5f);
            Vector3 ahead = path.Point(d + 3.5f * fly);
            cam.transform.position = ahead + Vector3.up * (1.55f + Mathf.Sin(Mathf.Clamp01(w / 1.2f) * Mathf.PI) * 0.9f - Smooth((w - 0.8f) / 0.6f) * 1.2f) + Shake(t, 0.1f * Mathf.Clamp01(1f - w));
            cam.transform.rotation = cam.transform.rotation * Quaternion.Euler(-75f * Mathf.Sin(fly * Mathf.PI), 35f * fly, 300f * fly);
            cam.fieldOfView = 75f;
            Point(lampB, sp + (eye - sp) * 0.6f, new Color(1f, 0.55f, 0.15f), 120f * Mathf.Clamp01(1f - w / 0.7f), 16f);
            ui.Flash(Color.Lerp(new Color(1f, 0.9f, 0.7f), new Color(0.8f, 0.02f, 0.02f), Mathf.Clamp01(w / 0.35f)), Mathf.Max(1f - w / 0.5f, 0.45f * Mathf.Clamp01(1f - w / 1.6f)));
            if (w > 1.45f) ui.Fade((w - 1.45f) / 0.4f);
        }
        ui.Fade(s < 0.15f ? 1f - s / 0.15f : 0f);
        ui.Caption("逃げろ。", t, 39.1f, 41.4f);
    }

    // 46–51.5秒：仲間と一緒に
    private void Crew(float t)
    {
        float s = t - 46f;
        float d = crewStart + s * 2.3f;
        const float boom = 3.7f;
        const float thrown = 2.4f; // 後ろから箱が飛んできて、3人目の背中で爆発する

        for (int i = 0; i < crew.Count; i++)
        {
            float di = d + 4.5f + (i == 1 ? 1.4f : 0f) - i * 0.35f;
            Vector3 p = path.Point(di);
            Vector3 f = path.Point(di + 1f) - p;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f.normalized);
            Vector3 pos = p + right * (i - 1) * 0.95f + Vector3.up * Mathf.Abs(Mathf.Sin(s * 8f + i * 2f)) * 0.09f;
            Quaternion rot = Quaternion.LookRotation(f.normalized);

            if (i == 2 && s > boom) // 1人が吹っ飛ぶ
            {
                float w = s - boom;
                float stop = Mathf.Min(w, 1.1f);
                Vector3 start = path.Point(di - w * 2.3f) + right * 0.95f;
                pos = start + right * stop * 0.8f + f.normalized * stop * 1.6f + Vector3.up * Mathf.Max(0.35f, stop * (5.2f - 4.6f * stop));
                rot = Quaternion.Euler(stop * 430f, stop * 250f, stop * 180f) * rot;
            }

            crew[i].gameObject.SetActive(true);
            crew[i].SetPositionAndRotation(pos, rot);

            Light l = crewTorches[i];
            l.enabled = true;
            l.transform.SetPositionAndRotation(pos + Vector3.up * 1.5f + rot * Vector3.forward * 0.45f, rot * Quaternion.Euler(22f, Mathf.Sin(s * 2f + i) * 16f, 0f));
            l.color = new Color(0.95f, 0.95f, 1f);
            l.intensity = 34f;
            l.range = 16f;
            l.spotAngle = 52f;
            l.innerSpotAngle = 22f;
        }

        Vector3 eye = path.Point(d) + Vector3.up * 1.9f;
        float kick = s > boom ? 0.08f * Mathf.Clamp01(1f - (s - boom) / 0.8f) : 0f;
        Look(eye + Shake(t, 0.015f + kick), path.Point(d + 5.5f) + Vector3.up * 0.95f, 66f);
        Point(lampA, eye + Vector3.up * 0.6f, new Color(0.35f, 0.4f, 0.8f), 9f, 12f);
        Point(lampC, path.Point(d + 7f) + Vector3.up * 3f, new Color(1f, 0.15f, 0.1f), 22f, 14f);

        // 爆発の位置は、そのとき3人目がいる所の手前（毎コマ、同じ道の上で求める）
        float targetD = crewStart + Mathf.Min(s, boom) * 2.3f + 4.5f - 2 * 0.35f;
        Vector3 targetForward = path.Point(targetD + 1f) - path.Point(targetD);
        targetForward.y = 0f;
        if (targetForward.sqrMagnitude < 0.01f) targetForward = Vector3.forward;
        targetForward.Normalize();
        Vector3 boxTarget = path.Point(targetD) + Vector3.Cross(Vector3.up, targetForward) * 0.8f - targetForward * 0.6f + Vector3.up * 1.0f;

        if (s >= thrown && s < boom)
        {
            // カメラの右上を抜けて、放物線で飛んでいく。近づくほど、速く赤く点滅する（ゲームの BombBox と同じ）
            float k = (s - thrown) / (boom - thrown);
            Vector3 from = eye + cam.transform.forward * 1.0f + cam.transform.right * 0.9f + Vector3.up * 0.1f;
            Vector3 position = Vector3.Lerp(from, boxTarget, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.65f;
            bool red = Mathf.Sin((s - thrown) * Mathf.Lerp(3f, 11f, k) * Mathf.PI * 2f) > 0f;
            ShowBox(position, Quaternion.Euler(s * 300f, s * 210f, 15f), red);
            Point(lampB, position + Vector3.up * 0.5f - targetForward * 0.5f,
                red ? new Color(1f, 0.1f, 0.05f) : new Color(1f, 0.85f, 0.65f), red ? 5f : 2.5f, 3.5f);
        }
        if (s >= boom)
        {
            float w = s - boom;
            Point(lampB, boxTarget, new Color(1f, 0.5f, 0.12f), 100f * Mathf.Clamp01(1f - w / 0.6f), 14f);
            ui.Flash(new Color(1f, 0.75f, 0.4f), 0.6f * Mathf.Clamp01(1f - w / 0.3f));
        }
        ui.Fade(1f - Smooth(s / 0.25f));
        ui.Caption("仲間と、生きて帰れ。", t, 46.5f, 50.7f);
        if (s > 5.2f) ui.Fade((s - 5.2f) / 0.3f);
    }

    // 51.5–55秒：出口の手前で、捕まる
    private void Caught(float t)
    {
        float s = t - 51.5f;
        const float scare = 1.75f;
        float d = caughtStart + Mathf.Min(s, scare) * 1.1f;
        Walk(d, s, 1.4f, 0.03f, s < 0.9f ? 0f : Mathf.Sin((s - 0.9f) * 3.6f) * 26f);
        Torch(s > 1.45f && s < scare ? 0f : 0.8f);
        PathLamps(d, true);

        if (s >= scare)
        {
            float w = s - scare;
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            f.Normalize();
            cam.transform.rotation = Quaternion.LookRotation(f);
            Vector3 face = cam.transform.position + f * Mathf.Lerp(1.15f, 0.62f, Mathf.Clamp01(w / 0.5f));
            santa.Show(face - Vector3.up * santa.HeadHeight, Quaternion.LookRotation(-f), PVActor.Run, 0.18f + w * 0.5f);
            cam.transform.position += Shake(t, 0.06f);
            cam.fieldOfView = 55f;
            Torch(1f);
            Point(lampA, face + Vector3.up * -0.45f - f * 0.35f, new Color(1f, 0.08f, 0.04f), 3.5f, 3f);
            ui.Flash(new Color(0.75f, 0f, 0f), 0.18f + 0.6f * Mathf.Clamp01((w - 0.45f) / 0.4f));
            if (w > 0.75f) ui.Fade((w - 0.75f) / 0.15f);
            if (w > 0.95f)
            {
                float a = Mathf.Clamp01((w - 0.95f) / 0.25f) * Mathf.Clamp01((3.5f - s) / 0.25f);
                ui.Text("死亡", new Vector2(0f, 20f), 230f + (w - 0.95f) * 20f, new Color(0.82f, 0.04f, 0.04f, a), 20f);
            }
        }
        ui.Fade(s < 0.25f ? 1f - s / 0.25f : 0f);
    }

    // 55–60秒：タイトル
    private void Logo(float t)
    {
        float s = t - 55f;
        RenderSettings.ambientLight = new Color(0.01f, 0.006f, 0.014f);
        Vector3 focus = titleSlot.ReelCenter;
        Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(-12f, 6f, s / 5f), 0f) * titleSlot.Front;
        Look(focus + dir * Mathf.Lerp(4.2f, 4.9f, s / 5f) + Vector3.up * 0.2f, focus, 48f);
        titleSlot.Spin(s, 0.2f, 1.4f, new[] { 0, 0, 0 });

        Vector3 right = Vector3.Cross(Vector3.up, titleSlot.Front);
        float on = s > 1.4f ? 1f : 0.35f;
        Point(lampA, focus + titleSlot.Front * 1.2f + right * 2.2f + Vector3.up, new Color(1f, 0.08f, 0.08f), 5f * on, 8f);
        Point(lampB, focus + titleSlot.Front * 1.2f - right * 2.2f, new Color(1f, 0.65f, 0.15f), 3.5f * on, 8f);

        // 台は暗くして、上にロゴを置く
        ui.Fade(Mathf.Max(1f - Smooth(s / 0.4f), 0.55f * Smooth((s - 1.1f) / 0.5f)));
        if (s > 1.4f)
        {
            float w = s - 1.4f;
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - w / 0.22f);
            if (w < 0.35f) // 最初の一瞬は、色ずれ
            {
                float off = 26f * (1f - w / 0.35f) * Mathf.Sign(Mathf.Sin(w * 90f));
                ui.Text("JACKDEAD", new Vector2(off, 80f), 220f * pop, new Color(1f, 0.05f, 0.1f, 0.7f), 6f, true, 39);
                ui.Text("JACKDEAD", new Vector2(-off, 72f), 220f * pop, new Color(0.1f, 0.8f, 1f, 0.7f), 6f, true, 39);
            }
            ui.Text("JACKDEAD", new Vector2(0f, 76f), 220f * pop, new Color(1f, 0.84f, 0.36f, 1f), 6f, true);
            ui.Line(-70f, 900f * Smooth((w - 0.2f) / 0.6f), 4f, new Color(0.85f, 0.05f, 0.05f, 1f));
            ui.Text("返すまで、出られない。", new Vector2(0f, -160f), 72f, new Color(0.95f, 0.92f, 0.86f, Smooth((w - 0.7f) / 0.5f)), 12f);
            ui.Text("― 開発中 ―", new Vector2(0f, -330f), 42f, new Color(0.7f, 0.68f, 0.65f, Smooth((w - 1.5f) / 0.5f)), 8f);
            ui.Flash(Color.white, 0.5f * Mathf.Clamp01(1f - w / 0.18f));
        }
    }

    // ═════════ 道具 ═════════

    // EnemyBrain の投げ方と同じ値（構え 0.7秒で腕を140度振りかぶり、0.2秒で-70度まで振り抜いて、0.5秒で戻す）
    private const float ThrowWindup = 0.7f;

    /// <summary>構え始めてからの時刻での、投げる腕の角度（EnemyBrain.UpdateThrowArm と同じ動き）</summary>
    private static float ThrowArmAngle(float time)
    {
        const float raised = 140f, followed = -70f, swing = 0.2f, back = 0.5f;
        if (time <= 0f) return 0f;
        if (time < ThrowWindup) return raised * Mathf.SmoothStep(0f, 1f, time / ThrowWindup);
        float follow = time - ThrowWindup;
        if (follow < swing) return Mathf.Lerp(raised, followed, follow / swing);
        return Mathf.Lerp(followed, 0f, (follow - swing) / back);
    }

    /// <summary>箱の見た目の中心が center になるように置く（モデルの原点がずれていても合う）。red で赤く点滅させる</summary>
    private void ShowBox(Vector3 center, Quaternion rotation, bool red)
    {
        box.SetActive(true);
        box.transform.rotation = rotation;
        box.transform.position = center - rotation * boxCenter;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color color = red ? new Color(1f, 0.15f, 0.1f) : Color.white;
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        foreach (Renderer r in box.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
    }

    private void Look(Vector3 from, Vector3 to, float fov)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
        cam.fieldOfView = fov;
    }

    /// <summary>道を歩く一人称の視点。yaw は首を左右に振る角度</summary>
    private void Walk(float d, float s, float stepRate, float bob, float yaw)
    {
        Vector3 eye = path.Point(d) + Vector3.up * (1.6f + Mathf.Abs(Mathf.Sin(s * stepRate * Mathf.PI)) * bob);
        Vector3 ahead = path.Point(d + 3.5f) + Vector3.up * 1.35f;
        Vector3 dir = ahead - eye;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(0f, yaw, 0f) * Quaternion.LookRotation(dir.normalized));
        cam.fieldOfView = 66f;
    }

    private void Torch(float power)
    {
        torch.enabled = power > 0f;
        Transform c = cam.transform;
        torch.transform.SetPositionAndRotation(c.position + c.right * 0.25f - c.up * 0.2f, c.rotation);
        torch.color = new Color(1f, 0.93f, 0.8f);
        torch.intensity = 36f * power;
        torch.range = 32f;
        torch.spotAngle = 68f;
        torch.innerSpotAngle = 28f;
    }

    /// <summary>道の先に置く、色のついた明かり</summary>
    private void PathLamps(float d, bool onlyFar = false)
    {
        float far = Mathf.Floor(d / 9f) * 9f + 12f;
        Point(lampC, path.Point(far) + Vector3.up * 2.8f, new Color(0.35f, 0.3f, 1f), 11f, 12f);
        if (!onlyFar) Point(lampB, path.Point(far + 8f) + Vector3.up * 2.4f, new Color(1f, 0.12f, 0.08f), 13f, 12f);
    }

    private static void Point(Light l, Vector3 position, Color color, float intensity, float range)
    {
        l.enabled = intensity > 0.01f;
        l.transform.position = position;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static float Flicker(float t, float seed)
    {
        float n = Mathf.PerlinNoise(t * 7f, seed);
        return n < 0.28f ? 0.35f : 0.85f + 0.3f * n;
    }

    private static Vector3 Shake(float t, float amount)
    {
        return new Vector3(
            Mathf.PerlinNoise(t * 23f, 1f) - 0.5f,
            Mathf.PerlinNoise(t * 27f, 2f) - 0.5f,
            Mathf.PerlinNoise(t * 21f, 3f) - 0.5f) * amount * 2f;
    }

    private static Bounds BoundsOf(GameObject o)
    {
        Renderer[] rs = o.GetComponentsInChildren<Renderer>().Where(r => r.GetComponent<TMPro.TMP_Text>() == null).ToArray();
        if (rs.Length == 0) return new Bounds(o.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static T Load<T>(string assetPath) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null) throw new Exception("素材がありません: " + assetPath);
        return asset;
    }

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void SetField(object target, string name, object value)
    {
        FieldInfo f = target.GetType().GetField(name, Any);
        if (f == null) throw new Exception($"{target.GetType().Name} に {name} がありません。");
        f.SetValue(target, value);
    }

    public static object GetField(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, Any);
        if (f == null) throw new Exception($"{target.GetType().Name} に {name} がありません。");
        return f.GetValue(target);
    }

    public static object Call(object target, string name, params object[] args)
    {
        MethodInfo m = target.GetType().GetMethod(name, Any);
        if (m == null) throw new Exception($"{target.GetType().Name} に {name} がありません。");
        return m.Invoke(target, args);
    }

    public void Dispose()
    {
        if (santa != null) santa.Dispose();
        if (generator != null)
        {
            NavMeshSurface surface = generator.GetComponent<NavMeshSurface>();
            if (surface != null) surface.RemoveData();
        }
        foreach (Object o in owned)
            if (o != null) Object.DestroyImmediate(o);
    }
}

/// <summary>折れ線の道。距離を指定して位置を返す</summary>
public class PVPath
{
    private readonly List<Vector3> points;
    private readonly List<float> distances = new List<float>();
    public float Total { get; }

    public PVPath(List<Vector3> source)
    {
        points = source;
        float sum = 0f;
        distances.Add(0f);
        for (int i = 1; i < points.Count; i++)
        {
            sum += Vector3.Distance(points[i - 1], points[i]);
            distances.Add(sum);
        }
        Total = Mathf.Max(1f, sum);
    }

    public Vector3 At(float d)
    {
        d = Mathf.Clamp(d, 0f, Total);
        for (int i = 1; i < points.Count; i++)
            if (d <= distances[i])
                return Vector3.Lerp(points[i - 1], points[i], Mathf.InverseLerp(distances[i - 1], distances[i], d));
        return points[points.Count - 1];
    }

    /// <summary>角をなめらかにした位置（前後の平均）</summary>
    public Vector3 Point(float d)
    {
        Vector3 sum = Vector3.zero;
        for (int i = -3; i <= 3; i++) sum += At(d + i * 0.4f);
        return sum / 7f;
    }
}

/// <summary>PV で使うスロット台。リールの絵を入れて、指定した時刻に指定した目で止める</summary>
public class PVSlot
{
    private readonly GameObject root;
    private readonly SlotMachineController controller;
    private readonly SlotReelScroller[] reels;
    private readonly Material[] materials;
    private readonly float stopOffset;
    private int[] shown;
    private readonly MapGenerator generator;

    public SlotMachineController Controller => controller;
    public SlotReelScroller[] ReelScrollers => reels;

    public Vector3 ReelCenter
    {
        get
        {
            Vector3 sum = Vector3.zero;
            foreach (SlotReelScroller r in reels) sum += r.GetComponent<Renderer>().bounds.center;
            return sum / reels.Length;
        }
    }

    public Vector3 Front => ((Vector3)PVDirector.Call(generator, "DetectSlotWorldFrontDirection", root)).normalized;

    public PVSlot(GameObject instance, MapGenerator mapGenerator, List<Object> owned)
    {
        root = instance;
        generator = mapGenerator;
        controller = instance.GetComponentInChildren<SlotMachineController>();
        if (controller == null) throw new Exception("スロット台に SlotMachineController がありません。");
        reels = (SlotReelScroller[])PVDirector.GetField(controller, "reels");
        stopOffset = (float)PVDirector.Call(controller, "GetTargetStopOffset");

        // 編集中は renderer.material を使えないので、複製した材質を先に渡しておく
        materials = new Material[reels.Length];
        for (int i = 0; i < reels.Length; i++)
        {
            Renderer r = reels[i].GetComponent<Renderer>();
            materials[i] = new Material(r.sharedMaterial);
            owned.Add(materials[i]);
            r.sharedMaterial = materials[i];
            PVDirector.SetField(reels[i], "reelRenderer", r);
            PVDirector.SetField(reels[i], "reelMaterial", materials[i]);
        }
        PVDirector.SetField(controller, "runtimeTextures", new Texture2D[reels.Length]);
        shown = Enumerable.Repeat(-1, reels.Length).ToArray();
        Strips(new[] { 0, 1, 2 });
        Idle();
    }

    /// <summary>台の正面を、指定の向きにする</summary>
    public void Face(Vector3 direction)
    {
        float yaw = Vector3.SignedAngle(Front, direction, Vector3.up);
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * root.transform.rotation;
    }

    private void Strips(int[] results)
    {
        for (int i = 0; i < reels.Length; i++)
        {
            int symbol = results[Mathf.Min(i, results.Length - 1)];
            if (shown[i] == symbol) continue;
            Texture2D[] textures = (Texture2D[])PVDirector.GetField(controller, "runtimeTextures");
            if (textures[i] != null) { Object.DestroyImmediate(textures[i]); textures[i] = null; }
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(100 + i * 17 + symbol);
            PVDirector.Call(controller, "GenerateReelStrip", i, symbol);
            UnityEngine.Random.state = state;
            shown[i] = symbol;
        }
    }

    public void Idle()
    {
        foreach (Material m in materials) m.mainTextureOffset = new Vector2(0f, stopOffset);
    }

    /// <summary>回す前に見せておく目（3列ともばらばら。当たりの目が先に見えないように）</summary>
    private static readonly int[] BeforeSpin = { 3, 1, 2 };

    /// <summary>start に回り始めて、左から順に止まり、end で最後の列が止まる</summary>
    public void Spin(float time, float start, float end, int[] results)
    {
        // 止まる目の絵は、回り始めてから入れる
        Strips(time < start ? BeforeSpin : results);
        const float speed = 2.6f;
        for (int i = 0; i < reels.Length; i++)
        {
            float stop = end - (reels.Length - 1 - i) * 0.65f;
            float offset = stopOffset;
            if (time < start) offset = stopOffset;
            else if (time < stop)
            {
                // 残りの回転量が、止まる時刻に 0 になる。最後の0.7秒は減速する
                float remain = stop - time;
                const float slow = 0.7f;
                float distance = remain > slow ? speed * (remain - slow * 0.5f) : speed * remain * remain / (2f * slow);
                offset = stopOffset - distance;
            }
            else
            {
                float w = time - stop;
                offset = stopOffset + (w < 0.18f ? Mathf.Sin(w / 0.18f * Mathf.PI) * 0.012f : 0f);
            }
            materials[i].mainTextureOffset = new Vector2(0f, Mathf.Repeat(offset, 1f));
        }
    }
}

/// <summary>PV で使う敵（サンタ）。アニメーションのクリップを、指定した時刻の姿勢にする</summary>
public class PVActor : IDisposable
{
    public const int Idle = 0;
    public const int Walk = 1;
    public const int Run = 2;

    private readonly GameObject body;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[3];
    private readonly AnimationClip[] clips;

    /// <summary>足元から顔までの高さ</summary>
    public float HeadHeight { get; } = 1.55f;

    private readonly Transform arm; // 投げる腕（右の上腕）
    public bool HasArm => arm != null;

    public PVActor(GameObject prefab)
    {
        body = (GameObject)Object.Instantiate(prefab);
        EnemyBrain brain = body.GetComponentInChildren<EnemyBrain>();
        clips = brain != null ? new[] { brain.idleClip, brain.walkClip, brain.runClip } : new AnimationClip[3];

        foreach (SkinnedMeshRenderer r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.forceMatrixRecalculationPerRender = true;
            r.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponentInChildren<Animator>();
        if (animator == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        graph = PlayableGraph.Create("PVActor");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Anim", animator);
        mixer = AnimationMixerPlayable.Create(graph, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
            graph.Connect(playables[i], 0, mixer, i);
        }
        output.SetSourcePlayable(mixer);

        if (animator.isHuman) arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            Show(Vector3.zero, Quaternion.identity, Idle, 0f);
            HeadHeight = Mathf.Clamp(head.position.y + 0.08f, 0.8f, 3f);
        }
    }

    /// <summary>next を指定すると、blend（0〜1）の割合で次の動きへ混ぜる</summary>
    public void Show(Vector3 position, Quaternion rotation, int clip, float time, int next = -1, float nextTime = 0f, float blend = 0f)
    {
        body.SetActive(true);
        body.transform.SetPositionAndRotation(position, rotation);
        if (!graph.IsValid() || clips[clip] == null) return;
        if (next < 0 || clips[next] == null) blend = 0f;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            mixer.SetInputWeight(i, i == clip ? 1f - blend : i == next ? blend : 0f);
        }
        playables[clip].SetTime(Mathf.Repeat(time, Mathf.Max(0.01f, clips[clip].length)));
        if (blend > 0f) playables[next].SetTime(Mathf.Repeat(nextTime, Mathf.Max(0.01f, clips[next].length)));
        graph.Evaluate(0f);
        body.transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>Show のあとに呼ぶ。投げる腕の骨へ、回転を上乗せする（ゲームの敵と同じ動かし方）</summary>
    public void RotateThrowArm(float angle)
    {
        if (arm == null || Mathf.Abs(angle) < 0.01f) return;
        arm.rotation = Quaternion.AngleAxis(angle, body.transform.right) * arm.rotation;
    }

    public void Hide() { body.SetActive(false); }

    public void Dispose()
    {
        if (graph.IsValid()) graph.Destroy();
        if (body != null) Object.DestroyImmediate(body);
    }
}
