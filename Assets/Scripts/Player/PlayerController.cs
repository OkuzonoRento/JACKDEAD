using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Player Input Actions アセット")]
    [SerializeField] private InputActionAsset playerInputActions;

    [Header("プレイヤーデータ参照")]
    public PlayerDataAsset PlayerData;

    [Header("投擲設定")]
    [SerializeField, Tooltip("アイテムを投げる際の基準となる勢い（重さで割られます）")]
    private float baseThrowForce = 3.5f; // ← インスペクターから変更可能になりました！

    private float _health;
    private float _sanity;
    private float _dopamine;
    private float _stamina;

    private float _maxHealth;
    private float _maxSanity;
    private float _maxDopamine;
    private float _maxStamina;

    [Header("インベントリ (所持アイテム)")]
    public ItemDataAsset[] Inventory = new ItemDataAsset[3];

    [Header("選択中スロット管理")]
    [SerializeField] private int selectedSlotIndex = 0;
    public int SelectedSlotIndex => selectedSlotIndex;

    // 外部（PlayerMovementやPlayerInteraction）から参照できるようにプロパティとして公開
    public InputActionAsset PlayerInputActionsAsset => playerInputActions;

    private InputAction slot1Action;
    private InputAction slot2Action;
    private InputAction slot3Action;

    public float Health => _health;
    public float Sanity => _sanity;
    public float Dopamine => _dopamine;
    public float Stamina => _stamina;

    private void Awake()
    {
        if (playerInputActions != null)
        {
            var map = playerInputActions.FindActionMap("Player");
            if (map != null)
            {
                slot1Action = map.FindAction("SelectSlot1");
                slot2Action = map.FindAction("SelectSlot2");
                slot3Action = map.FindAction("SelectSlot3");
            }
        }
    }

    private void OnEnable()
    {
        playerInputActions?.FindActionMap("Player")?.Enable();
    }

    private void OnDisable()
    {
        playerInputActions?.FindActionMap("Player")?.Disable();
    }

    private void Start()
    {
        StatusSetUp();
    }

    private void StatusSetUp()
    {
        if (PlayerData != null)
        {
            _maxHealth = PlayerData.MaxHealth;
            _health = PlayerData.MaxHealth;

            _maxSanity = PlayerData.MaxSanity;
            _sanity = PlayerData.MaxSanity;

            _maxDopamine = PlayerData.MaxDopamine;
            _dopamine = PlayerData.MaxDopamine;

            _maxStamina = PlayerData.MaxStamina;
            _stamina = PlayerData.MaxStamina;

            if (PlayerData.Inventory != null && PlayerData.Inventory.Length >= 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    Inventory[i] = PlayerData.Inventory[i];
                }
            }
        }
    }

    private void Update()
    {
        ConsumeSanity(Time.deltaTime * 0.15f);
        HandleSlotSelectionInput();
    }

    // --- スロット選択 (数字キー ＆ マウスホイール) ---
    private void HandleSlotSelectionInput()
    {
        if (slot1Action != null && slot1Action.triggered) SelectSlot(0);
        if (slot2Action != null && slot2Action.triggered) SelectSlot(1);
        if (slot3Action != null && slot3Action.triggered) SelectSlot(2);

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scrollValue = mouse.scroll.ReadValue().y;
            if (scrollValue > 0f)
            {
                int nextSlot = (selectedSlotIndex - 1 + Inventory.Length) % Inventory.Length;
                SelectSlot(nextSlot);
            }
            else if (scrollValue < 0f)
            {
                int nextSlot = (selectedSlotIndex + 1) % Inventory.Length;
                SelectSlot(nextSlot);
            }
        }
    }

    public void SelectSlot(int index)
    {
        if (index >= 0 && index < Inventory.Length)
        {
            selectedSlotIndex = index;
            Debug.Log($"[インベントリ] スロット [{selectedSlotIndex + 1}] を選択しました。");
        }
    }

    // --- Qキー：アイテム投擲 ---
    public void DropSelectedInventoryItem(Transform cameraTransform)
    {
        if (selectedSlotIndex < 0 || selectedSlotIndex >= Inventory.Length) return;

        ItemDataAsset itemDataToDrop = Inventory[selectedSlotIndex];
        if (itemDataToDrop == null) return;

        if (itemDataToDrop.ItemPrefab != null)
        {
            Vector3 spawnPosition = cameraTransform != null
                ? cameraTransform.position + cameraTransform.forward * 1.2f
                : transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;

            GameObject droppedObj = Instantiate(itemDataToDrop.ItemPrefab, spawnPosition, Quaternion.identity);
            droppedObj.layer = LayerMask.NameToLayer("Items");

            Item itemComp = droppedObj.GetComponent<Item>();
            if (itemComp == null) itemComp = droppedObj.AddComponent<Item>();
            itemComp.SetItemData(itemDataToDrop);

            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb == null) rb = droppedObj.AddComponent<Rigidbody>();

            // インスペクターで設定した変数（baseThrowForce）を使用します
            float weightFactor = Mathf.Max(0.1f, itemDataToDrop.ItemWeight);
            float throwForce = baseThrowForce / weightFactor;

            Vector3 throwDirection = cameraTransform != null
                ? (cameraTransform.forward + Vector3.up * 0.2f).normalized
                : (transform.forward + Vector3.up * 0.2f).normalized;

            rb.linearVelocity = Vector3.zero;
            rb.AddForce(throwDirection * throwForce, ForceMode.VelocityChange);

            Debug.Log($"[インベントリ] {itemDataToDrop.ItemName} を投げました（重さ: {itemDataToDrop.ItemWeight}）。");
        }

        Inventory[selectedSlotIndex] = null;
        SaveInventoryToData();
    }

    // --- Eキー：アイテム使用 ---
    public void UseSelectedInventoryItem()
    {
        if (selectedSlotIndex < 0 || selectedSlotIndex >= Inventory.Length) return;

        ItemDataAsset itemDataToUse = Inventory[selectedSlotIndex];
        if (itemDataToUse == null) return;

        Debug.Log($"[インベントリ] {itemDataToUse.ItemName} を使用しました！");
        Inventory[selectedSlotIndex] = null;
        SaveInventoryToData();
    }

    public float GetTotalInventoryWeight()
    {
        float totalWeight = 0f;
        foreach (var item in Inventory)
        {
            if (item != null)
            {
                totalWeight += item.ItemWeight;
            }
        }
        return totalWeight;
    }

    public void ConsumeSanity(float amount)
    {
        if (amount <= 0) return;
        if (_dopamine > 0f)
        {
            float consumeDopamine = Mathf.Min(_dopamine, amount);
            _dopamine -= consumeDopamine;
            float remainder = amount - consumeDopamine;
            if (remainder > 0f) _sanity = Mathf.Max(0f, _sanity - remainder);
        }
        else
        {
            _sanity = Mathf.Max(0f, _sanity - amount);
        }
    }

    public void SaveInventoryToData()
    {
        if (PlayerData != null)
        {
            if (PlayerData.Inventory == null || PlayerData.Inventory.Length < 3)
            {
                PlayerData.Inventory = new ItemDataAsset[3];
            }
            for (int i = 0; i < 3; i++)
            {
                PlayerData.Inventory[i] = Inventory[i];
            }
        }
    }
}