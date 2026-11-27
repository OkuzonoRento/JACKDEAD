using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 倒れたときの演出。「死んだ」という実感を出す。
///   ・赤い閃光と画面の揺れ（被弾の衝撃）、ソロでは一瞬スローモーション
///   ・カメラが崩れ落ちる（視点が床まで下がり、横に傾き、少し見上げる）。画角も狭まる
///   ・色が抜けて（彩度ダウン）、周りが暗くなる（ビネット）。赤い色味が残る
///   ・「死亡」の文字が出る
/// 画面の部品（赤い色味・黒いフェード・文字）は、ゲームシーンの Hierarchy にある「DeathEffectUI」に置いてある
/// （作成: メニュー JackDead/Game/死亡演出UIをシーンに作成）。このスクリプトは、それを動かすだけ。
/// 倒れたあと:
///   ・マルチで観戦に入る（final = false）: 崩れ落ちてから観戦のカメラへ切り替わる。復活するまで、うっすら赤く色が抜けたまま
///   ・最後の1人／ソロ（final = true）: 崩れ落ちたあと onComplete でゲームオーバー演出へ進む
/// </summary>
[DefaultExecutionOrder(1000)]
public class DeathEffect : MonoBehaviour
{
    [Header("画面の部品（DeathEffectUI の中）")]
    public Image tint;
    public Image fade;
    public TMP_Text title;
    public TMP_Text reason;

    [Header("崩れ落ちるカメラ")]
    [Min(0.2f)] public float collapseDuration = 1.3f;
    [Tooltip("目の高さを下げる量（m）")] public float dropHeight = 1.1f;
    [Tooltip("横に傾く角度（度）")] public float rollDegrees = 70f;
    [Tooltip("倒れたあと、見上げる角度（度）")] public float lookUpDegrees = 22f;
    [Tooltip("画角を、もとの何倍まで狭めるか")] [Range(0.4f, 1f)] public float fovScale = 0.72f;
    [Tooltip("被弾の衝撃の、画面の揺れの大きさ（m）")] public float shakeAmount = 0.12f;

    [Header("色・暗さ")]
    [Range(0f, 1f)] public float flashAlpha = 0.65f;
    [Range(0f, 1f)] public float deadTintAlpha = 0.35f;
    [Range(0f, 1f)] public float spectateTintAlpha = 0.16f;
    [Tooltip("倒れたときの彩度（-100 でモノクロ）")] public float deadSaturation = -85f;
    [Range(0f, 1f)] public float deadVignette = 0.6f;

    [Header("スローモーション（ソロのときだけ）")]
    [Range(0.05f, 1f)] public float slowScale = 0.3f;
    [Min(0f)] public float slowSeconds = 0.9f;

    private const float TextDelay = 0.35f;
    private const float HoldAfterCollapse = 0.5f;

    private Coroutine routine;
    private Volume volume;
    private Vignette vignette;
    private ColorAdjustments colorAdj;
    private float origVignette, origSaturation;
    private bool postReady;
    private bool slowChanged;

    // カメラの姿勢の上書き（LateUpdate で、ほかのスクリプトより後に適用する）
    private bool camActive;
    private float camBlend;       // 0 = ふだん / 1 = 崩れ落ちた姿勢
    private float shakeAge;
    private Camera cam;
    private Vector3 startPos;
    private Quaternion startRot;
    private float startFov;
    private float rollSign = 1f;

    public bool IsPlaying { get; private set; }

    /// <summary>倒れた演出を始める。final = true なら、崩れ落ちたあとに onComplete を呼ぶ（その後も姿勢は保つ）</summary>
    public void Play(string deathReason, bool final, System.Action onComplete)
    {
        if (routine != null) StopCoroutine(routine);
        GameAudio.Play(GameSound.Death);
        routine = StartCoroutine(PlayRoutine(deathReason, final, onComplete));
    }

    /// <summary>演出を片付ける（復活したとき・やり直すとき）。色・カメラ・時間を元に戻す</summary>
    public void Clear()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        camActive = false;
        camBlend = 0f;
        IsPlaying = false;
        RestoreTimeScale();
        SetPost(0f);
        SetAlpha(tint, 0f);
        SetAlpha(fade, 0f);
        SetAlpha(title, 0f);
        SetAlpha(reason, 0f);
    }

    private void OnDisable() { RestoreTimeScale(); }

    private void OnDestroy()
    {
        RestoreTimeScale();
        SetPost(0f);
    }

    // ───────── 演出の流れ ─────────

    private IEnumerator PlayRoutine(string deathReason, bool final, System.Action onComplete)
    {
        IsPlaying = true;
        // 体力が少ないときの脈動で、周りの赤み・暗さが変わっているので、元に戻してから始める
        HitFeedback hit = FindAnyObjectByType<HitFeedback>();
        if (hit != null) hit.ResetPost();
        PrepareCamera();
        PreparePost();

        bool offline = NetworkSession.Instance == null || !NetworkSession.Instance.IsOnline;
        rollSign = Random.value < 0.5f ? -1f : 1f;
        shakeAge = 0f;
        camActive = cam != null;
        camBlend = 0f;

        if (title != null) title.text = Localization.T("死亡", "YOU DIED");
        if (reason != null) reason.text = deathReason == "FELL TO DEATH" ? Localization.T("落下", "FELL TO DEATH") : "";
        SetAlpha(title, 0f);
        SetAlpha(reason, 0f);
        SetAlpha(fade, 0f);

        // 被弾の衝撃：赤い閃光と、一瞬のスローモーション（マルチは他の人が動き続けるので、時間は止めない）
        SetAlpha(tint, flashAlpha);
        if (offline && !PauseMenu.IsOpen)
        {
            Time.timeScale = slowScale;
            slowChanged = true;
        }

        float total = collapseDuration + HoldAfterCollapse;
        float t = 0f;
        while (t < total)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            shakeAge = t;

            // スローモーションを、ゆっくり元の速さへ戻す
            if (slowChanged && t > slowSeconds)
            {
                Time.timeScale = Mathf.MoveTowards(Time.timeScale, 1f, dt / 0.3f);
                if (Mathf.Approximately(Time.timeScale, 1f)) slowChanged = false;
            }

            float k = Mathf.Clamp01(t / collapseDuration);
            camBlend = EaseInCubic(k);

            // 色が抜けて、周りが暗くなる。赤い色味は、閃光から落ち着いた色へ
            SetPost(Mathf.Clamp01(t / 0.7f));
            SetAlpha(tint, Mathf.Lerp(flashAlpha, deadTintAlpha, Mathf.Clamp01(t / 0.9f)));

            // 「死亡」の文字
            float ta = Mathf.Clamp01((t - TextDelay) / 0.4f);
            SetAlpha(title, ta);
            SetAlpha(reason, ta * 0.85f);
            if (title != null) title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, EaseOutCubic(ta));
            yield return null;
        }
        RestoreTimeScale();

        if (final)
        {
            // 最後の1人：姿勢を保ったまま、ゲームオーバー演出へ。赤い色味と文字は、ゆっくり消えていく
            onComplete?.Invoke();
            float f = 0f;
            while (f < 0.6f)
            {
                f += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(f / 0.6f);
                SetAlpha(title, a);
                SetAlpha(reason, a * 0.85f);
                SetAlpha(tint, deadTintAlpha * a);
                yield return null;
            }
            IsPlaying = false;
            routine = null;
            yield break;
        }

        // 観戦へ：一瞬暗転して、観戦のカメラへ切り替える。倒れている間は、うっすら赤く色が抜けたまま
        float fadeIn = 0f;
        while (fadeIn < 0.25f)
        {
            fadeIn += Time.unscaledDeltaTime;
            SetAlpha(fade, Mathf.Clamp01(fadeIn / 0.25f));
            SetAlpha(title, 1f - Mathf.Clamp01(fadeIn / 0.25f));
            SetAlpha(reason, 0.85f * (1f - Mathf.Clamp01(fadeIn / 0.25f)));
            yield return null;
        }
        camActive = false;
        float fadeOut = 0f;
        while (fadeOut < 0.5f)
        {
            fadeOut += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(fadeOut / 0.5f);
            SetAlpha(fade, 1f - k);
            SetAlpha(tint, Mathf.Lerp(deadTintAlpha, spectateTintAlpha, k));
            SetPost(Mathf.Lerp(1f, 0.6f, k));
            yield return null;
        }
        IsPlaying = false;
        routine = null;
    }

    // ───────── カメラ ─────────

    private void PrepareCamera()
    {
        cam = Camera.main;
        if (cam == null) return;
        startPos = cam.transform.localPosition;
        startRot = cam.transform.localRotation;
        startFov = cam.fieldOfView;
    }

    private void LateUpdate()
    {
        if (!camActive || cam == null) return;

        Transform ct = cam.transform;
        float b = camBlend;

        // ふだんの姿勢（いまの操作・しゃがみの結果）から、崩れ落ちた姿勢へ。ふだんの位置は、毎フレームの値を基準にする
        Vector3 basePos = ct.localPosition;
        Vector3 targetPos = new Vector3(startPos.x, Mathf.Max(0.35f, startPos.y - dropHeight), startPos.z);
        Vector3 pos = Vector3.Lerp(new Vector3(basePos.x, startPos.y, basePos.z), targetPos, b);

        Quaternion target = Quaternion.Euler(-lookUpDegrees, 0f, rollDegrees * rollSign);
        Quaternion rot = Quaternion.Slerp(startRot, target, b);

        // 衝撃の揺れ（最初の0.35秒で収まる）
        float shake = shakeAmount * Mathf.Clamp01(1f - shakeAge / 0.35f);
        if (shake > 0f)
        {
            pos += new Vector3(Random.Range(-shake, shake), Random.Range(-shake, shake), 0f);
            rot *= Quaternion.Euler(Random.Range(-1f, 1f) * shake * 30f, Random.Range(-1f, 1f) * shake * 30f, 0f);
        }

        ct.localPosition = pos;
        ct.localRotation = rot;
        cam.fieldOfView = Mathf.Lerp(startFov, startFov * fovScale, b);
    }

    // ───────── 色・暗さ（URP の Volume） ─────────

    private void PreparePost()
    {
        if (postReady) return;
        volume = FindAnyObjectByType<Volume>();
        if (volume == null || volume.profile == null) return;

        // volume.profile は、このシーンだけの複製。アセットは書き換わらない
        VolumeProfile p = volume.profile;
        if (!p.TryGet(out vignette)) vignette = p.Add<Vignette>(true);
        if (!p.TryGet(out colorAdj)) colorAdj = p.Add<ColorAdjustments>(true);
        origVignette = vignette.active && vignette.intensity.overrideState ? vignette.intensity.value : 0f;
        origSaturation = colorAdj.active && colorAdj.saturation.overrideState ? colorAdj.saturation.value : 0f;
        vignette.active = true;
        colorAdj.active = true;
        vignette.intensity.overrideState = true;
        colorAdj.saturation.overrideState = true;
        postReady = true;
    }

    /// <summary>k = 0 でふだん、1 で倒れた色・暗さ</summary>
    private void SetPost(float k)
    {
        if (!postReady) return;
        if (vignette != null) vignette.intensity.value = Mathf.Lerp(origVignette, deadVignette, k);
        if (colorAdj != null) colorAdj.saturation.value = Mathf.Lerp(origSaturation, deadSaturation, k);
    }

    // ───────── 小さな道具 ─────────

    private void RestoreTimeScale()
    {
        if (!slowChanged) return;
        slowChanged = false;
        if (!PauseMenu.IsOpen) Time.timeScale = 1f;
    }

    private static float EaseInCubic(float x) { return x * x * x; }
    private static float EaseOutCubic(float x) { float y = 1f - x; return 1f - y * y * y; }

    private static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }
}
