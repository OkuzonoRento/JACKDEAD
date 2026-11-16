using System.Collections.Generic;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(MapGizmoDrawer))]
public class MapGenerator : MonoBehaviour
{
    [System.Serializable]
    public class SlotSpawnCandidate
    {
        [Tooltip("識別用の名前（例: 通常台1.5x、レア台2.0x）")]
        public string variantName = "Normal Slot";

        [Tooltip("スポーンさせるスロットマシンのプレハブ")]
        public GameObject slotPrefab;

        [Tooltip("出現確率の重み（数値が大きいほど出やすい）")]
        [Min(0f)]
        public float spawnWeight = 70f;
    }

    public List<MapChipData> allMapChips;

    [Header("生成設定")]
    [SerializeField] private int targetRoomCount = 9;
    [SerializeField] private float tileSize = 20f;
    [SerializeField] private int maxRetryCount = 50;

    [Header("バス設定")]
    [SerializeField] private GameObject busPrefab;
    [SerializeField] private float busOffsetDistance = 20f;
    [SerializeField] private float busYRotationOffset = -90f;

    [Header("経済・ノルマ・レベル進行設定")]
    [SerializeField, Min(1)] private int currentStageLevel = 1;
    [SerializeField] private int currentMapQuota = 1000;
    [SerializeField] private float quotaMultiplier = 2.5f;
    [Tooltip("1レベルクリアごとにノルマ金額へ乗算される増加倍率")]
    [SerializeField, Min(1.0f)] private float quotaGrowthRate = 1.5f;


    [Header("スロット自動配置設定")]
    [SerializeField] private List<SlotSpawnCandidate> slotCandidates = new List<SlotSpawnCandidate>();
    [SerializeField, Min(0)] private int minSlotCount = 2;
    [SerializeField, Min(0)] private int maxSlotCount = 4;
    [Tooltip("壁や障害物からスロットを離す最小距離（メートル）")]
    [SerializeField, Range(0.8f, 4.0f)] private float slotWallClearance = 1.6f;
    [Tooltip("スロットの向きの微調整オフセット角度（基本は 0 のままで自動で壁を背にします）")]
    [SerializeField] private float slotRotationYawOffset = 0f;

    [Header("スクラップ自動配置設定")]
    [SerializeField] private List<ItemDataAsset> scrapDataList;

    // --- 内部固定パラメータ（static不使用） ---
    private const float RoomChipBasePitch = -90f;
    private const int RightAngleDegrees = 90;
    private const int CardinalDirectionCount = 4;
    private const float FullCircleDegrees = 360f;

    private const int MinNeighborsForBusRoom = 2;
    private const float PlayerSpawnHeightOffset = 1.0f;

    private const int MaxSlotsPerRoom = 1;
    private const float RoomSlotSpawnChance = 0.9f;
    private const int MinManhattanDistFromBusForSlot = 1;
    private const int MaxSlotPlacementAttemptsPerRoom = 60;
    private const float SlotSampleAreaRatio = 0.38f;
    private const float SlotFootprintRadius = 0.9f;
    private const float SlotFrontStandDist = 1.35f;
    private const float SlotFrontWalkwayDist = 2.3f;
    private const float SlotFrontSideWidth = 0.6f;
    private const float SlotMinClearanceBetweenSlots = 3.0f;
    private const float SlotMinClearanceForScrap = 2.0f;
    private const float WallSearchStepSize = 0.25f;

    private const int HorizontalClearanceRayCount = 12;
    private const float ClearanceCheckHeightLow = 0.5f;
    private const float ClearanceCheckHeightHigh = 1.4f;
    private const float InsideObstacleSphereRadius = 0.35f;

    private const int MaxScrapSpawnIterations = 450;
    private const float ScrapSpawnAreaRatio = 0.36f;
    private const float ScrapFootprintRadius = 0.35f;
    private const float ScrapSurfaceOffsetHeight = 0.02f;
    private const float FallbackCubeScale = 0.4f;
    private const int DefaultFallbackScrapValue = 50;

    private const int RoomFloorScanGridSteps = 7;
    private const float FloorRaycastStartHeight = 12.0f;
    private const float FloorRaycastMaxDistance = 24.0f;
    private const float FloorNormalThreshold = 0.75f;
    private const float MaxWalkableFloorHeightAboveRoom = 1.0f;
    private const float FloorHeightMatchTolerance = 0.08f;
    private const float MinFloorColorBrightness = 0.08f;
    private const int QuotaRoundingUnit = 100;

    public int CurrentStageLevel => currentStageLevel;

    private readonly int[] allowedRotationAngles = { 0, 90, 180, 270 };
    private readonly int[] nonRotatedAngles = { 0 };
    private readonly float[] wallInwardYawAngles = { 180f, 0f, 90f, 270f };
    private readonly Vector3[] cardinalDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.right,
        Vector3.left
    };

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
        public float walkableFloorY;
        public bool hasBrightWalkableFloor;
    }

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

    /// <summary>
    /// 簡易リザルトの「次へ」ボタンから呼ばれ、レベルとノルマを引き上げて新しいマップを再生成する
    /// </summary>
    public void AdvanceToNextLevel()
    {
        currentStageLevel++;

        float rawNextQuota = currentMapQuota * quotaGrowthRate;
        currentMapQuota = Mathf.RoundToInt(rawNextQuota / QuotaRoundingUnit) * QuotaRoundingUnit;

        Debug.Log($"[MAP] STAGE {currentStageLevel} 開始！ 新ノルマ: ${currentMapQuota}");
        GenerateMap();
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
                    rightDoor = doors.right,
                    walkableFloorY = worldPos.y,
                    hasBrightWalkableFloor = false
                };

                generatedRooms[currentGrid] = newRoom;
                spawnedRoomList.Add(newRoom);

                Quaternion spawnRotation = Quaternion.Euler(RoomChipBasePitch, result.angle, 0f);
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
            Physics.SyncTransforms();

            AnalyzeAllRoomsWalkableFloor();

            // 1. バス配置 ➔ 2. スロット配置 ➔ 3. スクラップ配置
            SpawnBusAtOuterDoor();
            SpawnSlotMachines();
            Physics.SyncTransforms();
            SpawnRandomScraps();

            UpdateGizmoDrawer();
            Debug.Log($"[MAP] マップ生成成功！ (部屋数: {spawnedRoomList.Count}, スロット数: {spawnedSlotList.Count})");
            return true;
        }

        return false;
    }

    private void AnalyzeAllRoomsWalkableFloor()
    {
        float scanExtent = tileSize * 0.42f;

        foreach (var room in spawnedRoomList)
        {
            float highestBrightFloorY = float.MinValue;
            float highestAnyFloorY = float.MinValue;
            bool foundBright = false;
            bool foundAny = false;

            for (int gx = 0; gx < RoomFloorScanGridSteps; gx++)
            {
                float tx = (gx / (float)(RoomFloorScanGridSteps - 1)) * 2f - 1f;
                for (int gz = 0; gz < RoomFloorScanGridSteps; gz++)
                {
                    float tz = (gz / (float)(RoomFloorScanGridSteps - 1)) * 2f - 1f;
                    Vector3 sampleXZ = room.worldPos + new Vector3(tx * scanExtent, 0f, tz * scanExtent);

                    if (!TryGetTopmostEnvironmentHit(sampleXZ, room.worldPos.y, out RaycastHit topHit)) continue;
                    if (topHit.normal.y < FloorNormalThreshold) continue;

                    float relY = topHit.point.y - room.worldPos.y;
                    if (relY < -MaxWalkableFloorHeightAboveRoom || relY > MaxWalkableFloorHeightAboveRoom) continue;

                    if (topHit.point.y > highestAnyFloorY)
                    {
                        highestAnyFloorY = topHit.point.y;
                        foundAny = true;
                    }

                    if (GetSurfaceBrightness(topHit.collider) >= MinFloorColorBrightness)
                    {
                        if (topHit.point.y > highestBrightFloorY)
                        {
                            highestBrightFloorY = topHit.point.y;
                            foundBright = true;
                        }
                    }
                }
            }

            if (foundBright)
            {
                room.walkableFloorY = highestBrightFloorY;
                room.hasBrightWalkableFloor = true;
            }
            else if (foundAny)
            {
                room.walkableFloorY = highestAnyFloorY;
                room.hasBrightWalkableFloor = false;
            }
            else
            {
                room.walkableFloorY = room.worldPos.y;
                room.hasBrightWalkableFloor = false;
            }
        }
    }

    private void SpawnSlotMachines()
    {
        if (slotCandidates == null || slotCandidates.Count == 0) return;

        int targetSlotCount = Random.Range(minSlotCount, Mathf.Max(minSlotCount, maxSlotCount) + 1);
        if (targetSlotCount <= 0) return;

        List<GeneratedRoom> deadEndRooms = new List<GeneratedRoom>();
        List<GeneratedRoom> normalRooms = new List<GeneratedRoom>();

        foreach (var room in spawnedRoomList)
        {
            if (!IsRoomEligibleForSlot(room, out int doorCount)) continue;

            if (doorCount == 1) deadEndRooms.Add(room);
            else normalRooms.Add(room);
        }

        ShuffleList(deadEndRooms);
        ShuffleList(normalRooms);

        List<GeneratedRoom> candidateRooms = new List<GeneratedRoom>(deadEndRooms.Count + normalRooms.Count);
        candidateRooms.AddRange(deadEndRooms);
        candidateRooms.AddRange(normalRooms);

        int spawnedCount = 0;

        foreach (var room in candidateRooms)
        {
            if (spawnedCount >= targetSlotCount) break;
            if (Random.value > RoomSlotSpawnChance) continue;

            int slotsInThisRoom = 0;
            while (slotsInThisRoom < MaxSlotsPerRoom && spawnedCount < targetSlotCount)
            {
                SlotSpawnCandidate candidate = ChooseRandomSlotCandidate();
                if (candidate == null || candidate.slotPrefab == null) break;

                if (!TryFindOpenFloorSlotPlacement(
                        room,
                        out Vector3 targetBottomCenter,
                        out Vector3 desiredFrontDir))
                {
                    break;
                }

                GameObject slotInstance = Instantiate(
                    candidate.slotPrefab,
                    targetBottomCenter,
                    candidate.slotPrefab.transform.localRotation,
                    transform
                );

                // ★ 1. 最も壁に近い方向を「背面」、その反対（desiredFrontDir）を「正面」に向ける
                AlignSlotFrontToDirection(slotInstance, desiredFrontDir);

                // ★ 2. 回転後のモデル全体の中心(X,Z)と底面(Y)を床座標へピタッと合わせる
                AlignObjectBoundsToFloor(slotInstance, targetBottomCenter, 0f);
                Physics.SyncTransforms();

                spawnedSlotList.Add(slotInstance);
                spawnedCount++;
                slotsInThisRoom++;
            }
        }
    }

    private bool IsRoomEligibleForSlot(GeneratedRoom room, out int doorCount)
    {
        doorCount = (room.topDoor ? 1 : 0)
                  + (room.bottomDoor ? 1 : 0)
                  + (room.leftDoor ? 1 : 0)
                  + (room.rightDoor ? 1 : 0);

        if (room.gridPos == Vector2Int.zero) return false;

        if (spawnedBusGridPos.HasValue)
        {
            if (room.gridPos == spawnedBusGridPos.Value) return false;

            int manhattanDist = Mathf.Abs(room.gridPos.x - spawnedBusGridPos.Value.x)
                              + Mathf.Abs(room.gridPos.y - spawnedBusGridPos.Value.y);
            if (manhattanDist < MinManhattanDistFromBusForSlot) return false;
        }

        return true;
    }

    private bool TryFindOpenFloorSlotPlacement(
        GeneratedRoom room,
        out Vector3 validBottomCenter,
        out Vector3 bestFrontDir)
    {
        validBottomCenter = Vector3.zero;
        bestFrontDir = Vector3.forward;

        float maxSampleOffset = tileSize * SlotSampleAreaRatio;

        for (int attempt = 0; attempt < MaxSlotPlacementAttemptsPerRoom; attempt++)
        {
            float offsetX = Random.Range(-maxSampleOffset, maxSampleOffset);
            float offsetZ = Random.Range(-maxSampleOffset, maxSampleOffset);
            Vector3 candidateXZ = room.worldPos + new Vector3(offsetX, 0f, offsetZ);

            // 1. 他のスロットと近すぎないか確認
            if (IsTooCloseToAnySlot(candidateXZ, SlotMinClearanceBetweenSlots)) continue;

            // 2. スロットの足元（中心＋周囲8点）がすべて茶色い歩行床の上にあるか確認
            if (!HasSolidWalkableFootprint(room, candidateXZ, SlotFootprintRadius, out float floorY)) continue;

            // 3. 壁や障害物から slotWallClearance 以上離れているか確認
            if (!HasClearanceFromWallsAndObstacles(candidateXZ, floorY, slotWallClearance)) continue;

            // 4. ★ 4方向のうち「壁に最も近い側」を背面にし、反対側（開けた床側）を正面にする向きを決定
            if (!TrySelectWallBackedFacingDirection(room, candidateXZ, floorY, out bestFrontDir)) continue;

            validBottomCenter = new Vector3(candidateXZ.x, floorY, candidateXZ.z);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 前後左右4方向の「壁（または床の端）までの距離」を計測し、
    /// 背面側（-frontDir）が最も壁に近く、かつ正面側（frontDir）にプレイヤー用の広い床がある向きを選択する
    /// </summary>
    private bool TrySelectWallBackedFacingDirection(
        GeneratedRoom room,
        Vector3 slotXZ,
        float floorY,
        out Vector3 bestFrontDir)
    {
        bestFrontDir = Vector3.forward;
        bool foundValid = false;
        float closestBackWallDist = float.MaxValue;
        float bestFrontOpenDist = -1f;

        for (int dirIdx = 0; dirIdx < cardinalDirections.Length; dirIdx++)
        {
            Vector3 frontDir = cardinalDirections[dirIdx];
            Vector3 backDir = -frontDir;
            Vector3 rightDir = new Vector3(frontDir.z, 0f, -frontDir.x);

            // A. 正面（プレイヤーが立つ位置＋左右幅＋奥の通路）に茶色い床があり障害物がないか確認
            Vector3 standCenter = slotXZ + frontDir * SlotFrontStandDist;
            if (!IsValidWalkableFloorPoint(room, standCenter, out _)) continue;
            if (!IsValidWalkableFloorPoint(room, standCenter + rightDir * SlotFrontSideWidth, out _)) continue;
            if (!IsValidWalkableFloorPoint(room, standCenter - rightDir * SlotFrontSideWidth, out _)) continue;
            if (!HasClearanceFromWallsAndObstacles(standCenter, floorY, InsideObstacleSphereRadius * 2f)) continue;

            Vector3 walkwayPoint = slotXZ + frontDir * SlotFrontWalkwayDist;
            if (!IsValidWalkableFloorPoint(room, walkwayPoint, out _)) continue;

            // B. 背面側（backDir）にある壁までの距離と、正面側（frontDir）の開け具合を計測
            float backWallDist = MeasureDistanceToWallOrEdge(room, slotXZ, floorY, backDir);
            float frontOpenDist = MeasureDistanceToWallOrEdge(room, slotXZ, floorY, frontDir);

            // 正面よりも背面の方が壁から遠い（＝壁に向かって正面を向けている）向きは除外し、
            // 最も背面が壁に近い向きを採用する
            bool isBetter = !foundValid
                            || (backWallDist < closestBackWallDist - 0.05f)
                            || (Mathf.Abs(backWallDist - closestBackWallDist) <= 0.05f && frontOpenDist > bestFrontOpenDist);

            if (isBetter)
            {
                closestBackWallDist = backWallDist;
                bestFrontOpenDist = frontOpenDist;
                bestFrontDir = frontDir;
                foundValid = true;
            }
        }

        return foundValid;
    }

    /// <summary>
    /// 指定した水平方向(dir)へレイキャスト＆床面走査を行い、壁コライダーまたは床の境界までの距離を返す
    /// </summary>
    private float MeasureDistanceToWallOrEdge(GeneratedRoom room, Vector3 originXZ, float floorY, Vector3 dir)
    {
        float maxCheckDist = tileSize;
        float wallColliderDist = maxCheckDist;

        Vector3 lowOrigin = new Vector3(originXZ.x, floorY + ClearanceCheckHeightLow, originXZ.z);
        Vector3 highOrigin = new Vector3(originXZ.x, floorY + ClearanceCheckHeightHigh, originXZ.z);

        if (Physics.Raycast(lowOrigin, dir, out RaycastHit lowHit, maxCheckDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            wallColliderDist = Mathf.Min(wallColliderDist, lowHit.distance);
        }
        if (Physics.Raycast(highOrigin, dir, out RaycastHit highHit, maxCheckDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            wallColliderDist = Mathf.Min(wallColliderDist, highHit.distance);
        }

        // 茶色い歩行床が途切れる（黒い壁や空間に入る）までの距離も計測
        float floorEdgeDist = maxCheckDist;
        for (float d = WallSearchStepSize; d <= maxCheckDist; d += WallSearchStepSize)
        {
            if (!IsValidWalkableFloorPoint(room, originXZ + dir * d, out _))
            {
                floorEdgeDist = d;
                break;
            }
        }

        return Mathf.Min(wallColliderDist, floorEdgeDist);
    }

    /// <summary>
    /// スロットモデル内のボタン・リールの配置から実際の「モデルの正面方向」を検出し、
    /// desiredFrontDir（壁と反対の開けた方向）へ正面が向くようY軸回転を合わせる
    /// </summary>
    private void AlignSlotFrontToDirection(GameObject slotInstance, Vector3 desiredFrontDir)
    {
        Vector3 currentFrontDir = DetectSlotWorldFrontDirection(slotInstance);
        currentFrontDir.y = 0f;
        desiredFrontDir.y = 0f;

        if (currentFrontDir.sqrMagnitude < 0.0001f || desiredFrontDir.sqrMagnitude < 0.0001f) return;

        float deltaYaw = Vector3.SignedAngle(currentFrontDir.normalized, desiredFrontDir.normalized, Vector3.up) + slotRotationYawOffset;
        slotInstance.transform.rotation = Quaternion.Euler(0f, deltaYaw, 0f) * slotInstance.transform.rotation;
    }

    /// <summary>
    /// スロット筐体の中心とボタン(SlotButtonInteractable)・リール(SlotReelScroller)の座標から、
    /// 現在ワールド空間でスロットの正面が向いている水平ベクトルを返す
    /// </summary>
    private Vector3 DetectSlotWorldFrontDirection(GameObject slotInstance)
    {
        Bounds bodyBounds = GetCombinedMeshBounds(slotInstance, out bool hasBounds);
        Vector3 bodyCenter = hasBounds ? bodyBounds.center : slotInstance.transform.position;

        // 1. ボタンやリールの平均座標（筐体中心よりも正面側に位置するパーツ）を取得
        Vector3 frontFeatureSum = Vector3.zero;
        int featureCount = 0;

        SlotButtonInteractable[] buttons = slotInstance.GetComponentsInChildren<SlotButtonInteractable>();
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            Renderer r = buttons[i].GetComponent<Renderer>();
            frontFeatureSum += (r != null) ? r.bounds.center : buttons[i].transform.position;
            featureCount++;
        }

        SlotReelScroller[] reels = slotInstance.GetComponentsInChildren<SlotReelScroller>();
        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] == null) continue;
            Renderer r = reels[i].GetComponent<Renderer>();
            frontFeatureSum += (r != null) ? r.bounds.center : reels[i].transform.position;
            featureCount++;
        }

        Vector3 toFrontFeatures = (featureCount > 0)
            ? (frontFeatureSum / featureCount) - bodyCenter
            : slotInstance.transform.forward;
        toFrontFeatures.y = 0f;

        // 2. リールが複数ある場合はリール並び方向（左右軸）に直交する法線を基準にし、ボタン側を正面とする
        if (reels != null && reels.Length >= 2 && reels[0] != null && reels[reels.Length - 1] != null)
        {
            Renderer rLeft = reels[0].GetComponent<Renderer>();
            Renderer rRight = reels[reels.Length - 1].GetComponent<Renderer>();
            Vector3 leftPos = (rLeft != null) ? rLeft.bounds.center : reels[0].transform.position;
            Vector3 rightPos = (rRight != null) ? rRight.bounds.center : reels[reels.Length - 1].transform.position;

            Vector3 reelRight = rightPos - leftPos;
            reelRight.y = 0f;

            if (reelRight.sqrMagnitude > 0.0001f)
            {
                Vector3 normal = Vector3.Cross(reelRight.normalized, Vector3.up).normalized;
                if (toFrontFeatures.sqrMagnitude > 0.0001f && Vector3.Dot(normal, toFrontFeatures) < 0f)
                {
                    normal = -normal;
                }
                return normal;
            }
        }

        return toFrontFeatures.sqrMagnitude > 0.0001f ? toFrontFeatures.normalized : slotInstance.transform.forward;
    }

    private bool HasSolidWalkableFootprint(GeneratedRoom room, Vector3 centerXZ, float radius, out float floorY)
    {
        if (!IsValidWalkableFloorPoint(room, centerXZ, out floorY)) return false;

        float diag = radius * 0.7071f;
        Vector3[] offsets =
        {
            new Vector3(radius, 0f, 0f),
            new Vector3(-radius, 0f, 0f),
            new Vector3(0f, 0f, radius),
            new Vector3(0f, 0f, -radius),
            new Vector3(diag, 0f, diag),
            new Vector3(-diag, 0f, diag),
            new Vector3(diag, 0f, -diag),
            new Vector3(-diag, 0f, -diag)
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            if (!IsValidWalkableFloorPoint(room, centerXZ + offsets[i], out float subY))
            {
                return false;
            }
            if (Mathf.Abs(subY - floorY) > FloorHeightMatchTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidWalkableFloorPoint(GeneratedRoom room, Vector3 worldXZ, out float floorY)
    {
        floorY = room.walkableFloorY;

        if (!TryGetTopmostEnvironmentHit(worldXZ, room.worldPos.y, out RaycastHit topHit))
        {
            return false;
        }

        if (topHit.normal.y < FloorNormalThreshold)
        {
            return false;
        }

        if (Mathf.Abs(topHit.point.y - room.walkableFloorY) > FloorHeightMatchTolerance)
        {
            return false;
        }

        if (room.hasBrightWalkableFloor && GetSurfaceBrightness(topHit.collider) < MinFloorColorBrightness)
        {
            return false;
        }

        floorY = topHit.point.y;
        return true;
    }

    private bool TryGetTopmostEnvironmentHit(Vector3 worldXZ, float baseRoomY, out RaycastHit topmostHit)
    {
        topmostHit = default;

        Vector3 rayOrigin = new Vector3(worldXZ.x, baseRoomY + FloorRaycastStartHeight, worldXZ.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            FloorRaycastMaxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0) return false;

        float highestY = float.MinValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;

            if (col.GetComponentInParent<SlotMachineController>() != null) continue;
            if (col.GetComponentInParent<Item>() != null) continue;
            if (col.GetComponentInParent<PlayerController>() != null) continue;

            if (hits[i].point.y > highestY)
            {
                highestY = hits[i].point.y;
                topmostHit = hits[i];
                found = true;
            }
        }

        return found;
    }

    private float GetSurfaceBrightness(Collider col)
    {
        if (col == null) return 1f;
        Renderer r = col.GetComponent<Renderer>();
        if (r == null) r = col.GetComponentInParent<Renderer>();
        if (r == null || r.sharedMaterial == null) return 1f;

        Material[] mats = r.sharedMaterials;
        if (mats != null && mats.Length > 1)
        {
            float maxB = 0f;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                float b = ExtractMaterialBrightness(mats[i]);
                if (b > maxB) maxB = b;
            }
            return maxB;
        }

        return ExtractMaterialBrightness(r.sharedMaterial);
    }

    private float ExtractMaterialBrightness(Material mat)
    {
        if (mat == null) return 1f;
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }
        if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }
        return 1f;
    }

    private bool HasClearanceFromWallsAndObstacles(Vector3 centerXZ, float floorY, float clearance)
    {
        Vector3 lowOrigin = new Vector3(centerXZ.x, floorY + ClearanceCheckHeightLow, centerXZ.z);
        Vector3 highOrigin = new Vector3(centerXZ.x, floorY + ClearanceCheckHeightHigh, centerXZ.z);

        if (Physics.CheckSphere(lowOrigin, InsideObstacleSphereRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        float angleStep = FullCircleDegrees / HorizontalClearanceRayCount;
        for (int i = 0; i < HorizontalClearanceRayCount; i++)
        {
            float rad = (i * angleStep) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            if (Physics.Raycast(lowOrigin, dir, clearance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (Physics.Raycast(highOrigin, dir, clearance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
        }

        return true;
    }

    private Bounds GetCombinedMeshBounds(GameObject obj, out bool hasBounds)
    {
        Bounds combinedBounds = default;
        hasBounds = false;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers == null) return combinedBounds;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            if (r is ParticleSystemRenderer) continue;
            if (r.GetComponent<TMP_Text>() != null || r.GetComponentInParent<TMP_Text>() != null) continue;

            if (!hasBounds)
            {
                combinedBounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(r.bounds);
            }
        }

        return combinedBounds;
    }

    private void AlignObjectBoundsToFloor(GameObject obj, Vector3 targetBottomCenter, float extraOffsetY)
    {
        Bounds combinedBounds = GetCombinedMeshBounds(obj, out bool hasBounds);
        if (!hasBounds)
        {
            obj.transform.position = targetBottomCenter + Vector3.up * extraOffsetY;
            return;
        }

        Vector3 currentBottomCenter = new Vector3(combinedBounds.center.x, combinedBounds.min.y, combinedBounds.center.z);
        Vector3 desiredBottomCenter = new Vector3(targetBottomCenter.x, targetBottomCenter.y + extraOffsetY, targetBottomCenter.z);
        obj.transform.position += (desiredBottomCenter - currentBottomCenter);
    }

    private bool IsTooCloseToAnySlot(Vector3 candidateXZ, float minClearance)
    {
        float minSqDist = minClearance * minClearance;
        for (int i = 0; i < spawnedSlotList.Count; i++)
        {
            if (spawnedSlotList[i] == null) continue;
            Vector3 slotPos = spawnedSlotList[i].transform.position;
            float dx = candidateXZ.x - slotPos.x;
            float dz = candidateXZ.z - slotPos.z;
            if ((dx * dx + dz * dz) < minSqDist)
            {
                return true;
            }
        }
        return false;
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
        float maxOffset = tileSize * ScrapSpawnAreaRatio;

        while (currentTotalValue < targetTotalValue && safetyCounter < MaxScrapSpawnIterations)
        {
            safetyCounter++;

            GeneratedRoom targetRoom = availableRooms[Random.Range(0, availableRooms.Count)];

            float offsetX = Random.Range(-maxOffset, maxOffset);
            float offsetZ = Random.Range(-maxOffset, maxOffset);
            Vector3 candidateXZ = targetRoom.worldPos + new Vector3(offsetX, 0f, offsetZ);

            if (IsTooCloseToAnySlot(candidateXZ, SlotMinClearanceForScrap)) continue;

            if (!HasSolidWalkableFootprint(targetRoom, candidateXZ, ScrapFootprintRadius, out float floorY)) continue;

            Vector3 checkPos = new Vector3(candidateXZ.x, floorY + ClearanceCheckHeightLow, candidateXZ.z);
            if (Physics.CheckSphere(checkPos, InsideObstacleSphereRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 finalSpawnPos = new Vector3(candidateXZ.x, floorY + ScrapSurfaceOffsetHeight, candidateXZ.z);

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
                scrapObj.transform.localScale = Vector3.one * FallbackCubeScale;
                scrapObj.AddComponent<Rigidbody>();
                scrapObj.AddComponent<BoxCollider>();
                scrapObj.transform.SetParent(transform);
            }

            AlignObjectBoundsToFloor(scrapObj, new Vector3(candidateXZ.x, floorY, candidateXZ.z), ScrapSurfaceOffsetHeight);

            Item worldItem = scrapObj.GetComponent<Item>();
            if (worldItem == null)
            {
                worldItem = scrapObj.AddComponent<Item>();
            }
            worldItem.SetItemData(randomScrapData);

            int itemVal = randomScrapData.Amount > 0 ? randomScrapData.Amount : DefaultFallbackScrapValue;
            currentTotalValue += itemVal;
        }

        Debug.Log($"[MAP] ノルマ連動スクラップ配置完了！ 目標価値: {targetTotalValue}, 生成総価値: {currentTotalValue}");
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

            if (neighborCount < MinNeighborsForBusRoom) continue;

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

            SpawnPlayerAtPosition(bestCandidate.pos + Vector3.up * PlayerSpawnHeightOffset);
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
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[1] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.forward * busOffsetDistance, rot, 0));
        }
        if (isBottomEdge && room.bottomDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[0] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.back * busOffsetDistance, rot, 1));
        }
        if (isLeftEdge && room.leftDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[3] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.left * busOffsetDistance, rot, 2));
        }
        if (isRightEdge && room.rightDoor)
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[2] + busYRotationOffset, 0f);
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
            int[] angles = chip.allowRotation ? allowedRotationAngles : nonRotatedAngles;

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