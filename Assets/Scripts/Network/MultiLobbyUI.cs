using UnityEngine;

/// <summary>
/// タイトル画面の上に重ねて出す簡易ロビー（IMGUI）。ロビーシーンは使わない。
/// 流れ：「ルームを作る」→ セーブデータ選択 → 部屋作成 / 「ルームに参加する」→ 招待コード入力
/// （セーブ選択が必要なのはホストのみ。参加者はホストのセーブで遊ぶ）
/// </summary>
public class MultiLobbyUI : MonoBehaviour
{
    private enum Step { Menu, SelectSave, JoinCode }

    private Step step = Step.Menu;
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

    private async void CreateRoom(int slotNumber)
    {
        busy = true;
        // ホストになる人だけがセーブデータを選ぶ
        SaveSystem.SelectAndLoadSlot(true, slotNumber);
        NetworkSession.Instance.LocalPlayerName = playerName;
        bool ok = await NetworkSession.Instance.CreateRoomAsync($"{playerName}の部屋");
        if (!ok) step = Step.Menu;
        busy = false;
    }

    private async void JoinRoom()
    {
        busy = true;
        SaveSystem.IsMultiplayerMode = true;
        NetworkSession.Instance.LocalPlayerName = playerName;
        await NetworkSession.Instance.JoinRoomAsync(joinCode.Trim().ToUpper());
        busy = false;
    }

    private void OnGUI()
    {
        NetworkSession net = NetworkSession.Instance;
        const float w = 460f, h = 420f;
        Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(box, "MULTIPLAYER");

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
            switch (step)
            {
                case Step.Menu:
                    if (GUILayout.Button("ルームを作る（ホスト）", GUILayout.Height(36))) step = Step.SelectSave;
                    GUILayout.Space(8);
                    if (GUILayout.Button("ルームに参加する", GUILayout.Height(36))) step = Step.JoinCode;
                    break;

                case Step.SelectSave:
                    GUILayout.Label("セーブデータを選択");
                    for (int i = 1; i <= 3; i++)
                    {
                        GameSaveData data = SaveSystem.PeekSaveData(i);
                        string label = data != null && data.hasSaveData
                            ? $"SLOT {i}  -  DAY {data.currentDay}  |  DEBT: ${data.remainingDebt:N0}"
                            : $"SLOT {i}  -  [ NEW GAME ]";
                        if (GUILayout.Button(label, GUILayout.Height(36))) CreateRoom(i);
                    }
                    break;

                case Step.JoinCode:
                    GUILayout.Label("招待コード");
                    joinCode = GUILayout.TextField(joinCode, 6);
                    if (GUILayout.Button("コードで参加", GUILayout.Height(36))) JoinRoom();
                    break;
            }
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
        bool offline = net == null || net.State == NetworkSessionState.Offline;
        if (GUILayout.Button("戻る", GUILayout.Height(30)))
        {
            // 選択ステップ中は一つ前（ルーム作成/参加の選択）へ、メニューならタイトルへ
            if (offline && step != Step.Menu && !busy) step = Step.Menu;
            else Close();
        }

        GUILayout.EndArea();
    }
}
