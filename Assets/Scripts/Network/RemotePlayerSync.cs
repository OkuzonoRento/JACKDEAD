using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;

/// <summary>
/// 他プレイヤーの位置・向きを同期し、カプセル状のアバターとして表示する。
/// 自分の位置を一定間隔で送信し、ホストが他の参加者へ中継する（NetworkObject を使わない軽量方式）。
/// ゲームシーンに入ったときに NetworkSession が自動生成する。
/// </summary>
public class RemotePlayerSync : MonoBehaviour
{
    private const string MsgPose = "jd.pose";           // 参加者 → ホスト
    private const string MsgPoseRelay = "jd.poseRelay"; // ホスト → 参加者（誰の位置かを付けて中継）
    private const string MsgState = "jd.state";           // 参加者 → ホスト（体力・正気度・手持ち）
    private const string MsgStateRelay = "jd.stateRelay"; // ホスト → 参加者
    private const float StateSendInterval = 1f / 20f;   // 状態（体力・画面の表示など）。止まっていても、毎回送る

    private const float SendInterval = 1f / 20f;     // 位置・視点。止まっていても、毎回送る
    private const float SmoothingSpeed = 15f;
    private const float AvatarHeight = 2f;
    private const float LabelHeightOffset = 1.4f;
    private const float LabelFontSize = 3f;
    private const float LabelShowDistance = 20f;   // この距離より近いとき、名前を表示する
    private const float LabelHideDistance = 22f;   // この距離より遠いと、名前を隠す（境目でちらつかないよう、少し差をつける）
    private const float StaleTimeoutSeconds = 5f;
    private const float CrouchHeightScale = 0.6f;
    private const float FootstepWindow = 0.35f;       // 足音の速さを測る時間（秒）
    private const float SprintSpeedThreshold = 7.5f;  // これより速ければ走り（歩き 5、走り 10）

    private readonly List<ulong> staleIds = new List<ulong>();

    private class RemoteAvatar
    {
        public ulong clientId;
        public GameObject root;
        public TextMeshPro label;
        public Vector3 targetFeetPos;
        public float targetYaw;
        public bool crouching;
        public bool lightOn;         // 懐中電灯を点けているか
        public Light flashlight;     // 他の人の画面に出す、この人の懐中電灯
        public EyeView eye;        // 観戦用：本人のカメラ（滑らかに補間した現在値）
        public EyeView targetEye;  // 最後に届いた値
        public bool eyeInitialized;
        public float heightScale = 1f; // しゃがみ中は縮める（滑らかに補間）
        public bool initialized;
        public float lastReceivedTime;
        public Vector3 stepPos;      // 足音の速さを測り始めた位置と時刻
        public float stepTime;

        // プレイヤーの状態（体力・正気度・手持ちアイテム）
        public string playerName = "Player";
        public PlayerStatus status = PlayerStatus.Empty();

        // 手に持っているアイテムの見た目
        public Transform handAnchor;
        public GameObject heldVisual;
        public string heldVisualName = "";
    }

    /// <summary>他プレイヤーの状態（観戦中のHUD表示と、手持ちアイテムの見た目に使う）</summary>
    public struct PlayerStatus
    {
        public string[] inventory;   // ItemDataAsset のアセット名（空欄は何も持っていない）
        public int selectedSlot;
        public float health, sanity, dopamine, stamina;
        public string playerId;      // 名前を変えても変わらない、プレイヤーの ID（セーブの持ち主の判定に使う）
        public string playerAlias;   // 同じ人の以前の ID（Steam ID に移ったときの、PC の ID）

        // 本人の画面に出ている表示（観戦している人の画面にも同じものを出す）
        public bool lookTarget;      // 調べられる対象に照準が合っている
        public bool canInteract;     // その対象を今使える（照準の色）
        public string prompt;        // 対象の説明文
        public float hold;           // 長押しの進み具合（0〜1。押していなければ -1）
        public Vector3 promptPos;    // 説明文を出す場所（ワールド）
        public Vector3 holdPos;      // 長押しのゲージを出す場所（ワールド）
        public float betMultiplier;  // スロットの掛け金の倍率（正気度で変わる）

        public static PlayerStatus Empty()
        {
            return new PlayerStatus { inventory = new string[InventorySize], selectedSlot = 0, hold = -1f, betMultiplier = 1f };
        }

        public string Held =>
            inventory != null && selectedSlot >= 0 && selectedSlot < inventory.Length ? inventory[selectedSlot] : "";
    }

    private const int InventorySize = 3;

    private readonly Dictionary<ulong, RemoteAvatar> avatars = new Dictionary<ulong, RemoteAvatar>();

    private NetworkManager networkManager;
    private Transform localPlayer;
    private CharacterController localController;
    private PlayerMovement localMovement;
    private float sendTimer;
    private float stateTimer;
    private PlayerController localPc;
    private PlayerInteraction localInteraction;

    public static RemotePlayerSync Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    // ------------------------------------------------------------------
    // 観戦用：生きている他プレイヤーの一覧とアバター
    // ------------------------------------------------------------------

    /// <summary>生きている他プレイヤーの席番号（位置が届いていて、倒れていない人）</summary>
    public void GetAliveRemoteIds(List<ulong> result)
    {
        result.Clear();
        foreach (KeyValuePair<ulong, RemoteAvatar> pair in avatars)
        {
            if (pair.Value.root == null || !pair.Value.initialized) continue;
            if (GameplaySync.IsDead(pair.Key)) continue;
            result.Add(pair.Key);
        }
        result.Sort();
    }

    /// <summary>観戦中の相手の席番号（その人のアバターは隠す）。観戦していないときは -1</summary>
    public static long SpectatedClientId = -1;

    /// <summary>観戦先の、本人のカメラと同じ位置・向き・視野角を返す</summary>
    public bool TryGetAvatarView(ulong clientId, out Vector3 position, out Quaternion rotation, out float fov)
    {
        if (avatars.TryGetValue(clientId, out RemoteAvatar avatar) && avatar.eyeInitialized)
        {
            position = avatar.eye.pos;
            rotation = avatar.eye.rot;
            fov = avatar.eye.fov;
            return true;
        }
        position = Vector3.zero;
        rotation = Quaternion.identity;
        fov = 60f;
        return false;
    }

    /// <summary>他のプレイヤーがしゃがんでいるか（敵の気づきやすさに使う）</summary>
    public bool IsRemoteCrouching(ulong clientId)
    {
        return avatars.TryGetValue(clientId, out RemoteAvatar avatar) && avatar.crouching;
    }

    public class RemoteInfo
    {
        public ulong clientId;
        public string name;
        public PlayerStatus status;
    }

    /// <summary>他プレイヤー全員の、席番号・名前・ステータス（セーブ用。ホストが使う）</summary>
    public void CollectRemoteStatuses(List<RemoteInfo> result)
    {
        foreach (RemoteAvatar avatar in avatars.Values)
        {
            if (string.IsNullOrEmpty(avatar.status.playerId)) continue;
            result.Add(new RemoteInfo { clientId = avatar.clientId, name = avatar.playerName, status = avatar.status });
        }
    }


    /// <summary>観戦先の体力・正気度・スタミナ・インベントリ</summary>
    public bool TryGetStatus(ulong clientId, out PlayerStatus status)
    {
        if (avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            status = avatar.status;
            return true;
        }
        status = PlayerStatus.Empty();
        return false;
    }

    public string GetAvatarName(ulong clientId)
    {
        return avatars.TryGetValue(clientId, out RemoteAvatar avatar) ? avatar.playerName : "Player";
    }

    private void Start()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            Destroy(gameObject);
            return;
        }

        CustomMessagingManager messaging = networkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(MsgPose, OnPoseFromClient);
        messaging.RegisterNamedMessageHandler(MsgPoseRelay, OnPoseRelayed);
        messaging.RegisterNamedMessageHandler(MsgState, OnStateFromClient);
        messaging.RegisterNamedMessageHandler(MsgStateRelay, OnStateRelayed);
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (networkManager == null) return;

        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        if (networkManager.CustomMessagingManager != null)
        {
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MsgPose);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MsgPoseRelay);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MsgState);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MsgStateRelay);
        }
    }

    private void Update()
    {
        if (networkManager == null || !networkManager.IsListening) return;

        SendLocalPose();
        SendLocalState();
        SmoothAvatars();
    }

    // ------------------------------------------------------------------
    // プレイヤーの状態（体力・正気度・手持ちアイテム）
    // ------------------------------------------------------------------

    private void SendLocalState()
    {
        if (localPc == null) localPc = FindAnyObjectByType<PlayerController>();
        if (localPc == null) return;

        stateTimer += Time.unscaledDeltaTime;
        if (stateTimer < StateSendInterval) return;
        stateTimer = 0f;

        PlayerStatus status = PlayerStatus.Empty();
        status = BuildLocalStatus(status);


        SendStatus(status);
    }

    private PlayerStatus BuildLocalStatus(PlayerStatus status)
    {
        if (localPc.Inventory != null)
        {
            for (int i = 0; i < InventorySize && i < localPc.Inventory.Length; i++)
            {
                if (localPc.Inventory[i] != null) status.inventory[i] = localPc.Inventory[i].name;
            }
        }
        status.selectedSlot = localPc.SelectedSlotIndex;
        status.health = localPc.Health;
        status.sanity = localPc.Sanity;
        status.dopamine = localPc.Dopamine;
        if (localMovement == null) localMovement = localPc.GetComponent<PlayerMovement>();
        status.stamina = localMovement != null ? localMovement.CurrentStamina : 0f;
        status.playerId = PlayerIdentity.Id;
        status.playerAlias = PlayerIdentity.Alias;
        status.betMultiplier = localPc.BetPriceMultiplier;
        if (localInteraction == null) localInteraction = localPc.GetComponentInChildren<PlayerInteraction>();
        if (localInteraction == null) localInteraction = FindAnyObjectByType<PlayerInteraction>();
        if (localInteraction != null) localInteraction.WriteHud(ref status);
        return status;
    }

    private void SendStatus(PlayerStatus status)
    {
        if (networkManager.IsHost)
        {
            BroadcastState(networkManager.LocalClientId, status, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp, 2048))
            {
                WriteStatus(writer, status);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgState, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    private static void WriteStatus(FastBufferWriter writer, PlayerStatus status)
    {
        for (int i = 0; i < InventorySize; i++) writer.WriteValueSafe(status.inventory[i] ?? "");
        writer.WriteValueSafe(status.selectedSlot);
        writer.WriteValueSafe(status.health);
        writer.WriteValueSafe(status.sanity);
        writer.WriteValueSafe(status.dopamine);
        writer.WriteValueSafe(status.stamina);
        writer.WriteValueSafe(status.playerId ?? "");
        writer.WriteValueSafe(status.playerAlias ?? "");
        writer.WriteValueSafe(status.lookTarget);
        writer.WriteValueSafe(status.canInteract);
        writer.WriteValueSafe(status.prompt ?? "");
        writer.WriteValueSafe(status.hold);
        writer.WriteValueSafe(status.promptPos);
        writer.WriteValueSafe(status.holdPos);
        writer.WriteValueSafe(status.betMultiplier);
    }

    private static PlayerStatus ReadStatus(FastBufferReader reader)
    {
        PlayerStatus status = PlayerStatus.Empty();
        for (int i = 0; i < InventorySize; i++)
        {
            reader.ReadValueSafe(out string name);
            status.inventory[i] = name;
        }
        reader.ReadValueSafe(out status.selectedSlot);
        reader.ReadValueSafe(out status.health);
        reader.ReadValueSafe(out status.sanity);
        reader.ReadValueSafe(out status.dopamine);
        reader.ReadValueSafe(out status.stamina);
        reader.ReadValueSafe(out string pid);
        status.playerId = pid;
        reader.ReadValueSafe(out string palias);
        status.playerAlias = palias;
        reader.ReadValueSafe(out status.lookTarget);
        reader.ReadValueSafe(out status.canInteract);
        reader.ReadValueSafe(out string prompt);
        status.prompt = prompt;
        reader.ReadValueSafe(out status.hold);
        reader.ReadValueSafe(out status.promptPos);
        reader.ReadValueSafe(out status.holdPos);
        reader.ReadValueSafe(out status.betMultiplier);
        return status;
    }

    private void BroadcastState(ulong sourceId, PlayerStatus status, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp, 2048))
            {
                writer.WriteValueSafe(sourceId);
                WriteStatus(writer, status);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgStateRelay, clientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    // ホスト側：参加者の状態を反映し、他の参加者へ中継する
    private void OnStateFromClient(ulong senderId, FastBufferReader reader)
    {
        PlayerStatus status = ReadStatus(reader);

        ApplyState(senderId, status);
        BroadcastState(senderId, status, senderId);
    }

    // 参加者側：ホストが中継した他プレイヤーの状態を反映する
    private void OnStateRelayed(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sourceId);
        PlayerStatus status = ReadStatus(reader);

        if (sourceId == networkManager.LocalClientId) return;
        ApplyState(sourceId, status);
    }

    private void ApplyState(ulong clientId, PlayerStatus status)
    {
        // アバターは位置メッセージで作られる。まだ無ければ、次の状態送信で反映する
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar)) return;

        avatar.status = status;
        RefreshHeldVisual(avatar);
    }

    /// <summary>頭上には名前だけを表示する（手持ちアイテムは見た目で表す）</summary>
    private static void RefreshLabel(RemoteAvatar avatar)
    {
        if (avatar.label == null) return;

        avatar.label.color = Color.white;
        if (avatar.label.text != avatar.playerName) avatar.label.text = avatar.playerName;
    }

    /// <summary>選択中のアイテムのプレハブを、アバターの手元に飾る（見た目だけ。当たり判定や挙動は取り除く）</summary>
    private static void RefreshHeldVisual(RemoteAvatar avatar)
    {
        string held = avatar.status.Held ?? "";
        if (held == avatar.heldVisualName && (held == "" || avatar.heldVisual != null)) return;

        if (avatar.heldVisual != null) Destroy(avatar.heldVisual);
        avatar.heldVisual = null;
        avatar.heldVisualName = held;
        if (held == "" || avatar.handAnchor == null) return;

        ItemDataAsset data = GameplaySync.ResolveItemData(held);
        if (data == null || data.ItemPrefab == null) return;

        GameObject visual = Instantiate(data.ItemPrefab, avatar.handAnchor);
        visual.name = "HeldItemVisual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // 持ち物としての挙動（拾える・物理・スクリプト）はすべて無効にして、見た目だけ残す
        foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
        foreach (Collider col in visual.GetComponentsInChildren<Collider>(true)) Destroy(col);
        foreach (Rigidbody rb in visual.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        avatar.heldVisual = visual;
    }

    // ------------------------------------------------------------------
    // 送信
    // ------------------------------------------------------------------

    private void SendLocalPose()
    {
        if (localPlayer == null)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            localPlayer = player.transform;
            localController = player.GetComponent<CharacterController>();
            localMovement = player.GetComponent<PlayerMovement>();
        }

        sendTimer += Time.unscaledDeltaTime;
        if (sendTimer < SendInterval) return;
        sendTimer = 0f;

        Vector3 feetPos = localPlayer.position;
        if (localController != null) feetPos.y = localController.bounds.min.y;
        float yaw = localPlayer.eulerAngles.y;
        bool crouching = localMovement != null && localMovement.IsCrouching;
        bool lightOn = localMovement == null || localMovement.Flashlight == null || localMovement.Flashlight.enabled;
        string playerName = NetworkSession.Instance != null ? NetworkSession.Instance.LocalPlayerName : "Player";

        // 観戦してくれる人に「この人の目に映っている映像」をそのまま見せるため、カメラの位置・向き・視野角も送る
        Camera cam = Camera.main;
        EyeView eye = cam != null
            ? new EyeView { pos = cam.transform.position, rot = cam.transform.rotation, fov = cam.fieldOfView }
            : new EyeView { pos = feetPos + Vector3.up * 1.6f, rot = Quaternion.Euler(0f, yaw, 0f), fov = 60f };


        if (networkManager.IsHost)
        {
            // ホストの位置は、全参加者へ id=0 として配る
            BroadcastRelay(networkManager.LocalClientId, playerName, feetPos, yaw, crouching, lightOn, eye, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(192, Allocator.Temp, 1024))
            {
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
                writer.WriteValueSafe(lightOn);
                WriteEye(writer, eye);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgPose, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }
    }


    private struct EyeView
    {
        public Vector3 pos;
        public Quaternion rot;
        public float fov;
    }

    private static void WriteEye(FastBufferWriter writer, EyeView eye)
    {
        writer.WriteValueSafe(eye.pos);
        writer.WriteValueSafe(eye.rot);
        writer.WriteValueSafe(eye.fov);
    }

    private static EyeView ReadEye(FastBufferReader reader)
    {
        EyeView eye = new EyeView();
        reader.ReadValueSafe(out eye.pos);
        reader.ReadValueSafe(out eye.rot);
        reader.ReadValueSafe(out eye.fov);
        return eye;
    }

    private void BroadcastRelay(ulong sourceId, string playerName, Vector3 feetPos, float yaw, bool crouching, bool lightOn, EyeView eye, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(192, Allocator.Temp, 1024))
            {
                writer.WriteValueSafe(sourceId);
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
                writer.WriteValueSafe(lightOn);
                WriteEye(writer, eye);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgPoseRelay, clientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    // ------------------------------------------------------------------
    // 受信
    // ------------------------------------------------------------------

    // ホスト側：参加者から届いた位置を反映し、他の参加者へ中継する
    private void OnPoseFromClient(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out Vector3 feetPos);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool crouching);
        reader.ReadValueSafe(out bool lightOn);
        EyeView eye = ReadEye(reader);

        UpdateAvatar(senderId, playerName, feetPos, yaw, crouching, lightOn, eye);
        BroadcastRelay(senderId, playerName, feetPos, yaw, crouching, lightOn, eye, senderId);
    }

    // 参加者側：ホストが中継した他プレイヤーの位置を反映する
    private void OnPoseRelayed(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sourceId);
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out Vector3 feetPos);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool crouching);
        reader.ReadValueSafe(out bool lightOn);
        EyeView eye = ReadEye(reader);

        if (sourceId == networkManager.LocalClientId) return;
        UpdateAvatar(sourceId, playerName, feetPos, yaw, crouching, lightOn, eye);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            DestroyAvatar(avatar);
            avatars.Remove(clientId);
        }
    }

    // ------------------------------------------------------------------
    // 懐中電灯
    // ------------------------------------------------------------------

    /// <summary>この当たり判定が、他のプレイヤーのアバターなら、その人の席番号を返す</summary>
    public bool TryGetClientId(Collider hit, out ulong clientId)
    {
        clientId = 0;
        if (hit == null) return false;

        foreach (RemoteAvatar avatar in avatars.Values)
        {
            if (avatar.root != null && hit.transform.IsChildOf(avatar.root.transform))
            {
                clientId = avatar.clientId;
                return true;
            }
        }
        return false;
    }

    /// <summary>その人が懐中電灯を点けているか</summary>
    public bool TryGetAvatarLight(ulong clientId, out bool on)
    {
        on = false;
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar) || !avatar.eyeInitialized) return false;
        on = avatar.lightOn;
        return true;
    }

    // 他の人の懐中電灯は、その人のカメラの位置・向きに置く（自分のライトと同じ設定）。
    // 観戦している相手のぶんは、自分のカメラのライトが同じ役目をするので点けない
    private void UpdateRemoteFlashlight(RemoteAvatar avatar, bool alive)
    {
        if (avatar.flashlight == null)
        {
            Light source = localMovement != null ? localMovement.Flashlight : null;
            if (source == null) return;

            GameObject obj = new GameObject($"RemoteFlashlight_{avatar.clientId}");
            Light copy = obj.AddComponent<Light>();
            copy.type = source.type;
            copy.color = source.color;
            copy.range = source.range;
            copy.spotAngle = source.spotAngle;
            copy.innerSpotAngle = source.innerSpotAngle;
            copy.cookie = source.cookie;
            copy.shadows = source.shadows;
            copy.shadowStrength = source.shadowStrength;
            copy.shadowBias = source.shadowBias;
            copy.shadowNormalBias = source.shadowNormalBias;
            copy.shadowNearPlane = source.shadowNearPlane;
            avatar.flashlight = copy;
        }

        // 明るさは、その人が手に持っている物（懐中電灯なら明るい）で決める
        if (localMovement != null)
            localMovement.ApplyFlashlightSettings(avatar.flashlight, GameplaySync.ResolveItemData(avatar.status.Held));

        avatar.flashlight.transform.SetPositionAndRotation(avatar.eye.pos, avatar.eye.rot);
        bool on = alive && avatar.lightOn && (long)avatar.clientId != SpectatedClientId;
        if (avatar.flashlight.enabled != on) avatar.flashlight.enabled = on;
    }

    private static void DestroyAvatar(RemoteAvatar avatar)
    {
        if (avatar.root != null) Destroy(avatar.root);
        if (avatar.flashlight != null) Destroy(avatar.flashlight.gameObject);
    }

    // ------------------------------------------------------------------
    // アバター表示
    // ------------------------------------------------------------------

    private void UpdateAvatar(ulong clientId, string playerName, Vector3 feetPos, float yaw, bool crouching, bool lightOn, EyeView eye)
    {
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            avatar = CreateAvatar(clientId);
            avatars[clientId] = avatar;
        }

        // 他プレイヤーの足音：位置の変化から速さを割り出し、敵に聞こえる音として通知する（敵を動かすホストだけが使う）
        if (avatar.initialized && networkManager != null && networkManager.IsHost)
        {
            // 届く間隔のばらつきで、歩きが走りに見えないよう、少し長い時間の平均の速さで判定する
            float elapsed = Time.unscaledTime - avatar.stepTime;
            if (elapsed >= FootstepWindow)
            {
                Vector3 delta = feetPos - avatar.stepPos;
                delta.y = 0f;
                float speed = delta.magnitude / elapsed;
                // 通信が途切れたあとや、ステージ開始のワープなど、極端に速い移動は足音にしない
                if (elapsed < 1f && speed > 0.5f && speed < 14f)
                {
                    float loudness = crouching ? EnemyNoise.Crouch : (speed > SprintSpeedThreshold ? EnemyNoise.Sprint : EnemyNoise.Walk);
                    EnemyNoise.Emit(feetPos, loudness);
                }
                avatar.stepPos = feetPos;
                avatar.stepTime = Time.unscaledTime;
            }
        }

        avatar.targetFeetPos = feetPos;
        avatar.targetYaw = yaw;
        avatar.crouching = crouching;
        avatar.lightOn = lightOn;
        avatar.targetEye = eye;
        if (!avatar.eyeInitialized)
        {
            avatar.eyeInitialized = true;
            avatar.eye = eye;
        }
        avatar.lastReceivedTime = Time.unscaledTime;
        avatar.playerName = playerName;
        RefreshLabel(avatar);

        // 最初の1回は補間せず、その位置へ即座に置く
        if (!avatar.initialized)
        {
            avatar.initialized = true;
            avatar.root.transform.position = feetPos + Vector3.up * (AvatarHeight * 0.5f);
            avatar.root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    private RemoteAvatar CreateAvatar(ulong clientId)
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = $"RemotePlayer_{clientId}";

        // プレイヤー同士がぶつかるよう、カプセルの当たり判定は残す。
        // Ignore Raycast レイヤーにして、視線（インタラクト用のレイキャスト）や床判定の邪魔にならないようにする
        root.layer = 2;

        Renderer rend = root.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = Color.HSVToRGB((clientId * 0.27f) % 1f, 0.6f, 0.95f);
        }

        // 向きが分かるよう、前方に小さな目印を付ける
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        Collider noseCol = nose.GetComponent<Collider>();
        if (noseCol != null) Destroy(noseCol);
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.55f, 0.45f);
        nose.transform.localScale = new Vector3(0.25f, 0.15f, 0.25f);
        // 目印は目のすぐ前にあるので、本人の懐中電灯をさえぎらないよう、影を落とさない
        nose.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 手に持つアイテムを飾る位置（体の右前）
        GameObject hand = new GameObject("HandAnchor");
        hand.transform.SetParent(root.transform, false);
        hand.transform.localPosition = new Vector3(0.45f, 0.15f, 0.5f);

        GameObject labelObj = new GameObject("NameLabel");
        labelObj.transform.SetParent(root.transform, false);
        labelObj.transform.localPosition = new Vector3(0f, LabelHeightOffset, 0f);
        TextMeshPro label = labelObj.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = LabelFontSize;
        label.color = Color.white;

        return new RemoteAvatar { clientId = clientId, root = root, label = label, handAnchor = hand.transform };
    }

    private void SmoothAvatars()
    {
        float t = 1f - Mathf.Exp(-SmoothingSpeed * Time.deltaTime);
        Camera cam = Camera.main;

        // 一定時間更新が来ないアバター（退出した人など）を片付ける
        staleIds.Clear();
        foreach (KeyValuePair<ulong, RemoteAvatar> pair in avatars)
        {
            if (Time.unscaledTime - pair.Value.lastReceivedTime > StaleTimeoutSeconds) staleIds.Add(pair.Key);
        }
        foreach (ulong id in staleIds)
        {
            DestroyAvatar(avatars[id]);
            avatars.Remove(id);
        }

        foreach (RemoteAvatar avatar in avatars.Values)
        {
            if (avatar.root == null) continue;

            // 倒れて観戦中のプレイヤーは、見えないようにする
            // （自分が観戦している相手のアバターも、カメラが体の中に入るので隠す）
            bool shouldShow = !GameplaySync.IsDead(avatar.clientId) && (long)avatar.clientId != SpectatedClientId;
            if (avatar.root.activeSelf != shouldShow) avatar.root.SetActive(shouldShow);

            // 観戦用のカメラ位置・向き・視野角を滑らかに追従させる
            avatar.eye.pos = Vector3.Lerp(avatar.eye.pos, avatar.targetEye.pos, t);
            avatar.eye.rot = Quaternion.Slerp(avatar.eye.rot, avatar.targetEye.rot, t);
            avatar.eye.fov = Mathf.Lerp(avatar.eye.fov, avatar.targetEye.fov, t);

            UpdateRemoteFlashlight(avatar, !GameplaySync.IsDead(avatar.clientId));

            // しゃがみ中は縮めて表示する（名前表示は縦に潰れないよう打ち消す）
            avatar.heightScale = Mathf.Lerp(avatar.heightScale, avatar.crouching ? CrouchHeightScale : 1f, t);
            avatar.root.transform.localScale = new Vector3(1f, avatar.heightScale, 1f);
            if (avatar.label != null)
            {
                avatar.label.transform.localScale = new Vector3(1f, 1f / avatar.heightScale, 1f);
            }
            // 手に持ったアイテムも、しゃがんで縮まないよう打ち消す
            if (avatar.handAnchor != null)
            {
                avatar.handAnchor.localScale = new Vector3(1f, 1f / avatar.heightScale, 1f);
            }

            Vector3 targetPos = avatar.targetFeetPos + Vector3.up * (AvatarHeight * 0.5f * avatar.heightScale);
            avatar.root.transform.position = Vector3.Lerp(avatar.root.transform.position, targetPos, t);
            avatar.root.transform.rotation = Quaternion.Slerp(
                avatar.root.transform.rotation, Quaternion.Euler(0f, avatar.targetYaw, 0f), t);

            // 離れているプレイヤーの名前は、表示しない
            if (avatar.label != null && cam != null)
            {
                float distToCam = Vector3.Distance(cam.transform.position, avatar.root.transform.position);
                bool visible = avatar.label.enabled ? distToCam <= LabelHideDistance : distToCam <= LabelShowDistance;
                if (avatar.label.enabled != visible) avatar.label.enabled = visible;
            }

            // 名前は常にカメラの方を向ける（回転するカプセルの影響を受けないよう、親の回転を打ち消す）
            if (avatar.label != null && cam != null)
            {
                avatar.label.transform.rotation = Quaternion.LookRotation(
                    avatar.label.transform.position - cam.transform.position);
            }
        }
    }
}
