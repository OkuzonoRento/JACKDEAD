using UnityEngine;

public class Item : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;

    /// <summary>マルチプレイ同期用のID（0 = 同期しない）。マップ生成時のスクラップは生成順の連番、落とした物は GameplaySync.AllocateDropId()</summary>
    public int NetId { get; set; }

    private MapGenerator mapGenerator;

    /// <summary>この高さより下へ落ちたアイテム（落とし穴・マップの外）は、壊れてなくなる。床の高さは 0</summary>
    public const float BreakHeight = -8f;

    // 落ちているアイテムは、見つけやすいように光らせる（爆弾は、点滅で分かるのでそのまま）
    private void Start()
    {
        if (itemData != null && !itemData.IsBomb) ItemHighlight.Attach(gameObject);
    }

    // マルチプレイでは、各PCがそれぞれ自分の画面のアイテムを同じ高さで壊す
    private void Update()
    {
        if (transform.position.y >= BreakHeight) return;

        if (itemData != null) Debug.Log($"[アイテム] {itemData.ItemName} は落下して壊れました。");
        gameObject.SetActive(false); // 破棄されるのは次のフレームなので、先に止める（爆弾が爆発しないように）
        Destroy(gameObject);
    }

    // この1個だけの売値。生成時の揺れや、ぶつけて減った分を持つ（0 以下 = まだ決まっていないので、基準の価格）
    private int ownValue;

    /// <summary>この1個の売値。決まっていなければ、現在のレベルの価格補正を掛けた基準の価格</summary>
    public int CurrentValue => ownValue > 0 ? ownValue : BaseValue;

    /// <summary>現在のレベルの価格補正を掛けた基準の価格（揺れや傷みを含まない）</summary>
    public int BaseValue
    {
        get
        {
            if (itemData == null) return 0;
            if (mapGenerator == null) mapGenerator = FindAnyObjectByType<MapGenerator>();
            return mapGenerator != null ? mapGenerator.GetScrapValue(itemData) : itemData.Amount;
        }
    }

    /// <summary>この1個の売値を決める（生成したとき、持ち物から出したとき）</summary>
    public void SetValue(int value)
    {
        ownValue = Mathf.Max(0, value);
    }

    /// <summary>ぶつけて金額が変わった。減った額を表示し、0 になったら壊れてなくなる</summary>
    public void ApplyDamagedValue(int value)
    {
        int loss = CurrentValue - Mathf.Max(0, value);
        if (loss > 0)
        {
            ItemValuePopup.Spawn(transform.position, loss);
            GameAudio.PlayAt(GameSound.ScrapDamage, transform.position, value > 0 ? 0.75f : 1f, Random.Range(0.9f, 1.15f), 22f);
        }

        if (value > 0)
        {
            ownValue = value;
            return;
        }

        if (itemData != null) Debug.Log($"[アイテム] {itemData.ItemName} はぶつけて壊れました。");
        gameObject.SetActive(false);
        Destroy(gameObject);
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
            if (pc.TryAddItem(itemData, bomb != null ? bomb.Remaining : -1f, CurrentValue))
            {
                GameplaySync.NotifyItemPicked(this);
                Destroy(gameObject);
            }
        }
    }
}