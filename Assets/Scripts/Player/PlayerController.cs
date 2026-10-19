using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("プレイヤーデータ参照")]
    public PlayerDataAsset PlayerData;

    [Header("リアルタイム・ステータス (ランタイム用)")]
    [Min(0)] public float _health;
    [Min(0.0f)] public float _stamina;
    [Min(0.0f)] public float _sanity;
    [Min(0.0f)] public float _dopamine;
    private float _maxHealth;
    private float _maxStamina;
    private float _maxSanity;
    private float _maxDopamine;

    [Header("インベントリ (所持アイテム)")]
    public ItemDataAsset[] Inventory = new ItemDataAsset[3];

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
            _maxStamina = PlayerData.MaxStamina;
            _stamina = PlayerData.MaxStamina;
            _maxSanity = PlayerData.MaxSanity;
            _sanity = PlayerData.MaxSanity;
            _maxDopamine = PlayerData.MaxDopamine;
            _dopamine = PlayerData.MaxDopamine;

            if (PlayerData.Inventory != null && PlayerData.Inventory.Length >= 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    Inventory[i] = PlayerData.Inventory[i];
                }
            }
        }
    }

    // インベントリの状態を PlayerDataAsset（ScriptableObject）へ保存・同期する
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
}