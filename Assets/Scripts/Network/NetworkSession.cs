using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum NetworkSessionState
{
    Offline,    // 未接続（ひとりで遊ぶ場合もここ）
    Connecting, // 部屋の作成・参加の処理中
    InRoom,     // 部屋に入っている（ロビー）
    InGame      // ゲームシーンでプレイ中
}

public class LobbyMember
{
    public string playerId;
    public string name;
    public bool isHost;
    /// <summary>ホストまでの往復の通信時間（ミリ秒）。-1 はまだ測れていない。ホスト自身は 0</summary>
    public int pingMs = -1;
}

/// <summary>
/// マルチプレイの接続管理。UGS（Authentication / Lobby / Relay）と Netcode for GameObjects を束ねる。
/// ・部屋を作った人がホスト（サーバー兼プレイヤー）。最大4人
/// ・Relay は WebSocket(wss/443) を使うので、ポート開放なし・UDP不可の回線でも繋がる
/// ・ゲーム起動時に自動生成され、シーンをまたいで残る（DontDestroyOnLoad）
/// </summary>
public class NetworkSession : MonoBehaviour
{
    public const int MaxPlayers = 6;
    public const int CompatVersion = 1;
    public const string GameSceneName = "SampleScene";
    public const string TitleSceneName = "TitleScene";

    private const string LobbyKeyRelayCode = "relay";
    private const string LobbyKeyCompat = "compat";
    private const string PlayerKeyName = "name";
    private const string PlayerKeyPing = "ping";
    private const string PlayerPrefsName = "OnlinePlayerName";

    private const string MsgStart = "jd.start";
    private const string MsgStageClear = "jd.clear";
    private const string MsgPing = "jd.ping";
    private const string MsgPong = "jd.pong";

    private const float HeartbeatInterval = 15f;
    private const float PollInterval = 2f;
    private const float ConnectTimeoutSeconds = 15f;
    private const float PingInterval = 3f;        // 回線の測定の間隔（参加者 → ホスト）
    private const float PingPublishInterval = 4f;  // 測定値を部屋の情報へ書く最短の間隔

    public static NetworkSession Instance { get; private set; }

    public NetworkSessionState State { get; private set; } = NetworkSessionState.Offline;
    public string RoomCode { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public IReadOnlyList<LobbyMember> Members => members;

    /// <summary>ホストが決めた、全員共通のマップ生成シード（ゲーム開始時に配布される）</summary>
    public int SharedSeed { get; private set; }
    /// <summary>ホストのセーブデータ（ゲーム開始時に配布される）。参加者はこれを元にプレイする</summary>
    public GameSaveData SharedSave { get; private set; }

    public event System.Action<int, int> StageClearReceived; // (クリアしたレベル, 精算後の共有所持金)

    private NetworkManager networkManager;
    private UnityTransport transport;
    private Lobby currentLobby;
    private readonly List<LobbyMember> members = new List<LobbyMember>();

    /// <summary>自分からホストまでの往復の通信時間（ミリ秒）。-1 は未測定。ホストは 0</summary>
    public int LocalPingMs { get; private set; } = -1;
    private float pingTimer;
    private float pingPublishedAt;
    private int pingPublishedValue = int.MinValue;
    private bool isPublishingPing;

    private float heartbeatTimer;
    private float pollTimer;
    private bool isPolling;
    private bool isLeaving;

    public bool IsOnline => State == NetworkSessionState.InRoom || State == NetworkSessionState.InGame;
    public bool IsHost => networkManager != null && networkManager.IsListening && networkManager.IsHost;
    /// <summary>ホストが0番、参加者は入った順に1〜3番の席</summary>
    public int LocalSlotIndex => networkManager != null && networkManager.IsListening ? (int)networkManager.LocalClientId : 0;

    public string LocalPlayerName
    {
        get
        {
            string saved = PlayerPrefs.GetString(PlayerPrefsName, "");
            if (string.IsNullOrEmpty(saved))
            {
                saved = $"Player{Random.Range(1000, 10000)}";
                PlayerPrefs.SetString(PlayerPrefsName, saved);
            }
            return saved;
        }
        set
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim();
            PlayerPrefs.SetString(PlayerPrefsName, trimmed.Length > 16 ? trimmed.Substring(0, 16) : trimmed);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("NetworkSession");
        DontDestroyOnLoad(go);
        go.AddComponent<NetworkSession>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ゲームシーンに入ったら、他プレイヤーの位置同期を開始する
        if (scene.name == GameSceneName && IsOnline)
        {
            State = NetworkSessionState.InGame;
            new GameObject("RemotePlayerSync").AddComponent<RemotePlayerSync>();
            new GameObject("GameplaySync").AddComponent<GameplaySync>();
        }
    }

    private void Update()
    {
        if (currentLobby == null) return;

        float dt = Time.unscaledDeltaTime;
        heartbeatTimer += dt;
        pollTimer += dt;

        if (IsHost && heartbeatTimer >= HeartbeatInterval)
        {
            heartbeatTimer = 0f;
            _ = SendHeartbeatAsync();
        }

        UpdatePing(dt);

        // ロビー画面の間だけ参加者リストを更新する（ゲーム中は不要）
        if (State == NetworkSessionState.InRoom && pollTimer >= PollInterval && !isPolling)
        {
            pollTimer = 0f;
            _ = PollLobbyAsync();
        }
    }

    // ------------------------------------------------------------------
    // 認証・NetworkManager 準備
    // ------------------------------------------------------------------

    private async Task EnsureSignedInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            InitializationOptions options = new InitializationOptions();
            string profile = GetProfileName();
            if (!string.IsNullOrEmpty(profile)) options.SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }

        while (UnityServices.State == ServicesInitializationState.Initializing)
        {
            await Task.Yield();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    /// <summary>
    /// エディタ・開発ビルドでは起動ごとに別アカウントにする（同じPCで複数起動して試せるように）。
    /// -mpProfile 名前 で明示指定も可能。
    /// </summary>
    private string GetProfileName()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-mpProfile") return Sanitize(args[i + 1]);
        }

        if (Application.isEditor || Debug.isDebugBuild)
        {
            return System.Guid.NewGuid().ToString("N").Substring(0, 12);
        }
        return "";
    }

    private string Sanitize(string raw)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
        }
        string result = sb.ToString();
        return result.Length > 30 ? result.Substring(0, 30) : result;
    }

    private void EnsureNetworkManager()
    {
        if (networkManager != null) return;

        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            // 非アクティブのまま部品を組み立ててから有効化する（Awake より前に設定を入れるため）
            GameObject go = new GameObject("NetworkManager");
            go.SetActive(false);
            transport = go.AddComponent<UnityTransport>();
            networkManager = go.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = true
            };
            DontDestroyOnLoad(go);
            go.SetActive(true);
        }
        else
        {
            transport = networkManager.GetComponent<UnityTransport>();
        }

        // 受信キューの既定（128パケット）は、3人以上で位置などを頻繁にやりとりするとあふれ、
        // リレーとの接続が切れる（"Receive queue is full" → Transport failure）。余裕を持たせる
        if (transport != null)
        {
            transport.MaxPacketQueueSize = 2048;
        }

        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private Player BuildLocalPlayer()
    {
        return new Player(
            id: AuthenticationService.Instance.PlayerId,
            data: new Dictionary<string, PlayerDataObject>
            {
                { PlayerKeyName, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, LocalPlayerName) },
                { PlayerKeyPing, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, networkManager != null && networkManager.IsHost ? "0" : "-1") }
            });
    }

    /// <summary>前回のエラー表示を消す（ロビー画面を開くたびに呼ぶ）</summary>
    public void ClearError()
    {
        LastError = "";
    }

    private void SetError(string message)
    {
        LastError = message;
        Debug.LogWarning($"[ONLINE] {message}");
    }

    // ------------------------------------------------------------------
    // 部屋の作成・参加・退出
    // ------------------------------------------------------------------

    /// <summary>
    /// 部屋を作ってホストになる。isPrivate=true なら招待コードを知っている人だけが入れる
    /// </summary>
    public async Task<bool> CreateRoomAsync(string roomName, bool isPrivate = true)
    {
        if (State != NetworkSessionState.Offline) return false;

        State = NetworkSessionState.Connecting;
        LastError = "";

        try
        {
            await EnsureSignedInAsync();
            EnsureNetworkManager();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            transport.UseWebSockets = true;
            transport.SetRelayServerData(allocation.ToRelayServerData("wss"));

            if (!networkManager.StartHost())
            {
                throw new System.Exception("StartHost に失敗しました");
            }
            RegisterMessageHandlers();

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = BuildLocalPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    { LobbyKeyRelayCode, new DataObject(DataObject.VisibilityOptions.Member, relayCode) },
                    { LobbyKeyCompat, new DataObject(DataObject.VisibilityOptions.Public, CompatVersion.ToString()) }
                }
            };

            string safeName = string.IsNullOrWhiteSpace(roomName) ? $"{LocalPlayerName}'s room" : roomName;
            currentLobby = await LobbyService.Instance.CreateLobbyAsync(safeName, MaxPlayers, options);
            RoomCode = currentLobby.LobbyCode;

            RebuildMembers();
            heartbeatTimer = 0f;
            pollTimer = 0f;
            State = NetworkSessionState.InRoom;
            return true;
        }
        catch (System.Exception e)
        {
            SetError($"部屋を作れませんでした: {e.Message}");
            ResetConnection();
            return false;
        }
    }

    /// <summary>
    /// 6文字の招待コードで部屋に参加する
    /// </summary>
    public async Task<bool> JoinRoomAsync(string roomCode)
    {
        if (State != NetworkSessionState.Offline) return false;

        State = NetworkSessionState.Connecting;
        LastError = "";

        try
        {
            await EnsureSignedInAsync();
            EnsureNetworkManager();

            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(
                roomCode.Trim().ToUpperInvariant(),
                new JoinLobbyByCodeOptions { Player = BuildLocalPlayer() });
            currentLobby = lobby;

            if (!lobby.Data.TryGetValue(LobbyKeyCompat, out DataObject compat) || compat.Value != CompatVersion.ToString())
            {
                throw new System.Exception("ゲームのバージョンが違います");
            }
            if (!lobby.Data.TryGetValue(LobbyKeyRelayCode, out DataObject relay))
            {
                throw new System.Exception("部屋の接続情報が見つかりません");
            }

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relay.Value);

            transport.UseWebSockets = true;
            transport.SetRelayServerData(joinAllocation.ToRelayServerData("wss"));

            if (!networkManager.StartClient())
            {
                throw new System.Exception("StartClient に失敗しました");
            }

            float waited = 0f;
            while (!networkManager.IsConnectedClient && waited < ConnectTimeoutSeconds)
            {
                if (!networkManager.IsListening) throw new System.Exception("ホストに接続できませんでした");
                waited += Time.unscaledDeltaTime;
                await Task.Yield();
            }
            if (!networkManager.IsConnectedClient)
            {
                throw new System.Exception("接続がタイムアウトしました");
            }

            RegisterMessageHandlers();
            RoomCode = lobby.LobbyCode;
            RebuildMembers();
            heartbeatTimer = 0f;
            pollTimer = 0f;
            State = NetworkSessionState.InRoom;
            return true;
        }
        catch (System.Exception e)
        {
            SetError($"部屋に参加できませんでした: {e.Message}");
            string lobbyId = currentLobby?.Id;
            ResetConnection();
            if (lobbyId != null) _ = LeaveLobbyQuietlyAsync(lobbyId, false);
            return false;
        }
    }

    /// <summary>
    /// 部屋から抜けて通信を終了する（ホストが抜けると部屋は解散）。ゲームオーバー時のタイトル復帰にも使う
    /// </summary>
    public void LeaveRoom()
    {
        if (currentLobby != null)
        {
            _ = LeaveLobbyQuietlyAsync(currentLobby.Id, IsHost);
        }
        ResetConnection();
    }

    private async Task LeaveLobbyQuietlyAsync(string lobbyId, bool wasHost)
    {
        try
        {
            if (wasHost)
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            }
            else
            {
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"[ONLINE] 部屋の退出処理: {e.Message}");
        }
    }

    private void ResetConnection()
    {
        isLeaving = true;

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        currentLobby = null;
        members.Clear();
        RoomCode = "";
        LocalPingMs = -1;
        pingTimer = 0f;
        pingPublishedValue = int.MinValue;
        State = NetworkSessionState.Offline;
        isLeaving = false;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // 参加者側：ホストとの接続が切れた（ホストが退出・回線切断）
        if (isLeaving || networkManager == null || networkManager.IsHost) return;
        if (clientId != networkManager.LocalClientId) return;

        SetError("ホストとの接続が切れました");
        ResetConnection();

        if (SceneManager.GetActiveScene().name != TitleSceneName)
        {
            SceneManager.LoadScene(TitleSceneName);
        }
    }

    // ------------------------------------------------------------------
    // ロビーの更新
    // ------------------------------------------------------------------

    private async Task SendHeartbeatAsync()
    {
        if (currentLobby == null) return;
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ONLINE] ハートビート失敗: {e.Message}");
        }
    }

    private async Task PollLobbyAsync()
    {
        if (currentLobby == null) return;
        isPolling = true;
        try
        {
            currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            RebuildMembers();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ONLINE] 部屋情報の取得失敗: {e.Message}");
        }
        isPolling = false;
    }

    private void RebuildMembers()
    {
        members.Clear();
        if (currentLobby == null) return;

        foreach (Player p in currentLobby.Players)
        {
            string displayName = "Player";
            if (p.Data != null && p.Data.TryGetValue(PlayerKeyName, out PlayerDataObject nameData))
            {
                displayName = nameData.Value;
            }

            int ping = -1;
            if (p.Data != null && p.Data.TryGetValue(PlayerKeyPing, out PlayerDataObject pingData))
            {
                int.TryParse(pingData.Value, out ping);
            }
            bool isHostMember = p.Id == currentLobby.HostId;
            // 自分の値は、部屋の情報に反映されるのを待たず、手元の最新を使う
            if (p.Id == AuthenticationService.Instance.PlayerId && LocalPingMs >= 0) ping = LocalPingMs;
            if (isHostMember) ping = 0;

            members.Add(new LobbyMember
            {
                playerId = p.Id,
                name = displayName,
                isHost = isHostMember,
                pingMs = ping
            });
        }
    }

    // ------------------------------------------------------------------
    // ゲーム開始・ステージクリア通知（独自メッセージ）
    // ------------------------------------------------------------------

    private void RegisterMessageHandlers()
    {
        CustomMessagingManager messaging = networkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(MsgStart, OnStartMessage);
        messaging.RegisterNamedMessageHandler(MsgStageClear, OnStageClearMessage);
        messaging.RegisterNamedMessageHandler(MsgPing, OnPingMessage);
        messaging.RegisterNamedMessageHandler(MsgPong, OnPongMessage);
    }

    /// <summary>
    /// ホストだけが呼べる。全員に共通シードとセーブデータを配り、全員を同じゲームシーンへ移す
    /// </summary>
    public void StartGame()
    {
        if (!IsHost || State != NetworkSessionState.InRoom) return;

        SaveSystem.CommitNewSlot(); // 新規のセーブデータは、ゲーム開始のこのとき初めて作る

        SharedSeed = Random.Range(int.MinValue, int.MaxValue);
        SharedSave = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(SaveSystem.CurrentData));
        string saveJson = JsonUtility.ToJson(SharedSave);

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(32 + saveJson.Length * 2, Allocator.Temp))
            {
                writer.WriteValueSafe(SharedSeed);
                writer.WriteValueSafe(saveJson);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgStart, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        _ = LockLobbyAsync();

        State = NetworkSessionState.InGame;
        networkManager.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
    }

    private async Task LockLobbyAsync()
    {
        if (currentLobby == null) return;
        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions { IsLocked = true });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ONLINE] 部屋のロックに失敗: {e.Message}");
        }
    }

    private void OnStartMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int seed);
        reader.ReadValueSafe(out string saveJson);

        SharedSeed = seed;
        SharedSave = JsonUtility.FromJson<GameSaveData>(saveJson);
        // シーン移動はホストの NetworkManager.SceneManager が全員に対して行う
    }

    /// <summary>
    /// ステージクリアを全員に知らせる（レバーを引いた人から呼ぶ）
    /// </summary>
    public void BroadcastStageClear(int clearedLevel, int newMoney)
    {
        if (!IsOnline || networkManager == null || !networkManager.IsListening) return;

        if (networkManager.IsHost)
        {
            SendStageClear(clearedLevel, newMoney, ulong.MaxValue);
        }
        else
        {
            using (FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe(clearedLevel);
                writer.WriteValueSafe(newMoney);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgStageClear, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    private void SendStageClear(int clearedLevel, int newMoney, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;

            using (FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp))
            {
                writer.WriteValueSafe(clearedLevel);
                writer.WriteValueSafe(newMoney);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MsgStageClear, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    private void OnStageClearMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int clearedLevel);
        reader.ReadValueSafe(out int newMoney);

        // ホストは、送ってきた人以外の参加者へ転送する
        if (networkManager.IsHost)
        {
            SendStageClear(clearedLevel, newMoney, senderId);
        }

        StageClearReceived?.Invoke(clearedLevel, newMoney);
    }

    // ------------------------------------------------------------------
    // 回線の強さ（ホストまでの往復の通信時間）
    // ------------------------------------------------------------------

    /// <summary>参加者が、一定の間隔でホストへ ping を送って測る。ホスト自身は 0 とする</summary>
    private void UpdatePing(float dt)
    {
        if (State != NetworkSessionState.InRoom || networkManager == null || !networkManager.IsListening) return;

        if (networkManager.IsHost)
        {
            LocalPingMs = 0;
            return;
        }
        if (!networkManager.IsConnectedClient) return;

        pingTimer += dt;
        if (pingTimer < PingInterval) return;
        pingTimer = 0f;

        using (FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(Time.realtimeSinceStartup);
            networkManager.CustomMessagingManager.SendNamedMessage(
                MsgPing, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }
    }

    /// <summary>ホスト：受け取った時刻をそのまま送り返す</summary>
    private void OnPingMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float sentAt);
        if (networkManager == null || !networkManager.IsHost) return;

        using (FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(sentAt);
            networkManager.CustomMessagingManager.SendNamedMessage(MsgPong, senderId, writer, NetworkDelivery.Reliable);
        }
    }

    /// <summary>参加者：送った時刻との差が、往復の通信時間。ほかの人にも見えるよう、部屋の情報へ書く</summary>
    private void OnPongMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out float sentAt);
        int ms = Mathf.Max(1, Mathf.RoundToInt((Time.realtimeSinceStartup - sentAt) * 1000f));
        LocalPingMs = ms;

        bool changedEnough = pingPublishedValue == int.MinValue || Mathf.Abs(ms - pingPublishedValue) >= Mathf.Max(8, pingPublishedValue / 4);
        if (changedEnough && !isPublishingPing && Time.unscaledTime - pingPublishedAt >= PingPublishInterval)
        {
            _ = PublishPingAsync(ms);
        }
    }

    private async Task PublishPingAsync(int ms)
    {
        if (currentLobby == null) return;
        isPublishingPing = true;
        pingPublishedAt = Time.unscaledTime;
        pingPublishedValue = ms;
        try
        {
            await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { PlayerKeyPing, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, ms.ToString()) }
                    }
                });
        }
        catch (System.Exception e)
        {
            Debug.Log($"[ONLINE] 通信状況の更新に失敗: {e.Message}");
        }
        isPublishingPing = false;
    }
}