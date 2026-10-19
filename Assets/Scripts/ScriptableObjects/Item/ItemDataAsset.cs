using UnityEngine;

[CreateAssetMenu(fileName = "NewItemData", menuName = "JackDead/ObjectData/ItemData")]
public class ItemDataAsset : ScriptableObject
{
    [Min(0)] public int Amount = 0;
    [SerializeField] private string itemName = "New Item";
    [SerializeField] private Sprite itemIcon;
    [Min(0.0f)] private float itemWeight = 0.0f;

}
