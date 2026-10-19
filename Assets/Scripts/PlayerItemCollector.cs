
using UnityEngine;

public class PlayerItemCollector : MonoBehaviour
{
    [Header("回収設定")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private KeyCode pickupKey = KeyCode.F;
    private PlayerInventory inventory;
    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();

        if (inventory == null)
        {
            Debug.LogError("PlayerInventoryがPlayerに付いていません！", this);
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
        }


        if (playerCamera == null)
        {
            Debug.LogError(
                "PlayerItemCollector：カメラが見つかりません！",
                this
            );
        }
    }

    private void Update()
    {
        // Fキーが押されたか確認
        if (!Input.GetKeyDown(pickupKey))
            return;

        Debug.Log("Fキーを押した！", this);

        if (playerCamera == null)
        {
            Debug.LogError("カメラが設定されていません！", this);
            return;
        }

        // カメラの正面にRayを飛ばす
        Ray ray = new Ray(
     playerCamera.transform.position + playerCamera.transform.forward * 0.5f,
     playerCamera.transform.forward
         );

        Debug.DrawRay(
            ray.origin,
            ray.direction * pickupRange,
            Color.red,
            2f
        );

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, ~LayerMask.GetMask("Player")))
        {
            Debug.Log(
               "Rayが当たったColliderの親：" +
              hit.collider.transform.root.name
            );

            CollectibleItem item =
                hit.collider.GetComponentInParent<CollectibleItem>();

            if (item == null)
            {
                Debug.Log(
                    "このオブジェクトにはCollectibleItemがありません。",
                    hit.collider.gameObject
                );
                return;
            }

            if (item.ItemData == null)
            {
                Debug.LogWarning(
                    "アイテムデータが設定されていません！",
                    item.gameObject
                );
                return;
            }

            Debug.Log(
                "回収対象を発見：" + item.ItemData.name,
                item.gameObject
            );

            bool added = inventory != null && inventory.AddItem(item.ItemData);

            if (!added)
            {
                Debug.Log("回収できませんでした。インベントリが満杯か確認してね！");
                return;
            }

            Destroy(item.gameObject);

            Debug.Log("アイテムを回収してインベントリに保存した！");
        }

        else
        {
            Debug.Log("カメラの正面にオブジェクトがありません。", this);
        }
    }
}
