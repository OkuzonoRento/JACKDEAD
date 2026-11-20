using UnityEngine;

public class DepartureLever : MonoBehaviour, IInteractable
{
    [Header("インタラクト設定")]
    [SerializeField, Tooltip("レバーを引くのに必要な長押し時間")]
    private float requiredHoldTime = 1.5f;

    [SerializeField, Tooltip("UIに表示する名称")]
    private string leverPromptName = "出発レバー";

    [Header("バス参照（未設定なら親またはシーンから自動取得）")]
    [SerializeField] private DisplayTextManager displayTextManager;

    public float RequiredHoldTime => requiredHoldTime;
    public string LeverPromptName => leverPromptName;

    private bool isActivated = false;

    private void Awake()
    {
        if (displayTextManager == null)
        {
            displayTextManager = GetComponentInParent<DisplayTextManager>();
            if (displayTextManager == null)
            {
                displayTextManager = FindAnyObjectByType<DisplayTextManager>();
            }
        }
    }

    // --- IInteractable の実装 ---
    public string GetInteractPrompt() => leverPromptName;

    public float GetHoldTime() => requiredHoldTime;

    public bool CanInteract()
    {
        if (isActivated) return false;

        int totalAssets = CalculateTotalAssets(out _, out _, out _);
        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        int quota = mapGen != null ? mapGen.CurrentMapQuota : 0;

        return totalAssets >= quota;
    }

    public void OnInteract()
    {
        OnPullLever();
    }

    private void OnEnable()
    {
        if (NetworkSession.Instance != null)
        {
            NetworkSession.Instance.StageClearReceived += OnRemoteStageClear;
        }
    }

    private void OnDisable()
    {
        if (NetworkSession.Instance != null)
        {
            NetworkSession.Instance.StageClearReceived -= OnRemoteStageClear;
        }
    }

    /// <summary>
    /// 他プレイヤーがレバーを引いたとき、全員のマップが食い違わないよう同じクリア処理を実行する
    /// </summary>
    private void OnRemoteStageClear(int clearedLevel, int newSharedMoney)
    {
        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        if (mapGen == null || mapGen.CurrentStageLevel != clearedLevel) return;

        ExecuteClear(true, newSharedMoney);
    }

    public void OnPullLever()
    {
        ExecuteClear(false, 0);
    }

    /// <summary>
    /// triggeredByRemote=true のとき remoteSharedMoney は、レバーを引いた人が精算した共有所持金
    /// </summary>
    private void ExecuteClear(bool triggeredByRemote, int remoteSharedMoney)
    {
        if (isActivated) return;

        int totalAssets = CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue);

        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        int quota = mapGen != null ? mapGen.CurrentMapQuota : 1000;
        int currentLevel = mapGen != null ? mapGen.CurrentStageLevel : 1;

        Debug.Log($"[出発判定] 所持金: ${playerMoney} + トラック内: ${truckItemsValue} + インベントリ: ${inventoryItemsValue} = 総資産: ${totalAssets} (ノルマ: ${quota})");

        // 1. ノルマ未達成の場合（他プレイヤーからの通知による強制クリアでは判定しない）
        if (!triggeredByRemote && totalAssets < quota)
        {
            Debug.Log("[出発システム] ノルマに達していないため、出発できません！");
            return;
        }

        // 2. ノルマ達成の場合
        isActivated = true;

        // 所持金は全員共通。精算はレバーを引いた人が行い、結果の所持金を他の参加者へ配る
        // （強制クリア時に所持金がマイナスにならないよう、余剰は0以上にする）
        int surplusProfit = triggeredByRemote ? remoteSharedMoney : Mathf.Max(0, totalAssets - quota);
        if (triggeredByRemote) totalAssets = quota + surplusProfit;

        // マルチプレイ中は、他の参加者にもクリアを通知する
        if (!triggeredByRemote && NetworkSession.Instance != null)
        {
            NetworkSession.Instance.BroadcastStageClear(currentLevel, surplusProfit);
        }

        // トラック内のスクラップオブジェクトを消去
        if (displayTextManager != null)
        {
            displayTextManager.ClearBusScrapItems();
        }

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            // 手持ちインベントリのスクラップも精算して空にする
            player.ClearAllInventoryItems();

            // ノルマを支払った残額（超えた分の利益）を新しい共有所持金として反映（この時点では他の人へ配らない）
            player.SetMoneyLocal(surplusProfit);

            // 引いた人以外の手持ちスクラップは精算額に入っていないので、売却益を共有所持金へ足す（全員へ配られる）
            if (triggeredByRemote && inventoryItemsValue > 0)
            {
                player.AddMoney(inventoryItemsValue);
            }

            // ステージクリア時の正気度回復 ＆ 進行データ保存
            player.OnStageCleared(quota);

            Debug.Log($"[出発成功] STAGE {currentLevel} クリア！ 精算後の所持金: ${surplusProfit}");
        }

        // 3. 簡易リザルト画面を表示（未配置ならそのまま次レベルへ進行）
        ResultUIController resultUI = FindAnyObjectByType<ResultUIController>();
        if (resultUI != null)
        {
            resultUI.ShowStageClearResult(currentLevel, totalAssets, quota, surplusProfit);
        }
        else if (mapGen != null)
        {
            mapGen.AdvanceToNextLevel();
            if (player != null) player.ReviveIfSpectating();
        }
    }

    private int CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue)
    {
        playerMoney = 0;
        truckItemsValue = 0;
        inventoryItemsValue = 0;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        MapGenerator priceSource = FindAnyObjectByType<MapGenerator>();
        if (player != null)
        {
            playerMoney = player.PlayerMoney;

            if (player.Inventory != null)
            {
                for (int i = 0; i < player.Inventory.Length; i++)
                {
                    if (player.Inventory[i] != null)
                    {
                        // 現在のレベルの価格補正を掛けた売値で精算する
                        inventoryItemsValue += priceSource != null
                            ? priceSource.GetScrapValue(player.Inventory[i])
                            : player.Inventory[i].Amount;
                    }
                }
            }
        }

        if (displayTextManager == null)
        {
            displayTextManager = GetComponentInParent<DisplayTextManager>();
            if (displayTextManager == null)
            {
                displayTextManager = FindAnyObjectByType<DisplayTextManager>();
            }
        }

        if (displayTextManager != null)
        {
            truckItemsValue = displayTextManager.GetBusScrapTotalValue();
        }

        return playerMoney + truckItemsValue + inventoryItemsValue;
    }
}