using System;

/// <summary>
/// 音の波形を作る部品（素材を使わない）。SlotSynth と GameSynth が使う。
/// どれも「d の start 秒の所に、音を足す」。wrap を true にすると、終わりからはみ出た分を先頭へ回す（くり返す曲が、つなぎ目なしになる）。
/// UnityEngine を使わないので、Unity の外でも音を書き出して確認できる。
/// </summary>
public static class SynthKit
{
    public const int SampleRate = 44100;
    private const double Tau = Math.PI * 2.0;

    public static float[] New(float seconds)
    {
        return new float[(int)(seconds * SampleRate)];
    }

    /// <summary>音の高さ。ラ(440Hz)から半音いくつ上か</summary>
    public static float Note(int semitones)
    {
        return 440f * (float)Math.Pow(2.0, semitones / 12.0);
    }

    private static void Put(float[] d, int at, float value, bool wrap)
    {
        if (wrap) at %= d.Length;
        if (at >= 0 && at < d.Length) d[at] += value;
    }

    public static void Mix(float[] d, float[] part, float start, float volume, bool wrap = false)
    {
        int begin = (int)(start * SampleRate);
        for (int i = 0; i < part.Length; i++) Put(d, begin + i, part[i] * volume, wrap);
    }

    // ───────── 打つ・鳴らす ─────────

    /// <summary>正弦波。高さを from から to へ動かし、だんだん小さくする（decay が大きいほど早く消える）</summary>
    public static void Tone(float[] d, float start, float length, float from, float to, float volume, float attack, float decay, bool wrap = false)
    {
        int begin = (int)(start * SampleRate), count = (int)(length * SampleRate);
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate, k = i / (float)count;
            phase += (from + (to - from) * k) / SampleRate;
            float envelope = Math.Min(1f, t / Math.Max(0.0005f, attack)) * (float)Math.Exp(-decay * t) * Math.Min(1f, (1f - k) * 12f);
            Put(d, begin + i, (float)Math.Sin(Tau * phase) * volume * envelope, wrap);
        }
    }

    /// <summary>低い「ドン」。高さが急に下がる（太鼓・足音・衝撃）</summary>
    public static void Thud(float[] d, float start, float from, float to, float length, float volume, bool wrap = false)
    {
        int begin = (int)(start * SampleRate), count = (int)(length * SampleRate);
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float k = i / (float)count;
            phase += (to + (from - to) * Math.Exp(-7 * k)) / SampleRate;
            float envelope = (float)Math.Exp(-4.5 * k) * (1f - k) * Math.Min(1f, i / 90f);
            Put(d, begin + i, (float)Math.Sin(Tau * phase) * volume * envelope, wrap);
        }
    }

    /// <summary>倍音を指定して鳴らす（鐘・鈴・金属）。ratios は基音に対する高さ、高い倍音ほど早く消える</summary>
    public static void Partials(float[] d, float start, float frequency, float decay, float volume, float[] ratios, float[] levels, float attack = 0.002f, bool wrap = false)
    {
        int begin = (int)(start * SampleRate);
        for (int n = 0; n < ratios.Length; n++)
        {
            double f = frequency * ratios[n];
            if (f > SampleRate * 0.45) continue;
            float life = decay / (1f + n * 0.45f);
            int count = (int)(life * 5f * SampleRate);
            double step = Tau * f / SampleRate, phase = n * 1.7;
            float level = volume * levels[n];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Math.Min(1f, t / attack) * (float)Math.Exp(-t / life) * Math.Min(1f, (count - i) / 400f);
                Put(d, begin + i, (float)Math.Sin(phase) * level * envelope, wrap);
                phase += step;
            }
        }
    }

    /// <summary>オルゴールの歯。detune（半音の何分の1か）で調子を外すと、古びて不気味になる</summary>
    public static void MusicBox(float[] d, float start, float frequency, float decay, float volume, float detune = 0f, bool wrap = false)
    {
        float f = frequency * (float)Math.Pow(2.0, detune / 12.0);
        Partials(d, start, f, decay, volume, new[] { 1f, 1.006f, 3.98f, 6.7f }, new[] { 0.75f, 0.35f, 0.16f, 0.05f }, 0.001f, wrap);
        Noise(d, new Random((int)(start * 1000f) + 5), start, 0.004f, volume * 0.35f, 9000f, 2500f, 0.0005f, 400f, wrap); // 歯をはじく音
    }

    /// <summary>お寺の鐘のような、低くうなる鐘</summary>
    public static void TempleBell(float[] d, float start, float frequency, float decay, float volume, bool wrap = false)
    {
        Partials(d, start, frequency, decay, volume,
            new[] { 1f, 1.012f, 2.02f, 2.74f, 3.76f, 5.32f, 6.9f }, new[] { 0.6f, 0.45f, 0.3f, 0.22f, 0.13f, 0.07f, 0.04f }, 0.006f, wrap);
        Noise(d, new Random(91), start, 0.05f, volume * 0.5f, 1400f, 200f, 0.001f, 60f, wrap); // 撞木が当たる音
    }

    /// <summary>そりの鈴（小さな鈴がたくさん）</summary>
    public static void SleighBells(float[] d, Random r, float start, float length, float volume, bool wrap = false)
    {
        int count = 3 + (int)(length * 55f);
        for (int i = 0; i < count; i++)
        {
            float at = start + (float)r.NextDouble() * length;
            float f = 2300f + (float)r.NextDouble() * 2100f;
            Partials(d, at, f, 0.018f + (float)r.NextDouble() * 0.03f, volume * (0.25f + (float)r.NextDouble() * 0.35f),
                new[] { 1f, 1.47f, 2.09f }, new[] { 0.6f, 0.4f, 0.25f }, 0.0008f, wrap);
        }
    }

    /// <summary>木を打つ音（コトッ・ギシッ）</summary>
    public static void Knock(float[] d, Random r, float start, float frequency, float decay, float volume, bool wrap = false)
    {
        Partials(d, start, frequency, decay, volume, new[] { 1f, 2.37f, 3.91f }, new[] { 0.75f, 0.35f, 0.15f }, 0.0008f, wrap);
        Noise(d, r, start, 0.018f, volume * 0.7f, frequency * 8f, frequency, 0.0004f, 220f, wrap);
    }

    /// <summary>硬貨が当たる音</summary>
    public static void Coin(float[] d, Random r, float start, float volume, float pitch = 1f, bool wrap = false)
    {
        float f = (2100f + (float)r.NextDouble() * 900f) * pitch;
        Partials(d, start, f, 0.045f, volume, new[] { 1f, 1.53f, 2.31f }, new[] { 0.6f, 0.45f, 0.2f }, 0.0006f, wrap);
        Noise(d, r, start, 0.005f, volume * 0.35f, 12000f, 4000f, 0.0003f, 500f, wrap);
    }

    // ───────── こする・吹く ─────────

    /// <summary>雑音。lowPass より高い成分と、highPass より低い成分を削る。だんだん小さくする</summary>
    public static void Noise(float[] d, Random r, float start, float length, float volume, float lowPass, float highPass, float attack, float decay, bool wrap = false)
    {
        int begin = (int)(start * SampleRate), count = (int)(length * SampleRate);
        float a = Coefficient(lowPass), b = Coefficient(highPass);
        float low = 0f, low2 = 0f, floor = 0f;
        float gain = volume * (1f + 1200f / Math.Max(200f, lowPass)); // 削るほど小さくなるぶんを補う
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate, k = i / (float)count;
            float white = (float)(r.NextDouble() * 2 - 1);
            low += (white - low) * a;
            low2 += (low - low2) * a;
            floor += (low2 - floor) * b;
            float envelope = Math.Min(1f, t / Math.Max(0.0003f, attack)) * (float)Math.Exp(-decay * t) * Math.Min(1f, (1f - k) * 10f);
            Put(d, begin + i, (low2 - floor) * gain * envelope, wrap);
        }
    }

    private static float Coefficient(float frequency)
    {
        return (float)(1.0 - Math.Exp(-Tau * Math.Min(frequency, SampleRate * 0.45f) / SampleRate));
    }

    /// <summary>風を切る音。中ほどで、いちばん明るく大きくなる</summary>
    public static void Whoosh(float[] d, Random r, float start, float length, float volume, float fromHz, float toHz, bool wrap = false)
    {
        int begin = (int)(start * SampleRate), count = (int)(length * SampleRate);
        float low = 0f, floor = 0f;
        for (int i = 0; i < count; i++)
        {
            float k = i / (float)count;
            float swell = (float)Math.Sin(Math.PI * k);
            float a = Coefficient(fromHz + (toHz - fromHz) * swell);
            low += ((float)(r.NextDouble() * 2 - 1) - low) * a;
            floor += (low - floor) * a * 0.15f;
            Put(d, begin + i, (low - floor) * volume * 1.8f * swell * swell, wrap);
        }
    }

    /// <summary>
    /// ざらついた弦のような音（少しずれた のこぎり波を重ねて、高い成分を削る）。
    /// tremolo は1秒あたりの震えの回数（0 で震えない）
    /// </summary>
    public static void Strings(float[] d, float start, float length, float frequency, float volume, float cutoff, float attack, float release, float tremolo, bool wrap = false)
    {
        int count = (int)(length * SampleRate), begin = (int)(start * SampleRate);
        float[] part = new float[count];
        double[] detune = { 1.0, 1.0045, 0.9961 };
        for (int v = 0; v < detune.Length; v++)
        {
            double phase = v * 0.37, step = frequency * detune[v] / SampleRate;
            for (int i = 0; i < count; i++)
            {
                phase += step;
                if (phase >= 1) phase -= 1;
                // 角を丸めた のこぎり波（高い音で耳に刺さらないように）
                double saw = 2 * phase - 1;
                if (phase < step) { double x = phase / step; saw -= x + x - x * x - 1; }
                else if (phase > 1 - step) { double x = (phase - 1) / step; saw -= x * x + x + x + 1; }
                part[i] += (float)saw / detune.Length;
            }
        }
        LowPass(part, cutoff, 0.9f);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = Math.Min(1f, t / Math.Max(0.001f, attack)) * Math.Min(1f, (length - t) / Math.Max(0.001f, release));
            if (tremolo > 0f) envelope *= 0.65f + 0.35f * (float)Math.Sin(Tau * tremolo * t);
            Put(d, begin + i, part[i] * volume * envelope, wrap);
        }
    }

    /// <summary>
    /// 人の声のような音（のどの音を、口の形の2〜3か所で響かせる）。formants は響く高さ（Hz）。
    /// 高さを from から to へ動かす。breath は息の混ざり方
    /// </summary>
    public static void Voice(float[] d, Random r, float start, float length, float from, float to, float[] formants, float volume, float breath)
    {
        int count = (int)(length * SampleRate);
        float[] source = new float[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float k = i / (float)count;
            double f = (from + (to - from) * k) * (1.0 + 0.025 * Math.Sin(Tau * 23 * i / SampleRate) + (r.NextDouble() - 0.5) * 0.03); // しわがれ
            phase += f / SampleRate;
            if (phase >= 1) phase -= 1;
            source[i] = (float)(2 * phase - 1) * (1f - breath) + (float)(r.NextDouble() * 2 - 1) * breath;
        }

        float[] mouth = new float[count];
        for (int n = 0; n < formants.Length; n++)
        {
            float[] band = (float[])source.Clone();
            BandPass(band, formants[n], 6f);
            float level = 1f / (1f + n * 0.8f);
            for (int i = 0; i < count; i++) mouth[i] += band[i] * level;
        }
        for (int i = 0; i < count; i++)
        {
            float k = i / (float)count;
            float envelope = Math.Min(1f, k * 12f) * (float)Math.Pow(1f - k, 0.7);
            mouth[i] = (float)Math.Tanh(mouth[i] * 4f) * envelope;
        }
        Mix(d, mouth, start, volume);
    }

    // ───────── ととのえる ─────────

    public static void LowPass(float[] d, float frequency, float q) { Filter(d, frequency, q, 0); }
    public static void HighPass(float[] d, float frequency, float q) { Filter(d, frequency, q, 1); }
    public static void BandPass(float[] d, float frequency, float q) { Filter(d, frequency, q, 2); }

    private static void Filter(float[] d, float frequency, float q, int kind)
    {
        double w = Tau * frequency / SampleRate;
        double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
        double b0, b1, b2;
        if (kind == 0) { b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; }
        else if (kind == 1) { b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; }
        else { b0 = alpha; b1 = 0; b2 = -alpha; }
        double a0 = 1 + alpha, a1 = -2 * cos, a2 = 1 - alpha;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < d.Length; i++)
        {
            double x = d[i];
            double y = (b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            d[i] = (float)y;
        }
    }

    /// <summary>部屋の響きを足す。amount は混ぜる量、tail は響きの長さ（0.7〜0.9）。loop なら、終わりの響きが先頭に続く</summary>
    public static void Reverb(float[] d, float amount, float tail, bool loop = false)
    {
        float[] input = d;
        if (loop)
        {
            input = new float[d.Length * 2];
            Array.Copy(d, 0, input, 0, d.Length);
            Array.Copy(d, 0, input, d.Length, d.Length);
        }

        float[] wet = new float[input.Length];
        float[] times = { 0.0297f, 0.0371f, 0.0433f, 0.0531f };
        foreach (float time in times)
        {
            float[] line = new float[(int)(time * SampleRate)];
            float damp = 0f;
            for (int i = 0; i < input.Length; i++)
            {
                int at = i % line.Length;
                float echo = line[at];
                damp += (echo - damp) * 0.4f; // 響くほど、こもる
                line[at] = input[i] + damp * tail;
                wet[i] += echo * 0.25f;
            }
        }
        foreach (float time in new[] { 0.0053f, 0.0017f })
        {
            float[] pass = new float[(int)(time * SampleRate)];
            for (int i = 0; i < wet.Length; i++)
            {
                int at = i % pass.Length;
                float delayed = pass[at];
                float value = delayed - wet[i] * 0.6f;
                pass[at] = wet[i] + value * 0.6f;
                wet[i] = value;
            }
        }

        int offset = loop ? d.Length : 0;
        for (int i = 0; i < d.Length; i++) d[i] += wet[offset + i] * amount;
    }

    /// <summary>仕上げ：いちばん大きい所を peak にそろえる。drive が大きいほど、歪んで太くなる。fade は終わりを絞る秒数（くり返す音は 0）</summary>
    public static float[] Finish(float[] d, float peak, float drive = 1f, float fade = 0.015f)
    {
        float max = 0f;
        foreach (float v in d) max = Math.Max(max, Math.Abs(v));
        if (max > 0f)
        {
            double scale = Math.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = (float)(Math.Tanh(d[i] / max * drive) / scale) * peak;
        }
        int count = Math.Min(d.Length, (int)(fade * SampleRate));
        for (int i = 0; i < count; i++) d[d.Length - 1 - i] *= i / (float)count;
        return d;
    }
}
