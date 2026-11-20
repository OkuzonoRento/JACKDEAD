using UnityEngine;

public class Item : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;

    /// <summary>マルチプレイ同期用のID（0 = 同期しない）。マップ生成時のスクラップは生成順の連番、落とした物は GameplaySync.AllocateDropId()</summary>
    public int NetId { get; set; }

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
        if (itemData.IsBomb) return $"{itemData.ItemName}（時限爆弾）";
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
            // マルチプレイ：すでに他の人が拾っていたら拾えない。参加者はホストの許可が出てから受け取る（同時に拾っても複製されない）
            if (GameplaySync.IsClaimed(this)) return;
            if (GameplaySync.RequestPickIfClient(this)) return;

            // 時限爆弾なら、導火線の残りを持ち物へ引き継ぐ
            BombBox bomb = GetComponent<BombBox>();
            if (pc.TryAddItem(itemData, bomb != null ? bomb.Remaining : -1f))
            {
                GameplaySync.NotifyItemPicked(this);
                Destroy(gameObject);
            }
        }
    }
}