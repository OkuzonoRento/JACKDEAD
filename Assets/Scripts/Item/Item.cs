using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Item : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDataAsset itemData;
    [SerializeField] private float quickHoldTime = 0.15f;

    public ItemDataAsset ItemData => itemData;

    public void SetItemData(ItemDataAsset data)
    {
        itemData = data;
    }

    public string GetInteractPrompt()
    {
        return itemData != null ? $"[F] {itemData.ItemName} ‚ðE‚¤" : "[F] E‚¤";
    }

    public float GetHoldTime() => quickHoldTime;

    public void OnInteract(PlayerInteraction interactor)
    {
        if (interactor.TryPickupItem(itemData))
        {
            Destroy(gameObject);
        }
    }
}