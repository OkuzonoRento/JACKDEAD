
using UnityEngine;

public class CollectibleItem : MonoBehaviour
{
    [Header("アイテムデータ")]
    [SerializeField] private ItemDataAsset itemData;

    public ItemDataAsset ItemData => itemData;
}
