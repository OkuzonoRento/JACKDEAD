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

    [Header("選択スロット（0〜2）")]
    [SerializeField] private int selectedSlotIndex = 0;

    private InputAction interactAction;
    private InputAction dropAction;
    private InputAction slot1Action; // ★ 追加
    private InputAction slot2Action; // ★ 追加
    private InputAction slot3Action; // ★ 追加

    private IInteractable currentInteractable;
    private float currentHoldTimer = 0f;
    private bool isHolding = false;
    private bool hasInteractedThisPress = false;

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

                // ★ New Input Systemからスロット選択アクションを取得
                slot1Action = map.FindAction("SelectSlot1");
                slot2Action = map.FindAction("SelectSlot2");
                slot3Action = map.FindAction("SelectSlot3");

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
        HandleSlotSelection(); // 新仕様のスロット選択処理
        CheckTarget();
        HandleHoldInteraction();

        if (dropAction != null && dropAction.triggered)
        {
            DropSelectedItem();
        }
    }

    // --- New Input System を用いたスロット選択 ---
    private void HandleSlotSelection()
    {
        // 1, 2, 3 キーのアクションがトリガーされたかチェック
        if (slot1Action != null && slot1Action.triggered)
        {
            selectedSlotIndex = 0;
            Debug.Log("[スロット] 1を選択");
        }
        if (slot2Action != null && slot2Action.triggered)
        {
            selectedSlotIndex = 1;
            Debug.Log("[スロット] 2を選択");
        }
        if (slot3Action != null && slot3Action.triggered)
        {
            selectedSlotIndex = 2;
            Debug.Log("[スロット] 3を選択");
        }

        // マウスホイールでの切り替え（従来通り、あるいは必要に応じて調整）
        float scrollY = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (scrollY > 0f)
        {
            selectedSlotIndex = (selectedSlotIndex - 1 + 3) % 3;
        }
        else if (scrollY < 0f)
        {
            selectedSlotIndex = (selectedSlotIndex + 1) % 3;
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

        if (hasInteractedThisPress)
        {
            if (!interactAction.IsPressed())
            {
                hasInteractedThisPress = false;
            }
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
                hasInteractedThisPress = true;
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

    public bool TryPickupItem(ItemDataAsset item)
    {
        if (playerController == null || item == null) return false;

        for (int i = 0; i < playerController.Inventory.Length; i++)
        {
            if (playerController.Inventory[i] == null)
            {
                playerController.Inventory[i] = item;
                playerController.SaveInventoryToData();
                Debug.Log($"[インベントリ] Slot {i} に {item.ItemName} を格納しました");
                return true;
            }
        }

        Debug.LogWarning("[インベントリ] 満杯です！");
        return false;
    }

    private void DropSelectedItem()
    {
        if (playerController == null) return;

        ItemDataAsset itemToDrop = playerController.Inventory[selectedSlotIndex];
        if (itemToDrop != null)
        {
            playerController.Inventory[selectedSlotIndex] = null;
            playerController.SaveInventoryToData();

            GameObject prefabToSpawn = itemToDrop.ItemPrefab;
            Vector3 spawnPos = cameraTransform.position + cameraTransform.forward * 0.8f;

            GameObject droppedObj;
            if (prefabToSpawn != null)
            {
                droppedObj = Instantiate(prefabToSpawn, spawnPos, cameraTransform.rotation);
            }
            else
            {
                droppedObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                droppedObj.transform.position = spawnPos;
                droppedObj.transform.localScale = Vector3.one * 0.4f;
                droppedObj.AddComponent<Rigidbody>();
                droppedObj.AddComponent<BoxCollider>();
            }

            Item worldItem = droppedObj.GetComponent<Item>();
            if (worldItem == null)
            {
                worldItem = droppedObj.AddComponent<Item>();
            }
            worldItem.SetItemData(itemToDrop);

            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float weightFactor = Mathf.Clamp(1.0f / (1.0f + itemToDrop.ItemWeight * 0.5f), 0.1f, 1.0f);
                float finalForce = baseThrowForce * weightFactor;

                Vector3 throwDir = (cameraTransform.forward + Vector3.up * 0.2f).normalized;
                rb.AddForce(throwDir * finalForce, ForceMode.Impulse);

                Debug.Log($"[Q] スロット {selectedSlotIndex + 1} の {itemToDrop.ItemName} を投げました");
            }
        }
        else
        {
            Debug.Log($"[Q] スロット {selectedSlotIndex + 1} は空です。");
        }
    }
}