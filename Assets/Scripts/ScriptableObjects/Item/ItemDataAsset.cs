using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "JackDead/ObjectData/ItemData")]
public class ItemDataAsset : ScriptableObject
{
    [SerializeField, Min(0)] private int amount = 0;
    [SerializeField] private string itemName = "New Item";
    [SerializeField] private Sprite itemIcon;
    [SerializeField, Min(0.0f)] private float itemWeight = 0.0f;
    [SerializeField] private GameObject itemPrefab;

    [Header("壊れやすさ")]
    [SerializeField, Range(0f, 3f), Tooltip("投げてぶつけたときの、金額の減りやすさ（1 が標準。0 なら減らない）")]
    private float fragility = 1f;

    public float Fragility => fragility;

    [Header("投げて当てたときの特別な効果（スタンガンなど）")]
    [SerializeField, Min(0f), Tooltip("当てたプレイヤーがスタンする時間（秒）。0 なら通常の時間")]
    private float playerStunSeconds = 0f;
    [SerializeField, Min(0f), Tooltip("当てた敵がスタンする時間（秒）。0 なら通常の時間")]
    private float enemyStunSeconds = 0f;

    public float PlayerStunSeconds => playerStunSeconds;
    public float EnemyStunSeconds => enemyStunSeconds;

    [Header("手に持って選んでいる間の明かり（懐中電灯など）")]
    [SerializeField, Min(0.1f), Tooltip("自分のライトの強さに掛ける倍率（1 = 変わらない）")]
    private float heldLightIntensity = 1f;
    [SerializeField, Min(0.1f), Tooltip("光が届く距離に掛ける倍率")]
    private float heldLightRange = 1f;
    [SerializeField, Min(0.1f), Tooltip("照らす広さ（角度）に掛ける倍率")]
    private float heldLightAngle = 1f;

    public float HeldLightIntensity => heldLightIntensity;
    public float HeldLightRange => heldLightRange;
    public float HeldLightAngle => heldLightAngle;

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