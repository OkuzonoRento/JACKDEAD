using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトル画面のスロット台を、誰も触っていないのに勝手に回し続け、不気味にライトで照らす。
/// ・実際のプレイと同じ演出（レバー → 回転・ガタガタ → 左から順に停止 → 当たりで膨らむ）を SlotMachineController.PlayDemoRoll で再生する
/// ・回すのは不規則な間隔で、ときどき立て続けに2〜3回まわす
/// ・Hierarchy に置いたスロット台（SlotMachineController 付き）を自動で見つけて使う。このスクリプトをシーンに置かなくても、
///   タイトルシーンに SlotMachineController があれば自動で動く（下の Bootstrap）
/// ・ライト演出: 真上から吊り下げ照明のように照らす青白いスポットライト（ゆっくり揺れて、ちらつき、ときどき消える）
///   ＋ 台の背後の赤い光（スポットが消えた瞬間に強まる）＋ 周りの明るさを落として台だけ浮かび上がらせる
/// ・本体のわずかな傾きと震えも足す（どちらも切れる）
/// </summary>
public class TitleSlotMachine : MonoBehaviour
{
    [Header("対象（空欄ならシーン内の SlotMachineController を自動で探す）")]
    public SlotMachineController machine;

    [Header("互換用: 置いてなければこのプレハブを anchor の位置に生成する（Hierarchy に置いた場合は不要）")]
    public GameObject slotPrefab;
    public Transform anchor;

    [Header("回すタイミング")]
    [Tooltip("1回の演出が終わってから、次を回すまでの待ち時間（秒）の最小")]
    public float minInterval = 0.6f;
    [Tooltip("同・最大")]
    public float maxInterval = 2.5f;
    [Tooltip("立て続けに回す（休まず連続で回す）確率")]
    [Range(0f, 1f)] public float chainChance = 0.3f;

    [Header("ライト演出")]
    public bool eerieLight = true;
    [Tooltip("真上からのスポットライトの色（青白く、少し緑がかった冷たい光）")]
    public Color spotColor = new Color(0.75f, 0.95f, 0.85f);
    public float spotIntensity = 40f;
    [Tooltip("スポットの広がり（度）。小さいほど台だけを鋭く照らす")]
    public float spotAngle = 42f;
    [Tooltip("台の上端から、スポットライトを吊るす高さ")]
    public float spotHeight = 2.6f;
    [Tooltip("スポットライトが吊り下げ照明のように揺れる大きさ（度）")]
    public float spotSway = 7f;
    [Tooltip("台の背後に置く赤い光の色")]
    public Color rimColor = new Color(0.95f, 0.05f, 0.03f);
    public float rimIntensity = 6f;
    [Tooltip("台の周りの明るさ（ディレクショナルライトと環境光）にかける倍率。小さいほど暗くなり、台だけが浮かび上がる")]
    [Range(0.02f, 1f)] public float surroundingBrightness = 0.25f;

    [Header("本体の動き")]
    [Tooltip("本体の常時の震え・傾きを付ける")]
    public bool eerieMotion = true;
    public float idleShake = 0.004f;

    [HideInInspector] public bool sceneDimmed; // 周りの暗さをシーンに反映済み（二重に暗くしないための印）

    private Transform spotPivot;
    private Light spot;
    private Light rim;
    private float seed;
    private Quaternion spotBaseRot;
    private Quaternion appliedRotOffset = Quaternion.identity;

    // 周りを暗くしたときの元の値（このシーンを離れるときに戻す）
    private readonly System.Collections.Generic.List<Light> dimmedLights = new System.Collections.Generic.List<Light>();
    private readonly System.Collections.Generic.List<float> dimmedOriginals = new System.Collections.Generic.List<float>();
    private float originalAmbientIntensity;
    private bool ambientDimmed;

    // タイトルシーンに SlotMachineController があるのにこのスクリプトが無い場合、自動で付ける
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindAnyObjectByType<Title3DSystem>() == null) return; // タイトルシーンだけ
        if (FindAnyObjectByType<TitleSlotMachine>() != null) return;
        if (FindAnyObjectByType<SlotMachineController>() == null) return;
        new GameObject("TitleSlotMachine (auto)").AddComponent<TitleSlotMachine>();
    }

    private void Start()
    {
        seed = Random.value * 100f;

        if (machine == null) machine = FindAnyObjectByType<SlotMachineController>();

        // Hierarchy に台が無いときだけ、プレハブから生成する
        if (machine == null && slotPrefab != null)
        {
            Vector3 pos = anchor != null ? anchor.position : transform.position;
            Quaternion rot = anchor != null ? anchor.rotation : transform.rotation;
            GameObject go = Instantiate(slotPrefab, pos, rot, transform);
            machine = go.GetComponentInChildren<SlotMachineController>();
        }

        if (machine == null)
        {
            Debug.LogWarning("[タイトルスロット] SlotMachineController が見つかりません。Hierarchy にスロット台を置いてください。");
            enabled = false;
            return;
        }

        PrepareForTitle();

        if (eerieLight)
        {
            // シーンに置いてあるライト（TitleLightBake が作る）があれば、それを使う。周りの暗さもシーンに保存済み
            if (!UseBakedLights())
            {
                if (!sceneDimmed) DimSurroundings();
                CreateLights();
            }
        }
        StartCoroutine(AutoSpinLoop());
    }

    private void OnDestroy()
    {
        RestoreSurroundings();
    }

    /// <summary>
    /// ゲーム用のスロット台を、タイトルの飾り用に整える。
    /// 元のプレハブはゲーム本編でも使うので、ここでは書き換えず、タイトルで動かすときだけ次のように変える:
    /// ・ボタンを押せないようにする（賭け金のボタン表示も不要なので消す）
    /// ・台の上の倍率表示（RarityLabel）や、ボタン上の金額テキストなど、ゲーム用の文字を非表示にする
    /// </summary>
    private void PrepareForTitle()
    {
        foreach (SlotButtonInteractable b in machine.GetComponentsInChildren<SlotButtonInteractable>(true))
            b.enabled = false;

        foreach (TMPro.TextMeshPro t in machine.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            t.gameObject.SetActive(false);
    }

    // ───────── ライト ─────────

    /// <summary>台まわりの全体照明（ディレクショナルライト・環境光）を落とす。ほかのライトは触らない</summary>
    private void DimSurroundings()
    {
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            dimmedLights.Add(l);
            dimmedOriginals.Add(l.intensity);
            l.intensity *= surroundingBrightness;
        }

        originalAmbientIntensity = RenderSettings.ambientIntensity;
        RenderSettings.ambientIntensity = originalAmbientIntensity * surroundingBrightness;
        ambientDimmed = true;
    }

    private void RestoreSurroundings()
    {
        for (int i = 0; i < dimmedLights.Count; i++)
            if (dimmedLights[i] != null) dimmedLights[i].intensity = dimmedOriginals[i];
        dimmedLights.Clear();
        dimmedOriginals.Clear();

        if (ambientDimmed) RenderSettings.ambientIntensity = originalAmbientIntensity;
        ambientDimmed = false;
    }

    private bool TryGetMachineBounds(out Bounds bounds)
    {
        bounds = new Bounds(machine.transform.position, Vector3.zero);
        bool found = false;
        foreach (Renderer r in machine.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    /// <summary>台の子に置いてある EerieSpotPivot / EerieSpot / EerieRim を見つけて使う。見つからなければ false</summary>
    private bool UseBakedLights()
    {
        Transform pivot = machine.transform.Find("EerieSpotPivot");
        if (pivot == null) return false;
        Transform s = pivot.Find("EerieSpot");
        if (s == null) return false;
        spotPivot = pivot;
        spot = s.GetComponent<Light>();
        Transform r = machine.transform.Find("EerieRim");
        rim = r != null ? r.GetComponent<Light>() : null;
        spotBaseRot = spotPivot.rotation;
        return spot != null;
    }

    /// <summary>エディターから呼ぶ。台のまわりに、スポットライトと背後の赤い光をシーンのオブジェクトとして作る</summary>
    public void BuildLightsInScene()
    {
        if (machine == null) machine = FindAnyObjectByType<SlotMachineController>();
        if (machine == null) return;
        foreach (string n in new[] { "EerieSpotPivot", "EerieRim" })
        {
            Transform old = machine.transform.Find(n);
            if (old != null) DestroyImmediate(old.gameObject);
        }
        CreateLights();
    }

    /// <summary>周りの明るさを、シーンのライトと環境光に直接反映する（実行時の DimSurroundings と同じ値）</summary>
    public void DimSceneLights()
    {
        if (sceneDimmed) return;
        sceneDimmed = true;
        // すでに暗くなっている（環境光が目標以下）ときは、重ねて暗くしない
        if (RenderSettings.ambientIntensity <= surroundingBrightness + 0.05f) return;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.intensity *= surroundingBrightness;
        }
        RenderSettings.ambientIntensity *= surroundingBrightness;
    }

    private void CreateLights()
    {
        TryGetMachineBounds(out Bounds b);
        Vector3 center = b.center;

        Camera cam = Camera.main;
        Vector3 toCam = cam != null ? cam.transform.position - center : Vector3.back;
        toCam.y = 0f;
        toCam = toCam.sqrMagnitude > 0.0001f ? toCam.normalized : Vector3.back;

        // 1. 真上のやや手前から吊り下げ照明のように照らす、冷たいスポットライト
        spotPivot = new GameObject("EerieSpotPivot").transform;
        spotPivot.SetParent(machine.transform, true);
        spotPivot.position = new Vector3(center.x, b.max.y + spotHeight, center.z) + toCam * 0.6f;
        spotPivot.rotation = Quaternion.LookRotation(center - spotPivot.position);
        spotBaseRot = spotPivot.rotation;

        spot = new GameObject("EerieSpot").AddComponent<Light>();
        spot.transform.SetParent(spotPivot, false);
        spot.type = LightType.Spot;
        spot.color = spotColor;
        spot.range = spotHeight + b.size.y + 4f;
        spot.spotAngle = spotAngle;
        spot.innerSpotAngle = spotAngle * 0.35f;
        spot.intensity = spotIntensity;
        spot.shadows = LightShadows.Soft;

        // 2. 台の背後から輪郭を赤く縁取る光
        rim = new GameObject("EerieRim").AddComponent<Light>();
        rim.transform.SetParent(machine.transform, true);
        rim.transform.position = center - toCam * (b.extents.magnitude + 0.5f) + Vector3.up * (b.extents.y * 0.2f);
        rim.type = LightType.Point;
        rim.color = rimColor;
        rim.range = b.extents.magnitude * 3.5f + 2f;
        rim.intensity = rimIntensity;
        rim.shadows = LightShadows.None;
    }

    // ───────── 自動で回し続ける ─────────

    private IEnumerator AutoSpinLoop()
    {
        // 台の準備（Start でのリール絵柄の生成）が終わるまで待つ
        while (machine != null && !machine.IsVisualReady) yield return null;

        yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));

        while (machine != null)
        {
            yield return StartCoroutine(machine.PlayDemoRoll());

            // 立て続けに回すこともある
            if (Random.value < chainChance)
                yield return new WaitForSeconds(Random.Range(0.05f, 0.25f));
            else
                yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
        }
    }

    // ───────── 不気味さの演出 ─────────

    private void LateUpdate()
    {
        if (machine == null) return;
        UpdateBodyMotion();
        UpdateLights();
    }

    /// <summary>台のルートに微かな傾きと震えを足す（前フレームに足した分を引いてから足し直すので、置いた向きはずれない）</summary>
    private void UpdateBodyMotion()
    {
        Transform t = machine.transform;

        // 前フレームの分を戻す
        // （位置は回転演出のガタガタ＝SlotMachineController が直接動かすので、ここでは触らない。傾きだけ足す）
        t.rotation = t.rotation * Quaternion.Inverse(appliedRotOffset);

        if (!eerieMotion)
        {
            appliedRotOffset = Quaternion.identity;
            return;
        }

        float time = Time.time;
        float jitter = idleShake * 100f; // 度に換算した、細かい震え
        float tiltZ = (Mathf.PerlinNoise(seed, time * 0.35f) - 0.5f) * 3f + Random.Range(-jitter, jitter) * 0.25f;
        float tiltX = (Mathf.PerlinNoise(seed + 10f, time * 0.27f) - 0.5f) * 2f + Random.Range(-jitter, jitter) * 0.25f;
        appliedRotOffset = Quaternion.Euler(tiltX, 0f, tiltZ);

        t.rotation = t.rotation * appliedRotOffset;
    }

    private void UpdateLights()
    {
        if (spot == null) return;
        float time = Time.time;

        // スポットが吊り下げ照明のようにゆっくり揺れる（台の上を光がなめる）
        float swayX = (Mathf.PerlinNoise(seed + 5f, time * 0.45f) - 0.5f) * 2f * spotSway;
        float swayZ = (Mathf.PerlinNoise(seed + 15f, time * 0.38f) - 0.5f) * 2f * spotSway;
        spotPivot.rotation = spotBaseRot * Quaternion.Euler(swayX, 0f, swayZ);

        // 古い蛍光灯のようなちらつき。ときどき一瞬だけ消える
        float flicker = Mathf.Lerp(0.55f, 1.15f, Mathf.PerlinNoise(seed + 20f, time * 11f));
        float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.7f);
        float blackout = Mathf.PerlinNoise(seed + 40f, time * 2.6f) > 0.8f ? 0.04f : 1f;
        float key = flicker * breathe * blackout;
        spot.intensity = spotIntensity * key;

        // スポットが弱いほど、背後の赤い光が強まる（闇の中で赤く浮かぶ）
        if (rim != null)
        {
            float rimPulse = 0.8f + 0.2f * Mathf.Sin(time * 2.3f + seed);
            rim.intensity = rimIntensity * rimPulse * (1f + (1f - Mathf.Clamp01(key)) * 1.8f);
        }
    }
}
