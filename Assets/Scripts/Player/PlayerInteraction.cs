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
    [SerializeField] private GameObject holdUIObj;

    [Header("表示オフセット")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.75f, 0f);

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
            UpdateUIPosition(currentLookedItem.transform.position + worldOffset);
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

    private void UpdateUIPosition(Vector3 worldPosition)
    {
        if (mainCamera == null || promptRectTransform == null) return;

        Vector3 screenPoint = mainCamera.WorldToScreenPoint(worldPosition);

        if (screenPoint.z < 0)
        {
            if (promptCanvasObj.activeSelf) promptCanvasObj.SetActive(false);
            return;
        }

        if (!promptCanvasObj.activeSelf)
        {
            promptCanvasObj.SetActive(true);
        }

        promptRectTransform.position = screenPoint;
    }

    // インベントリに空きがあるかどうかをチェックする補助メソッド
    private bool HasInventorySpace()
    {
        if (playerController == null || playerController.Inventory == null) return false;

        foreach (var item in playerController.Inventory)
        {
            if (item == null) return true; // 1つでも空きスロットがあればtrue
        }
        return false;
    }

    // 交互作用（Fキー長押しで回収 ＆ Eキー単押しで使用）
    private void HandleInteractionInput()
    {
        bool isInteractPressed = interactAction != null && interactAction.IsPressed();

        // Fキー長押しによる回収
        if (currentLookedItem != null && isInteractPressed)
        {
            // ★ インベントリに空きがない場合はホールド処理を行わずUIも表示しない
            if (!HasInventorySpace())
            {
                ResetHold();
                return;
            }

            isHolding = true;
            currentHoldTime += Time.deltaTime;

            // ホールド中のみ UI（HoldUI / HoldGauge）を表示
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

        // Eキー（Useアクション）によるアイテム使用
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
                for (int i = 0; i < playerController.Inventory.Length; i++)
                {
                    if (playerController.Inventory[i] == null)
                    {
                        playerController.Inventory[i] = item.ItemData;
                        added = true;
                        playerController.SaveInventoryToData();
                        break;
                    }
                }

                if (!added)
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