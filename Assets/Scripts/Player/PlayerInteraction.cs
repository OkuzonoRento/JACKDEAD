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

    private float currentHoldTime = 0f;
    private bool isHolding = false;
    private bool hasTriggeredAction = false;

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

        if (currentInteractable != null && currentInteractableTransform != null)
        {
            UpdateUIPosition(currentInteractableTransform, promptWorldOffset);
        }
    }

    private void CheckForInteractables()
    {
        if (isHolding && currentInteractable != null) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            // ★ 子オブジェクトのコライダーに当たっても親から取得できるように GetComponentInParent を使用
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
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

        // プロンプト文字が空文字（スロットボタンなど）の場合はキャンバスを出さない
        string promptStr = currentInteractable != null ? currentInteractable.GetInteractPrompt() : "";
        bool hasPromptText = !string.IsNullOrEmpty(promptStr);

        if (promptRectTransform != null && promptCanvasObj != null)
        {
            if (!hasPromptText)
            {
                if (promptCanvasObj.activeSelf) promptCanvasObj.SetActive(false);
            }
            else
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

        // 長押しサークル（HoldUI）を対象の中心に追従させる
        if (holdUIRectTransform != null)
        {
            Collider col = targetTransform.GetComponent<Collider>();
            Vector3 centerWorldPos = col != null ? col.bounds.center : targetTransform.position;
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

            if (holdUIObj != null && !holdUIObj.activeSelf)
            {
                holdUIObj.SetActive(true);
            }
            if (radialProgressBar != null && !radialProgressBar.gameObject.activeSelf)
            {
                radialProgressBar.gameObject.SetActive(true);
            }

            float targetHoldTime = Mathf.Max(0.05f, currentInteractable.GetHoldTime());
            float progress = Mathf.Clamp01(currentHoldTime / targetHoldTime);

            if (radialProgressBar != null)
            {
                radialProgressBar.fillAmount = progress;
            }

            if (currentHoldTime >= targetHoldTime)
            {
                hasTriggeredAction = true;
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
        string text = interactable.GetInteractPrompt();
        bool hasText = !string.IsNullOrEmpty(text);

        if (promptCanvasObj != null)
        {
            promptCanvasObj.SetActive(hasText);
        }

        if (promptText != null && hasText)
        {
            promptText.text = text;
        }

        if (holdUIObj != null && !isHolding) holdUIObj.SetActive(false);
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
            if (holdUIObj == null) radialProgressBar.gameObject.SetActive(false);
        }

        if (holdUIObj != null)
        {
            holdUIObj.SetActive(false);
        }
    }
}