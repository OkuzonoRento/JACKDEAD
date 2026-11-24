using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルで選んだセーブスロット（PC内のJSON）と、ゲーム内の進行データ（PlayerDataAsset / MapGenerator）をつなぐ。
/// ・ゲームシーン開始時：セーブの内容（所持金・日数・借金返済額）を読み込んで開始する
/// ・ステージクリア時：進行を保存する／死亡時：進行を初期化して保存する
/// ・マルチプレイ中は、ホストのセーブを全員で共有する。保存するのはホストだけ（参加者はホストから配られた内容で遊ぶ）
/// </summary>
public static class GameSaveBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static GameSaveData GetActiveData()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsOnline && net.SharedSave != null)
        {
            return net.SharedSave;
        }
        return SaveSystem.CurrentData;
    }

    /// <summary>
    /// 自分のPCのJSONへ書き込んでよいか。オフライン、またはマルチのホストのときだけ書き込む
    /// </summary>
    private static bool CanWriteSave()
    {
        NetworkSession net = NetworkSession.Instance;
        return net == null || !net.IsOnline || net.IsHost;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != NetworkSession.GameSceneName) return;

        GameSaveData data = GetActiveData();
        // タイトルを経由せずエディタから直接ゲームシーンを開いた場合は、セーブなしで従来通り動く
        if (data == null || !data.hasSaveData) return;

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        MapGenerator map = Object.FindAnyObjectByType<MapGenerator>();
        if (player == null || player.PlayerData == null || map == null) return;

        // 各スクリプトの Start より前（シーン読み込み直後）に反映する
        PlayerDataAsset playerData = player.PlayerData;
        playerData.PlayerMoney = data.currentMoney;
        playerData.CurrentStageLevel = Mathf.Max(1, data.currentDay);
        playerData.TotalRepaidDebt = Mathf.Max(0, data.totalRepaid);
        playerData.Inventory = new ItemDataAsset[3];

        map.SetStartLevel(playerData.CurrentStageLevel);

        // 自分のステータス（体力・正気度・ドーパミン）を、ID で見分けて復元する。名前を変えていても同じ人として扱う。
        // 同じ PC の別プロセスと ID が重なっていた場合は、席番号つきの ID で保存されているので、それを先に探す
        PlayerSaveEntry mine = null;
        NetworkSession netNow = NetworkSession.Instance;
        if (netNow != null && netNow.IsOnline && !netNow.IsHost)
        {
            mine = data.FindPlayer(SeatedId(PlayerIdentity.Id, (ulong)netNow.LocalSlotIndex));
        }
        if (mine == null) mine = data.FindPlayerOrAlias(PlayerIdentity.Id, PlayerIdentity.Alias);
        if (mine != null) player.ApplySavedStats(mine.health, mine.sanity, mine.dopamine);


        Debug.Log($"[SAVE] スロット{SaveSystem.ActiveSlotIndex}を読み込みました: DAY {data.currentDay} / 所持金 ${data.currentMoney:N0} / 返済済み ${data.totalRepaid:N0}");
    }

    /// <summary>
    /// 全員のステータスを、ID ごとにセーブデータへ書く。自分は直接、他の人は届いている状態から。
    /// いま部屋にいない人の分は、そのまま残す（あとで戻ってきたときに復元できる）
    /// </summary>
    private static void SavePlayerStats(GameSaveData data, PlayerController player)
    {
        NetworkSession net = NetworkSession.Instance;
        string localName = net != null ? net.LocalPlayerName : "Player";

        PlayerSaveEntry mine = data.GetOrAddPlayer(PlayerIdentity.Id, PlayerIdentity.Alias);
        mine.lastName = localName;
        mine.health = player.Health;
        mine.sanity = player.Sanity;
        mine.dopamine = player.Dopamine;

        if (net == null || !net.IsOnline || RemotePlayerSync.Instance == null) return;

        List<RemotePlayerSync.RemoteInfo> others = new List<RemotePlayerSync.RemoteInfo>();
        RemotePlayerSync.Instance.CollectRemoteStatuses(others);
        others.Sort((a, b) => a.clientId.CompareTo(b.clientId)); // 重なったときの割り当てが、毎回同じになるように

        // 同じ PC で複数起動していると ID が重なる。重なった人は、席番号をつけて別の人として保存する
        HashSet<string> usedIds = new HashSet<string> { PlayerIdentity.Id };
        foreach (RemotePlayerSync.RemoteInfo other in others)
        {
            string id = other.status.playerId;
            string alias = other.status.playerAlias;
            if (!usedIds.Add(id))
            {
                id = SeatedId(other.status.playerId, other.clientId);
                alias = ""; // 席番号つきの ID には、別名を使わない（重なった相手の分を取り違えないように）
                usedIds.Add(id);
            }

            PlayerSaveEntry entry = data.GetOrAddPlayer(id, alias);
            entry.lastName = other.name;
            entry.health = other.status.health;
            entry.sanity = other.status.sanity;
            entry.dopamine = other.status.dopamine;
        }
    }

    /// <summary>ID が重なったときに使う、席番号つきの ID</summary>
    private static string SeatedId(string playerId, ulong clientId)
    {
        return playerId + "#" + clientId;
    }

    /// <summary>
    /// ステージクリア後の進行を保存する（次のレベルへ進んだ直後に呼ぶ）
    /// </summary>
    public static void SaveProgress(PlayerController player, MapGenerator map)
    {
        if (!CanWriteSave() || player == null || map == null || player.PlayerData == null) return;

        GameSaveData data = SaveSystem.CurrentData;
        if (!data.hasSaveData) return;

        data.currentMoney = player.PlayerMoney;
        data.currentDay = map.CurrentStageLevel;
        data.totalRepaid = player.PlayerData.TotalRepaidDebt;
        data.remainingDebt = Mathf.Max(0, map.TotalDebt - data.totalRepaid);
        SavePlayerStats(data, player);
        SaveSystem.SaveCurrentGame();
    }

    /// <summary>
    /// 全滅のあと、同じスロットで新しいセーブデータを用意する（マルチのホストだけ。ファイルは、次のゲーム開始のときに作る）
    /// </summary>
    public static void StartFreshSlot()
    {
        if (!CanWriteSave()) return;
        SaveSystem.SelectAndLoadSlot(true, SaveSystem.ActiveSlotIndex);
    }

    /// <summary>
    /// 死亡時に、このスロットのセーブデータを削除する（次回は新規データで始まる）。マルチではホストのPCのデータだけ削除する
    /// </summary>
    public static void DeleteSaveOnDeath()
    {
        if (!CanWriteSave()) return;

        SaveSystem.DeleteSlot(SaveSystem.ActiveSlotIndex);
        // メモリ上のデータも初期化し、この後に誤って保存し直されないようにする
        SaveSystem.ClearCurrentData();
    }
}
