using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MapGizmoDrawer))]
public class MapGenerator : MonoBehaviour
{
    [System.Serializable]
    public class SlotSpawnCandidate
    {
        [Tooltip("識別用の名前")]
        public string variantName = "Normal Slot";

        [Tooltip("スポーンさせるスロットマシンのプレハブ")]
        public GameObject slotPrefab;

        [Tooltip("出現確率（数値が大きいほど選ばれやすい）")]
        [Min(0f)]
        public float spawnWeight = 70f;
    }

    public List<MapChipData> allMapChips;

    [Header("生成設定")]
    [SerializeField] private int targetRoomCount = 9;
    [SerializeField] private float tileSize = 20f;
    [Tooltip("マップチップ生成時のX軸ベース回転角度（Blender出力モデル用）")]
    [SerializeField] private float roomChipBasePitch = -90f;

    [Header("生成リトライ設定")]
    [Tooltip("指定した数に届かなかった場合に再挑戦する最大回数")]
    [SerializeField] private int maxRetryCount = 50;

    [Header("バス・プレイヤースポーン設定")]
    [SerializeField] private GameObject busPrefab;
    [SerializeField] private float busOffsetDistance = 10f;
    [Tooltip("バスのY軸の向きを微調整するオフセット角度（度）")]
    [SerializeField] private float busYRotationOffset = 0f;
    [Tooltip("バス候補部屋として許可する最小隣接部屋数（突き出し部屋除外用）")]
    [SerializeField, Range(1, 4)] private int minNeighborsForBusRoom = 2;
    [Tooltip("バス位置からプレイヤーをスポーンさせる際の高さオフセット")]
    [SerializeField] private Vector3 playerSpawnOffsetFromBus = new Vector3(0f, 1f, 0f);

    [Header("経済・ノルマ設定")]
    [SerializeField] private int currentMapQuota = 1000;
    [SerializeField] private float quotaMultiplier = 2.5f;

    [Header("スクラップ自動配置設定")]
    [SerializeField] private List<ItemDataAsset> scrapDataList;
    [Tooltip("スクラップ生成の最大試行ループ回数")]
    [SerializeField, Min(1)] private int maxScrapSpawnIterations = 300;
    [Tooltip("部屋のタイルサイズに対するスクラップ配置範囲の比率（0.35なら中心から±35%の範囲）")]
    [SerializeField, Range(0.05f, 0.48f)] private float scrapSpawnAreaRatio = 0.35f;
    [Tooltip("スクラップ接地レイキャストを開始する上空高さ")]
    [SerializeField] private float scrapRaycastStartHeight = 5.0f;
    [Tooltip("スクラップ接地レイキャストの最大距離")]
    [SerializeField] private float scrapRaycastDistance = 10.0f;
    [Tooltip("床面が見つからない場合のスクラップ初期高さ")]
    [SerializeField] private float scrapFallbackSpawnHeight = 0.5f;
    [Tooltip("床面ヒット位置からスクラップを少し浮かせるオフセット高さ")]
    [SerializeField] private float scrapSurfaceOffsetHeight = 0.1f;
    [Tooltip("プレハブ未設定時のフォールバックCubeのサイズ")]
    [SerializeField] private float fallbackCubeScale = 0.4f;
    [Tooltip("アイテム価格が0以下の場合に加算するデフォルト価値")]
    [SerializeField, Min(1)] private int defaultFallbackScrapValue = 50;

    [Header("スロットマシン設置条件設定")]
    [Tooltip("配置するスロットマシンのプレハブと出現重みのリスト")]
    [SerializeField] private List<SlotSpawnCandidate> slotCandidates = new List<SlotSpawnCandidate>();
    [Tooltip("マップ全体に設置するスロットの最小台数")]
    [SerializeField, Min(0)] private int minSlotCountPerMap = 2;
    [Tooltip("マップ全体に設置するスロットの最大台数")]
    [SerializeField, Min(0)] private int maxSlotCountPerMap = 4;
    [Tooltip("1部屋あたりに設置できる最大台数")]
    [SerializeField, Range(1, 4)] private int maxSlotsPerRoom = 1;
    [Tooltip("候補の部屋にスロットが配置される確率（0〜1）")]
    [SerializeField, Range(0f, 1f)] private float roomSlotSpawnChance = 0.75f;
    [Tooltip("スタート部屋（Grid 0,0）には設置しない")]
    [SerializeField] private bool excludeStartRoomForSlot = true;
    [Tooltip("バスが接続された部屋には設置しない")]
    [SerializeField] private bool excludeBusRoomForSlot = true;
    [Tooltip("バス接続部屋からの最小グリッド距離（マンハッタン距離）")]
    [SerializeField, Min(0)] private int minManhattanDistanceFromBus = 1;
    [Tooltip("行き止まりの部屋（扉が1つだけの部屋）を優先して設置する")]
    [SerializeField] private bool prioritizeDeadEndRooms = true;

    [Header("スロット配置位置・向き・接地調整")]
    [Tooltip("部屋のタイルサイズに対する壁際への配置距離比率（例: 0.38 なら 20m × 0.38 = 中心から7.6m壁寄り）")]
    [SerializeField, Range(0.1f, 0.48f)] private float slotWallDistanceRatio = 0.38f;
    [Tooltip("部屋のタイルサイズに対する壁横方向のランダム配置幅比率（0で壁の中央固定）")]
    [SerializeField, Range(0f, 0.35f)] private float slotWallLateralSpreadRatio = 0.1f;
    [Tooltip("スロットの正面を部屋の内側に向けるためのY軸回転オフセット角度（度）")]
    [SerializeField] private float slotRotationYawOffset = 0f;
    [Tooltip("スロット接地レイキャストを開始する上空高さ")]
    [SerializeField] private float slotRaycastStartHeight = 5.0f;
    [Tooltip("スロット接地レイキャストの最大距離")]
    [SerializeField] private float slotRaycastDistance = 10.0f;
    [Tooltip("床面ヒット位置からスロットを浮かせる微調整高さ")]
    [SerializeField] private float slotFloorHeightOffset = 0.0f;

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

    private const int RightAngleDegrees = 90;
    private const int CardinalDirectionCount = 4;
    private const float FullCircleDegrees = 360f;
    private static readonly int[] AllowedRotationAngles = { 0, 90, 180, 270 };
    private static readonly int[] NonRotatedAngles = { 0 };
    private static readonly float[] WallInwardYawAngles = { 180f, 0f, 90f, 270f };

    private Dictionary<Vector2Int, GeneratedRoom> generatedRooms = new Dictionary<Vector2Int, GeneratedRoom>();
    private List<GeneratedRoom> spawnedRoomList = new List<GeneratedRoom>();
    private List<GameObject> spawnedSlotList = new List<GameObject>();
    private MapGizmoDrawer gizmoDrawer;

    private Vector2Int? spawnedBusGridPos = null;
    private int spawnedBusDirection = -1;

    public List<GeneratedRoom> GetSpawnedRooms() => spawnedRoomList;
    public List<GameObject> GetSpawnedSlots() => spawnedSlotList;
    public Vector2Int? GetBusGridPos() => spawnedBusGridPos;
    public int GetBusDirection() => spawnedBusDirection;
    public int CurrentMapQuota => currentMapQuota;

    private void Awake()
    {
        gizmoDrawer = GetComponent<MapGizmoDrawer>();
    }

    private void Start()
    {
        GenerateMap();
    }

    public void GenerateMap()
    {
        int retry = 0;
        bool success = false;

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

    private bool TryGenerateMapOnce()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
        generatedRooms.Clear();
        spawnedRoomList.Clear();
        spawnedSlotList.Clear();
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
                Vector3 worldPos = new Vector3(currentGrid.x * tileSize, 0f, currentGrid.y * tileSize);
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

                Quaternion spawnRotation = Quaternion.Euler(roomChipBasePitch, result.angle, 0f);
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

        if (spawnedRoomList.Count >= targetRoomCount)
        {
            SpawnBusAtOuterDoor();
            SpawnSlotMachines();
            SpawnRandomScraps();
            UpdateGizmoDrawer();
            Debug.Log($"[MAP] マップ生成成功！ (生成数: {spawnedRoomList.Count}個)");
            return true;
        }

        return false;
    }

    private void SpawnSlotMachines()
    {
        if (slotCandidates == null || slotCandidates.Count == 0) return;

        int maxCount = Mathf.Max(minSlotCountPerMap, maxSlotCountPerMap);
        int targetSlotCount = Random.Range(minSlotCountPerMap, maxCount + 1);
        if (targetSlotCount <= 0) return;

        List<GeneratedRoom> normalEligibleRooms = new List<GeneratedRoom>();
        List<GeneratedRoom> deadEndEligibleRooms = new List<GeneratedRoom>();

        foreach (var room in spawnedRoomList)
        {
            if (!IsRoomEligibleForSlot(room, out int doorCount)) continue;

            if (doorCount == 1)
            {
                deadEndEligibleRooms.Add(room);
            }
            else
            {
                normalEligibleRooms.Add(room);
            }
        }

        ShuffleList(deadEndEligibleRooms);
        ShuffleList(normalEligibleRooms);

        List<GeneratedRoom> orderedCandidateRooms = new List<GeneratedRoom>();
        if (prioritizeDeadEndRooms)
        {
            orderedCandidateRooms.AddRange(deadEndEligibleRooms);
            orderedCandidateRooms.AddRange(normalEligibleRooms);
        }
        else
        {
            orderedCandidateRooms.AddRange(deadEndEligibleRooms);
            orderedCandidateRooms.AddRange(normalEligibleRooms);
            ShuffleList(orderedCandidateRooms);
        }

        int spawnedCount = 0;

        foreach (var room in orderedCandidateRooms)
        {
            if (spawnedCount >= targetSlotCount) break;
            if (Random.value > roomSlotSpawnChance) continue;

            // 扉が存在しない壁面（0:上壁, 1:下壁, 2:左壁, 3:右壁）を抽出
            List<int> availableWalls = GetAvailableWallsInRoom(room);
            ShuffleList(availableWalls);

            int slotsInThisRoom = 0;
            foreach (int wallDir in availableWalls)
            {
                if (spawnedCount >= targetSlotCount || slotsInThisRoom >= maxSlotsPerRoom) break;

                SlotSpawnCandidate candidate = ChooseRandomSlotCandidate();
                if (candidate == null || candidate.slotPrefab == null) continue;

                CalculateWallSlotTransform(room, wallDir, candidate.slotPrefab.transform.localRotation, out Vector3 spawnPos, out Quaternion spawnRot);

                GameObject slotInstance = Instantiate(candidate.slotPrefab, spawnPos, spawnRot, transform);
                spawnedSlotList.Add(slotInstance);

                spawnedCount++;
                slotsInThisRoom++;
            }
        }

        Debug.Log($"[MAP] スロットマシン配置完了: {spawnedCount} / {targetSlotCount} 台");
    }

    private bool IsRoomEligibleForSlot(GeneratedRoom room, out int doorCount)
    {
        doorCount = (room.topDoor ? 1 : 0)
                  + (room.bottomDoor ? 1 : 0)
                  + (room.leftDoor ? 1 : 0)
                  + (room.rightDoor ? 1 : 0);

        // 四方すべてが扉の十字路は壁面がないため除外
        if (doorCount >= CardinalDirectionCount) return false;

        if (excludeStartRoomForSlot && room.gridPos == Vector2Int.zero) return false;

        if (spawnedBusGridPos.HasValue)
        {
            if (excludeBusRoomForSlot && room.gridPos == spawnedBusGridPos.Value) return false;

            int manhattanDist = Mathf.Abs(room.gridPos.x - spawnedBusGridPos.Value.x)
                              + Mathf.Abs(room.gridPos.y - spawnedBusGridPos.Value.y);
            if (manhattanDist < minManhattanDistanceFromBus) return false;
        }

        return true;
    }

    private List<int> GetAvailableWallsInRoom(GeneratedRoom room)
    {
        List<int> walls = new List<int>(CardinalDirectionCount);
        if (!room.topDoor) walls.Add(0);
        if (!room.bottomDoor) walls.Add(1);
        if (!room.leftDoor) walls.Add(2);
        if (!room.rightDoor) walls.Add(3);
        return walls;
    }

    private void CalculateWallSlotTransform(GeneratedRoom room, int wallDir, Quaternion prefabBaseRot, out Vector3 finalPos, out Quaternion finalRot)
    {
        Vector3 wallDirVec = Vector3.zero;
        Vector3 lateralDirVec = Vector3.zero;

        switch (wallDir)
        {
            case 0: // 上壁（北壁）➔ 南（部屋内側）を向く
                wallDirVec = Vector3.forward;
                lateralDirVec = Vector3.right;
                break;
            case 1: // 下壁（南壁）➔ 北（部屋内側）を向く
                wallDirVec = Vector3.back;
                lateralDirVec = Vector3.right;
                break;
            case 2: // 左壁（西壁）➔ 東（部屋内側）を向く
                wallDirVec = Vector3.left;
                lateralDirVec = Vector3.forward;
                break;
            case 3: // 右壁（東壁）➔ 西（部屋内側）を向く
                wallDirVec = Vector3.right;
                lateralDirVec = Vector3.forward;
                break;
        }

        float wallDistance = tileSize * slotWallDistanceRatio;
        float maxLateralSpread = tileSize * slotWallLateralSpreadRatio;
        float lateralOffset = Random.Range(-maxLateralSpread, maxLateralSpread);

        Vector3 horizontalPos = room.worldPos + (wallDirVec * wallDistance) + (lateralDirVec * lateralOffset);

        Vector3 rayOrigin = horizontalPos + Vector3.up * slotRaycastStartHeight;
        finalPos = horizontalPos + Vector3.up * slotFloorHeightOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, slotRaycastDistance))
        {
            finalPos = hit.point + Vector3.up * slotFloorHeightOffset;
        }

        float inwardYaw = WallInwardYawAngles[Mathf.Clamp(wallDir, 0, WallInwardYawAngles.Length - 1)];
        Quaternion worldYawRotation = Quaternion.Euler(0f, inwardYaw + slotRotationYawOffset, 0f);

        // プレハブ自体が持つ初期回転（Z=90など）を維持したまま部屋の真ん中へ向ける
        finalRot = worldYawRotation * prefabBaseRot;
    }

    private SlotSpawnCandidate ChooseRandomSlotCandidate()
    {
        float totalWeight = 0f;
        for (int i = 0; i < slotCandidates.Count; i++)
        {
            if (slotCandidates[i].slotPrefab != null)
            {
                totalWeight += Mathf.Max(0f, slotCandidates[i].spawnWeight);
            }
        }

        if (totalWeight <= 0f)
        {
            return slotCandidates[Random.Range(0, slotCandidates.Count)];
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < slotCandidates.Count; i++)
        {
            if (slotCandidates[i].slotPrefab == null) continue;

            cumulative += Mathf.Max(0f, slotCandidates[i].spawnWeight);
            if (roll <= cumulative)
            {
                return slotCandidates[i];
            }
        }

        return slotCandidates[slotCandidates.Count - 1];
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    private void SpawnRandomScraps()
    {
        if (scrapDataList == null || scrapDataList.Count == 0) return;

        float targetTotalValue = currentMapQuota * quotaMultiplier;
        float currentTotalValue = 0f;

        List<GeneratedRoom> availableRooms = spawnedRoomList.FindAll(r =>
            r.gridPos != Vector2Int.zero && (!spawnedBusGridPos.HasValue || r.gridPos != spawnedBusGridPos.Value)
        );

        if (availableRooms.Count == 0)
        {
            availableRooms = spawnedRoomList.FindAll(r => r.gridPos != Vector2Int.zero);
            if (availableRooms.Count == 0) availableRooms = spawnedRoomList;
        }

        int safetyCounter = 0;
        float maxOffset = tileSize * scrapSpawnAreaRatio;

        while (currentTotalValue < targetTotalValue && safetyCounter < maxScrapSpawnIterations)
        {
            safetyCounter++;

            GeneratedRoom targetRoom = availableRooms[Random.Range(0, availableRooms.Count)];

            float offsetX = Random.Range(-maxOffset, maxOffset);
            float offsetZ = Random.Range(-maxOffset, maxOffset);

            Vector3 rayOrigin = targetRoom.worldPos + new Vector3(offsetX, scrapRaycastStartHeight, offsetZ);
            Vector3 finalSpawnPos = targetRoom.worldPos + new Vector3(offsetX, scrapFallbackSpawnHeight, offsetZ);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, scrapRaycastDistance))
            {
                finalSpawnPos = hit.point + Vector3.up * scrapSurfaceOffsetHeight;
            }

            ItemDataAsset randomScrapData = scrapDataList[Random.Range(0, scrapDataList.Count)];

            GameObject prefabToUse = randomScrapData.ItemPrefab;
            GameObject scrapObj;

            if (prefabToUse != null)
            {
                Quaternion randomYaw = Quaternion.Euler(0f, Random.Range(0f, FullCircleDegrees), 0f);
                scrapObj = Instantiate(prefabToUse, finalSpawnPos, randomYaw, transform);
            }
            else
            {
                scrapObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                scrapObj.transform.position = finalSpawnPos;
                scrapObj.transform.localScale = Vector3.one * fallbackCubeScale;
                scrapObj.AddComponent<Rigidbody>();
                scrapObj.AddComponent<BoxCollider>();
                scrapObj.transform.SetParent(transform);
            }

            Item worldItem = scrapObj.GetComponent<Item>();
            if (worldItem == null)
            {
                worldItem = scrapObj.AddComponent<Item>();
            }
            worldItem.SetItemData(randomScrapData);

            int itemVal = randomScrapData.Amount > 0 ? randomScrapData.Amount : defaultFallbackScrapValue;
            currentTotalValue += itemVal;
        }

        Debug.Log($"[MAP] ノルマ連動スクラップ配置完了（バス周辺除外）！ 目標価値: {targetTotalValue}, 生成総価値: {currentTotalValue}");
    }

    private void SpawnPlayerAtPosition(Vector3 targetPosition)
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            Debug.LogWarning("[MAP] シーン内に PlayerController が見つかりませんでした！");
            return;
        }

        CharacterController charController = player.GetComponent<CharacterController>();
        if (charController != null)
        {
            charController.enabled = false;
            player.transform.position = targetPosition;
            charController.enabled = true;
        }
        else
        {
            player.transform.position = targetPosition;
        }

        Debug.Log($"[MAP] プレイヤーをバスの生成位置 ({targetPosition}) にスポーンさせました。");
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

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var room in spawnedRoomList)
        {
            if (room.gridPos.x < minX) minX = room.gridPos.x;
            if (room.gridPos.x > maxX) maxX = room.gridPos.x;
            if (room.gridPos.y < minY) minY = room.gridPos.y;
            if (room.gridPos.y > maxY) maxY = room.gridPos.y;
        }

        List<(GeneratedRoom room, Vector3 pos, Quaternion rot, int dir)> busCandidates = new List<(GeneratedRoom, Vector3, Quaternion, int)>();

        foreach (var room in spawnedRoomList)
        {
            if (room.gridPos == Vector2Int.zero) continue;

            bool isOuterEdge = (room.gridPos.x == minX || room.gridPos.x == maxX ||
                                room.gridPos.y == minY || room.gridPos.y == maxY);
            if (!isOuterEdge) continue;

            int neighborCount = 0;
            if (generatedRooms.ContainsKey(room.gridPos + Vector2Int.up)) neighborCount++;
            if (generatedRooms.ContainsKey(room.gridPos + Vector2Int.down)) neighborCount++;
            if (generatedRooms.ContainsKey(room.gridPos + Vector2Int.left)) neighborCount++;
            if (generatedRooms.ContainsKey(room.gridPos + Vector2Int.right)) neighborCount++;

            if (neighborCount < minNeighborsForBusRoom) continue;

            AddOuterDoorBusCandidates(room, busCandidates);
        }

        if (busCandidates.Count == 0)
        {
            foreach (var room in spawnedRoomList)
            {
                if (room.gridPos == Vector2Int.zero) continue;

                bool isOuterEdge = (room.gridPos.x == minX || room.gridPos.x == maxX ||
                                    room.gridPos.y == minY || room.gridPos.y == maxY);
                if (!isOuterEdge) continue;

                AddOuterDoorBusCandidates(room, busCandidates);
            }
        }

        if (busCandidates.Count > 0)
        {
            (GeneratedRoom room, Vector3 pos, Quaternion rot, int dir) bestCandidate = busCandidates[0];
            float maxDist = -1f;

            foreach (var candidate in busCandidates)
            {
                float dist = Vector3.Distance(Vector3.zero, candidate.room.worldPos);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    bestCandidate = candidate;
                }
            }

            Instantiate(busPrefab, bestCandidate.pos, bestCandidate.rot, transform);

            spawnedBusGridPos = bestCandidate.room.gridPos;
            spawnedBusDirection = bestCandidate.dir;

            SpawnPlayerAtPosition(bestCandidate.pos + playerSpawnOffsetFromBus);
            Debug.Log($"[バス生成] 最外周の安定した座標: {bestCandidate.room.gridPos} にバスを配置しました。");
        }
        else
        {
            Debug.LogWarning("[バス生成] 条件を満たすバスの配置位置が見つかりませんでした。");
        }
    }

    private void AddOuterDoorBusCandidates(GeneratedRoom room, List<(GeneratedRoom room, Vector3 pos, Quaternion rot, int dir)> busCandidates)
    {
        bool isTopEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.up);
        bool isBottomEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.down);
        bool isLeftEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.left);
        bool isRightEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.right);

        if (isTopEdge && room.topDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, WallInwardYawAngles[1] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.forward * busOffsetDistance, rot, 0));
        }
        if (isBottomEdge && room.bottomDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, WallInwardYawAngles[0] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.back * busOffsetDistance, rot, 1));
        }
        if (isLeftEdge && room.leftDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, WallInwardYawAngles[3] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.left * busOffsetDistance, rot, 2));
        }
        if (isRightEdge && room.rightDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, WallInwardYawAngles[2] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.right * busOffsetDistance, rot, 3));
        }
    }

    private List<(MapChipData chip, int angle)> FindAllFittingChips(
        bool needTop, bool needBottom, bool needLeft, bool needRight,
        bool reqTop, bool reqBottom, bool reqLeft, bool reqRight)
    {
        List<(MapChipData chip, int angle)> candidates = new List<(MapChipData, int)>();

        foreach (var chip in allMapChips)
        {
            int[] angles = chip.allowRotation ? AllowedRotationAngles : NonRotatedAngles;

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

    private (bool top, bool bottom, bool left, bool right) GetRotatedungeonsDoors(MapChipData chip, int angle)
    {
        bool t = chip.hasTopDoor;
        bool b = chip.hasBottomDoor;
        bool l = chip.hasLeftDoor;
        bool r = chip.hasRightDoor;

        int rotations = (angle / RightAngleDegrees) % CardinalDirectionCount;
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

    private (bool top, bool bottom, bool left, bool right) GetRotatedDoors(MapChipData chip, int angle)
    {
        return GetRotatedungeonsDoors(chip, angle);
    }
}