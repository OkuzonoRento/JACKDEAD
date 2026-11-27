using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ドーパミンが多いとき（ハイな状態）に、画面の端へ色の移り変わるオーラを出す。
/// しきい値から薄く出はじめ、多いほど濃く・内側へ広がる。増えた瞬間は、少し強まる。
/// 画面の部品は、ゲームシーンの PlayerStatusUI の「HighAura」（作成: メニュー JackDead/Game/ハイのオーラと音をシーンに作成）。
/// </summary>
public class HighAura : MonoBehaviour
{
    [Header("画面の部品")]
    public RawImage image;

    [Header("出方（ドーパミンの割合）")]
    [Range(0f, 1f), Tooltip("この割合から出はじめる（ギャンブルの獲得額が増えるのは 50% から）")]
    public float startRatio = 0.35f;
    [Range(0f, 1f), Tooltip("出はじめの強さ")]
    public float startStrength = 0.3f;
    [Min(0.1f), Tooltip("強さが変わる速さ（1秒あたり）")]
    public float changeSpeed = 0.8f;
    [Range(0f, 1f), Tooltip("ドーパミンが増えた瞬間に足す強さ")]
    public float surge = 0.35f;

    private PlayerController player;
    private Material material;
    private float current;
    private float boost;
    private float lastRatio = -1f;

    /// <summary>いまの強さ（0〜1。音にも使う）</summary>
    public float Strength => current;

    private void Awake()
    {
        if (image == null) return;
        Shader shader = Resources.Load<Shader>("Shaders/HighAura");
        if (shader == null)
        {
            Debug.LogWarning("[HighAura] シェーダー Shaders/HighAura が見つかりません。");
            image = null;
            return;
        }
        material = new Material(shader);
        image.material = material;
        image.color = Color.white;
        image.raycastTarget = false;
        image.enabled = false;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    private void Update()
    {
        if (image == null) return;
        if (player == null) player = FindAnyObjectByType<PlayerController>();

        float ratio = CurrentRatio();
        // 急に増えた（ギャンブルに勝った）ときは、少しの間、強める
        if (lastRatio >= 0f && ratio - lastRatio > 0.03f && ratio >= startRatio) boost = surge;
        lastRatio = ratio;
        boost = Mathf.MoveTowards(boost, 0f, Time.unscaledDeltaTime * 0.35f);

        float target = ratio >= startRatio && startRatio < 1f
            ? Mathf.Lerp(startStrength, 1f, Mathf.InverseLerp(startRatio, 1f, ratio))
            : 0f;
        current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * changeSpeed);

        float shown = Mathf.Clamp01(current + (current > 0f ? boost : 0f));
        bool visible = shown > 0.001f;
        if (image.enabled != visible) image.enabled = visible;
        if (!visible) return;

        material.SetFloat("_Intensity", shown);
        // 多いほど、ゆらぎが速くなる
        material.SetFloat("_Phase", material.GetFloat("_Phase") + Time.unscaledDeltaTime * Mathf.Lerp(0.7f, 1.6f, shown));
        material.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f);
    }

    /// <summary>画面に映している人のドーパミンの割合。倒れている間は 0（観戦中は、見ている相手）</summary>
    private float CurrentRatio()
    {
        if (player == null || player.MaxDopamine <= 0f) return 0f;

        long spectated = RemotePlayerSync.SpectatedClientId;
        if (spectated >= 0)
        {
            return RemotePlayerSync.Instance != null &&
                   RemotePlayerSync.Instance.TryGetStatus((ulong)spectated, out RemotePlayerSync.PlayerStatus status)
                ? Mathf.Clamp01(status.dopamine / player.MaxDopamine)
                : 0f;
        }
        return player.IsDown ? 0f : Mathf.Clamp01(player.Dopamine / player.MaxDopamine);
    }
}
