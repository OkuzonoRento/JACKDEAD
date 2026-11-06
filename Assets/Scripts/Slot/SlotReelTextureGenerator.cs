using UnityEngine;

public class SlotReelTextureGenerator : MonoBehaviour
{
    [Header("¶¬İ’è")]
    [SerializeField] private int textureWidth = 256;   // ‰¡•
    [SerializeField] private int textureHeight = 768;  // c‚Ì‰ğ‘œ“x
    [SerializeField] private int symbolCount = 5;      // }•¿‚Ì‘”

    private void Start()
    {
        GenerateAndApplyTexture();
    }

    private void GenerateAndApplyTexture()
    {
        Texture2D reelTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        reelTexture.filterMode = FilterMode.Point;
        reelTexture.wrapMode = TextureWrapMode.Repeat; // ã‰ºƒ‹[ƒv‚É•K{

        int symbolHeight = textureHeight / symbolCount;
        Color[] pixels = new Color[textureWidth * textureHeight];

        // ”wŒiFiˆÃ‚¢“S”ÂFj
        Color bgColor = new Color(0.1f, 0.1f, 0.12f);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = bgColor;
        }
        reelTexture.SetPixels(pixels);

        // c•ûŒü‚É“™ŠÔŠu‚ÅƒVƒ“ƒ{ƒ‹‚ğ•`‰æ
        for (int s = 0; s < symbolCount; s++)
        {
            int startY = s * symbolHeight;
            DrawSymbol(reelTexture, startY, symbolHeight, s);
        }

        reelTexture.Apply();

        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            if (rend.material.HasProperty("_BaseMap"))
            {
                rend.material.SetTexture("_BaseMap", reelTexture);
            }
            else
            {
                rend.material.mainTexture = reelTexture;
            }

            // š c•ûŒü‚Éí‚Éu3‚Âv‚Ì•¿‚ªŒ©‚¦‚é‚æ‚¤‚Éƒ^ƒCƒ‹”ä—¦‚ğİ’è
            float tilingY = (float)symbolCount / 3.0f;
            rend.material.mainTextureScale = new Vector2(1f, tilingY);
            rend.material.mainTextureOffset = Vector2.zero;
        }
    }

    private void DrawSymbol(Texture2D tex, int startY, int height, int symbolIndex)
    {
        int marginX = 10;
        int marginY = 10;
        Color baseColor = GetSymbolColor(symbolIndex);
        Color borderColor = Color.white;

        for (int y = startY + marginY; y < startY + height - marginY; y++)
        {
            for (int x = marginX; x < textureWidth - marginX; x++)
            {
                // ‰¡•‚¢‚Á‚Ï‚¢‚ÌlŠpŒ`‚ğ•`‰æ
                if (x <= marginX + 4 || x >= textureWidth - marginX - 4 || y <= startY + marginY + 4 || y >= startY + height - marginY - 4)
                {
                    tex.SetPixel(x, y, borderColor);
                }
                else
                {
                    tex.SetPixel(x, y, baseColor);
                }
            }
        }
    }

    private Color GetSymbolColor(int index)
    {
        Color[] symbolColors = new Color[]
        {
            new Color(0.85f, 0.15f, 0.15f), // Ô
            new Color(0.15f, 0.75f, 0.15f), // —Î
            new Color(0.85f, 0.75f, 0.15f), // ‰©
            new Color(0.55f, 0.15f, 0.85f), // ‡
            new Color(0.35f, 0.35f, 0.35f)  // ŠD
        };
        return symbolColors[index % symbolColors.Length];
    }
}