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

    [Header("レティクル設定")]
    [Tooltip("画面中央のレティクルImage")]
    [SerializeField] private Image reticleImage;
    [Tooltip("通常時のレティクル色")]
    [SerializeField] private Color normalReticleColor = Color.white;
    [Tooltip("インタラクト可能対象に照準が合っているときのレティクル色")]
    [SerializeField] private Color interactableReticleColor = new Color(0.3f, 1f, 0.4f, 1f);

    [Header("UI参照 (プロンプト・HoldUI)")]
    [SerializeField] private GameObject promptCanvasObj;
    [SerializeField] private RectTransform promptRectTransform;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private GameObject holdUIObj;
    [SerializeField] private RectTransform holdUIRectTransform;
    [SerializeField] private Image radialProgressBar;

    [Header("距離に応じたHoldUIサイズ設定")]
    [SerializeField] private float closeScale = 1.2f;
    [SerializeField] private float farScale = 0.6f;
    [SerializeField] private float scaleDistanceThreshold = 1.5f;

    [Header("表示オフセット (テキスト用)")]
    [SerializeField] private Vector3 promptWorldOffset = new Vector3(0f, 0.75f, 0f);

    [Header("長押し時間設定（個別管理）")]
    [Tooltip("アイテム等のデフォルト長押し時間（秒）")]
    [SerializeField, Min(0.05f)] private float defaultRequiredHoldTime = 1.0f;
    [Tooltip("スロットのボタン専用の長押し時間（秒）※アイテム等とは別設定")]
    [SerializeField, Min(0.05f)] private float slotButtonHoldTime = 0.4f;

    private const float MinHoldDuration = 0.05f;

    private float currentHoldTime = 0f;
    private bool isHolding = false;
    private bool hasTriggeredAction = false;

    private IInteractable currentInteractable;
    private Transform currentInteractableTransform;
    private Collider currentInteractableCollider;
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

        UpdateReticleColor(false);
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
        UpdateActiveUIPositions();
    }

    private void CheckForInteractables()
    {
        if (cameraTransform == null) return;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = hit.collider.GetComponentInParent<IInteractable>();
            }

            if (interactable != null)
            {
                if (currentInteractable != interactable)
                {
                    ResetHold();
                    currentInteractable = interactable;
                    currentInteractableTransform = hit.collider.transform;
                    currentInteractableCollider = hit.collider;
                }

                bool canInteractNow = currentInteractable.CanInteract();
                UpdateReticleColor(canInteractNow);
                ShowPrompt(currentInteractable);
                return;
            }
        }

        UpdateReticleColor(false);
        ClearPrompt();
    }

    private void UpdateReticleColor(bool isInteractableTarget)
    {
        if (reticleImage == null) return;
        reticleImage.color = isInteractableTarget ? interactableReticleColor : normalReticleColor;
    }

    private void UpdateActiveUIPositions()
    {
        if (mainCamera == null || currentInteractable == null || currentInteractableTransform == null) return;

        // 1. 親またはテキストプロンプトの座標更新
        if (promptRectTransform != null)
        {
            Vector3 textWorldPos = currentInteractableTransform.position + promptWorldOffset;
            Vector3 textScreenPoint = mainCamera.WorldToScreenPoint(textWorldPos);

            if (textScreenPoint.z >= 0f)
            {
                if (promptCanvasObj != null && !promptCanvasObj.activeSelf)
                {
                    promptCanvasObj.SetActive(true);
                }
                promptRectTransform.position = textScreenPoint;
            }
            else
            {
                if (promptCanvasObj != null && promptCanvasObj.activeSelf)
                {
                    promptCanvasObj.SetActive(false);
                }
                return;
            }
        }

        // 2. HoldUIの座標・サイズ更新（親の移動後に上書きすることで、ボタン等の中心へ正確に固定）
        if (holdUIRectTransform != null && isHolding)
        {
            Vector3 centerWorldPos = currentInteractableCollider != null
                ? currentInteractableCollider.bounds.center
                : currentInteractableTransform.position;

            Vector3 centerScreenPoint = mainCamera.WorldToScreenPoint(centerWorldPos);

            if (centerScreenPoint.z >= 0f)
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
        bool hasTarget = currentInteractable != null;

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

            // HoldUI が promptCanvasObj の子階層にある場合でも確実に表示されるよう親も有効化
            if (promptCanvasObj != null && !promptCanvasObj.activeSelf)
            {
                promptCanvasObj.SetActive(true);
            }
            if (holdUIObj != null && !holdUIObj.activeSelf)
            {
                holdUIObj.SetActive(true);
            }
            if (radialProgressBar != null && !radialProgressBar.gameObject.activeSelf)
            {
                radialProgressBar.gameObject.SetActive(true);
            }

            // ★ スロットボタンとアイテム等で長押し時間を別々に取得
            float targetHoldTime = GetTargetHoldDuration(currentInteractable);
            float progress = Mathf.Clamp01(currentHoldTime / targetHoldTime);

            if (radialProgressBar != null)
            {
                radialProgressBar.fillAmount = progress;
            }

            if (currentHoldTime >= targetHoldTime)
            {
                hasTriggeredAction = true;

                IInteractable targetToTrigger = currentInteractable;
                ResetHold();
                ClearPrompt();
                targetToTrigger.OnInteract();
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

    /// <summary>
    /// 対象に応じた長押し時間を返す（スロットボタンは専用時間、アイテム等は各自の設定時間）
    /// </summary>
    private float GetTargetHoldDuration(IInteractable interactable)
    {
        if (interactable is SlotButtonInteractable)
        {
            return Mathf.Max(MinHoldDuration, slotButtonHoldTime);
        }

        float customTime = interactable.GetHoldTime();
        float resolvedTime = customTime > 0f ? customTime : defaultRequiredHoldTime;
        return Mathf.Max(MinHoldDuration, resolvedTime);
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
        // HoldUIの親が非アクティブにならないよう promptCanvasObj 自体は有効にする
        if (promptCanvasObj != null && !promptCanvasObj.activeSelf)
        {
            promptCanvasObj.SetActive(true);
        }

        if (promptText != null)
        {
            // スロットボタンは GetInteractPrompt() が "" なので文字は表示されない
            promptText.text = interactable.GetInteractPrompt();
        }
    }

    private void ClearPrompt()
    {
        currentInteractable = null;
        currentInteractableTransform = null;
        currentInteractableCollider = null;

        if (promptText != null)
        {
            promptText.text = "";
        }

        if (promptCanvasObj != null && promptCanvasObj.activeSelf)
        {
            promptCanvasObj.SetActive(false);
        }

        ResetHold();
    }

    private void ResetHold()
    {
        isHolding = false;
        currentHoldTime = 0f;

        if (radialProgressBar != null)
        {
            radialProgressBar.fillAmount = 0f;
            if (holdUIObj == null)
            {
                radialProgressBar.gameObject.SetActive(false);
            }
        }

        if (holdUIObj != null && holdUIObj.activeSelf)
        {
            holdUIObj.SetActive(false);
        }
    }
}