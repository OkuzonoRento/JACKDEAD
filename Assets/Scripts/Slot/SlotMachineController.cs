using System.Collections;
using UnityEngine;

public class SlotMachineController : MonoBehaviour
{
    [Header("カメラ・視点設定")]
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private float slotCameraPitch = 45f;
    [SerializeField] private float cameraTransitionSpeed = 10f;

    [Header("リール設定 (左・中・右の3列)")]
    [SerializeField] private SlotReelScroller[] reels;

    [Header("出目の図柄リスト (後から何個でも追加OK！)")]
    [SerializeField] private Sprite[] symbolSprites;

    [Header("リールの見た目・位置調整")]
    [SerializeField] private int stripLength = 6;
    [SerializeField] private float windowSymbolCount = 2.4f;
    [Range(0f, 1f)]
    [SerializeField] private float centerFineTune = 0.0f;
    [SerializeField] private bool rotateSprite90 = true;

    [Header("ゲーム設定（配当倍率）")]
    [SerializeField] private float payoutMultiplier = 10.0f;

    [Header("プレイヤー参照")]
    [SerializeField] private PlayerController playerController;

    private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    private Texture2D[] runtimeTextures;

    // 仮カラーブロック用の固定カラー配列（順番を完全統一）
    private readonly Color[] fallbackColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f), // 0: 赤(Red)
        new Color(0.2f, 0.8f, 0.3f), // 1: 緑(Green)
        new Color(0.9f, 0.8f, 0.1f), // 2: 黄(Yellow)
        new Color(0.6f, 0.2f, 0.9f), // 3: 紫(Purple)
        new Color(0.2f, 0.6f, 0.9f)  // 4: 青(Blue)
    };

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }
    }

    private void Start()
    {
        runtimeTextures = new Texture2D[reels.Length];
        for (int i = 0; i < reels.Length; i++)
        {
            UpdateReelStrip(i, Random.Range(0, GetTotalSymbols()));
            reels[i].GetComponent<Renderer>().material.mainTextureOffset = new Vector2(GetTargetStopOffset(), 0f);
        }
    }

    public bool HasEnoughMoney(int amount)
    {
        if (playerController != null)
        {
            return playerController.PlayerMoney >= amount;
        }
        return false;
    }

    private int GetTotalSymbols()
    {
        return (symbolSprites != null && symbolSprites.Length > 0) ? symbolSprites.Length : fallbackColors.Length;
    }

    private float GetTargetStopOffset()
    {
        float tilingX = (float)stripLength / windowSymbolCount;
        float offset = (0.5f / stripLength) - (0.5f * tilingX) + centerFineTune;
        return Mathf.Repeat(offset, 1f);
    }

    public void OnPressBetButton(int betAmount)
    {
        if (isPlaying) return;
        if (!HasEnoughMoney(betAmount))
        {
            Debug.Log("[カジノ] 所持金が足りません！");
            return;
        }

        StartCoroutine(PlaySlotSequence(betAmount));
    }

    private IEnumerator PlaySlotSequence(int betAmount)
    {
        isPlaying = true;

        if (playerController != null)
        {
            playerController.AddMoney(-betAmount);
            Debug.Log($"[カジノ] {betAmount}円を賭けました。（残り所持金: {playerController.PlayerMoney}円）");
        }

        int totalSymbols = GetTotalSymbols();

        int resultLeft = Random.Range(0, totalSymbols);
        int resultCenter = Random.Range(0, totalSymbols);
        int resultRight = Random.Range(0, totalSymbols);
        int[] results = { resultLeft, resultCenter, resultRight };

        Debug.Log("==============================\n" +
                  "【 抽選結果 (中段1ライン) 】\n" +
                  $"  [左リール] : {GetSymbolName(resultLeft)}\n" +
                  $"  [中リール] : {GetSymbolName(resultCenter)}\n" +
                  $"  [右リール] : {GetSymbolName(resultRight)}\n" +
                  "==============================");

        for (int i = 0; i < reels.Length; i++)
        {
            UpdateReelStrip(i, results[i]);
        }

        float elapsed = 0f;
        Quaternion startRot = playerCameraTransform.localRotation;
        Quaternion targetRot = Quaternion.Euler(slotCameraPitch, startRot.eulerAngles.y, 0f);

        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            playerCameraTransform.localRotation = Quaternion.Slerp(startRot, targetRot, elapsed * cameraTransitionSpeed);
            yield return null;
        }
        playerCameraTransform.localRotation = targetRot;

        foreach (var reel in reels)
        {
            reel.StartRolling(8f);
        }

        yield return new WaitForSeconds(1.2f);

        float targetOffset = GetTargetStopOffset();

        yield return StartCoroutine(reels[0].StopAt(targetOffset, 0.7f));
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(reels[1].StopAt(targetOffset, 0.7f));
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(reels[2].StopAt(targetOffset, 0.8f));

        bool isWin = (resultLeft == resultCenter && resultCenter == resultRight);
        if (isWin)
        {
            int reward = Mathf.RoundToInt(betAmount * payoutMultiplier);
            if (playerController != null)
            {
                playerController.AddMoney(reward);
            }
            Debug.Log($"[ JACKPOT! ] {GetSymbolName(resultLeft)} が揃いました！ ➔ {reward}円獲得！");
        }
        else
        {
            Debug.Log("[ ハズレ ] ざんねん！揃いませんでした。");
        }

        yield return new WaitForSeconds(1.5f);

        elapsed = 0f;
        startRot = playerCameraTransform.localRotation;
        targetRot = Quaternion.Euler(0f, startRot.eulerAngles.y, 0f);

        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            playerCameraTransform.localRotation = Quaternion.Slerp(startRot, targetRot, elapsed * cameraTransitionSpeed);
            yield return null;
        }
        playerCameraTransform.localRotation = targetRot;

        isPlaying = false;
    }

    private void UpdateReelStrip(int reelIndex, int winningSymbolIndex)
    {
        int cellWidth = 256;
        int cellHeight = 256;
        int texWidth = cellWidth * stripLength;
        int texHeight = cellHeight;

        if (runtimeTextures[reelIndex] != null)
        {
            Destroy(runtimeTextures[reelIndex]);
        }

        Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        Color bgColor = new Color(0.12f, 0.12f, 0.15f);
        Color[] bgPixels = new Color[texWidth * texHeight];
        for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = bgColor;
        tex.SetPixels(bgPixels);

        int totalSymbols = GetTotalSymbols();

        for (int s = 0; s < stripLength; s++)
        {
            int symIdx = (s == 0) ? winningSymbolIndex : Random.Range(0, totalSymbols);
            int startX = s * cellWidth;

            if (symbolSprites != null && symbolSprites.Length > 0 && symbolSprites[symIdx] != null)
            {
                DrawSpriteOnTexture(tex, symbolSprites[symIdx], startX, cellWidth, cellHeight);
            }
            else
            {
                DrawTempColorBlock(tex, startX, cellWidth, cellHeight, symIdx);
            }
        }

        tex.Apply();
        runtimeTextures[reelIndex] = tex;

        float tilingX = (float)stripLength / windowSymbolCount;
        reels[reelIndex].SetReelTexture(tex, tilingX);
    }

    private void DrawSpriteOnTexture(Texture2D targetTex, Sprite sprite, int startX, int width, int height)
    {
        Texture2D srcTex = sprite.texture;
        if (!srcTex.isReadable)
        {
            DrawTempColorBlock(targetTex, startX, width, height, 0);
            return;
        }

        Rect rect = sprite.textureRect;
        int margin = 16;

        for (int y = margin; y < height - margin; y++)
        {
            for (int x = margin; x < width - margin; x++)
            {
                float u = (float)(x - margin) / (width - margin * 2);
                float v = (float)(y - margin) / (height - margin * 2);

                float sampleU = rotateSprite90 ? v : u;
                float sampleV = rotateSprite90 ? (1f - u) : v;

                int srcX = Mathf.Clamp(Mathf.FloorToInt(rect.x + sampleU * rect.width), 0, srcTex.width - 1);
                int srcY = Mathf.Clamp(Mathf.FloorToInt(rect.y + sampleV * rect.height), 0, srcTex.height - 1);

                Color c = srcTex.GetPixel(srcX, srcY);
                if (c.a > 0.1f)
                {
                    targetTex.SetPixel(startX + x, y, c);
                }
            }
        }
    }

    private void DrawTempColorBlock(Texture2D tex, int startX, int width, int height, int symbolIndex)
    {
        int margin = 16;
        // フォールバックカラー配列から確実に対応する色を取得
        Color symColor = fallbackColors[symbolIndex % fallbackColors.Length];

        for (int y = margin; y < height - margin; y++)
        {
            for (int x = startX + margin; x < startX + width - margin; x++)
            {
                if (x <= startX + margin + 6 || x >= startX + width - margin - 6 || y <= margin + 6 || y >= height - margin - 6)
                    tex.SetPixel(x, y, Color.white);
                else
                    tex.SetPixel(x, y, symColor);
            }
        }
    }

    private string GetSymbolName(int index)
    {
        if (symbolSprites != null && index < symbolSprites.Length && symbolSprites[index] != null)
        {
            return symbolSprites[index].name;
        }
        // フォールバックカラーの順番と完全に一致させた名称リスト
        string[] names = { "赤(Red)", "緑(Green)", "黄(Yellow)", "紫(Purple)", "青(Blue)" };
        return names[index % names.Length];
    }
}