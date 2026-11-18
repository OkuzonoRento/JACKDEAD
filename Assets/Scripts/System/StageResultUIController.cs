using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StageResultUIController : MonoBehaviour
{
    [Header("リザルト画面ルートパネル")]
    [Tooltip("簡易リザルト画面全体の親パネル（初期状態は非表示）")]
    [SerializeField] private GameObject resultPanelObj;

    [Header("REPO風演出タイミング設定")]
    [Tooltip("各項目が順番に表示される間隔（秒）")]
    [SerializeField, Min(0.05f)] private float stepInterval = 0.35f;
    [Tooltip("金額やゲージがカウントアップするアニメーション時間（秒）")]
    [SerializeField, Min(0.1f)] private float countUpDuration = 0.75f;
    [Tooltip("すべての演出が終わってから自動で次ステージへ進むまでの余韻時間（秒）")]
    [SerializeField, Min(0.5f)] private float autoAdvanceDelay = 2.0f;

    [Header("テキスト表示参照")]
    [Tooltip("クリアしたステージ番号（例: STAGE 1 CLEAR）")]
    [SerializeField] private TextMeshProUGUI stageClearTitleText;
    [Tooltip("今回のマップでの総資産（収集金額＋所持金）")]
    [SerializeField] private TextMeshProUGUI totalCollectedText;
    [Tooltip("今回の目標ノルマ金額")]
    [SerializeField] private TextMeshProUGUI quotaAmountText;
    [Tooltip("ノルマ返済後の残り所持金（利益）")]
    [SerializeField] private TextMeshProUGUI surplusMoneyText;

    [Header("ステータスゲージ参照（スタミナ除外）")]
    [SerializeField] private Slider healthGaugeSlider;
    [SerializeField] private Slider sanityGaugeSlider;
    [SerializeField] private Slider dopamineGaugeSlider;

    [Header("身体欠損・デバフ確認UI（拡張用）")]
    [Tooltip("身体部位の状態を表示するUIオブジェクト群（任意）")]
    [SerializeField] private GameObject bodyStatusContainerObj;
    [SerializeField] private TextMeshProUGUI debuffSummaryText;

    [Header("参照")]
    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerMovement playerMovement;

    // --- 内部固定パラメータ（static不使用） ---
    private const float LockTransitionDuration = 0.05f;
    private const float PopPunchScale = 1.25f;
    private const float TitlePopDuration = 0.25f;
    private const float EaseOutPower = 3f;

    private readonly Color surplusFlashColor = new Color(0.3f, 1f, 0.4f, 1f);

    private bool isResultShowing = false;
    private Coroutine resultSequenceCoroutine;

    private Vector3 titleDefaultScale = Vector3.one;
    private Vector3 totalTextDefaultScale = Vector3.one;
    private Vector3 quotaTextDefaultScale = Vector3.one;
    private Vector3 surplusTextDefaultScale = Vector3.one;
    private Color surplusDefaultColor = Color.white;

    public bool IsResultShowing => isResultShowing;

    private void Awake()
    {
        if (mapGenerator == null) mapGenerator = FindAnyObjectByType<MapGenerator>();
        if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
        if (playerMovement == null && playerController != null)
        {
            playerMovement = playerController.GetComponent<PlayerMovement>();
        }

        CacheDefaultUIStates();

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(false);
        }
    }

    private void CacheDefaultUIStates()
    {
        if (stageClearTitleText != null) titleDefaultScale = stageClearTitleText.rectTransform.localScale;
        if (totalCollectedText != null) totalTextDefaultScale = totalCollectedText.rectTransform.localScale;
        if (quotaAmountText != null) quotaTextDefaultScale = quotaAmountText.rectTransform.localScale;
        if (surplusMoneyText != null)
        {
            surplusTextDefaultScale = surplusMoneyText.rectTransform.localScale;
            surplusDefaultColor = surplusMoneyText.color;
        }
    }

    /// <summary>
    /// ステージクリア時に DepartureLever から呼び出され、REPO風の段階的リザルト演出を開始する
    /// </summary>
    public void ShowStageClearResult(int clearedStageLevel, int totalCollectedAssets, int quotaAmount, int surplusMoney)
    {
        isResultShowing = true;

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(true);
        }

        if (playerController != null)
        {
            playerController.SetPauseSanityDrain(true);
        }
        if (playerMovement != null)
        {
            playerMovement.LockControlAndMoveTo(
                playerMovement.transform.position,
                playerMovement.transform.position + playerMovement.transform.forward,
                LockTransitionDuration
            );
        }

        if (resultSequenceCoroutine != null)
        {
            StopCoroutine(resultSequenceCoroutine);
        }
        resultSequenceCoroutine = StartCoroutine(
            PlayRepoStyleResultSequence(clearedStageLevel, totalCollectedAssets, quotaAmount, surplusMoney)
        );
    }

    private IEnumerator PlayRepoStyleResultSequence(int clearedStageLevel, int totalCollectedAssets, int quotaAmount, int surplusMoney)
    {
        // 演出開始前に各表示を初期状態（非表示または0）にリセット
        ResetResultUIForSequence();

        // --- 1. ステージクリアタイトルをポップ表示 ---
        if (stageClearTitleText != null)
        {
            stageClearTitleText.gameObject.SetActive(true);
            stageClearTitleText.text = $"STAGE {clearedStageLevel} CLEAR";
            yield return StartCoroutine(AnimateRectPop(stageClearTitleText.rectTransform, titleDefaultScale, TitlePopDuration));
        }

        yield return new WaitForSeconds(stepInterval);

        // --- 2. 目標ノルマ額をポップ表示 ---
        if (quotaAmountText != null)
        {
            quotaAmountText.gameObject.SetActive(true);
            quotaAmountText.text = $"$ {quotaAmount:N0}";
            yield return StartCoroutine(AnimateRectPop(quotaAmountText.rectTransform, quotaTextDefaultScale, TitlePopDuration));
        }

        yield return new WaitForSeconds(stepInterval * 0.6f);

        // --- 3. 総資産（収集額＋所持金）を 0 からカウントアップ ---
        if (totalCollectedText != null)
        {
            totalCollectedText.gameObject.SetActive(true);
            yield return StartCoroutine(
                AnimateMoneyCountUp(totalCollectedText, totalTextDefaultScale, 0, totalCollectedAssets, countUpDuration, false)
            );
        }

        yield return new WaitForSeconds(stepInterval);

        // --- 4. ノルマ精算後の残り所持金（利益）をカウントアップ＆緑フラッシュ ---
        if (surplusMoneyText != null)
        {
            surplusMoneyText.gameObject.SetActive(true);
            yield return StartCoroutine(
                AnimateMoneyCountUp(surplusMoneyText, surplusTextDefaultScale, 0, surplusMoney, countUpDuration, true)
            );
        }

        yield return new WaitForSeconds(stepInterval * 0.6f);

        // --- 5. ステータスゲージ（体力・正気度・ドーパミン）を滑らかに上昇 ---
        yield return StartCoroutine(AnimateStatusGauges(countUpDuration));

        // --- 6. 身体欠損・デバフ確認UIを表示 ---
        if (bodyStatusContainerObj != null)
        {
            bodyStatusContainerObj.SetActive(true);
        }
        if (debuffSummaryText != null)
        {
            debuffSummaryText.gameObject.SetActive(true);
            if (string.IsNullOrEmpty(debuffSummaryText.text))
            {
                debuffSummaryText.text = "欠損部位・後遺症なし";
            }
        }

        // --- 7. 余韻のあと自動で次のステージへ遷移 ---
        yield return new WaitForSeconds(autoAdvanceDelay);
        AdvanceToNextStage();
    }

    private void ResetResultUIForSequence()
    {
        if (stageClearTitleText != null) stageClearTitleText.gameObject.SetActive(false);

        if (quotaAmountText != null)
        {
            quotaAmountText.text = "$ 0";
            quotaAmountText.gameObject.SetActive(false);
        }

        if (totalCollectedText != null)
        {
            totalCollectedText.text = "$ 0";
            totalCollectedText.gameObject.SetActive(false);
        }

        if (surplusMoneyText != null)
        {
            surplusMoneyText.text = "$ 0";
            surplusMoneyText.color = surplusDefaultColor;
            surplusMoneyText.gameObject.SetActive(false);
        }

        if (healthGaugeSlider != null) healthGaugeSlider.value = 0f;
        if (sanityGaugeSlider != null) sanityGaugeSlider.value = 0f;
        if (dopamineGaugeSlider != null) dopamineGaugeSlider.value = 0f;

        if (bodyStatusContainerObj != null) bodyStatusContainerObj.SetActive(false);
        if (debuffSummaryText != null) debuffSummaryText.gameObject.SetActive(false);
    }

    private IEnumerator AnimateRectPop(RectTransform targetRect, Vector3 baseScale, float duration)
    {
        if (targetRect == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float bounce = Mathf.Sin(t * Mathf.PI);
            float scaleMult = Mathf.Lerp(1f, PopPunchScale, bounce);
            targetRect.localScale = baseScale * scaleMult;
            yield return null;
        }

        targetRect.localScale = baseScale;
    }

    private IEnumerator AnimateMoneyCountUp(
        TextMeshProUGUI targetText,
        Vector3 baseScale,
        int startValue,
        int endValue,
        float duration,
        bool useFlashColor)
    {
        if (targetText == null) yield break;

        RectTransform rect = targetText.rectTransform;
        Color baseColor = targetText.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, EaseOutPower);

            int currentVal = Mathf.RoundToInt(Mathf.Lerp(startValue, endValue, easeOut));
            targetText.text = $"$ {currentVal:N0}";

            float bounce = Mathf.Sin(t * Mathf.PI);
            rect.localScale = baseScale * Mathf.Lerp(1f, PopPunchScale, bounce);

            if (useFlashColor)
            {
                targetText.color = Color.Lerp(surplusFlashColor, surplusDefaultColor, t);
            }

            yield return null;
        }

        targetText.text = $"$ {endValue:N0}";
        rect.localScale = baseScale;
        if (useFlashColor)
        {
            targetText.color = baseColor;
        }
    }

    private IEnumerator AnimateStatusGauges(float duration)
    {
        if (playerController == null) yield break;

        float targetHealth = playerController.MaxHealth > 0f
            ? Mathf.Clamp01(playerController.Health / playerController.MaxHealth)
            : 0f;
        float targetSanity = playerController.MaxSanity > 0f
            ? Mathf.Clamp01(playerController.Sanity / playerController.MaxSanity)
            : 0f;
        float targetDopamine = playerController.MaxDopamine > 0f
            ? Mathf.Clamp01(playerController.Dopamine / playerController.MaxDopamine)
            : 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, EaseOutPower);

            if (healthGaugeSlider != null) healthGaugeSlider.value = Mathf.Lerp(0f, targetHealth, easeOut);
            if (sanityGaugeSlider != null) sanityGaugeSlider.value = Mathf.Lerp(0f, targetSanity, easeOut);
            if (dopamineGaugeSlider != null) dopamineGaugeSlider.value = Mathf.Lerp(0f, targetDopamine, easeOut);

            yield return null;
        }

        if (healthGaugeSlider != null) healthGaugeSlider.value = targetHealth;
        if (sanityGaugeSlider != null) sanityGaugeSlider.value = targetSanity;
        if (dopamineGaugeSlider != null) dopamineGaugeSlider.value = targetDopamine;
    }

    public void AdvanceToNextStage()
    {
        if (!isResultShowing) return;
        isResultShowing = false;
        resultSequenceCoroutine = null;

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(false);
        }

        if (playerMovement != null)
        {
            playerMovement.UnlockControl();
        }
        if (playerController != null)
        {
            playerController.SetPauseSanityDrain(false);
        }

        if (mapGenerator != null)
        {
            mapGenerator.AdvanceToNextLevel();
        }
    }
}