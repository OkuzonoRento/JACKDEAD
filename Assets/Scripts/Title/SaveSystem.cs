using System.IO;
using UnityEngine;

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