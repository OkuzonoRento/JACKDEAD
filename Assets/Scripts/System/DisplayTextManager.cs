using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DisplayTextManager : MonoBehaviour
{
    [Header("UI テキスト参照（3Dテキスト）")]
    [SerializeField] private TextMeshPro quotaText;
    [SerializeField] private TextMeshPro incomeText;
    [SerializeField] private TextMeshPro remainingText;
    [Tooltip("現在のレベル表示。未設定なら、ノルマ表示（quotaText）を複製してその上に自動で作る")]
    [SerializeField] private TextMeshPro levelText;

    private MapGenerator mapGenerator;
    private PlayerController playerController;

    // バスの中（トリガー内）にあるスクラップを管理するセット
    private readonly HashSet<Item> itemsInBus = new HashSet<Item>();

    // プレイヤーがバスのエリア内に入っているかどうか
    private bool isPlayerInBus = false;

    public bool IsPlayerInBus => isPlayerInBus;

    private BoxCollider area; // トラックの中を表すトリガー

    private void Start()
    {
        mapGenerator = FindAnyObjectByType<MapGenerator>();
        playerController = FindAnyObjectByType<PlayerController>();
        EnsureLevelText();
    }

    /// <summary>
    /// levelText が未設定のとき、quotaText を複製して1行分上に置く（向き・大きさ・フォントはそのまま引き継ぐ）
    /// </summary>
    private void EnsureLevelText()
    {
        if (levelText != null || quotaText == null) return;

        levelText = Instantiate(quotaText, quotaText.transform.parent);
        levelText.name = "LevelText";
        float lineHeight = Mathf.Max(quotaText.renderedHeight, quotaText.fontSize * 0.1f) * quotaText.transform.lossyScale.y;
        levelText.transform.position = quotaText.transform.position + quotaText.transform.up * (lineHeight * 1.4f);
        levelText.transform.rotation = quotaText.transform.rotation;
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

    /// <summary>この位置が、トラックの中（トリガーの範囲。床や壁の表面も含むよう少し広め）かどうか</summary>
    public bool IsInsideTruck(Vector3 worldPoint)
    {
        const float Margin = 0.25f; // m

        if (area == null)
        {
            foreach (BoxCollider box in GetComponents<BoxCollider>())
            {
                if (box.isTrigger) { area = box; break; }
            }
            if (area == null) return false;
        }

        Vector3 local = area.transform.InverseTransformPoint(worldPoint) - area.center;
        Vector3 scale = area.transform.lossyScale;
        return Mathf.Abs(local.x) <= area.size.x * 0.5f + Margin / Mathf.Max(0.001f, Mathf.Abs(scale.x))
            && Mathf.Abs(local.y) <= area.size.y * 0.5f + Margin / Mathf.Max(0.001f, Mathf.Abs(scale.y))
            && Mathf.Abs(local.z) <= area.size.z * 0.5f + Margin / Mathf.Max(0.001f, Mathf.Abs(scale.z));
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

        if (levelText != null)
            levelText.text = mapGenerator.CurrentStageLevel.ToString();

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
            total += playerController.GetInventoryValue();
        }

        return total;
    }
}