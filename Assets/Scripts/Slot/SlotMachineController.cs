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

    public bool IsPlaying => isPlaying;

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

        if (machineBodyTransform != null)
        {
            bodyInitialLocalPos = machineBodyTransform.localPosition;
            bodyInitialLocalScale = machineBodyTransform.localScale;
        }

        if (leverTransform != null)
        {
            leverInitialLocalRot = leverTransform.localRotation;
        }

        RecalculateWinRates();
    }

    private void Start()
    {
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

        if (!Application.isPlaying || reels == null || isPlaying) return;

        float stopOffset = GetTargetStopOffset();
        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] != null)
            {
                reels[i].SetOffset(stopOffset);
            }
        }
    }

    private void OnDestroy()
    {
        if (isPlaying && playerMovement != null)
        {
            playerMovement.UnlockControl();
        }

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

    public void OnPressBetButton(int betAmount)
    {
        if (isPlaying || symbols.Count == 0 || !HasEnoughMoney(betAmount)) return;
        StartCoroutine(PlaySlotSequence(betAmount));
    }

    private IEnumerator PlaySlotSequence(int betAmount)
    {
        isPlaying = true;

        if (playerController != null)
        {
            playerController.AddMoney(-betAmount);
        }

        // ★ スロット正面の立ち位置と注視点を算出し、プレイヤーを滑らかに移動＆カメラ固定
        if (playerMovement != null)
        {
            CalculateSlotStandAndLookPositions(out Vector3 standPos, out Vector3 lookPos);
            playerMovement.LockControlAndMoveTo(standPos, lookPos, moveAndLookDuration);
            yield return new WaitForSeconds(moveAndLookDuration);
        }

        float targetOffset = GetTargetStopOffset();
        bool needsRoll = true;

        while (needsRoll)
        {
            needsRoll = false;

            if (leverTransform != null)
            {
                StartCoroutine(AnimateLeverPull());
            }

            // 1. 内部抽選を実行し、各リールの停止目とリール帯テクスチャを生成
            int winningIndex = RollOutcomeIndex();
            AssignReelResults(winningIndex);

            for (int i = 0; i < reels.Length; i++)
            {
                GenerateReelStrip(i, currentResults[i]);
            }

            // 2. リール回転開始 ＆ ガチャ風の本体ガタガタ振動スタート
            foreach (var reel in reels)
            {
                reel.StartRolling(3.5f);
            }

            Coroutine shakeCoroutine = null;
            if (machineBodyTransform != null)
            {
                shakeCoroutine = StartCoroutine(ShakeMachineWhileRolling());
            }

            yield return new WaitForSeconds(1.0f);

            // 3. 左から順に停止
            for (int i = 0; i < reels.Length; i++)
            {
                yield return StartCoroutine(reels[i].StopAt(targetOffset, 0.6f));
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

            // 4. 結果判定と配当・効果の実行
            if (winningIndex >= 0)
            {
                SlotSymbolData winSymbol = symbols[winningIndex];

                if (winSymbol.effectType == SlotSymbolEffect.Reroll)
                {
                    Debug.Log("[ REROLL! ] 再ロールが揃いました！もう一度回します！");
                    needsRoll = true;
                    yield return new WaitForSeconds(0.6f);
                }
                else
                {
                    int reward = Mathf.RoundToInt(betAmount * slotRarityMultiplier * winSymbol.symbolMultiplier);
                    if (reward > 0 && playerController != null)
                    {
                        playerController.AddMoney(reward);
                    }

                    if (machineBodyTransform != null)
                    {
                        yield return StartCoroutine(PunchMachineScale(0.08f, 0.25f));
                    }

                    Debug.Log($"[ 当選: 出目{winSymbol.symbolName} ({winSymbol.winRate}%) ] 賭け金({betAmount}) × 台倍率({slotRarityMultiplier}x) × 出目倍率({winSymbol.symbolMultiplier}x) = {reward}円獲得！");
                }
            }
            else
            {
                Debug.Log("[ ハズレ ] 0円");
            }
        }

        // 結果確認の余韻のあと、プレイヤーの移動＆視点ロックを解除
        yield return new WaitForSeconds(0.8f);

        if (playerMovement != null)
        {
            playerMovement.UnlockControl();
        }

        isPlaying = false;
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