using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵の出現設定。Resources/EnemySpawnConfig に置くと MapGenerator が自動で読み込む
/// （シーンの設定を触らなくてよい）。Editor メニュー「JackDead/Setup Enemy」で作られる。
///
/// REPO と同じく、敵を3つのティア（強さの段階）に分け、レベルごとに「各ティアから何体出すか」を決める。
/// 出す体はそのティアのプレハブからランダムに選ぶ。プレハブが空のティアは出さない。
/// 初期値は REPO の序盤の構成（レベル1〜2: T1×1 + T3×1 / 3〜5: 各ティア1体 / 6〜8: 各2体 / 9〜: T1×2, T2×3, T3×2）。
/// </summary>
[CreateAssetMenu(fileName = "EnemySpawnConfig", menuName = "JackDead/EnemySpawnConfig")]
public class EnemySpawnConfig : ScriptableObject
{
    public const string ResourceName = "EnemySpawnConfig";

    [System.Serializable]
    public class Tier
    {
        public string name = "Tier";
        [Tooltip("このティアで出る敵のプレハブ（複数ならランダム）")]
        public List<GameObject> prefabs = new List<GameObject>();
        [Tooltip("レベル1, 2, 3… で出す数。レベルがこれより進んだら最後の値のまま")]
        public int[] countsByLevel = new int[0];

        public int GetCount(int level)
        {
            if (countsByLevel == null || countsByLevel.Length == 0) return 0;
            int index = Mathf.Clamp(level - 1, 0, countsByLevel.Length - 1);
            return Mathf.Max(0, countsByLevel[index]);
        }
    }

    [Header("ティア別の出現数（REPO準拠）")]
    public Tier[] tiers = new Tier[]
    {
        new Tier { name = "Tier 1", countsByLevel = new[] { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 } },
        new Tier { name = "Tier 2", countsByLevel = new[] { 0, 0, 1, 1, 1, 2, 2, 2, 3, 3 } },
        new Tier { name = "Tier 3", countsByLevel = new[] { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 } },
    };

    [Header("出現位置")]
    [Min(0), Tooltip("バスの部屋から、これ以上（部屋数）離れた部屋にだけ出す")]
    public int minRoomDistanceFromBus = 2;

    /// <summary>このレベルで出す敵のプレハブ一覧（共有シードの乱数で選ぶので、マルチでも全員同じになる）</summary>
    public List<GameObject> RollSpawnList(int level)
    {
        List<GameObject> result = new List<GameObject>();
        if (tiers == null) return result;

        foreach (Tier tier in tiers)
        {
            if (tier == null || tier.prefabs == null) continue;
            List<GameObject> valid = tier.prefabs.FindAll(p => p != null);
            if (valid.Count == 0) continue;

            int count = tier.GetCount(level);
            for (int i = 0; i < count; i++) result.Add(valid[Random.Range(0, valid.Count)]);
        }
        return result;
    }

    private static EnemySpawnConfig cached;

    public static EnemySpawnConfig Load()
    {
        if (cached == null) cached = Resources.Load<EnemySpawnConfig>(ResourceName);
        return cached;
    }
}
