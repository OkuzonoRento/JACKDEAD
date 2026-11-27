using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SlotSymbolEffect
{
    Payout, // 賞金獲得（スロットのレア度倍率 × 賭け金 × 目の倍率）
    Reroll  // 再ロール（賭け金を消費せずもう一度回す）
}

[System.Serializable]
public class SlotSymbolData
{
    [Tooltip("目の名前（例: 1, 2, 3, 5, 10）")]
    public string symbolName = "1";

    [Tooltip("スロットに表示する目のSprite")]
    public Sprite sprite;

    [Tooltip("揃ったときの効果（Payout: 賞金獲得 / Reroll: 再ロール）")]
    public SlotSymbolEffect effectType = SlotSymbolEffect.Payout;

    [Tooltip("出目の倍率（例: 1, 2, 3, 5, 10）")]
    [Min(0.1f)]
    public float symbolMultiplier = 1.0f;

    [Tooltip("自動算出された3つ揃う当選確率（%）")]
    public float winRate;
}

public class SlotMachineController : MonoBehaviour
{
    [Tooltip("スロット正面からプレイヤーが立つ距離")]
    [SerializeField] private float standDistance = 1.35f;
    [Tooltip("カメラが見つめる高さの微調整")]
    [SerializeField] private float lookHeightOffset = -0.05f;
    [Tooltip("スロット正面へ移動・視点変更する時間")]
    [SerializeField] private float moveAndLookDuration = 0.4f;

    [Header("リール設定 (左・中・右の3列)")]
    [SerializeField] private SlotReelScroller[] reels;

    [Header("ガチャ風・物理ギミック演出")]
    [Tooltip("スロット稼働中にガタガタ揺らす本体モデル")]
    [SerializeField] private Transform machineBodyTransform;
    [Tooltip("スタート時に自動で倒れるレバーのTransform")]
    [SerializeField] private Transform leverTransform;
    [Tooltip("回転中の本体の揺れ強さ")]
    [SerializeField] private float rollShakeIntensity = 0.012f;

    [Header("スロットの倍率・確率設定")]
    [Tooltip("このスロット台のレア度倍率")]
    [SerializeField] private float slotRarityMultiplier = 1.5f;

    [Tooltip("ハズレになる確率（%）")]
    [SerializeField, Range(0f, 100f)] private float loseRate = 70.0f;

    [Tooltip("各出目のリスト（倍率から当選確率が自動算出）")]
    [SerializeField] private List<SlotSymbolData> symbols = new List<SlotSymbolData>();

    [Header("当たりの演出")]
    [SerializeField] private SlotCelebrationSettings celebration = new SlotCelebrationSettings();

    private SlotWinEffects winEffects;
    private Coroutine punchCoroutine;
    private bool resultSettled; // 配当を受け取り済み（このあと攻撃を受けても、失うものはない）
    private Coroutine shakeCoroutine;
    private bool remoteRolling;      // 他の人の回転を、こちらの画面で再生している間
    private SlotWinInfo? pendingWin; // 回転が止まったら見せる、他の人の当たり

    private Transform playerStandPoint;
    private PlayerController playerController;
    private PlayerMovement playerMovement;
    
    private const int CellResolution = 256;
    private const int StripCellCount = 10;
    private const float SymbolPadding = 0.1f;
    private const float SymbolScale = 0.75f;
    private const float WindowSymbolCount = 2.0f;
    private const float StopCenterOffset = 0.01f;

    private static readonly Color32 ReelBgColor = new Color32(31, 31, 38, 255);

    private Texture2D[] runtimeTextures;
    private int[] currentResults;
    private bool isPlaying = false;

    private Vector3 bodyInitialLocalPos;
    private Vector3 bodyInitialLocalScale;
    private Quaternion leverInitialLocalRot;

    /// <summary>他プレイヤーがこの台を回している間 true（マルチプレイ同期）</summary>
    private bool remoteBusy = false;
    private Coroutine remoteRollCoroutine;
    private float remoteBusySince;

    private const float NoiseInterval = 0.5f;          // 回している間、この間隔で音を出す
    private const float RemoteNoiseMaxSeconds = 10f;   // 終了の通知が届かない（切断など）場合に、音を打ち切る
    private float noiseTimer;

    /// <summary>マルチプレイ同期用のID（0 = 同期しない）。MapGenerator が生成順の連番を振る</summary>
    public int NetId { get; set; }

    public bool IsPlaying => isPlaying || remoteBusy;

    public void SetRemoteBusy(bool busy)
    {
        remoteBusy = busy;
        if (busy) remoteBusySince = Time.time;
    }

    // 回している間（自分でも他の人でも）は、ずっと音が鳴っていて、敵に聞こえる。敵を動かしているのはホストのPC
    private void Update()
    {
        bool sounding = isPlaying || (remoteBusy && Time.time - remoteBusySince < RemoteNoiseMaxSeconds);
        if (!sounding)
        {
            noiseTimer = 0f; // 次に回し始めたときは、すぐ鳴る
            return;
        }

        noiseTimer -= Time.deltaTime;
        if (noiseTimer > 0f) return;

        noiseTimer = NoiseInterval;
        EnemyNoise.Emit(transform.position, EnemyNoise.Slot);
    }

    /// <summary>他プレイヤーが回した1回分の演出だけを再生する（賭け金・配当は本人側のみ）</summary>
    public void PlayRemoteRoll(int[] results)
    {
        if (reels == null || currentResults == null || results == null) return;
        if (isPlaying || symbols.Count == 0) return;

        for (int i = 0; i < reels.Length && i < results.Length; i++)
        {
            currentResults[i] = Mathf.Clamp(results[i], 0, symbols.Count - 1);
        }

        if (remoteRollCoroutine != null) StopCoroutine(remoteRollCoroutine);
        StopCelebration(true); // ボタンを押した音は、鳴らしたままにする
        pendingWin = null;
        remoteBusy = true;
        remoteBusySince = Time.time; // 再ロールのたびに測り直す
        remoteRollCoroutine = StartCoroutine(RemoteRoll());
    }

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }

        if (playerMovement == null)
        {
            playerMovement = playerController != null
                ? playerController.GetComponent<PlayerMovement>()
                : FindAnyObjectByType<PlayerMovement>();
        }

        // 本体の基準位置・スケールはここでは記録しない。
        // 本体はプレハブのルートで、MapGenerator が生成直後（Awake の後）に位置・向きを床へ合わせ直すため、
        // Awake で記録すると調整前の位置に戻ってしまい、最初の1回の揺れの後だけ台がずれる。→ Start で記録する

        if (leverTransform != null)
        {
            leverInitialLocalRot = leverTransform.localRotation;
        }

        RecalculateWinRates();
    }

    private void Start()
    {
        // MapGenerator の位置合わせが終わったあとの姿勢を、揺れ・拡大演出の基準にする
        if (machineBodyTransform != null)
        {
            bodyInitialLocalPos = machineBodyTransform.localPosition;
            bodyInitialLocalScale = machineBodyTransform.localScale;
        }

        if (reels == null || reels.Length == 0 || symbols.Count == 0) return;

        runtimeTextures = new Texture2D[reels.Length];
        currentResults = new int[reels.Length];

        float stopOffset = GetTargetStopOffset();
        for (int i = 0; i < reels.Length; i++)
        {
            currentResults[i] = GetRandomReelSymbolIndex();
            GenerateReelStrip(i, currentResults[i]);
            reels[i].SetOffset(stopOffset);
        }
    }

    private void OnValidate()
    {
        RecalculateWinRates();

        // プレハブアセット自体（シーン外）ではマテリアルに触れない（Renderer.material はプレハブでエラーになる）
        if (!Application.isPlaying || !gameObject.scene.IsValid() || reels == null || isPlaying) return;

        float stopOffset = GetTargetStopOffset();
        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] != null)
            {
                reels[i].SetOffset(stopOffset);
            }
        }
    }

    private void OnEnable()
    {
        PlayerController.Damaged += OnPlayerDamaged;
    }

    private void OnDisable()
    {
        PlayerController.Damaged -= OnPlayerDamaged;
    }

    // 回している最中にプレイヤーがダメージを受けたら、その回転は即中止（賭け金は戻らない・配当もなし）
    private void OnPlayerDamaged()
    {
        if (!isPlaying) return;

        StopAllCoroutines();
        AbortVisual();

        if (playerMovement != null) playerMovement.UnlockControl();

        isPlaying = false;
        GameplaySync.NotifySlotEnd(this, true);
        Debug.Log(resultSettled
            ? "[スロット] 攻撃を受けたため、当たりの演出を中止しました（配当は受け取り済みです）。"
            : "[スロット] 攻撃を受けたため、回転を中止しました（賭け金は戻りません）。");
    }

    /// <summary>リールの回転・本体の揺れ・レバーを止めて、元の見た目に戻す（他プレイヤーの画面でも中止の通知で呼ばれる）</summary>
    public void AbortVisual()
    {
        // 自分が回していなければ、他の人の回転の再生もここで止める
        if (!isPlaying) StopAllCoroutines();
        remoteRollCoroutine = null;
        remoteBusy = false;
        remoteRolling = false;
        pendingWin = null;
        shakeCoroutine = null;
        punchCoroutine = null;
        StopCelebration();

        if (reels != null)
        {
            float stopOffset = GetTargetStopOffset();
            foreach (SlotReelScroller reel in reels)
            {
                if (reel != null) reel.Halt(stopOffset);
            }
        }

        if (machineBodyTransform != null)
        {
            machineBodyTransform.localPosition = bodyInitialLocalPos;
            machineBodyTransform.localScale = bodyInitialLocalScale;
        }
        if (leverTransform != null) leverTransform.localRotation = leverInitialLocalRot;
    }

    private void OnDestroy()
    {
        if (isPlaying && playerMovement != null)
        {
            playerMovement.UnlockControl();
        }

        if (winEffects != null) Destroy(winEffects.gameObject);

        if (runtimeTextures == null) return;
        for (int i = 0; i < runtimeTextures.Length; i++)
        {
            if (runtimeTextures[i] != null) Destroy(runtimeTextures[i]);
        }
    }

    /// <summary>
    /// Blender等でPivotがズレていても正確なメッシュ中心座標を返す
    /// </summary>
    private Vector3 GetVisualCenter(Transform t)
    {
        if (t == null) return transform.position;
        Renderer r = t.GetComponent<Renderer>();
        if (r != null) return r.bounds.center;
        Collider c = t.GetComponent<Collider>();
        if (c != null) return c.bounds.center;
        return t.position;
    }

    /// <summary>
    /// スロット画面の中央座標と、スロット正面の立ち位置座標を計算する
    /// </summary>
    private void CalculateSlotStandAndLookPositions(out Vector3 standPos, out Vector3 lookPos)
    {
        // 1. 注視点（中央リールのメッシュ中心 + 高さオフセット）
        Transform centerReel = (reels != null && reels.Length > 0 && reels[reels.Length / 2] != null)
            ? reels[reels.Length / 2].transform
            : transform;

        Vector3 reelCenter = GetVisualCenter(centerReel);
        lookPos = reelCenter + Vector3.up * lookHeightOffset;

        // 2. 立ち位置（playerStandPoint が指定されていればそこを使用し、未指定なら正面方向を自動計算）
        if (playerStandPoint != null)
        {
            standPos = playerStandPoint.position;
            return;
        }

        Vector3 frontNormal = transform.forward;
        if (reels != null && reels.Length >= 2 && reels[0] != null && reels[reels.Length - 1] != null)
        {
            Vector3 leftPos = GetVisualCenter(reels[0].transform);
            Vector3 rightPos = GetVisualCenter(reels[reels.Length - 1].transform);
            Vector3 reelRight = rightPos - leftPos;
            reelRight.y = 0f;

            if (reelRight.sqrMagnitude > 0.0001f)
            {
                // 左リール→右リールのベクトルに直交する水平ベクトル＝スロット画面の正面軸
                frontNormal = Vector3.Cross(reelRight.normalized, Vector3.up).normalized;

                if (playerMovement != null)
                {
                    Vector3 toPlayer = playerMovement.transform.position - reelCenter;
                    toPlayer.y = 0f;
                    if (Vector3.Dot(frontNormal, toPlayer) < 0f)
                    {
                        frontNormal = -frontNormal;
                    }
                }
            }
        }

        float playerY = playerMovement != null ? playerMovement.transform.position.y : transform.position.y;
        standPos = new Vector3(reelCenter.x, playerY, reelCenter.z) + frontNormal * standDistance;
    }

    private TMPro.TextMeshPro rarityLabel;

    public float RarityMultiplier => slotRarityMultiplier;

    /// <summary>この台の倍率を設定し、台の上に「×倍率」を表示する（高倍率ほど目立つ色）</summary>
    public void SetRarityMultiplier(float multiplier)
    {
        slotRarityMultiplier = Mathf.Max(0.1f, multiplier);

        if (rarityLabel == null)
        {
            GameObject labelObj = new GameObject("RarityLabel");
            labelObj.transform.SetParent(transform, false);
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r is TMPro.TMP_SubMeshUI || r.gameObject == labelObj) continue;
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            labelObj.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.45f, bounds.center.z); // 下に当たりの掲示板が出る分、高くする

            rarityLabel = labelObj.AddComponent<TMPro.TextMeshPro>();
            rarityLabel.alignment = TMPro.TextAlignmentOptions.Center;
            rarityLabel.fontSize = 4f;
            rarityLabel.fontStyle = TMPro.FontStyles.Bold;
        }

        rarityLabel.text = $"×{slotRarityMultiplier:0.#}";
        rarityLabel.color = slotRarityMultiplier >= 10f ? new Color(1f, 0.3f, 0.9f)
            : slotRarityMultiplier >= 5f ? new Color(1f, 0.55f, 0.1f)
            : slotRarityMultiplier >= 2f ? new Color(1f, 0.9f, 0.2f)
            : slotRarityMultiplier >= 1f ? Color.white
            : new Color(0.65f, 0.65f, 0.65f);
    }

    private void LateUpdate()
    {
        // 倍率表示は常にカメラの方を向ける
        if (rarityLabel != null && Camera.main != null)
        {
            rarityLabel.transform.rotation = Quaternion.LookRotation(
                rarityLabel.transform.position - Camera.main.transform.position);
        }
    }

    private void RecalculateWinRates()
    {
        if (symbols == null || symbols.Count == 0) return;

        float totalWinPool = Mathf.Clamp(100f - loseRate, 0f, 100f);
        if (totalWinPool <= 0f)
        {
            for (int i = 0; i < symbols.Count; i++) symbols[i].winRate = 0f;
            return;
        }

        float totalWeight = 0f;
        float[] weights = new float[symbols.Count];

        for (int i = 0; i < symbols.Count; i++)
        {
            float mult = Mathf.Max(0.1f, symbols[i].symbolMultiplier);
            weights[i] = 1f / (Mathf.Pow(mult, 1.55f) + 2.0f);
            totalWeight += weights[i];
        }

        for (int i = 0; i < symbols.Count; i++)
        {
            float rawRate = (weights[i] / totalWeight) * totalWinPool;
            symbols[i].winRate = Mathf.Round(rawRate * 100f) / 100f;
        }
    }

    public bool HasEnoughMoney(int amount)
    {
        return playerController != null && playerController.PlayerMoney >= amount;
    }

    private int RollOutcomeIndex()
    {
        RecalculateWinRates();

        float totalWeight = Mathf.Max(0f, loseRate);
        for (int i = 0; i < symbols.Count; i++)
        {
            totalWeight += Mathf.Max(0f, symbols[i].winRate);
        }

        if (totalWeight <= 0f) return -1;

        float roll = Random.Range(0f, totalWeight);
        if (roll < loseRate) return -1;

        float cumulative = loseRate;
        for (int i = 0; i < symbols.Count; i++)
        {
            cumulative += Mathf.Max(0f, symbols[i].winRate);
            if (roll <= cumulative) return i;
        }

        return symbols.Count - 1;
    }

    private int GetRandomReelSymbolIndex()
    {
        if (symbols == null || symbols.Count == 0) return 0;

        float totalSymbolWeight = 0f;
        for (int i = 0; i < symbols.Count; i++)
        {
            totalSymbolWeight += Mathf.Max(0f, symbols[i].winRate);
        }

        if (totalSymbolWeight <= 0f) return Random.Range(0, symbols.Count);

        float roll = Random.Range(0f, totalSymbolWeight);
        float cumulative = 0f;

        for (int i = 0; i < symbols.Count; i++)
        {
            cumulative += Mathf.Max(0f, symbols[i].winRate);
            if (roll <= cumulative) return i;
        }

        return symbols.Count - 1;
    }

    private void AssignReelResults(int winningSymbolIndex)
    {
        if (winningSymbolIndex >= 0)
        {
            for (int i = 0; i < reels.Length; i++)
            {
                currentResults[i] = winningSymbolIndex;
            }
        }
        else
        {
            for (int i = 0; i < reels.Length; i++)
            {
                currentResults[i] = GetRandomReelSymbolIndex();
            }

            if (symbols.Count > 1 && IsAllMatched())
            {
                int lastReel = reels.Length - 1;
                int offset = Random.Range(1, symbols.Count);
                currentResults[lastReel] = (currentResults[0] + offset) % symbols.Count;
            }
        }
    }

    private float GetTilingY()
    {
        return WindowSymbolCount / StripCellCount;
    }

    private float GetTargetStopOffset()
    {
        float tilingY = GetTilingY();
        float offset = (0.5f / StripCellCount) - (0.5f * tilingY) + StopCenterOffset;
        return Mathf.Repeat(offset, 1f);
    }

    /// <summary>Start でリール帯の生成が終わり、演出を再生できる状態か</summary>
    public bool IsVisualReady => reels != null && currentResults != null && symbols.Count > 0;

    /// <summary>
    /// タイトル画面などの飾り用。賭け金・プレイヤー・同期なしで、実際のプレイと同じ回転演出
    /// （レバー → 回転・ガタガタ → 左から順に停止 → 当たりなら本体がボコッと膨らむ）を1回だけ再生する。
    /// </summary>
    public IEnumerator PlayDemoRoll()
    {
        if (isPlaying || !IsVisualReady) yield break;

        isPlaying = true;
        int winningIndex = RollOutcomeIndex();
        AssignReelResults(winningIndex);
        yield return StartCoroutine(RollVisual(GetTargetStopOffset()));

        if (winningIndex >= 0 && symbols[winningIndex].effectType == SlotSymbolEffect.Payout && machineBodyTransform != null)
        {
            yield return StartCoroutine(PunchMachineScale(0.08f, 0.25f));
        }
        isPlaying = false;
    }

    /// <summary>cost: 実際に払う額（正気度が低いと上がる）/ payoutBase: 配当の基準の額（上がる前の掛け金）</summary>
    public void OnPressBetButton(int cost, int payoutBase)
    {
        if (IsPlaying || symbols.Count == 0 || !HasEnoughMoney(cost)) return;
        StartCoroutine(PlaySlotSequence(cost, payoutBase));
    }

    /// <summary>
    /// currentResults の出目でリール帯を作り、レバー・回転・ガタガタ・左から順の停止までを演出する
    /// </summary>
    private IEnumerator RollVisual(float targetOffset, SlotWinEffects sound = null)
    {
        if (sound != null) sound.StartSpin();

        if (leverTransform != null)
        {
            StartCoroutine(AnimateLeverPull());
        }

        for (int i = 0; i < reels.Length; i++)
        {
            GenerateReelStrip(i, currentResults[i]);
        }

        // リール回転開始 ＆ ガチャ風の本体ガタガタ振動スタート
        foreach (var reel in reels)
        {
            reel.StartRolling(3.5f);
        }

        shakeCoroutine = null;
        if (machineBodyTransform != null)
        {
            shakeCoroutine = StartCoroutine(ShakeMachineWhileRolling());
        }

        yield return new WaitForSeconds(1.0f);

        // 左から順に停止
        for (int i = 0; i < reels.Length; i++)
        {
            yield return StartCoroutine(reels[i].StopAt(targetOffset, 0.6f));
            if (sound != null) sound.ReelStopped(i, CountMatched(i));
            if (machineBodyTransform != null)
            {
                StartCoroutine(PunchMachineScale(0.03f, 0.12f));
            }
            if (i < reels.Length - 1) yield return new WaitForSeconds(0.2f);
        }

        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            machineBodyTransform.localPosition = bodyInitialLocalPos;
        }
    }

    /// <summary>左のリールからここまで同じ目が並んでいれば、その数（並んでいなければ 0）</summary>
    private int CountMatched(int reelIndex)
    {
        for (int i = 1; i <= reelIndex; i++)
        {
            if (currentResults[i] != currentResults[0]) return 0;
        }
        return reelIndex + 1;
    }

    /// <summary>他プレイヤーが当てたときの演出（台の音と画面は、周りの人にも見える）</summary>
    public void PlayRemoteWin(SlotWinTier tier, int reward, float symbolMultiplier, float buffMultiplier)
    {
        if (isPlaying || !IsVisualReady) return;

        SlotWinInfo info = new SlotWinInfo
        {
            tier = tier,
            reward = reward,
            symbolMultiplier = symbolMultiplier,
            machineMultiplier = slotRarityMultiplier,
            buffMultiplier = buffMultiplier
        };

        // こちらの画面のリールがまだ回っていれば、止まってから見せる
        if (remoteRolling) pendingWin = info;
        else Celebrate(info, false);
    }

    private IEnumerator RemoteRoll()
    {
        remoteRolling = true;
        SlotWinEffects effects = EnsureEffects();
        yield return RollVisual(GetTargetStopOffset(), effects);
        remoteRolling = false;

        if (pendingWin.HasValue)
        {
            Celebrate(pendingWin.Value, false);
            pendingWin = null;
        }
        else if (!IsAllMatched() && effects != null)
        {
            effects.PlayLose();
        }
    }

    /// <summary>当たりの演出を始める。戻り値は、金額が出るまでの時間（秒）</summary>
    private float Celebrate(SlotWinInfo info, bool shakeCamera)
    {
        SlotWinEffects effects = EnsureEffects();
        if (effects == null)
        {
            if (info.tier != SlotWinTier.Reroll) Punch(0.08f);
            return 0.25f;
        }

        effects.PlayWin(info, shakeCamera, Punch);
        return effects.GetRevealTime(info);
    }

    /// <summary>台を一度はねさせる</summary>
    private void Punch(float amount)
    {
        if (machineBodyTransform == null) return;
        if (punchCoroutine != null) StopCoroutine(punchCoroutine);
        punchCoroutine = StartCoroutine(PunchMachineScale(amount, 0.22f));
    }

    /// <param name="keepStartSound">ボタンを押したときの音は止めない</param>
    private void StopCelebration(bool keepStartSound = false)
    {
        if (punchCoroutine != null)
        {
            StopCoroutine(punchCoroutine);
            punchCoroutine = null;
            if (machineBodyTransform != null) machineBodyTransform.localScale = bodyInitialLocalScale;
        }

        if (winEffects == null) return;
        if (keepStartSound) winEffects.Clear();
        else winEffects.Stop();
    }

    /// <summary>他の人がボタンを押した。同じ音を鳴らす</summary>
    public void PlayRemoteStart()
    {
        if (isPlaying || !IsVisualReady) return;

        StopCelebration();
        SlotWinEffects effects = EnsureEffects();
        if (effects != null) effects.PlayStart();
    }

    /// <summary>音と演出の部品（オフにしているときは null）。台が揺れる前に呼んで、位置を測る</summary>
    private SlotWinEffects EnsureEffects()
    {
        if (!celebration.enabled || reels == null || reels.Length == 0) return null;
        if (winEffects == null)
        {
            winEffects = new GameObject("SlotWinEffects").AddComponent<SlotWinEffects>();
            winEffects.Setup(MeasureAnchors(), reels, celebration);
        }
        return winEffects;
    }

    /// <summary>演出を置く場所：リールの窓の中心と大きさ、台の正面の向き、台の下端と上端</summary>
    private SlotAnchors MeasureAnchors()
    {
        SlotAnchors anchors = new SlotAnchors();
        Vector3 first = GetVisualCenter(reels[0].transform);
        Vector3 last = GetVisualCenter(reels[reels.Length - 1].transform);
        anchors.center = (first + last) * 0.5f;

        // 正面は、ボタンがある側（どのPCでも同じ向きになる）
        Vector3 toButtons = Vector3.zero;
        foreach (SlotButtonInteractable button in GetComponentsInChildren<SlotButtonInteractable>())
        {
            toButtons += GetVisualCenter(button.transform) - anchors.center;
        }
        toButtons.y = 0f;

        Vector3 front = transform.forward;
        Vector3 along = last - first;
        along.y = 0f;
        if (along.sqrMagnitude > 0.0001f) front = Vector3.Cross(along.normalized, Vector3.up);
        if (toButtons.sqrMagnitude > 0.0001f && Vector3.Dot(front, toButtons) < 0f) front = -front;
        front.y = 0f;
        anchors.front = front.normalized;

        Vector3 right = Vector3.Cross(Vector3.up, -anchors.front); // 正面から見て右
        anchors.reelOffsets = new float[reels.Length];
        for (int i = 0; i < reels.Length; i++)
        {
            anchors.reelOffsets[i] = Vector3.Dot(GetVisualCenter(reels[i].transform) - anchors.center, right);
        }

        Renderer middle = reels[reels.Length / 2].GetComponent<Renderer>();
        Bounds reelBounds = middle != null ? middle.bounds : new Bounds(anchors.center, new Vector3(0.4f, 0.7f, 0.05f));
        float spacing = reels.Length >= 2 ? Mathf.Abs(anchors.reelOffsets[1] - anchors.reelOffsets[0]) : reelBounds.size.x;
        anchors.windowWidth = spacing * reels.Length;
        anchors.windowHeight = reelBounds.size.y;
        // 窓には縦に 2 つ分の目が見える。目は横長に表示されるので、枠はリールの幅に合わせる
        anchors.frameWidth = spacing * 0.97f;
        anchors.frameHeight = reelBounds.size.y * 0.44f;
        anchors.symbolY = -StopCenterOffset / GetTilingY() * reelBounds.size.y; // 目は窓の中心より少し下で止まる
        anchors.face = Vector3.Dot(reelBounds.center - anchors.center, anchors.front)
            + Mathf.Abs(anchors.front.x) * reelBounds.extents.x + Mathf.Abs(anchors.front.z) * reelBounds.extents.z;

        anchors.floorY = anchors.center.y - 1f;
        anchors.topY = anchors.center.y + 0.45f;
        bool found = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null) continue;
            anchors.floorY = found ? Mathf.Min(anchors.floorY, r.bounds.min.y) : r.bounds.min.y;
            anchors.topY = found ? Mathf.Max(anchors.topY, r.bounds.max.y) : r.bounds.max.y;
            found = true;
        }
        return anchors;
    }

    // 演出の確認用：再生中に、このコンポーネント右上の ︙ メニューから選ぶ（お金は動かない）
    [ContextMenu("演出テスト/ハズレ（再生中のみ）")]
    private void TestLose() => StartTest(null);

    [ContextMenu("演出テスト/WIN（再生中のみ）")]
    private void TestWin() => StartTest(SlotWinTier.Win);

    [ContextMenu("演出テスト/BIG WIN（再生中のみ）")]
    private void TestBigWin() => StartTest(SlotWinTier.BigWin);

    [ContextMenu("演出テスト/JACKPOT（再生中のみ）")]
    private void TestJackpot() => StartTest(SlotWinTier.Jackpot);

    [ContextMenu("演出テスト/再ロール（再生中のみ）")]
    private void TestReroll() => StartTest(SlotWinTier.Reroll);

    private void StartTest(SlotWinTier? tier)
    {
        if (!Application.isPlaying || IsPlaying || !IsVisualReady) return;
        StartCoroutine(TestSpin(tier));
    }

    private IEnumerator TestSpin(SlotWinTier? tier)
    {
        isPlaying = true;
        resultSettled = true;
        StopCelebration();

        // 見せたい当たりに合う目を選ぶ（WIN は低い倍率、BIG WIN は中くらい、JACKPOT は最高）
        int index = -1;
        if (tier.HasValue)
        {
            float wanted = tier == SlotWinTier.Jackpot ? 1000f : tier == SlotWinTier.BigWin ? 3f : 0f;
            for (int i = 0; i < symbols.Count; i++)
            {
                bool reroll = symbols[i].effectType == SlotSymbolEffect.Reroll;
                if (reroll != (tier == SlotWinTier.Reroll)) continue;
                if (index < 0 || Mathf.Abs(symbols[i].symbolMultiplier - wanted) < Mathf.Abs(symbols[index].symbolMultiplier - wanted)) index = i;
            }
        }
        AssignReelResults(index);

        SlotWinEffects effects = EnsureEffects();
        if (effects != null)
        {
            effects.PlayStart();
            yield return new WaitForSeconds(SlotWinEffects.StartWait);
        }
        yield return RollVisual(GetTargetStopOffset(), effects);

        if (index < 0)
        {
            if (effects != null) effects.PlayLose();
        }
        else
        {
            float multiplier = symbols[index].symbolMultiplier;
            SlotWinInfo info = new SlotWinInfo
            {
                tier = tier.Value,
                reward = tier == SlotWinTier.Reroll ? 0 : Mathf.RoundToInt(1000f * slotRarityMultiplier * multiplier),
                symbolMultiplier = multiplier,
                machineMultiplier = slotRarityMultiplier,
                buffMultiplier = 1f
            };
            yield return new WaitForSeconds(Celebrate(info, true) + 0.2f);
        }
        isPlaying = false;
    }

    private IEnumerator PlaySlotSequence(int cost, int betAmount)
    {
        isPlaying = true;
        resultSettled = false;
        StopCelebration();
        GameplaySync.NotifySlotBegin(this);

        if (playerController != null)
        {
            playerController.AddMoney(-cost);
        }

        // ボタンを押した音（てっててっててん）
        float startWait = 0f;
        SlotWinEffects starter = EnsureEffects();
        if (starter != null)
        {
            starter.PlayStart();
            startWait = SlotWinEffects.StartWait;
        }

        // ★ スロット正面の立ち位置と注視点を算出し、プレイヤーを滑らかに移動＆カメラ固定
        if (playerMovement != null)
        {
            CalculateSlotStandAndLookPositions(out Vector3 standPos, out Vector3 lookPos);
            playerMovement.LockControlAndMoveTo(standPos, lookPos, moveAndLookDuration);
            startWait = Mathf.Max(startWait, moveAndLookDuration);
        }
        if (startWait > 0f) yield return new WaitForSeconds(startWait);

        float targetOffset = GetTargetStopOffset();
        bool needsRoll = true;

        while (needsRoll)
        {
            needsRoll = false;

            // 1. 内部抽選を実行し、各リールの停止目を決める（他プレイヤーにも同じ出目で演出を見せる）
            int winningIndex = RollOutcomeIndex();
            AssignReelResults(winningIndex);
            GameplaySync.NotifySlotRoll(this, currentResults);

            // 2. リール帯の生成 → 回転 → 左から順に停止
            SlotWinEffects effects = EnsureEffects();
            yield return StartCoroutine(RollVisual(targetOffset, effects));

            // 4. 結果判定と配当・効果の実行
            if (winningIndex >= 0)
            {
                SlotSymbolData winSymbol = symbols[winningIndex];

                if (winSymbol.effectType == SlotSymbolEffect.Reroll)
                {
                    Debug.Log("[ REROLL! ] 再ロールが揃いました！もう一度回します！");
                    needsRoll = true;
                    SlotWinInfo again = new SlotWinInfo { tier = SlotWinTier.Reroll, buffMultiplier = 1f };
                    float reveal = Celebrate(again, false);
                    GameplaySync.NotifySlotWin(this, again);
                    yield return new WaitForSeconds(reveal + 0.5f);
                    StopCelebration();
                }
                else
                {
                    // ドーパミンが多いと、獲得額が増える。勝ちはドーパミンを増やす
                    float buff = playerController != null ? playerController.GamblePayoutMultiplier : 1f;
                    int reward = Mathf.RoundToInt(betAmount * slotRarityMultiplier * winSymbol.symbolMultiplier * buff);
                    if (playerController != null) playerController.OnGambleResult(true, winSymbol.symbolMultiplier);
                    if (reward > 0 && playerController != null)
                    {
                        playerController.AddMoney(reward);
                    }

                    Debug.Log($"[ 当選: 出目{winSymbol.symbolName} ({winSymbol.winRate}%) ] 賭け金({betAmount}) × 台倍率({slotRarityMultiplier}x) × 出目倍率({winSymbol.symbolMultiplier}x) = {reward}円獲得！");

                    resultSettled = true;
                    SlotWinInfo info = new SlotWinInfo
                    {
                        tier = celebration.GetTier(slotRarityMultiplier, winSymbol.symbolMultiplier),
                        reward = reward,
                        symbolMultiplier = winSymbol.symbolMultiplier,
                        machineMultiplier = slotRarityMultiplier,
                        buffMultiplier = buff
                    };
                    float wait = Celebrate(info, true);
                    GameplaySync.NotifySlotWin(this, info);
                    yield return new WaitForSeconds(wait + (celebration.enabled ? celebration.GetLinger(info.tier) : 0f));
                }
            }
            else
            {
                if (playerController != null) playerController.OnGambleResult(false); // 負けはドーパミンが減る
                Debug.Log("[ ハズレ ] 0円");
                if (effects != null) effects.PlayLose();
            }
        }

        // 結果確認の余韻のあと、プレイヤーの移動＆視点ロックを解除（当たりは、演出の中で待った）
        yield return new WaitForSeconds(resultSettled ? 0.1f : 0.8f);

        if (playerMovement != null)
        {
            playerMovement.UnlockControl();
        }

        isPlaying = false;
        GameplaySync.NotifySlotEnd(this);
    }

    private IEnumerator ShakeMachineWhileRolling()
    {
        while (true)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-rollShakeIntensity, rollShakeIntensity),
                Random.Range(-rollShakeIntensity, rollShakeIntensity),
                Random.Range(-rollShakeIntensity, rollShakeIntensity)
            );
            machineBodyTransform.localPosition = bodyInitialLocalPos + randomOffset;
            yield return new WaitForSeconds(0.03f);
        }
    }

    private IEnumerator PunchMachineScale(float punchAmount, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Sin((elapsed / duration) * Mathf.PI) * punchAmount;
            machineBodyTransform.localScale = bodyInitialLocalScale + new Vector3(s, -s * 0.5f, s);
            yield return null;
        }
        machineBodyTransform.localScale = bodyInitialLocalScale;
    }

    private IEnumerator AnimateLeverPull()
    {
        Quaternion pulledRot = leverInitialLocalRot * Quaternion.Euler(45f, 0f, 0f);
        float t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            leverTransform.localRotation = Quaternion.Slerp(leverInitialLocalRot, pulledRot, t / 0.12f);
            yield return null;
        }
        t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            leverTransform.localRotation = Quaternion.Slerp(pulledRot, leverInitialLocalRot, t / 0.25f);
            yield return null;
        }
        leverTransform.localRotation = leverInitialLocalRot;
    }

    private bool IsAllMatched()
    {
        if (currentResults == null || currentResults.Length == 0) return false;
        int first = currentResults[0];
        for (int i = 1; i < currentResults.Length; i++)
        {
            if (currentResults[i] != first) return false;
        }
        return true;
    }

    private void GenerateReelStrip(int reelIndex, int stopSymbolIndex)
    {
        int texWidth = CellResolution;
        int texHeight = CellResolution * StripCellCount;

        if (runtimeTextures[reelIndex] != null)
        {
            Destroy(runtimeTextures[reelIndex]);
        }

        Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };

        Color32[] pixels = new Color32[texWidth * texHeight];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = ReelBgColor;

        for (int s = 0; s < StripCellCount; s++)
        {
            int symIdx = (s == 0) ? stopSymbolIndex : GetRandomReelSymbolIndex();

            Sprite sp = symbols[symIdx].sprite;
            if (sp != null)
            {
                DrawSpriteIntoVerticalCell(pixels, texWidth, texHeight, sp, s * CellResolution);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
        runtimeTextures[reelIndex] = tex;

        reels[reelIndex].SetReelTexture(tex, GetTilingY());
    }

    private void DrawSpriteIntoVerticalCell(Color32[] buffer, int texWidth, int texHeight, Sprite sprite, int cellStartY)
    {
        Texture2D srcTex = sprite.texture;
        if (srcTex == null || !srcTex.isReadable) return;

        Rect rect = sprite.textureRect;
        int srcXMin = Mathf.RoundToInt(rect.x);
        int srcYMin = Mathf.RoundToInt(rect.y);
        int srcW = Mathf.RoundToInt(rect.width);
        int srcH = Mathf.RoundToInt(rect.height);
        if (srcW <= 0 || srcH <= 0) return;

        const float usableRatio = 1.0f - (SymbolPadding * 2f);
        const float maxDrawSize = CellResolution * usableRatio * SymbolScale;

        float scale = Mathf.Min(maxDrawSize / srcW, maxDrawSize / srcH);
        int targetW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        int targetH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

        int offsetX = (CellResolution - targetW) / 2;
        int offsetY = cellStartY + (CellResolution - targetH) / 2;

        for (int y = 0; y < targetH; y++)
        {
            int dstY = offsetY + y;
            if (dstY < 0 || dstY >= texHeight) continue;

            float v = (y + 0.5f) / targetH;
            int sampleY = Mathf.Clamp(srcYMin + Mathf.FloorToInt(v * srcH), srcYMin, srcYMin + srcH - 1);

            for (int x = 0; x < targetW; x++)
            {
                int dstX = offsetX + x;
                if (dstX < 0 || dstX >= texWidth) continue;

                float u = (x + 0.5f) / targetW;
                int sampleX = Mathf.Clamp(srcXMin + Mathf.FloorToInt(u * srcW), srcXMin, srcXMin + srcH - 1);

                Color32 c = srcTex.GetPixel(sampleX, sampleY);
                if (c.a > 12)
                {
                    buffer[dstY * texWidth + dstX] = c;
                }
            }
        }
    }
}