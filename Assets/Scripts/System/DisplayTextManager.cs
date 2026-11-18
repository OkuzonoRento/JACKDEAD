using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DisplayTextManager : MonoBehaviour
{
    [Header("UI テキスト参照（3Dテキスト）")]
    [SerializeField] private TextMeshPro quotaText;
    [SerializeField] private TextMeshPro incomeText;
    [SerializeField] private TextMeshPro remainingText;

    private MapGenerator mapGenerator;
    private PlayerController playerController;

    // バスの中（トリガー内）にあるスクラップを管理するセット
    private readonly HashSet<Item> itemsInBus = new HashSet<Item>();

    // プレイヤーがバスのエリア内に入っているかどうか
    private bool isPlayerInBus = false;

    public bool IsPlayerInBus => isPlayerInBus;

    private void Start()
    {
        mapGenerator = FindAnyObjectByType<MapGenerator>();
        playerController = FindAnyObjectByType<PlayerController>();
    }

    private void Update()
    {
        UpdateQuotaUI();
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterColliderInBus(other);
    }

    // バス内でQキー投擲されたアイテム（Instantiate後にItemが付与されるケース）も確実に拾う
    private void OnTriggerStay(Collider other)
    {
        RegisterColliderInBus(other);
    }

    private void OnTriggerExit(Collider other)
    {
        Item item = other.GetComponent<Item>();
        if (item == null) item = other.GetComponentInParent<Item>();

        if (item != null)
        {
            itemsInBus.Remove(item);
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) player = other.GetComponentInParent<PlayerController>();

        if (player != null || other.CompareTag("Player"))
        {
            isPlayerInBus = false;
        }
    }

    private void RegisterColliderInBus(Collider other)
    {
        Item item = other.GetComponent<Item>();
        if (item == null) item = other.GetComponentInParent<Item>();

        if (item != null && item.ItemData != null)
        {
            itemsInBus.Add(item);
        }

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) player = other.GetComponentInParent<PlayerController>();

        if (player != null || other.CompareTag("Player"))
        {
            isPlayerInBus = true;
        }
    }

    /// <summary>
    /// トラック（バス）のトリガー内にある床置きスクラップの合計金額を返す（DepartureLeverと共通利用）
    /// </summary>
    public int GetBusScrapTotalValue()
    {
        int total = 0;
        itemsInBus.RemoveWhere(item => item == null);

        foreach (var item in itemsInBus)
        {
            if (item != null && item.ItemData != null)
            {
                total += item.CurrentValue;
            }
        }
        return total;
    }

    /// <summary>
    /// レベルクリア精算時にバス内の床置きスクラップをすべて消去する
    /// </summary>
    public void ClearBusScrapItems()
    {
        itemsInBus.RemoveWhere(item => item == null);
        foreach (var item in itemsInBus)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }
        itemsInBus.Clear();
    }

    private void UpdateQuotaUI()
    {
        if (mapGenerator == null)
        {
            mapGenerator = FindAnyObjectByType<MapGenerator>();
            if (mapGenerator == null) return;
        }

        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }

        int currentQuota = mapGenerator.CurrentMapQuota;
        int totalIncome = CalculateTotalIncome();
        int remaining = Mathf.Max(0, currentQuota - totalIncome);

        if (quotaText != null)
            quotaText.text = $"${currentQuota:D6}";

        if (incomeText != null)
            incomeText.text = $"${totalIncome:D6}";

        if (remainingText != null)
            remainingText.text = $"${remaining:D6}";
    }

    public int CalculateTotalIncome()
    {
        int total = GetBusScrapTotalValue();

        if (isPlayerInBus && playerController != null)
        {
            total += playerController.PlayerMoney;

            if (playerController.Inventory != null)
            {
                foreach (var itemData in playerController.Inventory)
                {
                    if (itemData != null)
                    {
                        total += mapGenerator != null ? mapGenerator.GetScrapValue(itemData) : itemData.Amount;
                    }
                }
            }
        }

        return total;
    }
}