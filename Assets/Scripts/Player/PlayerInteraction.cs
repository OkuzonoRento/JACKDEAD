using NUnit.Framework.Internal.Execution;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Input Actions アセット")]
    [SerializeField] private InputActionAsset playerInputActions;

    [Header("参照")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Transform cameraTransform;

    [Header("レイキャスト・インタラクト設定")]
    [SerializeField] private float interactRange = 3.0f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("投擲（Qキー）設定")]
    [SerializeField] private float baseThrowForce = 8.0f;

    private InputAction interactAction;
    private InputAction dropAction;

    private IInteractable currentInteractable;
    private float currentHoldTimer = 0f;
    private bool isHolding = false;

    private void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    private void OnEnable()
    {
        if (playerInputActions != null)
        {
            var map = playerInputActions.FindActionMap("Player");
            if (map != null)
            {
                interactAction = map.FindAction("Interact");
                dropAction = map.FindAction("Drop");
                map.Enable();
            }
        }
    }

    private void OnDisable()
    {
        playerInputActions?.FindActionMap("Player")?.Disable();
    }

    private void Update()
    {
        CheckTarget();
        HandleHoldInteraction();

        if (dropAction != null && dropAction.triggered)
        {
            DropItem();
        }
    }

    private void CheckTarget()
    {
        if (cameraTransform == null) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            var interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                if (currentInteractable != interactable)
                {
                    currentInteractable = interactable;
                    ResetHold();
                }
                return;
            }
        }

        currentInteractable = null;
        ResetHold();
    }

    private void HandleHoldInteraction()
    {
        if (interactAction == null || currentInteractable == null)
        {
            ResetHold();
            return;
        }

        float requiredTime = currentInteractable.GetHoldTime();

        if (interactAction.IsPressed())
        {
            isHolding = true;
            currentHoldTimer += Time.deltaTime;

            if (currentHoldTimer >= requiredTime)
            {
                currentInteractable.OnInteract(this);
                ResetHold();
            }
        }
        else
        {
            if (isHolding) ResetHold();
        }
    }

    private void ResetHold()
    {
        isHolding = false;
        currentHoldTimer = 0f;
    }

    // アイテムを拾う処理 ＆ PlayerDataAssetへの同期
    public bool TryPickupItem(ItemDataAsset item)
    {
        if (playerController == null || item == null) return false;

        for (int i = 0; i < playerController.Inventory.Length; i++)
        {
            if (playerController.Inventory[i] == null)
            {
                playerController.Inventory[i] = item;

                // PlayerDataAsset 側へ保存を反映
                playerController.SaveInventoryToData();

                Debug.Log($"[インベントリ] Slot {i} に {item.ItemName} を格納しました（データ保存完了）");
                return true;
            }
        }

        Debug.LogWarning("[インベントリ] 満杯です！");
        return false;
    }

    // Qキー：ItemDataAsset内のプレハブを参照して生成し、重さで飛ばす
    private void DropItem()
    {
        if (playerController == null) return;

        for (int i = playerController.Inventory.Length - 1; i >= 0; i--)
        {
            ItemDataAsset itemToDrop = playerController.Inventory[i];
            if (itemToDrop != null)
            {
                // インベントリから削除 ＆ データの同期
                playerController.Inventory[i] = null;
                playerController.SaveInventoryToData();

                // ItemDataAsset が持っているプレハブをスポーン（未設定の場合はエラー回避のフォールバック処理）
                GameObject prefabToSpawn = itemToDrop.ItemPrefab;
                Vector3 spawnPos = cameraTransform.position + cameraTransform.forward * 0.8f;

                GameObject droppedObj;
                if (prefabToSpawn != null)
                {
                    droppedObj = Instantiate(prefabToSpawn, spawnPos, cameraTransform.rotation);
                }
                else
                {
                    // プレハブが未設定の場合のダミー生成
                    droppedObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    droppedObj.transform.position = spawnPos;
                    droppedObj.transform.localScale = Vector3.one * 0.4f;
                    droppedObj.AddComponent<Rigidbody>();
                    droppedObj.AddComponent<BoxCollider>();
                }

                // WorldItem コンポーネントにデータをセット
                Item worldItem = droppedObj.GetComponent<Item>();
                if (worldItem == null)
                {
                    worldItem = droppedObj.AddComponent<Item>();
                }
                worldItem.SetItemData(itemToDrop);

                // 重さ（ItemWeight）による投擲距離の計算
                Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    float weightFactor = Mathf.Clamp(1.0f / (1.0f + itemToDrop.ItemWeight * 0.5f), 0.1f, 1.0f);
                    float finalForce = baseThrowForce * weightFactor;

                    Vector3 throwDir = (cameraTransform.forward + Vector3.up * 0.2f).normalized;
                    rb.AddForce(throwDir * finalForce, ForceMode.Impulse);

                    Debug.Log($"[Q] {itemToDrop.ItemName} (重さ: {itemToDrop.ItemWeight}kg) を投げました");
                }

                break;
            }
        }
    }
}