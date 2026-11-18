using UnityEngine;

/// <summary>
/// タイトル画面の上に重ねて出す簡易ロビー（IMGUI）。ロビーシーンは使わない。
/// 部屋を作る／招待コードで入る／メンバー確認／ホストがゲーム開始、までを担当する。
/// </summary>
public class MultiLobbyUI : MonoBehaviour
{
    private string joinCode = "";
    private string playerName = "";
    private bool busy;
    private System.Action onClose;

    public static MultiLobbyUI Open(System.Action onClose)
    {
        GameObject go = new GameObject("MultiLobbyUI");
        MultiLobbyUI ui = go.AddComponent<MultiLobbyUI>();
        ui.onClose = onClose;
        return ui;
    }

    private void Start()
    {
        if (NetworkSession.Instance != null)
        {
            playerName = NetworkSession.Instance.LocalPlayerName;
        }
    }

    private void Close()
    {
        if (NetworkSession.Instance != null) NetworkSession.Instance.LeaveRoom();
        onClose?.Invoke();
        Destroy(gameObject);
    }

    private async void CreateRoom()
    {
        busy = true;
        NetworkSession.Instance.LocalPlayerName = playerName;
        await NetworkSession.Instance.CreateRoomAsync($"{playerName}の部屋");
        busy = false;
    }

    private async void JoinRoom()
    {
        busy = true;
        NetworkSession.Instance.LocalPlayerName = playerName;
        await NetworkSession.Instance.JoinRoomAsync(joinCode.Trim().ToUpper());
        busy = false;
    }

    private void OnGUI()
    {
        NetworkSession net = NetworkSession.Instance;
        const float w = 460f, h = 420f;
        Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(box, "MULTIPLAYER LOBBY");

        GUILayout.BeginArea(new Rect(box.x + 20, box.y + 36, w - 40, h - 56));

        if (net == null)
        {
            GUILayout.Label("ネットワーク機能を初期化できませんでした。");
        }
        else if (net.State == NetworkSessionState.Offline || net.State == NetworkSessionState.Connecting)
        {
            GUILayout.Label("プレイヤー名");
            playerName = GUILayout.TextField(playerName, 16);
            GUILayout.Space(8);

            GUI.enabled = !busy && net.State == NetworkSessionState.Offline;
            if (GUILayout.Button("部屋を作る（ホスト）", GUILayout.Height(36))) CreateRoom();

            GUILayout.Space(12);
            GUILayout.Label("招待コード");
            joinCode = GUILayout.TextField(joinCode, 6);
            if (GUILayout.Button("コードで参加", GUILayout.Height(36))) JoinRoom();
            GUI.enabled = true;

            if (busy || net.State == NetworkSessionState.Connecting) GUILayout.Label("接続中...");
        }
        else
        {
            GUILayout.Label($"招待コード: {net.RoomCode}");
            GUILayout.Label($"メンバー ({net.Members.Count}/{NetworkSession.MaxPlayers})");
            foreach (LobbyMember m in net.Members)
            {
                GUILayout.Label($"  {(m.isHost ? "★ " : "")}{m.name}");
            }
            GUILayout.Space(8);

            if (net.IsHost)
            {
                if (GUILayout.Button("ゲーム開始", GUILayout.Height(40))) net.StartGame();
            }
            else
            {
                GUILayout.Label("ホストの開始を待っています...");
            }
        }

        if (net != null && !string.IsNullOrEmpty(net.LastError))
        {
            GUILayout.Space(6);
            GUILayout.Label($"<color=#FF6666>{net.LastError}</color>");
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("戻る", GUILayout.Height(30))) Close();

        GUILayout.EndArea();
    }
}
