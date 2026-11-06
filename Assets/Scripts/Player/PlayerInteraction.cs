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
    [SerializeField] private GameObject holdUIObj;
    [SerializeField] private RectTransform holdUIRectTransform;

    [Header("距離に応じた固定サイズ設定")]
    [SerializeField] private float closeScale = 1.2f;
    [SerializeField] private float farScale = 0.6f;
    [SerializeField] private float scaleDistanceThreshold = 1.5f;

    [Header("表示オフセット")]
    [SerializeField] private Vector3 promptWorldOffset = new Vector3(0f, 0.75f, 0f);

    [Header("デフォルト長押し設定（フォールバック用）")]
    [SerializeField] private float defaultRequiredHoldTime = 1.0f;
    private float currentHoldTime = 0f;
    private bool isHolding = false;
    private bool hasTriggeredAction = false;

    // 個別のオブジェクトではなく、IInteractableインターフェースで共通管理
    private IInteractable currentInteractable;
    private Transform currentInteractableTransform;
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
        CheckForInteractables();
        HandleInteractionInput();
        HandleItemDropInput();

        if (currentInteractable != null && currentInteractableTransform != null && promptCanvasObj != null && promptCanvasObj.activeSelf)
        {
            UpdateUIPosition(currentInteractableTransform, promptWorldOffset);
        }
    }

    // ★ 今後は新しいオブジェクトを追加しても、ここをいじる必要は一切ありません！
    private void CheckForInteractables()
    {
        if (isHolding && currentInteractable != null) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            // ぶつかったオブジェクトから IInteractable を持っているか一括で探す
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                currentInteractable = interactable;
                currentInteractableTransform = hit.collider.transform;
                ShowPrompt(interactable);
                return;
            }
        }

        if (!isHolding)
        {
            ClearPrompt();
        }
    }

    private void UpdateUIPosition(Transform targetTransform, Vector3 offset)
    {
        if (mainCamera == null || targetTransform == null) return;

        if (promptRectTransform != null)
        {
            Vector3 textWorldPos = targetTransform.position + offset;
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
    }

    private void HandleInteractionInput()
    {
        bool isInteractPressed = interactAction != null && interactAction.IsPressed();
        bool hasTarget = (currentInteractable != null);

        if (!isInteractPressed)
        {
            hasTriggeredAction = false;
        }

        if (hasTarget && isInteractPressed && !hasTriggeredAction)
        {
            // インタラクト条件を満たしているかチェック
            if (!currentInteractable.CanInteract())
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

            if (radialProgressBar != null && !radialProgressBar.gameObject.activeSelf)
            {
                radialProgressBar.gameObject.SetActive(true);
            }

            float targetHoldTime = currentInteractable.GetHoldTime();
            float progress = Mathf.Clamp01(currentHoldTime / targetHoldTime);

            if (radialProgressBar != null)
            {
                radialProgressBar.fillAmount = progress;
            }

            if (currentHoldTime >= targetHoldTime)
            {
                hasTriggeredAction = true;

                // インタラクト実行！
                currentInteractable.OnInteract();
                ClearPrompt();

                ResetHold();
            }
        }
        else
        {
            if (!isInteractPressed && isHolding)
            {
                ResetHold();
            }
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

    private void ShowPrompt(IInteractable interactable)
    {
        if (promptCanvasObj != null && !promptCanvasObj.activeSelf)
        {
            promptCanvasObj.SetActive(true);
        }

        if (promptText != null)
        {
            promptText.text = interactable.GetInteractPrompt();
        }

        if (holdUIObj != null) holdUIObj.SetActive(false);
    }

    private void ClearPrompt()
    {
        currentInteractable = null;
        currentInteractableTransform = null;
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
            radialProgressBar.gameObject.SetActive(false);
        }

        if (holdUIObj != null)
        {
            holdUIObj.SetActive(false);
        }
    }
}