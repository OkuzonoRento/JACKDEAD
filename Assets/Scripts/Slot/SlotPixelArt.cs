using System;
using System.Collections.Generic;

/// <summary>
/// スロットの演出で使うドット絵（文字・枠・電飾・星）を描く。画像素材を使わない。
/// UnityEngine を使わないので、Unity の外でも絵を書き出して確認できる。
/// </summary>
public static class SlotPixelArt
{
    public struct Rgba
    {
        public byte r, g, b, a;
        public Rgba(int r, int g, int b, int a = 255) { this.r = (byte)r; this.g = (byte)g; this.b = (byte)b; this.a = (byte)a; }
    }

    /// <summary>描いた絵。左上が (0,0)</summary>
    public class Image
    {
        public readonly int width;
        public readonly int height;
        public readonly Rgba[] pixels;

        public Image(int width, int height)
        {
            this.width = width;
            this.height = height;
            pixels = new Rgba[width * height];
        }

        public void Set(int x, int y, Rgba color)
        {
            if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = color;
        }

        public Rgba Get(int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height ? pixels[y * width + x] : default;
        }

        public void Fill(int x, int y, int w, int h, Rgba color)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) Set(i, j, color);
        }
    }

    // 5×7 の文字
    private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
    {
        { '0', "01110 10001 10011 10101 11001 10001 01110" },
        { '1', "00100 01100 00100 00100 00100 00100 01110" },
        { '2', "01110 10001 00001 00010 00100 01000 11111" },
        { '3', "11110 00001 00001 01110 00001 00001 11110" },
        { '4', "00010 00110 01010 10010 11111 00010 00010" },
        { '5', "11111 10000 11110 00001 00001 10001 01110" },
        { '6', "00110 01000 10000 11110 10001 10001 01110" },
        { '7', "11111 00001 00010 00100 01000 01000 01000" },
        { '8', "01110 10001 10001 01110 10001 10001 01110" },
        { '9', "01110 10001 10001 01111 00001 00010 01100" },
        { 'A', "01110 10001 10001 11111 10001 10001 10001" },
        { 'B', "11110 10001 10001 11110 10001 10001 11110" },
        { 'C', "01110 10001 10000 10000 10000 10001 01110" },
        { 'E', "11111 10000 10000 11110 10000 10000 11111" },
        { 'G', "01110 10001 10000 10111 10001 10001 01111" },
        { 'I', "11111 00100 00100 00100 00100 00100 11111" },
        { 'J', "00111 00010 00010 00010 00010 10010 01100" },
        { 'K', "10001 10010 10100 11000 10100 10010 10001" },
        { 'L', "10000 10000 10000 10000 10000 10000 11111" },
        { 'N', "10001 11001 11001 10101 10011 10011 10001" },
        { 'O', "01110 10001 10001 10001 10001 10001 01110" },
        { 'P', "11110 10001 10001 11110 10000 10000 10000" },
        { 'R', "11110 10001 10001 11110 10100 10010 10001" },
        { 'T', "11111 00100 00100 00100 00100 00100 00100" },
        { 'W', "10001 10001 10001 10101 10101 10101 01010" },
        { '$', "00100 01111 10100 01110 00101 11110 00100" },
        { '+', "00000 00100 00100 11111 00100 00100 00000" },
        { 'x', "00000 10001 01010 00100 01010 10001 00000" },
        { '.', "00000 00000 00000 00000 00000 01100 01100" },
        { ',', "00000 00000 00000 00000 01100 00100 01000" },
        { '!', "00100 00100 00100 00100 00100 00000 00100" },
        { '=', "00000 00000 11111 00000 11111 00000 00000" },
    };

    private static bool IsLit(char c, int x, int y)
    {
        return Glyphs.TryGetValue(c, out string rows) && rows[y * 6 + x] == '1';
    }

    /// <summary>大きい見出し。中の色（上下で変わる）・内側の縁・外側の黒い縁・影</summary>
    public static Image Title(string text, Rgba top, Rgba bottom, Rgba rim, Rgba outline)
    {
        const int scale = 2, pad = 3;
        int advance = 7 * scale;
        Image image = new Image(text.Length * advance - 2 * scale + pad * 2 + 1, 7 * scale + pad * 2 + 2);
        bool[] mask = new bool[image.width * image.height];

        for (int n = 0; n < text.Length; n++)
        {
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 5; x++)
                {
                    if (!IsLit(text[n], x, y)) continue;
                    for (int j = 0; j < scale; j++)
                        for (int i = 0; i < scale; i++)
                            mask[(pad + y * scale + j) * image.width + pad + n * advance + x * scale + i] = true;
                }
        }

        bool[] inner = Grow(mask, image.width, image.height);
        bool[] outer = Grow(Grow(inner, image.width, image.height), image.width, image.height);

        for (int y = 0; y < image.height; y++)
            for (int x = 0; x < image.width; x++)
            {
                int i = y * image.width + x;
                if (mask[i]) image.pixels[i] = y - pad < 7 ? top : bottom;
                else if (inner[i]) image.pixels[i] = rim;
                else if (outer[i]) image.pixels[i] = outline;
                else if (y >= 2 && outer[i - 2 * image.width]) image.pixels[i] = new Rgba(0, 0, 0, 150);
            }
        return image;
    }

    private static bool[] Grow(bool[] source, int width, int height)
    {
        bool[] result = (bool[])source.Clone();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!source[y * width + x]) continue;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int u = x + i, v = y + j;
                        if (u >= 0 && v >= 0 && u < width && v < height) result[v * width + u] = true;
                    }
            }
        return result;
    }

    public const int LedColumns = 12;

    /// <summary>台の上の電光掲示板。文字は粒（ドット）で光る</summary>
    public static Image Led(string text, Rgba lit)
    {
        const int dot = 3, margin = 6;
        Image image = new Image(LedColumns * 6 * dot + margin * 2, 7 * dot + margin * 2);

        image.Fill(0, 0, image.width, image.height, new Rgba(70, 45, 38));
        image.Fill(1, 1, image.width - 2, image.height - 2, new Rgba(130, 36, 24));
        image.Fill(3, 3, image.width - 6, image.height - 6, new Rgba(14, 6, 6));

        Rgba dim = new Rgba(40, 16, 12);
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < LedColumns * 6; x++) image.Fill(margin + x * dot, margin + y * dot, 2, 2, dim);

        if (text.Length > LedColumns) text = text.Substring(text.Length - LedColumns);
        int start = (LedColumns * 6 - (text.Length * 6 - 1)) / 2;
        for (int n = 0; n < text.Length; n++)
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 5; x++)
                    if (IsLit(text[n], x, y))
                        image.Fill(margin + (start + n * 6 + x) * dot, margin + y * dot, dot, dot, lit);
        return image;
    }

    /// <summary>そろった目を囲む枠（白で描き、色は表示するときに掛ける）</summary>
    public static Image Frame(int width, int height)
    {
        Image image = new Image(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                int corner = Math.Min(x, width - 1 - x) + Math.Min(y, height - 1 - y);
                if (corner < 2) continue; // 角を落とす
                if (edge == 0) image.Set(x, y, new Rgba(90, 90, 90));
                else if (edge <= 2) image.Set(x, y, new Rgba(255, 255, 255));
                else if (edge == 3) image.Set(x, y, new Rgba(255, 255, 255, 60));
                else image.Set(x, y, new Rgba(255, 255, 255, 5));
            }
        return image;
    }

    /// <summary>リールの窓を囲む、光る太い枠。内と外へ向かって薄くなる</summary>
    public static Image Glow(int width, int height)
    {
        Image image = new Image(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                if (edge == 0) image.Set(x, y, new Rgba(255, 190, 20, 70));
                else if (edge == 1) image.Set(x, y, new Rgba(255, 200, 25, 150));
                else if (edge <= 4) image.Set(x, y, new Rgba(255, 225, 45));
                else if (edge == 5) image.Set(x, y, new Rgba(255, 245, 160));
                else if (edge <= 9) image.Set(x, y, new Rgba(255, 190, 30, (10 - edge) * 28));
            }
        return image;
    }

    /// <summary>飛び散る星</summary>
    public static Image Star()
    {
        string[] rows =
        {
            "....#....",
            "...###...",
            "...###...",
            "#########",
            ".#######.",
            "..#####..",
            "..#####..",
            ".###.###.",
            ".#.....#.",
        };
        Image image = new Image(9, 9);
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
                if (rows[y][x] == '#') image.Set(x, y, new Rgba(255, 255, 255));
        return image;
    }
}
