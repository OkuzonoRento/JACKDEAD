using UnityEngine;
using System.Collections.Generic;

public class DepartureLever : MonoBehaviour
{
    [Header("インタラクト設定")]
    [SerializeField, Tooltip("レバーを引くのに必要な長押し時間")]
    private float requiredHoldTime = 1.5f;

    [SerializeField, Tooltip("UIに表示する名称")]
    private string leverPromptName = "出発レバー";

    public float RequiredHoldTime => requiredHoldTime;
    public string LeverPromptName => leverPromptName;

    private bool isActivated = false;

    // バス（トラック）のトリガー内にあるスクラップを保持するセット
    private HashSet<Item> itemsInBus = new HashSet<Item>();

    private void OnTriggerEnter(Collider other)
    {
        Item item = other.GetComponent<Item>();
        if (item != null)
        {
            if (!itemsInBus.Contains(item))
            {
                itemsInBus.Add(item);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Item item = other.GetComponent<Item>();
        if (item != null)
        {
            if (itemsInBus.Contains(item))
            {
                itemsInBus.Remove(item);
            }
        }
    }

    public bool CanInteract()
    {
        return !isActivated;
    }

    // プレイヤーが長押しを完了したときに呼ばれる
    public void OnPullLever()
    {
        if (isActivated) return;

        // 総資産（所持金 ＋ トラック内アイテム ＋ インベントリ内アイテム）を計算
        int totalAssets = CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue);

        MapGenerator mapGen = FindAnyObjectByType<MapGenerator>();
        int quota = mapGen != null ? mapGen.CurrentMapQuota : 1000;

        Debug.Log($"[出発判定] 所持金: ${playerMoney} + トラック内: ${truckItemsValue} + インベントリ: ${inventoryItemsValue} = 総資産: ${totalAssets} (ノルマ: ${quota})");

        // 1. ノルマ未達成の場合（インタラクト失敗）
        if (totalAssets < quota)
        {
            Debug.Log("[出発システム] ノルマに達していないため、出発できません！");
            // TODO: 失敗時のブザー音や警告UIなど
            return;
        }

        // 2. ノルマ達成の場合（インタラクト成功）
        isActivated = true;

        // 超えた分（利益）を計算
        int surplusProfit = totalAssets - quota;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            int newMoney = surplusProfit;
            player.AddMoney(newMoney - player.PlayerMoney);

            Debug.Log($"[出発成功] ノルマ達成！ 超えた分の利益 ${surplusProfit} を所持金として獲得しました！");
        }

        // TODO: バス発進、リザルト画面表示、次のシーン遷移などの処理
    }

    // 総資産を計算するヘルパー
    private int CalculateTotalAssets(out int playerMoney, out int truckItemsValue, out int inventoryItemsValue)
    {
        playerMoney = 0;
        truckItemsValue = 0;
        inventoryItemsValue = 0;

        // A. プレイヤーの現在の所持金
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            playerMoney = player.PlayerMoney;
        }

        // B. ★ トラック（バス）のトリガー内にあるスクラップのみを合算する
        itemsInBus.RemoveWhere(item => item == null);
        foreach (var item in itemsInBus)
        {
            if (item != null && item.ItemData != null)
            {
                truckItemsValue += item.ItemData.Amount;
            }
        }

        // C. プレイヤーのインベントリ内にあるアイテムの合計
        if (player != null && player.Inventory != null)
        {
            foreach (var itemData in player.Inventory)
            {
                if (itemData != null)
                {
                    inventoryItemsValue += itemData.Amount;
                }
            }
        }

        return playerMoney + truckItemsValue + inventoryItemsValue;
    }
}