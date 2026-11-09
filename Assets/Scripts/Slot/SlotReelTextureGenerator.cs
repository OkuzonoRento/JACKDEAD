using UnityEngine;

public class SlotReelTextureGenerator : MonoBehaviour
{
    [Header("仮テクスチャ生成設定")]
    [SerializeField] private int textureWidth = 1024;  // 横幅
    [SerializeField] private int textureHeight = 256;  // 縦幅
    [SerializeField] private int symbolCount = 5;      // 図柄の種類数
    [SerializeField] private float windowSymbolCount = 3.0f; // 窓の中に並べる数

    [Header("表示位置の微調整")]
    [Range(0f, 1f)]
    [SerializeField] private float textureOffsetY = 0.0f; // ★ ここで縦の表示位置を微調整できます

    private void Start()
    {
        GenerateAndApplyTemporaryTexture();
    }

    private void GenerateAndApplyTemporaryTexture()
    {
        Texture2D tempTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        tempTexture.filterMode = FilterMode.Point;
        tempTexture.wrapMode = TextureWrapMode.Repeat;

        int symbolWidth = textureWidth / symbolCount;
        Color[] pixels = new Color[textureWidth * textureHeight];

        Color bgColor = new Color(0.12f, 0.12f, 0.15f);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = bgColor;
        }
        tempTexture.SetPixels(pixels);

        for (int s = 0; s < symbolCount; s++)
        {
            int startX = s * symbolWidth;
            DrawTemporarySymbol(tempTexture, startX, symbolWidth, s);
        }

        tempTexture.Apply();

        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = rend.material;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tempTexture);
            }
            else
            {
                mat.mainTexture = tempTexture;
            }

            float tilingX = (float)symbolCount / windowSymbolCount;
            mat.mainTextureScale = new Vector2(tilingX, 1f);

            // 縦方向の微調整オフセットを適用（X軸方向が回転軸）
            mat.mainTextureOffset = new Vector2(textureOffsetY, 0f);

            Debug.Log("[Slot] テクスチャを再生成し、オフセットを適用しました");
        }
    }

    private void DrawTemporarySymbol(Texture2D tex, int startX, int width, int symbolIndex)
    {
        int marginX = 10;
        int marginY = 10;
        Color symbolColor = GetSymbolColor(symbolIndex);
        Color borderColor = Color.white;

        for (int y = marginY; y < textureHeight - marginY; y++)
        {
            for (int x = startX + marginX; x < startX + width - marginX; x++)
            {
                if (x <= startX + marginX + 4 || x >= startX + width - marginX - 4 || y <= marginY + 4 || y >= textureHeight - marginY - 4)
                {
                    tex.SetPixel(x, y, borderColor);
                }
                else
                {
                    tex.SetPixel(x, y, symbolColor);
                }
            }
        }
    }

    private Color GetSymbolColor(int index)
    {
        Color[] colors = new Color[]
        {
            new Color(0.9f, 0.2f, 0.2f), // 0: 赤
            new Color(0.2f, 0.8f, 0.3f), // 1: 緑
            new Color(0.9f, 0.8f, 0.1f), // 2: 黄
            new Color(0.6f, 0.2f, 0.9f), // 3: 紫
            new Color(0.2f, 0.6f, 0.9f)  // 4: 青
        };
        return colors[index % colors.Length];
    }
}