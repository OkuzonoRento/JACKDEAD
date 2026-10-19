using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MapGizmoDrawer))]
public class MapGenerator : MonoBehaviour
{
    public List<MapChipData> allMapChips;

    [Header("生成設定")]
    public int targetRoomCount = 9;
    public float tileSize = 20f;
    [Header("生成リトライ設定")]
    [Tooltip("指定した数に届かなかった場合に再挑戦する最大回数")]
    public int maxRetryCount = 50;

    [Header("バス設定")]
    public GameObject busPrefab;
    public float busOffsetDistance = 10f;

    public class GeneratedRoom
    {
        public Vector2Int gridPos;
        public Vector3 worldPos;
        public MapChipData chipData;
        public int rotationAngle;
        public bool topDoor;
        public bool bottomDoor;
        public bool leftDoor;
        public bool rightDoor;
    }

    private Dictionary<Vector2Int, GeneratedRoom> generatedRooms = new Dictionary<Vector2Int, GeneratedRoom>();
    private List<GeneratedRoom> spawnedRoomList = new List<GeneratedRoom>();
    private MapGizmoDrawer gizmoDrawer;

    private Vector2Int? spawnedBusGridPos = null;
    private int spawnedBusDirection = -1;

    public List<GeneratedRoom> GetSpawnedRooms() => spawnedRoomList;
    public Vector2Int? GetBusGridPos() => spawnedBusGridPos;
    public int GetBusDirection() => spawnedBusDirection;

    void Awake()
    {
        gizmoDrawer = GetComponent<MapGizmoDrawer>();
    }

    void Start()
    {
        GenerateMap();
    }

    public void GenerateMap()
    {
        int retry = 0;
        bool success = false;

        // 目標数に到達するまで、または最大リトライ回数に達するまでループ
        while (retry < maxRetryCount && !success)
        {
            retry++;
            if (TryGenerateMapOnce())
            {
                success = true;
            }
        }

        if (!success)
        {
            Debug.LogWarning($"[MAP] 警告: 最大リトライ回数({maxRetryCount})以内に目標数({targetRoomCount})の生成ができませんでした。（現在の生成数: {spawnedRoomList.Count}）");
        }
    }

    // 1回分の生成トライアル
    private bool TryGenerateMapOnce()
    {
        // 既存の生成物をすべてクリア
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
        generatedRooms.Clear();
        spawnedRoomList.Clear();
        spawnedBusGridPos = null;
        spawnedBusDirection = -1;
        if (gizmoDrawer != null) gizmoDrawer.Clear();

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        Vector2Int startPos = Vector2Int.zero;
        stack.Push(startPos);
        visited.Add(startPos);

        while (stack.Count > 0 && spawnedRoomList.Count < targetRoomCount)
        {
            Vector2Int currentGrid = stack.Peek();

            bool reqTop = generatedRooms.ContainsKey(currentGrid + Vector2Int.up);
            bool reqBottom = generatedRooms.ContainsKey(currentGrid + Vector2Int.down);
            bool reqLeft = generatedRooms.ContainsKey(currentGrid + Vector2Int.left);
            bool reqRight = generatedRooms.ContainsKey(currentGrid + Vector2Int.right);

            bool needTop = reqTop && generatedRooms[currentGrid + Vector2Int.up].bottomDoor;
            bool needBottom = reqBottom && generatedRooms[currentGrid + Vector2Int.down].topDoor;
            bool needLeft = reqLeft && generatedRooms[currentGrid + Vector2Int.left].rightDoor;
            bool needRight = reqRight && generatedRooms[currentGrid + Vector2Int.right].leftDoor;

            if ((reqTop && !needTop) || (reqBottom && !needBottom) || (reqLeft && !needLeft) || (reqRight && !needRight))
            {
                stack.Pop();
                continue;
            }

            var fittingChips = FindAllFittingChips(needTop, needBottom, needLeft, needRight, reqTop, reqBottom, reqLeft, reqRight);

            if (fittingChips.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var result = fittingChips[Random.Range(0, fittingChips.Count)];
            var doors = GetRotatedDoors(result.chip, result.angle);

            if (!generatedRooms.ContainsKey(currentGrid))
            {
                Vector3 worldPos = new Vector3(currentGrid.x * tileSize, 0, currentGrid.y * tileSize);
                GeneratedRoom newRoom = new GeneratedRoom
                {
                    gridPos = currentGrid,
                    worldPos = worldPos,
                    chipData = result.chip,
                    rotationAngle = result.angle,
                    topDoor = doors.top,
                    bottomDoor = doors.bottom,
                    leftDoor = doors.left,
                    rightDoor = doors.right
                };

                generatedRooms[currentGrid] = newRoom;
                spawnedRoomList.Add(newRoom);

                Quaternion spawnRotation = Quaternion.Euler(-90f, result.angle, 0f);
                Instantiate(result.chip.chipPrefab, worldPos, spawnRotation, transform);

                if (spawnedRoomList.Count >= targetRoomCount) break;
            }

            List<Vector2Int> nextCandidates = new List<Vector2Int>();

            if (doors.top && !visited.Contains(currentGrid + Vector2Int.up)) nextCandidates.Add(currentGrid + Vector2Int.up);
            if (doors.bottom && !visited.Contains(currentGrid + Vector2Int.down)) nextCandidates.Add(currentGrid + Vector2Int.down);
            if (doors.left && !visited.Contains(currentGrid + Vector2Int.left)) nextCandidates.Add(currentGrid + Vector2Int.left);
            if (doors.right && !visited.Contains(currentGrid + Vector2Int.right)) nextCandidates.Add(currentGrid + Vector2Int.right);

            if (nextCandidates.Count > 0)
            {
                Vector2Int nextGrid = nextCandidates[Random.Range(0, nextCandidates.Count)];
                visited.Add(nextGrid);
                stack.Push(nextGrid);
            }
            else
            {
                stack.Pop();
            }
        }

        // 目標数に到達できたか判定
        if (spawnedRoomList.Count >= targetRoomCount)
        {
            SpawnBusAtOuterDoor();
            UpdateGizmoDrawer();
            Debug.Log($"[MAP] マップ生成成功！ (生成数: {spawnedRoomList.Count}個)");
            return true;
        }

        return false; // 失敗したので再試行する
    }

    private void UpdateGizmoDrawer()
    {
        if (gizmoDrawer == null) return;

        List<MapGizmoDrawer.RoomGizmoInfo> gizmoInfos = new List<MapGizmoDrawer.RoomGizmoInfo>();
        foreach (var room in spawnedRoomList)
        {
            gizmoInfos.Add(new MapGizmoDrawer.RoomGizmoInfo
            {
                position = room.worldPos,
                chipData = room.chipData,
                rotationAngle = room.rotationAngle,
                topDoor = room.topDoor,
                bottomDoor = room.bottomDoor,
                leftDoor = room.leftDoor,
                rightDoor = room.rightDoor
            });
        }
        gizmoDrawer.Setup(gizmoInfos, tileSize);
    }

    private void SpawnBusAtOuterDoor()
    {
        if (busPrefab == null) return;

        List<(GeneratedRoom room, Vector3 pos, Quaternion rot, int dir)> busCandidates = new List<(GeneratedRoom, Vector3, Quaternion, int)>();

        foreach (var room in spawnedRoomList)
        {
            bool isTopEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.up);
            bool isBottomEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.down);
            bool isLeftEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.left);
            bool isRightEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.right);

            if (isTopEdge && room.topDoor)
            {
                busCandidates.Add((room, room.worldPos + Vector3.forward * busOffsetDistance, Quaternion.Euler(0, 0, 0), 0));
            }
            if (isBottomEdge && room.bottomDoor)
            {
                busCandidates.Add((room, room.worldPos + Vector3.back * busOffsetDistance, Quaternion.Euler(0, 180, 0), 1));
            }
            if (isLeftEdge && room.leftDoor)
            {
                busCandidates.Add((room, room.worldPos + Vector3.left * busOffsetDistance, Quaternion.Euler(0, 270, 0), 2));
            }
            if (isRightEdge && room.rightDoor)
            {
                busCandidates.Add((room, room.worldPos + Vector3.right * busOffsetDistance, Quaternion.Euler(0, 90, 0), 3));
            }
        }

        if (busCandidates.Count > 0)
        {
            var selected = busCandidates[Random.Range(0, busCandidates.Count)];
            Instantiate(busPrefab, selected.pos, selected.rot, transform);

            spawnedBusGridPos = selected.room.gridPos;
            spawnedBusDirection = selected.dir;
        }
    }

    private List<(MapChipData chip, int angle)> FindAllFittingChips(
        bool needTop, bool needBottom, bool needLeft, bool needRight,
        bool reqTop, bool reqBottom, bool reqLeft, bool reqRight)
    {
        List<(MapChipData chip, int angle)> candidates = new List<(MapChipData, int)>();

        foreach (var chip in allMapChips)
        {
            int[] angles = chip.allowRotation ? new int[] { 0, 90, 180, 270 } : new int[] { 0 };

            foreach (int angle in angles)
            {
                var doors = GetRotatedDoors(chip, angle);

                if (reqTop && doors.top != needTop) continue;
                if (reqBottom && doors.bottom != needBottom) continue;
                if (reqLeft && doors.left != needLeft) continue;
                if (reqRight && doors.right != needRight) continue;

                candidates.Add((chip, angle));
            }
        }

        return candidates;
    }

    private (bool top, bool bottom, bool left, bool right) GetRotatedDoors(MapChipData chip, int angle)
    {
        bool t = chip.hasTopDoor;
        bool b = chip.hasBottomDoor;
        bool l = chip.hasLeftDoor;
        bool r = chip.hasRightDoor;

        int rotations = (angle / 90) % 4;
        for (int i = 0; i < rotations; i++)
        {
            bool temp = t;
            t = l;
            l = b;
            b = r;
            r = temp;
        }

        return (t, b, l, r);
    }
}