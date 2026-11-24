using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.AI.Navigation;
using UnityEngine.AI;

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
    [SerializeField] private float tileSize = 20f;
    [SerializeField] private int maxRetryCount = 50;

    [Header("バス設定")]
    [SerializeField] private GameObject busPrefab;
    [SerializeField] private float busOffsetDistance = 20f;
    [SerializeField] private float busYRotationOffset = -90f;

    [Header("レベル進行設定")]
    [SerializeField, Min(1)] private int currentStageLevel = 1;
    [Tooltip("このレベルでマップサイズ・ノルマが最大になる（以降は最大サイズのまま）")]
    [SerializeField, Min(2)] private int maxStageLevel = 20;
    [Tooltip("レベル1の部屋数")]
    [SerializeField, Min(1)] private int minRoomCount = 5;
    [Tooltip("最大レベルでの部屋数（レベルに応じて線形に増える）")]
    [SerializeField, Min(1)] private int maxRoomCount = 40;

    [Header("ノルマ・借金設定")]
    [Tooltip("レベル1のノルマ（基準値）")]
    [SerializeField, Min(1)] private int baseQuotaLevel1 = 500;
    [Tooltip("最大レベルのノルマ（基準値）。レベル1からこの値まで指数的に増える")]
    [SerializeField, Min(1)] private int baseQuotaFinalLevel = 284500;
    [Tooltip("基準ノルマに掛けるランダム幅（0.08 = ±8%）")]
    [SerializeField, Range(0f, 0.5f)] private float quotaRandomRange = 0.08f;
    [Tooltip("ノルマに対してマップへ配置するスクラップ総価値の倍率")]
    [SerializeField] private float quotaMultiplier = 2.5f;
    [Tooltip("最大レベルを超えた後（借金返済後の無限モード）の、1レベルごとのノルマ・価格の増加倍率")]
    [SerializeField, Min(1f)] private float postMaxLevelGrowthRate = 1.1f;

    [Header("価格補正（レベルが上がるほど価値が上がる）")]
    [Tooltip("最大レベルでの価格倍率。レベル1の1倍から指数的に増える。スクラップの売値・スロットのベット額に適用される")]
    [SerializeField, Min(1f)] private float priceMultiplierFinalLevel = 150f;

    [Header("スロット自動配置設定")]
    [Tooltip("このレベル以降のマップにだけスロットマシンを配置する")]
    [SerializeField, Min(1)] private int slotStartLevel = 5;
    [SerializeField] private List<SlotSpawnCandidate> slotCandidates = new List<SlotSpawnCandidate>();
    [SerializeField, Min(0)] private int minSlotCount = 2;
    [SerializeField, Min(0)] private int maxSlotCount = 4;

    [System.Serializable]
    public struct SlotRarityTier
    {
        [Tooltip("スロット台の倍率")] public float multiplier;
        [Tooltip("このレベル以降の台だけ、この倍率が出る")] public int minLevel;
        [Tooltip("出やすさ（大きいほど出やすい）。高倍率ほど小さくする")] public float weight;

        public SlotRarityTier(float multiplier, int minLevel, float weight)
        {
            this.multiplier = multiplier;
            this.minLevel = minLevel;
            this.weight = weight;
        }
    }

    [Tooltip("スロット台ごとの倍率の抽選表。高倍率は出にくく、レベルが上がるまで出ない")]
    [SerializeField] private List<SlotRarityTier> slotRarityTiers = new List<SlotRarityTier>
    {
        new SlotRarityTier(0.8f, 1, 30f),
        new SlotRarityTier(1.0f, 1, 30f),
        new SlotRarityTier(1.5f, 1, 20f),
        new SlotRarityTier(2.0f, 7, 12f),
        new SlotRarityTier(3.0f, 10, 6f),
        new SlotRarityTier(5.0f, 14, 3f),
        new SlotRarityTier(10.0f, 18, 1f),
    };

    /// <summary>現在のレベルで出せる倍率から、重み付きでスロット台の倍率を1つ引く</summary>
    private float RollSlotRarity()
    {
        float total = 0f;
        foreach (SlotRarityTier tier in slotRarityTiers)
        {
            if (currentStageLevel >= tier.minLevel) total += Mathf.Max(0f, tier.weight);
        }
        if (total <= 0f) return 1f;

        float roll = Random.value * total;
        float cumulative = 0f;
        float last = 1f;
        foreach (SlotRarityTier tier in slotRarityTiers)
        {
            if (currentStageLevel < tier.minLevel) continue;
            cumulative += Mathf.Max(0f, tier.weight);
            last = tier.multiplier;
            if (roll < cumulative) return tier.multiplier;
        }
        return last;
    }
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
    private const int MaxRoomCountDegradeSteps = 5;
    private const int QuotaSmallRoundingUnit = 50;
    private const int QuotaMediumRoundingUnit = 500;
    private const int QuotaLargeRoundingUnit = 1000;
    private const int QuotaMediumThreshold = 10000;
    private const int QuotaLargeThreshold = 100000;
    private const int TotalDebtRoundingUnit = 10000;
    private const double MaxQuotaValue = 1.5e9;
    private const double PriceRoundingMinValue = 100;

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

    // 現在のレベルで生成する部屋数と、今回のマップで確定したノルマ（基準値にランダム幅を掛けたもの）
    private int currentTargetRoomCount;
    private int currentMapQuota;

    /// <summary>
    /// 借金の総額：レベル1〜最大レベルの基準ノルマの合計（1万単位で丸め）。
    /// 全レベルのノルマを払い切ると、ちょうど借金を完済できる。
    /// </summary>
    public int TotalDebt
    {
        get
        {
            long sum = 0;
            for (int level = 1; level <= maxStageLevel; level++)
            {
                sum += GetBaseQuota(level);
            }
            return (int)(Mathf.Round(sum / (float)TotalDebtRoundingUnit) * TotalDebtRoundingUnit);
        }
    }

    /// <summary>
    /// 指定レベルの基準ノルマ。レベル1の値から最大レベルの値まで指数的に増加する
    /// </summary>
    public int GetBaseQuota(int level)
    {
        double raw;
        if (level <= maxStageLevel)
        {
            double growth = System.Math.Pow((double)baseQuotaFinalLevel / baseQuotaLevel1, 1.0 / (maxStageLevel - 1));
            raw = baseQuotaLevel1 * System.Math.Pow(growth, System.Math.Max(0, level - 1));
        }
        else
        {
            // 最大レベル以降（借金返済後の無限モード）は緩やかに増加し続ける
            raw = baseQuotaFinalLevel * System.Math.Pow(postMaxLevelGrowthRate, level - maxStageLevel);
        }

        return RoundQuota((float)System.Math.Min(raw, MaxQuotaValue));
    }

    /// <summary>
    /// 指定レベルの価格倍率。最大レベルまでは 1 → priceMultiplierFinalLevel へ指数的に増え、以降は緩やかに増え続ける
    /// </summary>
    public float GetPriceMultiplier(int level)
    {
        if (level <= maxStageLevel)
        {
            float t = (level - 1) / (float)(maxStageLevel - 1);
            return Mathf.Pow(priceMultiplierFinalLevel, Mathf.Max(0f, t));
        }

        return priceMultiplierFinalLevel * Mathf.Pow(postMaxLevelGrowthRate, level - maxStageLevel);
    }

    public float CurrentPriceMultiplier => GetPriceMultiplier(currentStageLevel);

    /// <summary>
    /// 現在のレベルの価格補正を掛けたスクラップの売値
    /// </summary>
    public int GetScrapValue(ItemDataAsset item)
    {
        if (item == null) return 0;
        return Mathf.RoundToInt(item.Amount * CurrentPriceMultiplier);
    }

    /// <summary>
    /// 基準額（レベル1相当）に現在の価格補正を掛け、上位2桁で丸めた金額を返す（スロットのベット額など）
    /// </summary>
    public int ScaleToCurrentPrice(int baseAmount)
    {
        double scaled = baseAmount * (double)CurrentPriceMultiplier;
        if (scaled < PriceRoundingMinValue) return Mathf.RoundToInt((float)scaled);

        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(scaled)) - 1);
        return (int)System.Math.Min(System.Math.Round(scaled / magnitude) * magnitude, int.MaxValue);
    }

    /// <summary>
    /// 指定レベルの部屋数。レベル1の minRoomCount から最大レベルの maxRoomCount まで線形に増加する
    /// </summary>
    public int GetRoomCountForLevel(int level)
    {
        float t = Mathf.Clamp01((level - 1) / (float)(maxStageLevel - 1));
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(minRoomCount, maxRoomCount, t)));
    }

    private int RoundQuota(float rawQuota)
    {
        int unit = rawQuota < QuotaMediumThreshold ? QuotaSmallRoundingUnit
                 : rawQuota < QuotaLargeThreshold ? QuotaMediumRoundingUnit
                 : QuotaLargeRoundingUnit;
        return Mathf.Max(unit, Mathf.RoundToInt(rawQuota / unit) * unit);
    }

    private void RollQuotaForCurrentLevel()
    {
        float randomFactor = 1f + Random.Range(-quotaRandomRange, quotaRandomRange);
        currentMapQuota = RoundQuota(GetBaseQuota(currentStageLevel) * randomFactor);
    }

    private void Awake()
    {
        gizmoDrawer = GetComponent<MapGizmoDrawer>();
    }

    /// <summary>
    /// セーブデータから再開するとき、Start より前に開始レベルを設定する
    /// </summary>
    public void SetStartLevel(int level)
    {
        currentStageLevel = Mathf.Max(1, level);
    }

    /// <summary>
    /// マルチプレイ中は、全員が同じマップ・ノルマになるよう乱数を揃える
    /// </summary>
    private void SyncRandomWithSession()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsOnline)
        {
            Random.InitState(net.SharedSeed + currentStageLevel * 7919);
        }
    }

    private void Start()
    {
        SyncRandomWithSession();
        RollQuotaForCurrentLevel();
        Debug.Log($"[MAP] 借金総額: ${TotalDebt:N0} / STAGE {currentStageLevel} ノルマ: ${currentMapQuota:N0}");
        GenerateMap();
    }

    public void GenerateMap()
    {
        int requestedRoomCount = GetRoomCountForLevel(currentStageLevel);

        // 目標の部屋数に届かない場合は、少しずつ目標を下げて再挑戦する（プレイヤー・バス未配置の詰み防止）
        for (int step = 0; step <= MaxRoomCountDegradeSteps; step++)
        {
            currentTargetRoomCount = requestedRoomCount - step;
            if (currentTargetRoomCount < 1) break;

            for (int retry = 0; retry < maxRetryCount; retry++)
            {
                if (TryGenerateMapOnce())
                {
                    if (step > 0)
                    {
                        Debug.LogWarning($"[MAP] 目標部屋数({requestedRoomCount})を作れなかったため、{currentTargetRoomCount}部屋で生成しました。");
                    }
                    return;
                }
            }
        }

        Debug.LogWarning($"[MAP] 警告: 目標部屋数({requestedRoomCount})の生成に失敗しました。（現在の生成数: {spawnedRoomList.Count}）");
    }

    /// <summary>
    /// 簡易リザルトの「次へ」ボタンから呼ばれ、レベルを上げてノルマを決め直し、新しいマップを再生成する
    /// </summary>
    public void AdvanceToNextLevel()
    {
        currentStageLevel++;
        SyncRandomWithSession();
        RollQuotaForCurrentLevel();

        Debug.Log($"[MAP] STAGE {currentStageLevel} 開始！ 部屋数: {GetRoomCountForLevel(currentStageLevel)} / 新ノルマ: ${currentMapQuota:N0}");
        GenerateMap();
    }

    private bool TryGenerateMapOnce()
    {
        foreach (Transform child in transform)
        {
            // Destroy は次のフレームまで残るので、先に無効化して、物理判定・NavMeshの対象から外す
            child.gameObject.SetActive(false);
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

        while (stack.Count > 0 && spawnedRoomList.Count < currentTargetRoomCount)
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

                if (spawnedRoomList.Count >= currentTargetRoomCount) break;
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

        if (spawnedRoomList.Count >= currentTargetRoomCount)
        {
            Physics.SyncTransforms();

            AnalyzeAllRoomsWalkableFloor();

            // 1. バス配置 ➔ 2. スロット配置 ➔ 3. スクラップ配置
            SpawnBusAtOuterDoor();
            SpawnSlotMachines();
            Physics.SyncTransforms();
            SpawnRandomScraps();
            SpawnEnemies();

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
        if (currentStageLevel < slotStartLevel) return;
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

                // マルチプレイ同期用：生成順の連番IDを振る（全員同じシードなので同じ台が同じIDになる）
                SlotMachineController slotController = slotInstance.GetComponentInChildren<SlotMachineController>();
                if (slotController != null)
                {
                    slotController.NetId = currentStageLevel * 1000 + spawnedSlotList.Count;
                    // 台ごとの倍率（共有シードで抽選するので、マルチでも全員同じ倍率になる）
                    slotController.SetRarityMultiplier(RollSlotRarity());
                }

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
        int scrapNetId = 0;
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
            worldItem.NetId = currentStageLevel * 100000 + (++scrapNetId); // マルチプレイ同期用ID（ステージごとに重ならないよう、ステージ番号を含める）

            int itemVal = randomScrapData.Amount > 0 ? GetScrapValue(randomScrapData) : DefaultFallbackScrapValue;
            currentTotalValue += itemVal;
        }

        Debug.Log($"[MAP] ノルマ連動スクラップ配置完了！ 目標価値: {targetTotalValue}, 生成総価値: {currentTotalValue}");
    }

    // ------------------------------------------------------------------
    // 敵：マップ生成のたびに NavMesh を焼き直し、部屋の中に敵を出す
    // （共有シードで抽選するので、マルチプレイでも全員の画面に同じ敵が同じ場所へ出る。動かすのはホストだけ）
    // ------------------------------------------------------------------

    private NavMeshSurface navSurface;

    private bool BuildNavMesh()
    {
        if (navSurface == null)
        {
            navSurface = GetComponent<NavMeshSurface>();
            if (navSurface == null) navSurface = gameObject.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.Children;
            navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            // 落ちているアイテムや他プレイヤーの当たり判定は、歩ける床として数えない
            int itemsLayer = LayerMask.NameToLayer("Items");
            int mask = ~(1 << 2);
            if (itemsLayer >= 0) mask &= ~(1 << itemsLayer);
            navSurface.layerMask = mask;
        }

        Physics.SyncTransforms();
        navSurface.BuildNavMesh();
        return navSurface.navMeshData != null;
    }

    private void SpawnEnemies()
    {
        EnemySpawnConfig config = EnemySpawnConfig.Load();
        if (config == null) return;

        // REPO と同じく、ティアごとに決まった数を出す
        List<GameObject> spawnList = config.RollSpawnList(currentStageLevel);
        int count = spawnList.Count;
        if (count <= 0) return;

        if (!BuildNavMesh())
        {
            Debug.LogWarning("[ENEMY] NavMesh を作れなかったため、敵を出しません。");
            return;
        }

        // バスから離れた部屋だけを候補にする
        List<GeneratedRoom> candidates = spawnedRoomList.FindAll(r =>
        {
            if (!spawnedBusGridPos.HasValue) return r.gridPos != Vector2Int.zero;
            int dist = Mathf.Abs(r.gridPos.x - spawnedBusGridPos.Value.x) + Mathf.Abs(r.gridPos.y - spawnedBusGridPos.Value.y);
            return dist >= config.minRoomDistanceFromBus;
        });
        if (candidates.Count == 0) candidates = spawnedRoomList.FindAll(r => r.gridPos != Vector2Int.zero);
        if (candidates.Count == 0) return;

        int spawned = 0;
        for (int attempt = 0; attempt < count * 10 && spawned < count; attempt++)
        {
            GeneratedRoom room = candidates[Random.Range(0, candidates.Count)];
            float range = tileSize * 0.3f;
            Vector3 probe = new Vector3(
                room.worldPos.x + Random.Range(-range, range),
                room.walkableFloorY + 0.5f,
                room.worldPos.z + Random.Range(-range, range));

            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;

            GameObject enemyObj = Instantiate(spawnList[spawned], hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            EnemyBrain brain = enemyObj.GetComponent<EnemyBrain>();
            if (brain == null) brain = enemyObj.AddComponent<EnemyBrain>();
            brain.Init(currentStageLevel * 100 + (++spawned));
        }

        Debug.Log($"[ENEMY] 敵を {spawned}/{count} 体配置しました。");
    }

    /// <summary>
    /// 自分の席番号に応じて、基準点の周りへずらした出現位置を返す（席ごとに向きが違うので全員がばらける）。
    /// 壁や物にめり込む位置は避けて、近い順に空いている場所を探す。オンラインでなければそのまま
    /// </summary>
    private Vector3 GetSeatSpawnPosition(Vector3 basePos)
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null || !net.IsOnline || Unity.Netcode.NetworkManager.Singleton == null) return basePos;

        int seat = (int)(Unity.Netcode.NetworkManager.Singleton.LocalClientId % 8);
        int itemsLayer = LayerMask.NameToLayer("Items");
        int mask = ~(1 << 2);
        if (itemsLayer >= 0) mask &= ~(1 << itemsLayer);

        float[] radii = { 1.0f, 1.7f, 2.4f };
        foreach (float radius in radii)
        {
            for (int step = 0; step < 8; step++)
            {
                // 席ごとに最初の向きを変え、空いていなければ45度ずつ回して探す
                float angle = (seat * 45f + step * 45f) * Mathf.Deg2Rad;
                Vector3 candidate = basePos + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;

                // 足元に床がある場所だけを選ぶ（バスの外の何もない所に出ると、落下して即死になる）
                bool hasGround = Physics.Raycast(candidate + Vector3.up * 1.0f, Vector3.down, 3f, mask, QueryTriggerInteraction.Ignore);
                if (hasGround && !Physics.CheckCapsule(candidate + Vector3.up * 0.6f, candidate + Vector3.up * 1.4f, 0.4f, mask, QueryTriggerInteraction.Ignore))
                {
                    return candidate;
                }
            }
        }
        return basePos;
    }

    /// <summary>プレイヤーを最後に置いた位置（落下からの復帰先）</summary>
    public Vector3 PlayerSpawnPoint { get; private set; }
    public bool HasPlayerSpawnPoint { get; private set; }

    private void SpawnPlayerAtPosition(Vector3 targetPosition)
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            Debug.LogWarning("[MAP] シーン内に PlayerController が見つかりませんでした！");
            return;
        }

        // マルチプレイ：全員が同じ点に出ると重なるので、席番号ごとに少しずつずらす
        targetPosition = GetSeatSpawnPosition(targetPosition);

        // 出現位置の真下に床がなければ、上から探して床の上へ置く（床のない所に出ると落下してしまう）
        int groundMask = ~(1 << 2);
        if (!Physics.Raycast(targetPosition + Vector3.up * 0.5f, Vector3.down, 4f, groundMask, QueryTriggerInteraction.Ignore) &&
            Physics.Raycast(targetPosition + Vector3.up * 12f, Vector3.down, out RaycastHit groundHit, 40f, groundMask, QueryTriggerInteraction.Ignore))
        {
            targetPosition = groundHit.point + Vector3.up * 1.0f;
        }
        PlayerSpawnPoint = targetPosition;
        HasPlayerSpawnPoint = true;

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

        // それでも見つからない場合：スタート部屋も含め、外側に面した扉のある部屋ならどこでもよい
        if (busCandidates.Count == 0)
        {
            foreach (var room in spawnedRoomList) AddOuterDoorBusCandidates(room, busCandidates);
        }

        // 最後の手段：扉がなくても、外側に面した部屋の辺にバスを置く（バスが無いまま始まるのを防ぐ）
        if (busCandidates.Count == 0)
        {
            Debug.LogWarning("[MAP] 外側に扉のある部屋が無いため、扉のない辺にバスを配置します。");
            foreach (var room in spawnedRoomList) AddOuterDoorBusCandidates(room, busCandidates, false);
        }

        if (busCandidates.Count == 0)
        {
            Debug.LogError("[MAP] バスを置ける場所が見つかりませんでした。");
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

    private void AddOuterDoorBusCandidates(GeneratedRoom room, List<(GeneratedRoom room, Vector3 pos, Quaternion rot, int dir)> busCandidates, bool requireDoor = true)
    {
        bool isTopEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.up);
        bool isBottomEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.down);
        bool isLeftEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.left);
        bool isRightEdge = !generatedRooms.ContainsKey(room.gridPos + Vector2Int.right);

        if (isTopEdge && (!requireDoor || room.topDoor))
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[1] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.forward * busOffsetDistance, rot, 0));
        }
        if (isBottomEdge && (!requireDoor || room.bottomDoor))
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[0] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.back * busOffsetDistance, rot, 1));
        }
        if (isLeftEdge && (!requireDoor || room.leftDoor))
        {
            Quaternion rot = Quaternion.Euler(0f, wallInwardYawAngles[3] + busYRotationOffset, 0f);
            busCandidates.Add((room, room.worldPos + Vector3.left * busOffsetDistance, rot, 2));
        }
        if (isRightEdge && (!requireDoor || room.rightDoor))
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