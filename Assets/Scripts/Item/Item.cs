using UnityEngine;

public class Item : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;

    public void SetItemData(ItemDataAsset data)
    {
        itemData = data;
    }

    // --- IInteractable ‚ÌŽÀ‘• ---
    public string GetInteractPrompt()
    {
        if (itemData == null) return "Item";
        return $"{itemData.ItemName}  $  {itemData.Amount}";
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