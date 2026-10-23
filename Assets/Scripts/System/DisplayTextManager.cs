using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class DisplayTextManager : MonoBehaviour
{
    [Header("UI テキスト参照（3Dテキスト）")]
    [SerializeField] private TextMeshPro quotaText;       // 本日のノルマ
    [SerializeField] private TextMeshPro incomeText;      // 本日の収入
    [SerializeField] private TextMeshPro remainingText;   // 残り返済額

    private MapGenerator mapGenerator;
    private PlayerController playerController;

    // バスの中（トリガー内）にあるスクラップを管理するセット
    private HashSet<Item> itemsInBus = new HashSet<Item>();

    // プレイヤーがバスのエリア内に入っているかどうか
    private bool isPlayerInBus = false;

    void Start()
    {
        // プレハブからでもシーン上のマネージャーやプレイヤーを自動取得
        mapGenerator = FindAnyObjectByType<MapGenerator>();
        playerController = FindAnyObjectByType<PlayerController>();
    }

    void Update()
    {
        UpdateQuotaUI();
    }

    // === トリガーに入ったときの処理（スクラップ ＆ プレイヤー） ===
    private void OnTriggerEnter(Collider other)
    {
        // 1. スクラップがバス内に入った場合
        Item item = other.GetComponent<Item>();
        if (item != null)
        {
            if (!itemsInBus.Contains(item))
            {
                itemsInBus.Add(item);
            }
        }

        // 2. プレイヤーがバス内に入った（乗った）場合
        // ※ プレイヤー自身、またはPlayerControllerを持つオブジェクトが対象
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null || other.CompareTag("Player"))
        {
            isPlayerInBus = true;
        }
    }

    // === トリガーから出たときの処理（スクラップ ＆ プレイヤー） ===
    private void OnTriggerExit(Collider other)
    {
        // 1. スクラップがバスの外に出た場合
        Item item = other.GetComponent<Item>();
        if (item != null)
        {
            if (itemsInBus.Contains(item))
            {
                itemsInBus.Remove(item);
            }
        }

        // 2. プレイヤーがバスの外に出た場合
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null || other.CompareTag("Player"))
        {
            isPlayerInBus = false;
        }
    }

    private void UpdateQuotaUI()
    {
        if (mapGenerator == null)
        {
            mapGenerator = FindAnyObjectByType<MapGenerator>();
            if (mapGenerator == null) return;
        }

        // 1. ノルマ金額の取得
        int currentQuota = mapGenerator.CurrentMapQuota;

        // 2. 収入の計算
        int totalIncome = CalculateTotalIncome();

        // 3. 残り返済額の計算（ノルマ - 収入）
        int remaining = Mathf.Max(0, currentQuota - totalIncome);

        // UIへの反映（$000000 フォーマット）
        if (quotaText != null)
            quotaText.text = $"${currentQuota:D6}";

        if (incomeText != null)
            incomeText.text = $"${totalIncome:D6}";

        if (remainingText != null)
            remainingText.text = $"${remaining:D6}";
    }

    private int CalculateTotalIncome()
    {
        int total = 0;

        // A. バス内にある床置きスクラップの金額を合算
        itemsInBus.RemoveWhere(item => item == null);
        foreach (var item in itemsInBus)
        {
            if (item != null && item.ItemData != null)
            {
                total += item.ItemData.Amount;
            }
        }

        // B. 【プレイヤーがバスに乗っている場合のみ】インベントリ内の金額を合算
        if (isPlayerInBus && playerController != null && playerController.Inventory != null)
        {
            foreach (var itemData in playerController.Inventory)
            {
                if (itemData != null)
                {
                    total += itemData.Amount;
                }
            }
        }

        return total;
    }
}