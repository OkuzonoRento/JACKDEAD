using UnityEngine;

[CreateAssetMenu(fileName = "NewMapChipData", menuName = "JackDead/ObjectData/MapChipData")]
public class MapChipData : ScriptableObject
{
    public string chipName;
    public GameObject chipPrefab;

    [Header("基本の扉の有無（回転0度時点）")]
    public bool hasTopDoor = true;
    public bool hasBottomDoor = true;
    public bool hasLeftDoor = true;
    public bool hasRightDoor = true;

    [Header("設定")]
    public bool allowRotation = true; // 回転を許可するかどうか
}