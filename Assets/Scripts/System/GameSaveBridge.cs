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

        Debug.Log($"[SAVE] スロット{SaveSystem.ActiveSlotIndex}を読み込みました: DAY {data.currentDay} / 所持金 ${data.currentMoney:N0} / 返済済み ${data.totalRepaid:N0}");
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
        SaveSystem.SaveCurrentGame();
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
