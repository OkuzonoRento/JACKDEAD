using UnityEngine;

public class Item : MonoBehaviour
{
    private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;

    public void SetItemData(ItemDataAsset data)
    {
        itemData = data;
        // 必要であればここで見た目の初期化など
    }

    // 視点を合わせたときに呼び出す情報取得用プロパティ
    public string GetInteractionPrompt()
    {
        if (itemData == null) return "Item";
        return $"アイテム名: {itemData.ItemName}\n売却金額: {itemData.Amount}G";
    }
}