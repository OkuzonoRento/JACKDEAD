using System.Collections;
using UnityEngine;
using TMPro;

public class TitleLogoGlitch : MonoBehaviour
{
    [Header("対象テキスト")]
    public TextMeshProUGUI logoText;

    [Header("ベース文字とグリッチ時の化け文字")]
    public string originalText = "JACKDEAD";
    public string[] glitchTexts = new string[]
    {
        "J4CKD3AD",
        "JACK____",
        "____DEAD",
        "JACKDEBT",
        "J#CKD&AD"
    };

    [Header("ネオン明滅（フリッカー）設定")]
    public float minAlpha = 0.65f;     // 一番暗くなったときの透明度
    public float maxAlpha = 1.0f;      // 一番明るいときの透明度
    public float flickerSpeed = 12f;   // 明滅の速さ

    [Header("グリッチ発生設定")]
    public float minGlitchInterval = 3.0f; // 最短何秒おきにノイズが走るか
    public float maxGlitchInterval = 7.0f; // 最長何秒おきにノイズが走るか
    public float glitchShakeAmount = 8f;   // 文字がガタッとズレる幅

    private RectTransform rectTransform;
    private Vector2 initialPos;
    private Color baseColor;
    private bool isGlitching = false;

    void Start()
    {
        if (logoText == null)
            logoText = GetComponent<TextMeshProUGUI>();

        rectTransform = GetComponent<RectTransform>();
        initialPos = rectTransform.anchoredPosition;
        baseColor = logoText.color;
        logoText.text = originalText;

        StartCoroutine(GlitchLoop());
    }

    void Update()
    {
        if (logoText == null) return;

        // グリッチ中以外はパーリンノイズで不規則なネオンの明滅を行う
        if (!isGlitching)
        {
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            Color c = baseColor;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, noise);
            logoText.color = c;
        }
    }

    private IEnumerator GlitchLoop()
    {
        while (true)
        {
            // 次のグリッチ発生までランダムに待機
            float waitTime = Random.Range(minGlitchInterval, maxGlitchInterval);
            yield return new WaitForSeconds(waitTime);

            isGlitching = true;

            // 2〜4回ほど高速で文字が化けてガタガタ震える
            int burstCount = Random.Range(2, 5);
            for (int i = 0; i < burstCount; i++)
            {
                // ランダムな化け文字に差し替え
                if (glitchTexts.Length > 0)
                {
                    logoText.text = glitchTexts[Random.Range(0, glitchTexts.Length)];
                }

                // 位置をランダムに少しズラす
                Vector2 randomOffset = new Vector2(
                    Random.Range(-glitchShakeAmount, glitchShakeAmount),
                    Random.Range(-glitchShakeAmount * 0.5f, glitchShakeAmount * 0.5f)
                );
                rectTransform.anchoredPosition = initialPos + randomOffset;

                // 一瞬だけ明るく光らせる（または暗く落とす）
                Color flashColor = baseColor;
                flashColor.a = (i % 2 == 0) ? 1.0f : 0.2f;
                logoText.color = flashColor;

                yield return new WaitForSeconds(Random.Range(0.04f, 0.09f));
            }

            // 元の文字・位置に戻す
            logoText.text = originalText;
            rectTransform.anchoredPosition = initialPos;
            logoText.color = baseColor;
            isGlitching = false;
        }
    }
}