using System.IO;
using UnityEngine;

/// <summary>プレイヤーごとのステータス。持ち主は、名前ではなく ID で見分ける（名前を変えても同じ人として扱える）</summary>
[System.Serializable]
public class PlayerSaveEntry
{
    public string playerId = "";
    public string lastName = "";   // 最後に使っていた名前（表示用。見分けには使わない）
    public float health = -1f;     // 負の値は「保存なし」
    public float sanity = -1f;
    public float dopamine = 0f;
}

[System.Serializable]
public class GameSaveData
{
    public bool hasSaveData = false;
    public int currentMoney = 0;         // 所持金
    public int remainingDebt = 1000000;  // 残り借金（初期100万）
    public int currentDay = 1;           // 経過日数
    public int totalRepaid = 0;          // total repaid debt (continues past full repayment for endless mode)

    // 体のパーツ（true = 残っている, false = 売却済み）
    public bool hasLeftArm = true;
    public bool hasRightArm = true;
    public bool hasLeftEye = true;
    public bool hasLegs = true;

    // 各プレイヤーのステータス（ID ごと）。マルチプレイでは、全員分をホストのセーブに保存する
    public System.Collections.Generic.List<PlayerSaveEntry> players = new System.Collections.Generic.List<PlayerSaveEntry>();

    public PlayerSaveEntry FindPlayer(string playerId)
    {
        if (players == null || string.IsNullOrEmpty(playerId)) return null;
        foreach (PlayerSaveEntry e in players) if (e != null && e.playerId == playerId) return e;
        return null;
    }

    public PlayerSaveEntry GetOrAddPlayer(string playerId)
    {
        if (players == null) players = new System.Collections.Generic.List<PlayerSaveEntry>();
        PlayerSaveEntry e = FindPlayer(playerId);
        if (e == null) { e = new PlayerSaveEntry { playerId = playerId }; players.Add(e); }
        return e;
    }

    /// <summary>
    /// ID で探し、無ければ以前の ID（別名）で探す。別名で見つかったら、ID を新しいものへ書き換えて引き継ぐ
    /// （PC の ID から Steam ID へ移ったときなど）
    /// </summary>
    public PlayerSaveEntry FindPlayerOrAlias(string playerId, string alias)
    {
        PlayerSaveEntry e = FindPlayer(playerId);
        if (e != null) return e;
        e = FindPlayer(alias);
        if (e != null) e.playerId = playerId;
        return e;
    }

    public PlayerSaveEntry GetOrAddPlayer(string playerId, string alias)
    {
        PlayerSaveEntry e = FindPlayerOrAlias(playerId, alias);
        return e ?? GetOrAddPlayer(playerId);
    }
}

public static class SaveSystem
{
    public static bool IsMultiplayerMode { get; set; } = false;
    public static int ActiveSlotIndex { get; private set; } = 1;
    public static GameSaveData CurrentData { get; private set; } = new GameSaveData();

    private static string GetSavePath(int slotIndex)
    {
        return Path.Combine(Application.persistentDataPath, $"save_slot{slotIndex}.json");
    }

    public static bool HasSaveFile(int slotIndex)
    {
        return File.Exists(GetSavePath(slotIndex));
    }

    public static GameSaveData PeekSaveData(int slotIndex)
    {
        string path = GetSavePath(slotIndex);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<GameSaveData>(json);
        }
        return null;
    }

    public static GameSaveData SelectAndLoadSlot(bool isMultiplayer, int slotIndex)
    {
        IsMultiplayerMode = isMultiplayer;
        ActiveSlotIndex = slotIndex;

        string path = GetSavePath(slotIndex);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            CurrentData = JsonUtility.FromJson<GameSaveData>(json);
        }
        else
        {
            CurrentData = new GameSaveData();
            // 新しいセーブデータは、ここでは作らない（メモリ上だけ）。ゲームを開始したときに CommitNewSlot で作る。
            // こうすると、スロットを選んで戻っただけでは、空のセーブファイルが残らない
            CurrentData.hasSaveData = true;
        }
        return CurrentData;
    }

    /// <summary>ゲーム開始のときに呼ぶ。選んだスロットにセーブファイルがまだ無ければ、ここで新規に作る</summary>
    public static void CommitNewSlot()
    {
        if (!File.Exists(GetSavePath(ActiveSlotIndex))) SaveCurrentGame();
    }

    public static void SaveCurrentGame()
    {
        CurrentData.hasSaveData = true;
        string path = GetSavePath(ActiveSlotIndex);
        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(path, json);
        Debug.Log($"セーブ完了 (Slot {ActiveSlotIndex})");
    }

    /// <summary>
    /// メモリ上のセーブデータを未使用の状態に戻す（ファイルは触らない）
    /// </summary>
    public static void ClearCurrentData()
    {
        CurrentData = new GameSaveData();
    }

    public static void DeleteSlot(int slotIndex)
    {
        string path = GetSavePath(slotIndex);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"データ削除 (Slot {slotIndex})");
        }
    }
}