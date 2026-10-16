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
}
