using UnityEngine;

[CreateAssetMenu(fileName = "PlayerData", menuName = "JackDead/CharacterData/PlayerData")]
public class PlayerDataAsset : ScriptableObject
{
    [Min(0)] public int MaxHealth = 100;
    [Min(0.0f)] public float MaxStamina = 100.0f;
    [Min(0.0f)] public float MaxSanity = 100.0f;
    [Min(0.0f)] public float MaxDopamine = 100.0f;
    [Min(0.0f)] public float MoveSpeed = 5.0f;
    [Min(0.0f)] public float JumpPower = 5.0f;
    public ItemDataAsset[] Inventory = new ItemDataAsset[3];

    [Header("所持金・進行データ設定")]
    [Min(0), Tooltip("プレイヤーの現在の所持金")]
    public int PlayerMoney = 0;

    [Min(1), Tooltip("現在のステージレベル")]
    public int CurrentStageLevel = 1;

    [Min(0), Tooltip("これまでの累計借金返済額")]
    public int TotalRepaidDebt = 0;
}