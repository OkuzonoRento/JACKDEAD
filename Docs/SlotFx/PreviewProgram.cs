// スロットの音とドット絵を、Unity の外で書き出して確認するためのプログラム（Unity では読み込まれない）。
// Assets/Scripts/Slot の SlotSynth.cs と SlotPixelArt.cs と一緒にコンパイルする。
// 引数: 音の出力先、絵の出力先
// 音の鳴るタイミングは SlotMachineController.RollVisual と SlotWinEffects.RunWin に合わせている（そちらを変えたらここも直す）。
using System;
using System.IO;
using System.IO.Compression;

public static class PreviewProgram
{
    private const int Rate = SlotSynth.SampleRate;

    public static void Main(string[] args)
    {
        string soundDir = args[0], imageDir = args[1];
        Directory.CreateDirectory(soundDir);
        Directory.CreateDirectory(imageDir);

        // ボタンを押したときの音
        Single(Path.Combine(soundDir, "開始.wav"), SlotSynth.Sound.Start);

        // ボタンを押してから 1 回し分
        WriteDemo(Path.Combine(soundDir, "1回し_ハズレ.wav"), 0, 0f, 0f, 0f, 0f);
        WriteDemo(Path.Combine(soundDir, "1回し_WIN.wav"), 1, 0.17f, 0.3f, 0f, 1.4f);
        WriteDemo(Path.Combine(soundDir, "1回し_BIGWIN.wav"), 2, 0.21f, 0.65f, 0f, 2.8f);
        WriteDemo(Path.Combine(soundDir, "1回し_JACKPOT.wav"), 3, 0.27f, 1.3f, 0f, 4.9f);

        var dark = C(45, 5, 40); var white = C(255, 255, 235); var pink = C(255, 50, 185); var deep = C(215, 20, 150);
        var yellow = C(255, 225, 40); var cream = C(255, 244, 220);
        Png(Path.Combine(imageDir, "title_jackpot.png"), SlotPixelArt.Title("JACKPOT", pink, deep, yellow, dark));
        Png(Path.Combine(imageDir, "title_bigwin.png"), SlotPixelArt.Title("BIG WIN!", pink, deep, yellow, dark));
        foreach (var pair in new[] { new[] { "amount_big", "+$6,750" }, new[] { "amount_count", "+$4,120" }, new[] { "amount_win", "+$2,250" } })
            Png(Path.Combine(imageDir, pair[0] + ".png"), SlotPixelArt.Title(pair[1], C(255, 90, 205), C(230, 25, 160), cream, C(40, 5, 35)));
        // 掲示板の文字は SlotWinEffects の Cream
        var board = C(255, 242, 219);
        Png(Path.Combine(imageDir, "led_chain.png"), SlotPixelArt.Led("x7 x1.5", board));
        Png(Path.Combine(imageDir, "led_count.png"), SlotPixelArt.Led("+$4,120", board));
        Png(Path.Combine(imageDir, "led_jackpot.png"), SlotPixelArt.Led("+$10,500", board));
        Png(Path.Combine(imageDir, "led_big.png"), SlotPixelArt.Led("+$6,750", board));
        Png(Path.Combine(imageDir, "led_win.png"), SlotPixelArt.Led("+$2,250", board));
        Png(Path.Combine(imageDir, "frame.png"), SlotPixelArt.Frame(32, 24));
        Png(Path.Combine(imageDir, "star.png"), SlotPixelArt.Star());
        // 台の大きさ（窓 1.26m × 0.73m）から Unity と同じ計算
        Png(Path.Combine(imageDir, "glow.png"), SlotPixelArt.Glow((int)Math.Round(1.35 / 0.008), (int)Math.Round(0.82 / 0.008)));
        Console.WriteLine("ok");
    }

    private static SlotPixelArt.Rgba C(int r, int g, int b) => new SlotPixelArt.Rgba(r, g, b);

    private static void Single(string path, SlotSynth.Sound sound)
    {
        float[] data = SlotSynth.Build(sound);
        Wav(path, data, data.Length / (float)Rate);
    }

    private static void WriteDemo(string path, int tier, float step, float count, float unused, float finale)
    {
        const float spin = 0.85f; // SlotWinEffects.StartWait
        float[] mix = new float[Rate * 14];
        Add(mix, SlotSynth.Sound.Start, 0f, 0f, 1f);
        Add(mix, SlotSynth.Sound.Lever, spin, 0f, 1f);

        // 回転中の音：リールが止まるたびに低くなる
        float[] stops = { spin + 1.6f, spin + 2.4f, spin + 3.2f };
        float[] loop = SlotSynth.Build(SlotSynth.Sound.SpinLoop);
        double position = 0;
        for (int i = (int)(spin * Rate); i < (int)(stops[2] * Rate); i++)
        {
            float t = i / (float)Rate;
            float pitch = t < stops[0] ? 1f : t < stops[1] ? 0.88f : 0.76f;
            mix[i] += loop[(int)position % loop.Length] * 0.6f;
            position += pitch;
        }
        for (int i = 0; i < 3; i++)
        {
            Add(mix, SlotSynth.Sound.ReelStop, stops[i], -i, 1f);
            if (tier > 0 && i >= 1) Add(mix, SlotSynth.Sound.Blip, stops[i], -7f + (i - 1) * 4f, 0.55f);
        }

        float now = stops[2];
        if (tier == 0)
        {
            Add(mix, SlotSynth.Sound.Lose, now, 0f, 0.7f);
            Wav(path, mix, now + 1.1f);
            return;
        }

        now += 0.18f;
        int[] notes = { -5, -4, -2 };
        for (int i = 0; i < 3; i++, now += step) Add(mix, SlotSynth.Sound.Blip, now, notes[i], 1f);
        for (int i = 0; i < 2; i++, now += step) Add(mix, SlotSynth.Sound.Blip, now, 3f + i, 1f);
        float tick = 0f;
        while (tick < count)
        {
            float k = tick / count;
            Add(mix, SlotSynth.Sound.Tick, now + tick, -3f + 15f * k, 0.7f);
            tick += 0.075f + (0.035f - 0.075f) * k;
        }
        now += count;
        if (tier >= 2) Add(mix, SlotSynth.Sound.Slam, now, 0f, 1f);
        Add(mix, SlotSynth.Sound.Payout, now, 0f, tier == 1 ? 1f : 0.55f);
        if (tier == 2) Add(mix, SlotSynth.Sound.BigWin, now, 0f, 1f);
        if (tier == 3) Add(mix, SlotSynth.Sound.Jackpot, now, 0f, 1f);

        // メダルが床で跳ねる音（だいたいの時間）
        Random r = new Random(5);
        int coins = tier == 3 ? 40 : tier == 2 ? 16 : 5;
        float spread = tier == 3 ? 2.6f : tier == 2 ? 1f : 0.25f;
        for (int i = 0; i < coins; i++)
            Add(mix, SlotSynth.Sound.Coin, now + 0.5f + spread * i / coins + (float)r.NextDouble() * 0.08f, -3f + 7f * (float)r.NextDouble(), 0.35f);

        Wav(path, mix, now + finale);
    }

    private static void Add(float[] mix, SlotSynth.Sound sound, float at, float semitones, float volume)
    {
        float[] data = SlotSynth.Build(sound);
        double pitch = Math.Pow(2, semitones / 12.0);
        int start = (int)(at * Rate);
        for (int i = 0; start + i < mix.Length; i++)
        {
            int from = (int)(i * pitch);
            if (from >= data.Length) break;
            mix[start + i] += data[from] * volume;
        }
    }

    private static void Wav(string path, float[] mix, float seconds)
    {
        int count = Math.Min(mix.Length, (int)(seconds * Rate));
        float peak = 0f;
        for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(mix[i]));
        Console.WriteLine($"{Path.GetFileName(path)} {count / (float)Rate:0.00}s peak {peak * 0.7f:0.00}");

        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write("RIFF".ToCharArray()); w.Write(36 + count * 2); w.Write("WAVEfmt ".ToCharArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(count * 2);
            for (int i = 0; i < count; i++) w.Write((short)(Math.Max(-1f, Math.Min(1f, mix[i])) * 32000 * 0.7f)); // 0.7 = SlotCelebrationSettings.soundVolume の標準
        }
    }

    private static void Png(string path, SlotPixelArt.Image image)
    {
        MemoryStream raw = new MemoryStream();
        for (int y = 0; y < image.height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < image.width; x++)
            {
                SlotPixelArt.Rgba p = image.pixels[y * image.width + x];
                raw.WriteByte(p.r); raw.WriteByte(p.g); raw.WriteByte(p.b); raw.WriteByte(p.a);
            }
        }
        MemoryStream packed = new MemoryStream();
        using (ZLibStream z = new ZLibStream(packed, CompressionMode.Compress, true)) z.Write(raw.ToArray());

        using (FileStream f = File.Create(path))
        {
            f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            byte[] header = new byte[13];
            Big(header, 0, (uint)image.width); Big(header, 4, (uint)image.height); header[8] = 8; header[9] = 6;
            Chunk(f, "IHDR", header);
            Chunk(f, "IDAT", packed.ToArray());
            Chunk(f, "IEND", new byte[0]);
        }
    }

    private static void Big(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] length = new byte[4]; Big(length, 0, (uint)data.Length); s.Write(length);
        byte[] body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        Array.Copy(data, 0, body, 4, data.Length);
        s.Write(body);
        uint crc = 0xffffffff;
        foreach (byte b in body)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        byte[] tail = new byte[4]; Big(tail, 0, crc ^ 0xffffffff); s.Write(tail);
    }
}
