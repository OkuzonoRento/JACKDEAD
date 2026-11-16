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
    private float baseThrowForce = 3.5f;

    [Header("正気度・クリア時回復設定")]
    [SerializeField, Tooltip("1秒あたりの基本正気度減少量")]
    private float sanityDrainPerSecond = 0.15f;
    [SerializeField, Tooltip("各マップクリア時に回復する正気度（Sanity）の量")]
    private float sanityRecoveryOnClear = 20f;

    private float _health;
    private float _sanity;
    private float _dopamine;
    private float _stamina;

    private float _maxHealth = 100f;
    private float _maxSanity = 100f;
    private float _maxDopamine = 100f;
    private float _maxStamina = 100f;

    private bool pauseSanityDrain = false;

    [Header("インベントリ (所持アイテム)")]
    public ItemDataAsset[] Inventory = new ItemDataAsset[3];

    [Header("経済・所持金")]
    [SerializeField] private int playerMoney = 0;
    public int PlayerMoney => playerMoney;

    [Header("選択中スロット管理")]
    [SerializeField] private int selectedSlotIndex = 0;
    public int SelectedSlotIndex => selectedSlotIndex;

    public InputActionAsset PlayerInputActionsAsset => playerInputActions;

    private InputAction slot1Action;
    private InputAction slot2Action;
    private InputAction slot3Action;

    public float Health => _health;
    public float Sanity => _sanity;
    public float Dopamine => _dopamine;
    public float Stamina => _stamina;

    public float MaxHealth => _maxHealth;
    public float MaxSanity => _maxSanity;
    public float MaxDopamine => _maxDopamine;
    public float MaxStamina => _maxStamina;

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

            playerMoney = PlayerData.PlayerMoney;

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
        if (!pauseSanityDrain)
        {
            ConsumeSanity(Time.deltaTime * sanityDrainPerSecond);
        }
        HandleSlotSelectionInput();
    }

    public void SetPauseSanityDrain(bool pause)
    {
        pauseSanityDrain = pause;
    }

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

    public void UseSelectedInventoryItem()
    {
        if (selectedSlotIndex < 0 || selectedSlotIndex >= Inventory.Length) return;

        ItemDataAsset itemDataToUse = Inventory[selectedSlotIndex];
        if (itemDataToUse == null) return;

        Debug.Log($"[インベントリ] {itemDataToUse.ItemName} を使用しました！");
        Inventory[selectedSlotIndex] = null;
        SaveInventoryToData();
    }

    public bool HasInventorySpace()
    {
        if (Inventory == null) return false;
        foreach (var item in Inventory)
        {
            if (item == null) return true;
        }
        return false;
    }

    public bool TryAddItem(ItemDataAsset itemData)
    {
        if (itemData == null || Inventory == null) return false;

        if (selectedSlotIndex >= 0 && selectedSlotIndex < Inventory.Length && Inventory[selectedSlotIndex] == null)
        {
            Inventory[selectedSlotIndex] = itemData;
            SaveInventoryToData();
            return true;
        }

        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i] == null)
            {
                Inventory[i] = itemData;
                SaveInventoryToData();
                return true;
            }
        }

        return false;
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
        if (amount <= 0f) return;
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

    /// <summary>
    /// クリア精算時にインベントリ内のアイテムをすべて空にして保存する
    /// </summary>
    public void ClearAllInventoryItems()
    {
        if (Inventory == null) return;
        for (int i = 0; i < Inventory.Length; i++)
        {
            Inventory[i] = null;
        }
        SaveInventoryToData();
    }

    /// <summary>
    /// ステージクリア時の正気度回復と進行データ保存
    /// </summary>
    public void OnStageCleared(int repaidQuota)
    {
        _sanity = Mathf.Clamp(_sanity + sanityRecoveryOnClear, 0f, _maxSanity);

        if (PlayerData != null)
        {
            PlayerData.TotalRepaidDebt += repaidQuota;
            PlayerData.CurrentStageLevel += 1;
        }
    }

    public void AddMoney(int amount)
    {
        playerMoney += amount;
        SaveMoneyToData();
    }

    public void SaveMoneyToData()
    {
        if (PlayerData != null)
        {
            PlayerData.PlayerMoney = playerMoney;
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