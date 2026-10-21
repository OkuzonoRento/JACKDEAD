using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("プレイヤーデータ参照")]
    public PlayerDataAsset PlayerData;

    private float _health;
    private float _sanity;
    private float _dopamine;
    private float _stamina;

    private float _maxHealth;
    private float _maxSanity;
    private float _maxDopamine;
    private float _maxStamina;

    [Header("インベントリ (所持アイテム)")]
    public ItemDataAsset[] Inventory = new ItemDataAsset[3];

    // プロパティ（外部からの参照用）
    public float Health => _health;
    public float Sanity => _sanity;
    public float Dopamine => _dopamine;
    public float Stamina => _stamina;

    private void Start()
    {
        StatusSetUp();
    }

    private void StatusSetUp()
    {
        if (PlayerData != null)
        {
            _maxHealth = PlayerData.MaxHealth;
            _health = PlayerData.MaxHealth;

            _maxSanity = PlayerData.MaxSanity;
            _sanity = PlayerData.MaxSanity;

            _maxDopamine = PlayerData.MaxDopamine;
            _dopamine = PlayerData.MaxDopamine;

            _maxStamina = PlayerData.MaxStamina;
            _stamina = PlayerData.MaxStamina;

            if (PlayerData.Inventory != null && PlayerData.Inventory.Length >= 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    Inventory[i] = PlayerData.Inventory[i];
                }
            }
        }
    }

    private void Update()
    {
        // テスト用：時間経過による正気度減少（マップ滞在時を想定）
        // 肩代わりシステムやデバフのテストに活用できます
        ConsumeSanity(Time.deltaTime * 0.15f); // 換算10分で減る調整用のベース値
    }

    // --- 正気度減少 ＆ ドーパミン肩代わりシステム ---
    public void ConsumeSanity(float amount)
    {
        if (amount <= 0) return;

        // ドーパミンが溜まっている（0より大きい）場合は、正気度の代わりにドーパミンを優先消費する
        if (_dopamine > 0f)
        {
            float consumeDopamine = Mathf.Min(_dopamine, amount);
            _dopamine -= consumeDopamine;

            // 減少量がドーパミンで肩書ききれなかった分があれば、残りは正気度から削る
            float remainder = amount - consumeDopamine;
            if (remainder > 0f)
            {
                _sanity = Mathf.Max(0f, _sanity - remainder);
            }

            Debug.Log($"[正気度/ドーパミン] ドーパミンが正気度の低下を肩代わりしました！ (残Dopamine: {_dopamine:F1}, 残Sanity: {_sanity:F1})");
        }
        else
        {
            // ドーパミンがない場合は通常通り正気度が減る
            _sanity = Mathf.Max(0f, _sanity - amount);
        }

        CheckSanityDebuffs();
    }

    // 正気度が低い状態のデバフ判定
    private void CheckSanityDebuffs()
    {
        if (_sanity < 30f)
        {
            // TODO: ギャンブルの掛け金上昇やスタミナ減少ペナルティフラグを有効にする
        }
    }

    // 体力の増減
    public void ModifyHealth(float amount)
    {
        _health = Mathf.Clamp(_health + amount, 0f, _maxHealth);
        if (_health <= 0f)
        {
            TriggerGameOver();
        }
    }

    // ドーパミンの増減（ギャンブル結果など）
    public void ModifyDopamine(float amount)
    {
        _dopamine = Mathf.Clamp(_dopamine + amount, 0f, _maxDopamine);
    }

    // --- インベントリの総重量を計算するメソッド ---
    public float GetTotalInventoryWeight()
    {
        float totalWeight = 0f;
        foreach (var item in Inventory)
        {
            if (item != null)
            {
                totalWeight += item.ItemWeight;
            }
        }
        return totalWeight;
    }

    // インベントリの状態を PlayerDataAsset へ保存
    public void SaveInventoryToData()
    {
        if (PlayerData != null)
        {
            if (PlayerData.Inventory == null || PlayerData.Inventory.Length < 3)
            {
                PlayerData.Inventory = new ItemDataAsset[3];
            }

            for (int i = 0; i < 3; i++)
            {
                PlayerData.Inventory[i] = Inventory[i];
            }
        }
    }

    private void TriggerGameOver()
    {
        Debug.LogWarning("[GAME OVER] 体力が0になりました。ゲームオーバー処理を実行します。");
        // TODO: ゲームオーバー画面やスクラップロスト処理
    }
}