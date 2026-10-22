using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerInteraction : MonoBehaviour
{
    [Header("プレイヤーコントローラー参照")]
    [SerializeField] private PlayerController playerController;

    [Header("インタラクト設定")]
    [SerializeField] private float interactRange = 3.0f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private Transform cameraTransform;

    [Header("UI参照 (TextMeshPro版)")]
    [SerializeField] private GameObject promptCanvasObj;
    [SerializeField] private RectTransform promptRectTransform;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Image radialProgressBar;

    [Header("HoldUI 参照 (アイテム中心に重畳)")]
    [SerializeField] private GameObject holdUIObj;              // HoldUIルート
    [SerializeField] private RectTransform holdUIRectTransform; // HoldUIのRectTransform

    [Header("距離に応じた固定サイズ設定")]
    [SerializeField] private float closeScale = 1.2f;   // 近いときのサイズ
    [SerializeField] private float farScale = 0.6f;     // 遠いときのサイズ
    [SerializeField] private float scaleDistanceThreshold = 1.5f; // サイズを切り替える境界の距離

    [Header("表示オフセット (ItemInfo用)")]
    [SerializeField] private Vector3 promptWorldOffset = new Vector3(0f, 0.75f, 0f);

    [Header("デフォルト長押し設定（フォールバック用）")]
    [SerializeField] private float defaultRequiredHoldTime = 1.0f;
    private float currentHoldTime = 0f;
    private bool isHolding = false;

    private Item currentLookedItem;
    private Camera mainCamera;

    private InputActionAsset playerInputActions;
    private InputAction interactAction;
    private InputAction useAction;
    private InputAction dropAction;

    private void Awake()
    {
        mainCamera = Camera.main;
        if (cameraTransform == null && mainCamera != null)
        {
            cameraTransform = mainCamera.transform;
        }

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
    }

    private void OnEnable()
    {
        if (playerController != null)
        {
            playerInputActions = playerController.PlayerInputActionsAsset;
        }

        if (playerInputActions != null)
        {
            var map = playerInputActions.FindActionMap("Player");
            if (map != null)
            {
                interactAction = map.FindAction("Interact");
                useAction = map.FindAction("Use");
                dropAction = map.FindAction("Drop");
            }
        }
    }

    private void Update()
    {
        CheckForItems();
        HandleInteractionInput();
        HandleItemDropInput();

        if (currentLookedItem != null && promptCanvasObj != null && promptCanvasObj.activeSelf)
        {
            UpdateUIPosition(currentLookedItem);
        }
    }

    private void CheckForItems()
    {
        if (isHolding && currentLookedItem != null) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            Item item = hit.collider.GetComponent<Item>();
            if (item != null)
            {
                currentLookedItem = item;
                ShowItemPrompt(item);
                return;
            }
        }

        if (!isHolding)
        {
            ClearItemPrompt();
        }
    }

    private void UpdateUIPosition(Item targetItem)
    {
        if (mainCamera == null || targetItem == null) return;

        // 1. テキスト（ItemInfo）はアイテムの頭上に配置
        if (promptRectTransform != null)
        {
            Vector3 textWorldPos = targetItem.transform.position + promptWorldOffset;
            Vector3 textScreenPoint = mainCamera.WorldToScreenPoint(textWorldPos);

            if (textScreenPoint.z >= 0)
            {
                if (!promptCanvasObj.activeSelf) promptCanvasObj.SetActive(true);
                promptRectTransform.position = textScreenPoint;
            }
            else
            {
                if (promptCanvasObj.activeSelf) promptCanvasObj.SetActive(false);
                return;
            }
        }

        // 2. HoldUI はアイテムの正確な中心点に配置
        if (holdUIRectTransform != null)
        {
            Collider col = targetItem.GetComponent<Collider>();
            Vector3 centerWorldPos = col != null ? col.bounds.center : targetItem.transform.position;

            Vector3 centerScreenPoint = mainCamera.WorldToScreenPoint(centerWorldPos);

            if (centerScreenPoint.z >= 0)
            {
                holdUIRectTransform.position = centerScreenPoint;

                float distance = Vector3.Distance(cameraTransform.position, centerWorldPos);
                float targetScale = distance <= scaleDistanceThreshold ? closeScale : farScale;

                holdUIRectTransform.localScale = new Vector3(targetScale, targetScale, 1f);
            }
        }
    }

    private bool HasInventorySpace()
    {
        if (playerController == null || playerController.Inventory == null) return false;

        foreach (var item in playerController.Inventory)
        {
            if (item == null) return true;
        }
        return false;
    }

    private void HandleInteractionInput()
    {
        bool isInteractPressed = interactAction != null && interactAction.IsPressed();

        if (currentLookedItem != null && isInteractPressed)
        {
            if (!HasInventorySpace())
            {
                ResetHold();
                return;
            }

            if (!isHolding)
            {
                isHolding = true;
                currentHoldTime = 0f;
            }

            currentHoldTime += Time.deltaTime;

            if (holdUIObj != null && !holdUIObj.activeSelf)
            {
                holdUIObj.SetActive(true);
            }
            else if (radialProgressBar != null && !radialProgressBar.gameObject.activeSelf)
            {
                radialProgressBar.gameObject.SetActive(true);
            }

            float targetHoldTime = (currentLookedItem.ItemData != null) ? currentLookedItem.ItemData.RequiredHoldTime : defaultRequiredHoldTime;
            float progress = Mathf.Clamp01(currentHoldTime / targetHoldTime);

            if (radialProgressBar != null)
            {
                radialProgressBar.fillAmount = progress;
            }

            if (currentHoldTime >= targetHoldTime)
            {
                CompleteInteraction(currentLookedItem);
                ResetHold();
            }
        }
        else
        {
            if (isHolding) ResetHold();
        }

        if (useAction != null && useAction.triggered)
        {
            if (playerController != null)
            {
                playerController.UseSelectedInventoryItem();
            }
        }
    }

    private void HandleItemDropInput()
    {
        if (dropAction != null && dropAction.triggered)
        {
            if (playerController != null)
            {
                playerController.DropSelectedInventoryItem(cameraTransform);
            }
        }
    }

    private void ShowItemPrompt(Item item)
    {
        if (promptCanvasObj != null && !promptCanvasObj.activeSelf)
        {
            promptCanvasObj.SetActive(true);
        }

        if (promptText != null && item.ItemData != null)
        {
            promptText.text = $"{item.ItemData.ItemName}  $  {item.ItemData.Amount}";
        }

        if (holdUIObj != null) holdUIObj.SetActive(false);
        if (radialProgressBar != null && holdUIObj == null) radialProgressBar.gameObject.SetActive(false);
    }

    private void ClearItemPrompt()
    {
        currentLookedItem = null;
        if (promptCanvasObj != null && !isHolding) promptCanvasObj.SetActive(false);
        ResetHold();
    }

    private void ResetHold()
    {
        isHolding = false;
        currentHoldTime = 0f;

        if (radialProgressBar != null)
        {
            radialProgressBar.fillAmount = 0f;
            if (holdUIObj == null) radialProgressBar.gameObject.SetActive(false);
        }

        if (holdUIObj != null)
        {
            holdUIObj.SetActive(false);
        }
    }

    private void CompleteInteraction(Item item)
    {
        if (item.ItemData != null)
        {
            Debug.Log($"[インタラクト] {item.ItemData.ItemName} を回収しました！");

            if (playerController != null)
            {
                bool added = false;

                // ★ 1. まず現在選択しているスロットが空いているか確認して優先的に格納
                int selectedSlot = playerController.SelectedSlotIndex;
                if (playerController.Inventory[selectedSlot] == null)
                {
                    playerController.Inventory[selectedSlot] = item.ItemData;
                    added = true;
                }
                else
                {
                    // ★ 2. 選択中スロットが埋まっている場合は、他の空きスロットを探す
                    for (int i = 0; i < playerController.Inventory.Length; i++)
                    {
                        if (playerController.Inventory[i] == null)
                        {
                            playerController.Inventory[i] = item.ItemData;
                            added = true;
                            break;
                        }
                    }
                }

                if (added)
                {
                    playerController.SaveInventoryToData();
                }
                else
                {
                    Debug.LogWarning("[インベントリ] 満杯です！");
                    return;
                }
            }
        }

        Destroy(item.gameObject);
        ClearItemPrompt();
    }
}