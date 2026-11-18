using UnityEngine;

public class Item : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;

    private MapGenerator mapGenerator;

    /// <summary>
    /// 現在のレベルの価格補正を掛けた売値
    /// </summary>
    public int CurrentValue
    {
        get
        {
            if (itemData == null) return 0;
            if (mapGenerator == null) mapGenerator = FindAnyObjectByType<MapGenerator>();
            return mapGenerator != null ? mapGenerator.GetScrapValue(itemData) : itemData.Amount;
        }
    }

    public void SetItemData(ItemDataAsset data)
    {
        itemData = data;
    }

    // --- IInteractable の実装 ---
    public string GetInteractPrompt()
    {
        if (itemData == null) return "Item";
        return $"{itemData.ItemName}  $  {CurrentValue:N0}";
    }

    public float GetHoldTime()
    {
        return itemData != null ? itemData.RequiredHoldTime : 0.5f;
    }

    public bool CanInteract()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null)
        {
            return pc.HasInventorySpace();
        }
        return true;
    }

    public void OnInteract()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null && itemData != null)
        {
            if (pc.TryAddItem(itemData))
            {
                Destroy(gameObject);
            }
        }
    }
}