
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("インベントリ設定")]
    [SerializeField] private int maxSlots = 3;

    private ItemDataAsset[] items;

    public int MaxSlots => maxSlots;

    private void Awake()
    {
        maxSlots = Mathf.Max(1, maxSlots);
        items = new ItemDataAsset[maxSlots];
    }

    public bool AddItem(ItemDataAsset item)
    {
        if (item == null)
        {
            Debug.LogWarning("追加するアイテムデータがありません。");
            return false;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null)
            {
                items[i] = item;
                Debug.Log($"インベントリのスロット{i + 1}に追加：{item.name}");
                return true;
            }
        }

        Debug.Log("インベントリがいっぱいです！");
        return false;
    }

    public ItemDataAsset GetItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= items.Length)
            return null;

        return items[slotIndex];
    }
}
