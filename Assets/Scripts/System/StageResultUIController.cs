using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StageResultUIController : MonoBehaviour
{
    [Header("リザルト画面ルートパネル")]
    [Tooltip("簡易リザルト画面全体の親パネル（初期状態は非表示）")]
    [SerializeField] private GameObject resultPanelObj;

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

    [Header("次ステージ進行ボタン（任意）")]
    [SerializeField] private Button nextStageButton;

    [Header("参照")]
    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerMovement playerMovement;

    private bool isResultShowing = false;
    public bool IsResultShowing => isResultShowing;

    private void Awake()
    {
        if (mapGenerator == null) mapGenerator = FindAnyObjectByType<MapGenerator>();
        if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
        if (playerMovement == null && playerController != null)
        {
            playerMovement = playerController.GetComponent<PlayerMovement>();
        }

        if (nextStageButton != null)
        {
            nextStageButton.onClick.RemoveListener(OnClickNextStageButton);
            nextStageButton.onClick.AddListener(OnClickNextStageButton);
        }

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(false);
        }
    }

    /// <summary>
    /// ステージクリア時に DepartureLever から呼び出され、簡易リザルトを表示する
    /// </summary>
    public void ShowStageClearResult(int clearedStageLevel, int totalCollectedAssets, int quotaAmount, int surplusMoney)
    {
        isResultShowing = true;

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(true);
        }

        // 1. プレイヤーの移動・視点ロック ＆ 正気度減少ストップ
        if (playerController != null)
        {
            playerController.SetPauseSanityDrain(true);
        }
        if (playerMovement != null)
        {
            playerMovement.LockControlAndMoveTo(
                playerMovement.transform.position,
                playerMovement.transform.position + playerMovement.transform.forward,
                0.05f
            );
        }

        // マウスカーソルを表示してボタンを押せるようにする
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 2. テキスト情報の更新
        if (stageClearTitleText != null)
        {
            stageClearTitleText.text = $"STAGE {clearedStageLevel} CLEAR";
        }
        if (totalCollectedText != null)
        {
            totalCollectedText.text = $"$ {totalCollectedAssets:N0}";
        }
        if (quotaAmountText != null)
        {
            quotaAmountText.text = $"$ {quotaAmount:N0}";
        }
        if (surplusMoneyText != null)
        {
            surplusMoneyText.text = $"$ {surplusMoney:N0}";
        }

        // 3. 各種ステータスゲージの更新（スタミナを除く 体力・正気度・ドーパミン）
        if (playerController != null)
        {
            if (healthGaugeSlider != null)
            {
                healthGaugeSlider.value = playerController.MaxHealth > 0f
                    ? Mathf.Clamp01(playerController.Health / playerController.MaxHealth)
                    : 0f;
            }
            if (sanityGaugeSlider != null)
            {
                sanityGaugeSlider.value = playerController.MaxSanity > 0f
                    ? Mathf.Clamp01(playerController.Sanity / playerController.MaxSanity)
                    : 0f;
            }
            if (dopamineGaugeSlider != null)
            {
                dopamineGaugeSlider.value = playerController.MaxDopamine > 0f
                    ? Mathf.Clamp01(playerController.Dopamine / playerController.MaxDopamine)
                    : 0f;
            }
        }

        // 4. 身体欠損確認UIの表示更新（初期状態は欠損なし表示）
        if (debuffSummaryText != null && string.IsNullOrEmpty(debuffSummaryText.text))
        {
            debuffSummaryText.text = "欠損部位・後遺症なし";
        }
    }

    /// <summary>
    /// リザルト画面の「次のステージへ」ボタンから呼ばれる処理
    /// </summary>
    public void OnClickNextStageButton()
    {
        if (!isResultShowing) return;
        isResultShowing = false;

        if (resultPanelObj != null)
        {
            resultPanelObj.SetActive(false);
        }

        // マウスカーソルを再びロックし、プレイヤーの操作制限と正気度減少を再開
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerMovement != null)
        {
            playerMovement.UnlockControl();
        }
        if (playerController != null)
        {
            playerController.SetPauseSanityDrain(false);
        }

        // 次のレベルへ進み、ノルマを引き上げてマップを再生成
        if (mapGenerator != null)
        {
            mapGenerator.AdvanceToNextLevel();
        }
    }
}