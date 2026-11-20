using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "JackDead/ObjectData/ItemData")]
public class ItemDataAsset : ScriptableObject
{
    [SerializeField, Min(0)] private int amount = 0;
    [SerializeField] private string itemName = "New Item";
    [SerializeField] private Sprite itemIcon;
    [SerializeField, Min(0.0f)] private float itemWeight = 0.0f;
    [SerializeField] private GameObject itemPrefab;

    [Header("インタラクト設定")]
    [SerializeField, Min(0.1f)] private float requiredHoldTime = 1.0f;

    [Header("特殊アイテム")]
    [Tooltip("時限爆弾（持ち続けると爆発する）かどうか。Box 用")]
    [SerializeField] private bool isBomb = false;

    public bool IsBomb => isBomb;
    public int Amount => amount;
    public string ItemName => itemName;
    public Sprite ItemIcon => itemIcon;
    public float ItemWeight => itemWeight;
    public GameObject ItemPrefab => itemPrefab;
    public float RequiredHoldTime => requiredHoldTime;
}