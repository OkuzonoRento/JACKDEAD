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
    private const float StateSendInterval = 0.5f;

    private const float SendInterval = 1f / 15f;
    private const float SmoothingSpeed = 15f;
    private const float AvatarHeight = 2f;
    private const float LabelHeightOffset = 1.4f;
    private const float LabelFontSize = 3f;
    private const float StaleTimeoutSeconds = 5f;
    private const float CrouchHeightScale = 0.6f;

    private readonly List<ulong> staleIds = new List<ulong>();

    private class RemoteAvatar
    {
        public GameObject root;
        public TextMeshPro label;
        public Vector3 targetFeetPos;
        public float targetYaw;
        public bool crouching;
        public float heightScale = 1f; // しゃがみ中は縮める（滑らかに補間）
        public bool initialized;
        public float lastReceivedTime;

        // プレイヤーの状態（体力・正気度・手持ちアイテム）
        public string playerName = "Player";
        public string heldItem = "";
    }

    private readonly Dictionary<ulong, RemoteAvatar> avatars = new Dictionary<ulong, RemoteAvatar>();

    private NetworkManager networkManager;
    private Transform localPlayer;
    private CharacterController localController;
    private PlayerMovement localMovement;
    private float sendTimer;
    private float stateTimer;
    private PlayerController localPc;

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

        string held = "";
        if (localPc.Inventory != null && localPc.SelectedSlotIndex >= 0 && localPc.SelectedSlotIndex < localPc.Inventory.Length)
        {
            ItemDataAsset item = localPc.Inventory[localPc.SelectedSlotIndex];
            if (item != null) held = item.ItemName;
        }

        if (networkManager.IsHost)
        {
            BroadcastState(networkManager.LocalClientId, held, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(96, Allocator.Temp))
            {
                writer.WriteValueSafe(held);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgState, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
            }
        }
    }

    private void BroadcastState(ulong sourceId, string held, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(96, Allocator.Temp))
            {
                writer.WriteValueSafe(sourceId);
                writer.WriteValueSafe(held);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgStateRelay, clientId, writer, NetworkDelivery.Reliable);
            }
        }
    }

    // ホスト側：参加者の状態を反映し、他の参加者へ中継する
    private void OnStateFromClient(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string held);

        ApplyState(senderId, held);
        BroadcastState(senderId, held, senderId);
    }

    // 参加者側：ホストが中継した他プレイヤーの状態を反映する
    private void OnStateRelayed(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sourceId);
        reader.ReadValueSafe(out string held);

        if (sourceId == networkManager.LocalClientId) return;
        ApplyState(sourceId, held);
    }

    private void ApplyState(ulong clientId, string held)
    {
        // アバターは位置メッセージで作られる。まだ無ければ、次の状態送信（0.5秒後）で反映する
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar)) return;

        avatar.heldItem = held;
        RefreshLabel(avatar);
    }

    /// <summary>名前の下に、体力・正気度・手持ちアイテムを小さく表示する。倒れている人は赤く表示</summary>
    private static void RefreshLabel(RemoteAvatar avatar)
    {
        if (avatar.label == null) return;

        string text = avatar.playerName;
        if (!string.IsNullOrEmpty(avatar.heldItem)) text += $"\n<size=60%>[{avatar.heldItem}]</size>";

        avatar.label.color = Color.white;
        if (avatar.label.text != text) avatar.label.text = text;
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

        if (networkManager.IsHost)
        {
            // ホストの位置は、全参加者へ id=0 として配る
            BroadcastRelay(networkManager.LocalClientId, playerName, feetPos, yaw, crouching, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgPose, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    private void BroadcastRelay(ulong sourceId, string playerName, Vector3 feetPos, float yaw, bool crouching, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(sourceId);
                writer.WriteValueSafe(playerName);
                writer.WriteValueSafe(feetPos);
                writer.WriteValueSafe(yaw);
                writer.WriteValueSafe(crouching);
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

        UpdateAvatar(senderId, playerName, feetPos, yaw, crouching);
        BroadcastRelay(senderId, playerName, feetPos, yaw, crouching, senderId);
    }

    // 参加者側：ホストが中継した他プレイヤーの位置を反映する
    private void OnPoseRelayed(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sourceId);
        reader.ReadValueSafe(out string playerName);
        reader.ReadValueSafe(out Vector3 feetPos);
        reader.ReadValueSafe(out float yaw);
        reader.ReadValueSafe(out bool crouching);

        if (sourceId == networkManager.LocalClientId) return;
        UpdateAvatar(sourceId, playerName, feetPos, yaw, crouching);
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

    private void UpdateAvatar(ulong clientId, string playerName, Vector3 feetPos, float yaw, bool crouching)
    {
        if (!avatars.TryGetValue(clientId, out RemoteAvatar avatar))
        {
            avatar = CreateAvatar(clientId);
            avatars[clientId] = avatar;
        }

        avatar.targetFeetPos = feetPos;
        avatar.targetYaw = yaw;
        avatar.crouching = crouching;
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

        // 他プレイヤーの見た目だけ。当たり判定があると操作の邪魔になるため取り除く
        Collider col = root.GetComponent<Collider>();
        if (col != null) Destroy(col);

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

        GameObject labelObj = new GameObject("NameLabel");
        labelObj.transform.SetParent(root.transform, false);
        labelObj.transform.localPosition = new Vector3(0f, LabelHeightOffset, 0f);
        TextMeshPro label = labelObj.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = LabelFontSize;
        label.color = Color.white;

        return new RemoteAvatar { root = root, label = label };
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

            // しゃがみ中は縮めて表示する（名前表示は縦に潰れないよう打ち消す）
            avatar.heightScale = Mathf.Lerp(avatar.heightScale, avatar.crouching ? CrouchHeightScale : 1f, t);
            avatar.root.transform.localScale = new Vector3(1f, avatar.heightScale, 1f);
            if (avatar.label != null)
            {
                avatar.label.transform.localScale = new Vector3(1f, 1f / avatar.heightScale, 1f);
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
