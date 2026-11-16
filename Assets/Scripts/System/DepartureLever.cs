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

    public void OnPullLever()
    {
        if (isActivated) return;

        int totalAssets = CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue);

        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        int quota = mapGen != null ? mapGen.CurrentMapQuota : 1000;
        int currentLevel = mapGen != null ? mapGen.CurrentStageLevel : 1;

        Debug.Log($"[出発判定] 所持金: ${playerMoney} + トラック内: ${truckItemsValue} + インベントリ: ${inventoryItemsValue} = 総資産: ${totalAssets} (ノルマ: ${quota})");

        // 1. ノルマ未達成の場合
        if (totalAssets < quota)
        {
            Debug.Log("[出発システム] ノルマに達していないため、出発できません！");
            return;
        }

        // 2. ノルマ達成の場合
        isActivated = true;

        // トラック内のスクラップオブジェクトを消去
        if (displayTextManager != null)
        {
            displayTextManager.ClearBusScrapItems();
        }

        int surplusProfit = totalAssets - quota;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            // 手持ちインベントリのスクラップも精算して空にする
            player.ClearAllInventoryItems();

            // ノルマを支払った残額（超えた分の利益）を新しい所持金として反映
            player.AddMoney(surplusProfit - player.PlayerMoney);

            // ステージクリア時の正気度回復 ＆ 進行データ保存
            player.OnStageCleared(quota);

            Debug.Log($"[出発成功] STAGE {currentLevel} クリア！ 精算後の所持金: ${surplusProfit}");
        }

        // 3. 簡易リザルト画面を表示（未配置ならそのまま次レベルへ進行）
        StageResultUIController resultUI = FindAnyObjectByType<StageResultUIController>();
        if (resultUI != null)
        {
            resultUI.ShowStageClearResult(currentLevel, totalAssets, quota, surplusProfit);
        }
        else if (mapGen != null)
        {
            mapGen.AdvanceToNextLevel();
        }
    }

    private int CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue)
    {
        playerMoney = 0;
        truckItemsValue = 0;
        inventoryItemsValue = 0;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            playerMoney = player.PlayerMoney;

            if (player.Inventory != null)
            {
                for (int i = 0; i < player.Inventory.Length; i++)
                {
                    if (player.Inventory[i] != null)
                    {
                        inventoryItemsValue += player.Inventory[i].Amount;
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