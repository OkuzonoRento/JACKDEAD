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
    private const float StateSendInterval = 0.25f;

    private const float SendInterval = 1f / 10f;
    private const float SmoothingSpeed = 15f;
    private const float AvatarHeight = 2f;
    private const float LabelHeightOffset = 1.4f;
    private const float LabelFontSize = 3f;
    private const float StaleTimeoutSeconds = 5f;
    private const float CrouchHeightScale = 0.6f;

    private readonly List<ulong> staleIds = new List<ulong>();

    private class RemoteAvatar
    {
        public ulong clientId;
        public GameObject root;
        public TextMeshPro label;
        public Vector3 targetFeetPos;
        public float targetYaw;
        public bool crouching;
        public EyeView eye;        // 観戦用：本人のカメラ（滑らかに補間した現在値）
        public EyeView targetEye;  // 最後に届いた値
        public bool eyeInitialized;
        public float heightScale = 1f; // しゃがみ中は縮める（滑らかに補間）
        public bool initialized;
        public float lastReceivedTime;

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

        public static PlayerStatus Empty()
        {
            return new PlayerStatus { inventory = new string[InventorySize], selectedSlot = 0 };
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

        if (networkManager.IsHost)
        {
            BroadcastState(networkManager.LocalClientId, status, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp))
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
        return status;
    }

    private void BroadcastState(ulong sourceId, PlayerStatus status, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp))
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
        string playerName = NetworkSession.Instance != null ? NetworkSession.Instance.LocalPlayerName : "Player";

        // 観戦してくれる人に「この人の目に映っている映像」をそのまま見せるため、カメラの位置・向き・視野角も送る
        Camera cam = Camera.main;
        EyeView eye = cam != null
            ? new EyeView { pos = cam.transform.position, rot = cam.transform.rotation, fov = cam.fieldOfView }
            : new EyeView { pos = feetPos + Vector3.up * 1.6f, rot = Quaternion.Euler(0f, yaw, 0f), fov = 60f };

        if (networkManager.IsHost)
        {
            // ホストの位置は、全参加者へ id=0 として配る
            BroadcastRelay(networkManager.LocalClientId, playerName, feetPos, yaw, crouching, eye, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
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

    private void BroadcastRelay(ulong sourceId, string playerName, Vector3 feetPos, float yaw, bool crouching, EyeView eye, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(sourceId);
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
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
        EyeView eye = ReadEye(reader);

        UpdateAvatar(senderId, playerName, feetPos, yaw, crouching, eye);
        BroadcastRelay(senderId, playerName, feetPos, yaw, crouching, eye, senderId);
    }

    // 参加者側：ホストが中継した他プレイヤーの位置を反映する
    private void OnPoseRelayed(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sourceId);
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out Vector3 feetPos);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool crouching);
        EyeView eye = ReadEye(reader);

        if (sourceId == networkManager.LocalClientId) return;
        UpdateAvatar(sourceId, playerName, feetPos, yaw, crouching, eye);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            if (avatar.root != null) Destroy(avatar.root);
            avatars.Remove(clientId);
        }
    }

    // ------------------------------------------------------------------
    // アバター表示
    // ------------------------------------------------------------------

    private void UpdateAvatar(ulong clientId, string playerName, Vector3 feetPos, float yaw, bool crouching, EyeView eye)
    {
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            avatar = CreateAvatar(clientId);
            avatars[clientId] = avatar;
        }

        // 他プレイヤーの足音：位置の変化から速さを割り出し、敵に聞こえる音として通知する（敵を動かすホストだけが使う）
        if (avatar.initialized && networkManager != null && networkManager.IsHost)
        {
            float elapsed = Time.unscaledTime - avatar.lastReceivedTime;
            if (elapsed > 0.02f && elapsed < 0.6f)
            {
                Vector3 delta = feetPos - avatar.targetFeetPos;
                delta.y = 0f;
                float speed = delta.magnitude / elapsed;
                // ステージ開始のワープなど、極端に速い移動は足音にしない
                if (speed > 0.5f && speed < 12f)
                {
                    float loudness = crouching ? EnemyNoise.Crouch : (speed > 5f ? EnemyNoise.Sprint : EnemyNoise.Walk);
                    EnemyNoise.Emit(feetPos, loudness);
                }
            }
        }

        avatar.targetFeetPos = feetPos;
        avatar.targetYaw = yaw;
        avatar.crouching = crouching;
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
            if (avatars[id].root != null) Destroy(avatars[id].root);
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

            // 名前は常にカメラの方を向ける（回転するカプセルの影響を受けないよう、親の回転を打ち消す）
            if (avatar.label != null && cam != null)
            {
                avatar.label.transform.rotation = Quaternion.LookRotation(
                    avatar.label.transform.position - cam.transform.position);
            }
        }
    }
}
