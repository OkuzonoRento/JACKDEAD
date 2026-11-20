using System.Collections.Generic;
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
    [SerializeField, Tooltip("投げるときの速さの上限（m/s）。軽いアイテムが飛びすぎないようにする")]
    private float maxThrowSpeed = 11f;

    [Header("正気度・クリア時回復設定")]
    [SerializeField, Tooltip("1秒あたりの基本正気度減少量")]
    private float sanityDrainPerSecond = 0.15f;
    [SerializeField, Tooltip("各マップクリア時に回復する正気度（Sanity）の量")]
    private float sanityRecoveryOnClear = 20f;

    [Header("落下死設定")]
    [SerializeField, Tooltip("このY座標より下に落ちたら死亡する（マップの床は Y=0 付近）")]
    private float fallDeathHeight = -30f;

    private float _health;
    private float _sanity;
    private float _dopamine;
    private float _stamina;

    private float _maxHealth = 100f;
    private float _maxSanity = 100f;
    private float _maxDopamine = 100f;
    private float _maxStamina = 100f;

    private bool pauseSanityDrain = false;
    private bool statusInitialized = false;
    private bool isGameOver = false;

    [Header("マルチプレイ：倒れたときの観戦・復活")]
    [SerializeField, Range(0f, 1f), Tooltip("復活時の体力の減少割合（最大体力に対して。0.7 = 7割減＝最大体力の3割で復活）。最大体力そのものは減らない")]
    private float reviveHealthLossRate = 0.7f;
    [SerializeField, Range(0f, 1f), Tooltip("復活時の正気度の減少割合（最大正気度に対して。0.6 = 6割減＝最大正気度の4割で復活）。最大正気度そのものは減らない")]
    private float reviveSanityLossRate = 0.6f;

    private bool isSpectating = false;
    private string spectateDeathReason = "YOU DIED";
    private SpectatorController spectator;
    private Vector3 lastSafePosition;

    /// <summary>倒れて仲間を観戦中（マルチプレイのみ。次のステージで復活する）</summary>
    public bool IsSpectating => isSpectating;

    /// <summary>倒れている（観戦中・ゲームオーバー中）。敵や爆発の対象にならない</summary>
    public bool IsDown => isGameOver || isSpectating;

    // インベントリに入れている時限爆弾（Box）の残り時間。スロットごとに管理する
    private readonly float[] heldFuse = new float[3];
    private readonly bool[] heldFuseActive = new bool[3];

    /// <summary>ローカルのプレイヤーが実際にダメージを受けたときに通知される（スロットの回転を中止するなど）</summary>
    public static event System.Action Damaged;

    /// <summary>レバーでクリアしたあと、リザルト演出と次のステージへの移行が終わるまでは無敵</summary>
    public bool IsInvulnerable()
    {
        ResultUIController resultUI = FindAnyObjectByType<ResultUIController>();
        return resultUI != null && (resultUI.IsResultShowing || resultUI.IsTransitioning);
    }

    /// <summary>爆発などで吹き飛ばされる。着地するまで操作できない</summary>
    public void Launch(Vector3 velocity, float recoveryStunSeconds = 2.5f)
    {
        if (IsDown || IsInvulnerable()) return;
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.Launch(velocity, recoveryStunSeconds);
    }

    /// <summary>スタンする（Box が当たったとき）。しばらく動けず、視界が揺れる</summary>
    public void Stun(float seconds)
    {
        if (seconds <= 0f || IsDown || IsInvulnerable()) return;
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.Stun(seconds);
    }

    /// <summary>ダメージを受ける（敵のBox・爆発など）。体力が0になれば CheckGameOver が処理する</summary>
    public void TakeDamage(float amount, string reason = "")
    {
        if (amount <= 0f || IsDown || !statusInitialized || IsInvulnerable()) return;
        _health = Mathf.Max(0f, _health - amount);
        Debug.Log($"[ダメージ] {reason} {amount:0.#} → 体力 {_health:0}/{_maxHealth:0}");
        Damaged?.Invoke();
    }

    private static BombBox GetBombSettings(ItemDataAsset data)
    {
        return data != null && data.ItemPrefab != null ? data.ItemPrefab.GetComponent<BombBox>() : null;
    }

    /// <summary>持っている Box の導火線を進め、尽きたらその場で爆発させる</summary>
    private void TickHeldBombs()
    {
        if (isSpectating || isGameOver || Inventory == null) return;

        for (int i = 0; i < Inventory.Length && i < heldFuse.Length; i++)
        {
            ItemDataAsset data = Inventory[i];
            if (data == null || !data.IsBomb)
            {
                heldFuseActive[i] = false;
                continue;
            }

            BombBox settings = GetBombSettings(data);
            float fuseTime = settings != null ? settings.FuseTime : 8f;
            if (!heldFuseActive[i])
            {
                heldFuseActive[i] = true;
                heldFuse[i] = fuseTime;
            }

            heldFuse[i] -= Time.deltaTime;
            if (heldFuse[i] > 0f) continue;

            // 所持中に爆発：その場の全員が範囲内ならダメージを受ける
            float radius = settings != null ? settings.ExplosionRadius : 5f;
            float damage = settings != null ? settings.ExplosionDamage : 40f;
            Inventory[i] = null;
            heldFuseActive[i] = false;
            SaveInventoryToData();

            Vector3 center = transform.position + Vector3.up;
            GameplaySync.NotifyExplosion(center, radius, damage);
            BombBox.ExplodeAt(center, radius, damage);
        }
    }

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

            statusInitialized = true;
        }
    }

    private void Update()
    {
        // 落下死のとき観戦中に体を戻せるよう、最後に安全だった位置を覚えておく
        if (!isSpectating && transform.position.y > fallDeathHeight + 10f)
        {
            lastSafePosition = transform.position;
        }

        if (!pauseSanityDrain)
        {
            ConsumeSanity(Time.deltaTime * sanityDrainPerSecond);
        }
        HandleSlotSelectionInput();
        TickHeldBombs();
        CheckGameOver();
    }

    public void SetPauseSanityDrain(bool pause)
    {
        pauseSanityDrain = pause;
    }

    /// <summary>
    /// 体力または正気度が尽きたら全滅（ゲームオーバー）演出を一度だけ開始する
    /// </summary>
    private void CheckGameOver()
    {
        if (isGameOver || isSpectating || !statusInitialized) return;

        string deathReason;
        if (transform.position.y < fallDeathHeight)
        {
            // マップの外へ落下した場合は即死
            _health = 0f;
            deathReason = "FELL TO DEATH";
        }
        else if (_health <= 0f)
        {
            deathReason = "YOU DIED";
        }
        else if (_sanity <= 0f)
        {
            deathReason = "LOST YOUR SANITY";
        }
        else
        {
            return;
        }

        // マルチプレイで他に生きている人がいれば、ゲームオーバーにはならず観戦に回る（次のステージで復活）
        // （観戦に入るのは体力が0になった場合のみ。正気度が尽きた場合は従来どおりゲームオーバー）
        if (_health <= 0f && TryBeginSpectating(deathReason)) return;

        isGameOver = true;
        pauseSanityDrain = true;

        // ゲームオーバー演出とタイトルへのシーン推移は ResultUIController が担当する
        ResultUIController resultUI = FindAnyObjectByType<ResultUIController>();
        if (resultUI != null)
        {
            resultUI.BeginGameOver(deathReason);
        }
        else
        {
            Debug.LogWarning("[ゲームオーバー] ResultUIController がシーンにありません。");
            ResetForNewRun();
        }
    }

    private bool TryBeginSpectating(string deathReason)
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null || !net.IsOnline || RemotePlayerSync.Instance == null || GameplaySync.Instance == null) return false;

        List<ulong> alive = new List<ulong>();
        RemotePlayerSync.Instance.GetAliveRemoteIds(alive);
        if (alive.Count == 0) return false; // 自分が最後の1人なら全滅 → ゲームオーバー

        isSpectating = true;
        spectateDeathReason = deathReason;
        pauseSanityDrain = true;

        // 落下死の場合は、マップの外に体が残らないよう最後に立っていた場所へ戻す
        if (transform.position.y < fallDeathHeight)
        {
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = lastSafePosition;
            if (cc != null) cc.enabled = true;
        }

        // 持っているアイテムはその場に落とす
        DropAllItemsOnDeath();

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.SetSpectating(true);

        spectator = SpectatorController.Begin(this);
        GameplaySync.NotifyLocalDead(true);
        Debug.Log($"[観戦] 倒れました（{deathReason}）。生きている仲間を観戦します。");
        return true;
    }

    /// <summary>
    /// 倒れて観戦中なら復活させる。ステージクリア後の次のステージで、プレイヤーがバス（トラック）へ移された直後に呼ぶ。
    /// 最大体力・最大正気度は変えず、現在の体力・正気度だけが減った状態で復活する
    /// </summary>
    public void ReviveIfSpectating()
    {
        if (!isSpectating) return;

        if (spectator != null)
        {
            spectator.End();
            spectator = null;
        }

        _health = Mathf.Max(1f, _maxHealth * (1f - reviveHealthLossRate));
        _sanity = Mathf.Max(1f, _maxSanity * (1f - reviveSanityLossRate));

        isSpectating = false;
        pauseSanityDrain = false;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.SetSpectating(false);

        GameplaySync.NotifyLocalDead(false);
        Debug.Log($"[観戦] 復活しました。体力 {_health:0}/{_maxHealth:0} / 正気度 {_sanity:0}/{_maxSanity:0}");
    }

    /// <summary>観戦中に、生きている仲間が全員いなくなった（全滅）ときに SpectatorController から呼ばれる</summary>
    public void BeginGameOverFromSpectating()
    {
        if (isGameOver) return;
        isGameOver = true;
        isSpectating = false;

        if (spectator != null)
        {
            spectator.End();
            spectator = null;
        }

        ResultUIController resultUI = FindAnyObjectByType<ResultUIController>();
        if (resultUI != null)
        {
            resultUI.BeginGameOver(spectateDeathReason);
        }
        else
        {
            ResetForNewRun();
        }
    }

    /// <summary>
    /// 全滅後の最初からやり直し：所持金・インベントリ・進行度・ステータスを初期状態に戻す
    /// </summary>
    public void ResetForNewRun()
    {
        if (spectator != null)
        {
            spectator.End();
            spectator = null;
        }
        isSpectating = false;
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.SetSpectating(false);

        if (PlayerData != null)
        {
            PlayerData.PlayerMoney = 0;
            PlayerData.CurrentStageLevel = 1;
            PlayerData.TotalRepaidDebt = 0;
            PlayerData.Inventory = new ItemDataAsset[3];
        }

        for (int i = 0; i < Inventory.Length; i++)
        {
            Inventory[i] = null;
            if (i < heldFuseActive.Length) heldFuseActive[i] = false;
        }
        playerMoney = 0;
        selectedSlotIndex = 0;

        StatusSetUp();

        isGameOver = false;
        pauseSanityDrain = false;
    }

    public void RestoreHealth(float amount)
    {
        _health = Mathf.Clamp(_health + amount, 0f, _maxHealth);
    }

    public void RestoreSanity(float amount)
    {
        _sanity = Mathf.Clamp(_sanity + amount, 0f, _maxSanity);
    }

    public void RestoreDopamine(float amount)
    {
        _dopamine = Mathf.Clamp(_dopamine + amount, 0f, _maxDopamine);
    }

    private void HandleSlotSelectionInput()
    {
        if (PauseMenu.IsOpen || isSpectating) return;

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

        Vector3 spawnPosition = cameraTransform != null
            ? cameraTransform.position + cameraTransform.forward * 1.2f
            : transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;

        // 軽いアイテム（Box など）が飛びすぎないよう、投げる速さに上限を設ける
        float throwForce = Mathf.Min(baseThrowForce / Mathf.Max(0.1f, itemDataToDrop.ItemWeight), maxThrowSpeed);
        Vector3 throwDirection = cameraTransform != null
            ? (cameraTransform.forward + Vector3.up * 0.2f).normalized
            : (transform.forward + Vector3.up * 0.2f).normalized;

        SpawnDroppedItem(selectedSlotIndex, spawnPosition, throwDirection * throwForce);
    }

    /// <summary>
    /// スロットのアイテムを、指定の位置・初速でワールドに出し、スロットを空にする（マルチプレイでは他の人にも同じ物が出る）
    /// </summary>
    private void SpawnDroppedItem(int slotIndex, Vector3 spawnPosition, Vector3 velocity)
    {
        ItemDataAsset itemDataToDrop = Inventory[slotIndex];
        if (itemDataToDrop == null) return;

        if (itemDataToDrop.ItemPrefab != null)
        {
            GameObject droppedObj = Instantiate(itemDataToDrop.ItemPrefab, spawnPosition, Quaternion.identity);
            droppedObj.layer = LayerMask.NameToLayer("Items");

            Item itemComp = droppedObj.GetComponent<Item>();
            if (itemComp == null) itemComp = droppedObj.AddComponent<Item>();
            itemComp.SetItemData(itemDataToDrop);
            itemComp.NetId = GameplaySync.AllocateDropId();

            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb == null) rb = droppedObj.AddComponent<Rigidbody>();

            rb.linearVelocity = Vector3.zero;
            rb.AddForce(velocity, ForceMode.VelocityChange);

            // 時限爆弾なら、持っている間に進んだ導火線の残りを引き継ぐ（投げた本人には少しの間当たらない）
            float fuseToCarry = -1f;
            BombBox bomb = droppedObj.GetComponent<BombBox>();
            if (bomb != null)
            {
                if (heldFuseActive[slotIndex])
                {
                    fuseToCarry = heldFuse[slotIndex];
                    bomb.SetRemaining(fuseToCarry);
                }
            }
            GameplaySync.NotifyItemDropped(itemComp, itemDataToDrop, spawnPosition, velocity, fuseToCarry);
            EnemyNoise.Emit(spawnPosition, EnemyNoise.ItemDrop);

            Debug.Log($"[インベントリ] {itemDataToDrop.ItemName} を投げました（重さ: {itemDataToDrop.ItemWeight}）。");
        }

        Inventory[slotIndex] = null;
        heldFuseActive[slotIndex] = false;
        SaveInventoryToData();
    }

    /// <summary>倒れたとき、持っているアイテムをすべてその場に落とす（周囲に少し散らす）</summary>
    private void DropAllItemsOnDeath()
    {
        if (Inventory == null) return;

        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i] == null) continue;

            float angle = (360f / Inventory.Length) * i;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 spawnPosition = transform.position + Vector3.up * 1.0f + dir * 0.6f;
            SpawnDroppedItem(i, spawnPosition, dir * 1.5f + Vector3.up * 1.5f);
        }
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

    /// <param name="bombFuse">時限爆弾を拾うとき、その時点の導火線の残り（負なら満タンから）</param>
    public bool TryAddItem(ItemDataAsset itemData, float bombFuse = -1f)
    {
        if (itemData == null || Inventory == null) return false;

        if (selectedSlotIndex >= 0 && selectedSlotIndex < Inventory.Length && Inventory[selectedSlotIndex] == null)
        {
            StoreItem(selectedSlotIndex, itemData, bombFuse);
            return true;
        }

        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i] == null)
            {
                StoreItem(i, itemData, bombFuse);
                return true;
            }
        }

        return false;
    }

    private void StoreItem(int slot, ItemDataAsset itemData, float bombFuse)
    {
        Inventory[slot] = itemData;
        heldFuseActive[slot] = itemData.IsBomb && bombFuse > 0f;
        if (heldFuseActive[slot]) heldFuse[slot] = bombFuse;
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
            if (i < heldFuseActive.Length) heldFuseActive[i] = false;
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

    /// <summary>
    /// 所持金を増減する。所持金は全員共通なので、マルチプレイ中は増減分を他の参加者にも配る
    /// </summary>
    public void AddMoney(int amount)
    {
        if (amount == 0) return;
        playerMoney += amount;
        SaveMoneyToData();
        GameplaySync.NotifyMoneyDelta(amount);
    }

    /// <summary>他の参加者から届いた所持金の増減を反映する（再送しない）</summary>
    public void ApplySharedMoneyDelta(int amount)
    {
        playerMoney += amount;
        SaveMoneyToData();
    }

    /// <summary>所持金をこの値にする（他の参加者へは配らない。ステージクリア精算用）</summary>
    public void SetMoneyLocal(int amount)
    {
        playerMoney = amount;
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