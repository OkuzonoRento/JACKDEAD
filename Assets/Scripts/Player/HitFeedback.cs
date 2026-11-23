using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// ダメージを受けたときの体感を強くする。
///   ・被弾：赤い閃光（ダメージが大きいほど濃い）、画面の揺れ、視点の傾き、画角のはね。大きな被弾は、一瞬だけ時間が止まる（ソロのみ）
///   ・体力が少ない間：周りが赤く暗くなり、鼓動に合わせて脈打つ（体力が減るほど速く強くなる）
/// 赤い閃光の部品は、ゲームシーンの Hierarchy の「HitEffectUI」に置いてある（作成: メニュー JackDead/Game/ダメージ・死亡演出UIをシーンに作成）。
/// 倒れたあとの演出は DeathEffect が行う。この間、ここは何もしない。
/// </summary>
[DefaultExecutionOrder(-1000)]
public class HitFeedback : MonoBehaviour
{
    [Header("画面の部品（HitEffectUI の中）")]
    public Image flash;

    [Header("被弾の閃光")]
    [Range(0f, 1f)] public float flashMin = 0.22f;
    [Range(0f, 1f)] public float flashMax = 0.7f;
    [Tooltip("このダメージで、閃光・揺れが最大になる")] public float damageForMax = 30f;
    [Min(0.1f)] public float flashSeconds = 0.6f;

    [Header("被弾の揺れ")]
    [Tooltip("画面の揺れの大きさ（m）。小さいダメージ〜最大のダメージ")] public Vector2 shakeRange = new Vector2(0.03f, 0.14f);
    [Min(0.1f)] public float shakeSeconds = 0.45f;
    [Tooltip("視点が傾く最大の角度（度）")] public float rollMax = 6f;
    [Tooltip("被弾で画角が広がる最大の角度（度）。衝撃でぐっと押される感じ")] public float fovPunch = 7f;

    [Header("一瞬の時間停止（ソロのみ）")]
    [Tooltip("このダメージ以上で止まる")] public float hitStopDamage = 20f;
    [Range(0.02f, 1f)] public float hitStopScale = 0.12f;
    [Min(0f)] public float hitStopSeconds = 0.08f;

    [Header("体力が少ないときの脈動")]
    [Range(0f, 1f), Tooltip("体力がこの割合を下回ると始まる")] public float lowHealthThreshold = 0.35f;
    [Range(0f, 1f)] public float lowVignette = 0.55f;
    public Color lowVignetteColor = new Color(0.55f, 0f, 0f, 1f);
    [Tooltip("鼓動の速さ（回/秒）。体力が少し少ない〜ほとんどない")] public Vector2 beatRate = new Vector2(1.1f, 2.4f);

    private PlayerController player;
    private Camera cam;
    private Volume volume;
    private Vignette vignette;
    private float origVignette;
    private Color origVignetteColor;
    private bool postReady;

    private float flashPeak;
    private float flashAge = 99f;
    private float shakeAmount;
    private float shakeAge = 99f;
    private float rollKick;
    private float fovKick;
    private float beatClock;
    private Coroutine hitStopRoutine;
    private bool slowChanged;

    // 直前のフレームで足したカメラのずれ（次のフレームの最初に、取り除く）
    private Vector3 lastPosOffset;
    private Quaternion lastRotOffset = Quaternion.identity;
    private float lastFovOffset;

    private void OnEnable() { PlayerController.DamageTaken += OnDamage; }

    private void OnDisable()
    {
        PlayerController.DamageTaken -= OnDamage;
        RestoreTimeScale();
        ResetPost();
    }

    private void OnDamage(float amount)
    {
        float k = Mathf.Clamp01(amount / Mathf.Max(1f, damageForMax));
        flashPeak = Mathf.Lerp(flashMin, flashMax, k);
        flashAge = 0f;
        shakeAmount = Mathf.Lerp(shakeRange.x, shakeRange.y, k);
        shakeAge = 0f;
        rollKick = Random.Range(0.5f, 1f) * rollMax * k * (Random.value < 0.5f ? -1f : 1f);
        fovKick = fovPunch * Mathf.Lerp(0.3f, 1f, k);

        bool offline = NetworkSession.Instance == null || !NetworkSession.Instance.IsOnline;
        if (offline && amount >= hitStopDamage && hitStopSeconds > 0f && !PauseMenu.IsOpen)
        {
            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStop());
        }
    }

    /// <summary>転がっている体が壁や床にぶつかったときの揺れ（ダメージではないので、閃光は出さない）。strength: 0〜1</summary>
    public void Impact(float strength)
    {
        float k = Mathf.Clamp01(strength);
        shakeAmount = Mathf.Max(shakeAmount * Mathf.Clamp01(1f - shakeAge / shakeSeconds), Mathf.Lerp(shakeRange.x, shakeRange.y, k) * 0.8f);
        shakeAge = 0f;
        rollKick = Random.Range(0.4f, 1f) * rollMax * 0.7f * k * (Random.value < 0.5f ? -1f : 1f);
        fovKick = fovPunch * Mathf.Lerp(0.2f, 0.7f, k);
    }

    private System.Collections.IEnumerator HitStop()
    {
        Time.timeScale = hitStopScale;
        slowChanged = true;
        yield return new WaitForSecondsRealtime(hitStopSeconds);
        RestoreTimeScale();
        hitStopRoutine = null;
    }

    private void RestoreTimeScale()
    {
        if (!slowChanged) return;
        slowChanged = false;
        // 倒れた演出がスローモーションを使っているときは、そちらに任せる
        DeathEffect death = FindAnyObjectByType<DeathEffect>();
        if (death != null && death.IsPlaying) return;
        if (!PauseMenu.IsOpen) Time.timeScale = 1f;
    }

    // ───────── 毎フレーム ─────────

    private void Update()
    {
        // 前のフレームで足したカメラのずれを、先に取り除く（移動のスクリプトが、ずれを含んだ値から計算しないように）
        RemoveCameraOffset();

        if (player == null) player = FindAnyObjectByType<PlayerController>();
        if (player == null) return;
        if (cam == null) cam = Camera.main;

        // 倒れたあとは DeathEffect の演出だけにする
        if (player.IsDown)
        {
            SetFlashAlpha(0f);
            return;
        }

        float dt = Time.unscaledDeltaTime;
        flashAge += dt;
        shakeAge += dt;

        // 被弾の閃光
        float flashA = flashAge < flashSeconds ? flashPeak * Mathf.Pow(1f - flashAge / flashSeconds, 2f) : 0f;

        SetFlashAlpha(flashA);
    }

    private void LateUpdate()
    {
        if (player == null || cam == null || player.IsDown) return;

        float shakeK = shakeAge < shakeSeconds ? Mathf.Pow(1f - shakeAge / shakeSeconds, 2f) : 0f;
        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        float fov = 0f;

        if (shakeK > 0f)
        {
            float s = shakeAmount * shakeK;
            pos = new Vector3(Random.Range(-s, s), Random.Range(-s, s), 0f);
            float roll = rollKick * shakeK;
            rot = Quaternion.Euler(Random.Range(-1f, 1f) * s * 40f, Random.Range(-1f, 1f) * s * 40f, roll);
            fov += fovKick * shakeK;
        }

        Transform ct = cam.transform;
        ct.localPosition += pos;
        ct.localRotation = ct.localRotation * rot;
        cam.fieldOfView += fov;
        lastPosOffset = pos;
        lastRotOffset = rot;
        lastFovOffset = fov;
    }

    private void RemoveCameraOffset()
    {
        if (cam == null) return;
        Transform ct = cam.transform;
        ct.localPosition -= lastPosOffset;
        ct.localRotation = ct.localRotation * Quaternion.Inverse(lastRotOffset);
        cam.fieldOfView -= lastFovOffset;
        lastPosOffset = Vector3.zero;
        lastRotOffset = Quaternion.identity;
        lastFovOffset = 0f;
    }

    /// <summary>鼓動の山（0〜1）。start から width の間だけ、なだらかに立ち上がって落ちる</summary>
    private static float Pulse(float p, float start, float width)
    {
        float x = (p - start) / width;
        if (x < 0f || x > 1f) return 0f;
        return Mathf.Sin(x * Mathf.PI);
    }

    private void SetFlashAlpha(float a)
    {
        if (flash == null) return;
        Color c = flash.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        flash.color = c;
    }

    // ───────── 周りの赤み・暗さ（URP の Volume） ─────────

    private void PreparePost()
    {
        if (postReady) return;
        volume = FindAnyObjectByType<Volume>();
        if (volume == null || volume.profile == null) return;

        VolumeProfile p = volume.profile; // このシーンだけの複製。アセットは書き換わらない
        if (!p.TryGet(out vignette)) vignette = p.Add<Vignette>(true);
        origVignette = vignette.active && vignette.intensity.overrideState ? vignette.intensity.value : 0f;
        origVignetteColor = vignette.active && vignette.color.overrideState ? vignette.color.value : Color.black;
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.color.overrideState = true;
        postReady = true;
    }

    private void ApplyPost(float severity, float beat)
    {
        PreparePost();
        if (!postReady || vignette == null) return;
        float k = severity * (0.55f + 0.45f * beat);
        vignette.intensity.value = Mathf.Clamp01(origVignette + lowVignette * k);
        vignette.color.value = Color.Lerp(origVignetteColor, lowVignetteColor, Mathf.Clamp01(severity * 1.5f));
    }

    /// <summary>周りの赤み・暗さを元に戻す（死亡演出が、元の値を読む前や、このスクリプトが止まるときに呼ぶ）</summary>
    public void ResetPost()
    {
        if (!postReady || vignette == null) return;
        vignette.intensity.value = origVignette;
        vignette.color.value = origVignetteColor;
    }
}
