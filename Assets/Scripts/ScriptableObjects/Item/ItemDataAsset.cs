using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "JackDead/ObjectData/ItemData")]
public class ItemDataAsset : ScriptableObject
{
    [SerializeField, Min(0)] private int amount = 0;
    [SerializeField] private string itemName = "New Item";
    [SerializeField] private Sprite itemIcon;
    [SerializeField, Min(0.0f)] private float itemWeight = 0.0f;
    [SerializeField] private GameObject itemPrefab;

    public int Amount => amount;
    public string ItemName => itemName;
    public Sprite ItemIcon => itemIcon;
    public float ItemWeight => itemWeight;
    public GameObject ItemPrefab => itemPrefab;
}