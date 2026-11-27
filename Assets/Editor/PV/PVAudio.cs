using System;
using UnityEngine;

/// <summary>
/// PV の音を、すべて計算で作る（素材の音声ファイルは使わない）。時刻は PVDirector の場面とそろえている。
/// 戻り値は、左右が交互に並んだステレオの波形。
/// </summary>
public static class PVAudio
{
    public const int SampleRate = 48000;

    /// <summary>全体の音量（0.5 で約 -6dB）</summary>
    private const float Master = 0.45f;

    private static float[] mix;
    private static System.Random random;

    public static float[] Build(float length)
    {
        mix = new float[Mathf.RoundToInt(length * SampleRate) * 2];
        random = new System.Random(1234);

        // 低いうなり（場面ごとに強さを変える）
        Drone(0f, 11f, 55f, 0.11f);
        Drone(11f, 25f, 49f, 0.13f);
        Drone(25f, 35f, 55f, 0.07f);
        Drone(35f, 38.5f, 41f, 0.12f);
        Drone(46f, 51.5f, 49f, 0.09f);
        Drone(51.5f, 53.25f, 37f, 0.08f);
        Drone(55f, 60f, 55f, 0.10f);

        // 0–6秒：リール
        ReelRoll(0.6f, 3.4f);
        Thunk(2.1f); Thunk(2.75f); Thunk(3.4f);
        Boom(2.4f, 0.35f);

        // 6–11秒：部屋が組み上がる音
        for (int i = 0; i < 16; i++) Impact(6.2f + i * 0.19f, 0.22f + i * 0.008f, 70f + i * 5f);
        Boom(9.4f, 0.5f);
        Riser(9.2f, 11f, 0.16f);

        // 11–15秒：到着
        Boom(11.0f, 0.55f);
        Bell(11.7f, 220f, 0.12f, 2.5f);

        // 15–25秒：足音と、拾う音
        for (float t = 15.2f; t < 21.4f; t += 0.5f) Step(t, 0.28f);
        for (float t = 21.6f; t < 23.2f; t += 0.55f) Step(t, 0.18f);
        for (float t = 15f; t < 25f; t += 1.25f) Bass(t, 55f, 0.14f);
        Blip(23.5f, 660f, 0.12f);
        Coin(23.95f, 0.35f);

        // 25–35秒：スロット（ゲームで鳴る音と同じ。時刻は PVDirector.Gamble と SlotWinEffects に合わせる）
        for (float t = 25.2f; t < 26.4f; t += 0.55f) Step(t, 0.2f);
        Game(SlotSynth.Sound.Start, 26.5f);
        Game(SlotSynth.Sound.Lever, 27.35f);
        Game(SlotSynth.Sound.SpinLoop, 27.35f, 0f, 0.5f, 2.65f);
        for (int i = 0; i < 3; i++) Game(SlotSynth.Sound.ReelStop, 28.7f + i * 0.65f, -i);
        Game(SlotSynth.Sound.Blip, 29.35f, -7f, 0.55f);
        Game(SlotSynth.Sound.Blip, 30.0f, -3f, 0.55f);
        float[] notes = { -5f, -4f, -2f };
        for (int i = 0; i < 3; i++) Game(SlotSynth.Sound.Blip, 30.18f + i * 0.27f, notes[i]);
        Game(SlotSynth.Sound.Blip, 30.99f, 3f);
        Game(SlotSynth.Sound.Blip, 31.26f, 4f);
        for (float t = 0f; t < 1.3f; t += Mathf.Lerp(0.075f, 0.035f, t / 1.3f))
            Game(SlotSynth.Sound.Tick, 31.53f + t, Mathf.Lerp(-3f, 12f, t / 1.3f), 0.55f);
        Game(SlotSynth.Sound.Payout, 32.83f, 0f, 0.45f);
        Game(SlotSynth.Sound.Slam, 32.83f);
        Game(SlotSynth.Sound.Jackpot, 32.83f, 0f, 0.8f, 0f, 35.0f);
        for (int i = 0; i < 14; i++) Game(SlotSynth.Sound.Coin, 33.3f + i * 0.11f, (i * 5) % 7 - 3, 0.3f);
        // 35–38.5秒：静寂と心音
        for (float t = 35.3f; t < 38.3f; t += 0.95f) Heart(t, 0.5f);
        Noise(36.45f, 0.25f, 0.18f, 0.35f);
        Riser(37.2f, 38.5f, 0.28f);

        // 38.5–46秒：追跡と爆発（42.4 でサンタが立ち止まり、42.55 から振りかぶって、43.25 に投げる）
        Stinger(38.5f, 0.35f);
        for (float t = 38.5f; t < 44.1f; t += 0.375f) Impact(t, 0.38f, 60f);
        for (float t = 38.7f; t < 42.4f; t += 0.31f) Step(t, 0.22f);
        for (float t = 42.6f; t < 44.1f; t += 0.5f) Step(t, 0.16f);
        Riser(42.55f, 43.25f, 0.2f);
        Whoosh(43.2f, 0.45f);
        for (float t = 38.5f; t < 44.1f; t += 0.75f) Bass(t, t < 41.5f ? 73.4f : 77.8f, 0.2f);
        for (float t = 39.0f; t < 44.1f; t += 0.55f) Heart(t, 0.28f);
        Riser(42.6f, 44.1f, 0.25f);
        Explosion(44.1f, 0.95f);
        Ring(44.3f, 1.6f, 0.07f);

        // 46–51.5秒：仲間
        for (float t = 46.1f; t < 49.7f; t += 0.36f) Impact(t, 0.25f, 65f);
        for (float t = 46.2f; t < 49.7f; t += 0.23f) Step(t, 0.14f);
        for (float t = 46f; t < 49.7f; t += 0.72f) Bass(t, 65.4f, 0.18f);
        Whoosh(48.4f, 0.4f);
        foreach (float t in new[] { 48.5f, 48.85f, 49.12f, 49.33f, 49.49f, 49.61f }) Blip(t, 1320f, 0.14f); // 箱の点滅
        Explosion(49.7f, 0.7f);

        // 51.5–55秒：静けさ → 捕まる
        for (float t = 51.7f; t < 52.9f; t += 0.55f) Step(t, 0.22f);
        Heart(52.0f, 0.4f); Heart(52.75f, 0.5f);
        Stinger(53.25f, 0.75f);
        Explosion(53.25f, 0.5f);
        Boom(54.2f, 0.6f);

        // 55–60秒：タイトル
        ReelRoll(55.2f, 56.4f);
        Thunk(55.1f + 0.0f); Thunk(55.75f); Thunk(56.4f);
        Boom(56.4f, 0.85f);
        float[] chord = { 110f, 164.8f, 220f, 261.6f, 329.6f };
        foreach (float f in chord) Bell(56.45f, f, 0.1f, 3.4f);
        Coin(57.9f, 0.18f);

        Finish(length);
        return mix;
    }

    // ───────── 音の部品 ─────────

    private delegate float Wave(float time);

    /// <summary>start から seconds 秒の間、wave の値を足す。pan は -1（左）〜 1（右）</summary>
    private static void Add(float start, float seconds, Wave wave, float pan = 0f)
    {
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.RoundToInt(seconds * SampleRate);
        float left = Mathf.Min(1f, 1f - pan), right = Mathf.Min(1f, 1f + pan);
        for (int i = 0; i < count; i++)
        {
            int n = (first + i) * 2;
            if (n < 0 || n + 1 >= mix.Length) continue;
            float v = wave(i / (float)SampleRate);
            mix[n] += v * left;
            mix[n + 1] += v * right;
        }
    }

    /// <summary>ゲームの効果音（SlotWinSounds）を重ねる。semitones で高さを変える。loopSeconds が 0 より大きければ、その長さだけ繰り返す。cut で打ち切る</summary>
    private static void Game(SlotSynth.Sound sound, float start, float semitones = 0f, float volume = 0.7f, float loopSeconds = 0f, float cut = 0f)
    {
        AudioClip clip = SlotWinSounds.Get(sound);
        float[] data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        int channels = clip.channels;
        double rate = clip.frequency * Math.Pow(2.0, semitones / 12.0);
        float length = loopSeconds > 0f ? loopSeconds : (float)(clip.samples / rate);
        if (cut > 0f) length = Mathf.Min(length, cut - start);
        float total = length;
        Add(start, total, t =>
        {
            int i = (int)(t * rate);
            if (loopSeconds > 0f) i %= clip.samples;
            if (i >= clip.samples) return 0f;
            return data[i * channels] * volume * Mathf.Min(1f, (total - t) / 0.05f);
        });
    }

    private static float Sin(float frequency, float time) { return Mathf.Sin((float)(2.0 * Math.PI * ((double)frequency * time % 1.0))); }
    private static float White() { return (float)(random.NextDouble() * 2.0 - 1.0); }

    /// <summary>高い音を削った雑音（smooth が小さいほど、こもる）</summary>
    private static Wave LowNoise(float smooth)
    {
        float last = 0f;
        return _ => { last += (White() - last) * smooth; return last; };
    }

    private static void Drone(float start, float end, float f, float volume)
    {
        float length = end - start;
        Wave wind = LowNoise(0.012f);
        Add(start, length, t =>
        {
            float env = Mathf.Min(1f, t / 0.8f) * Mathf.Min(1f, (length - t) / 0.5f);
            float swell = 0.8f + 0.2f * Sin(0.23f, t);
            return env * swell * volume * (Sin(f, t) + 0.6f * Sin(f * 1.007f, t) + 0.35f * Sin(f * 1.498f, t) + 0.15f * Sin(f * 2.01f, t) + wind(t) * 1.6f);
        });
    }

    private static void Boom(float start, float volume)
    {
        Wave n = LowNoise(0.03f);
        Add(start, 1.8f, t => volume * Mathf.Exp(-t * 2.6f) * (Mathf.Sin(2f * Mathf.PI * (34f * t + 40f * (1f - Mathf.Exp(-t * 9f)) / 9f)) + n(t) * 0.8f));
    }

    private static void Impact(float start, float volume, float f)
    {
        Wave n = LowNoise(0.15f);
        Add(start, 0.3f, t => volume * Mathf.Exp(-t * 16f) * (Sin(f * (1f + Mathf.Exp(-t * 30f)), t) + n(t) * 0.6f));
    }

    private static void Thunk(float start)
    {
        Wave n = LowNoise(0.35f);
        Add(start, 0.25f, t => 0.4f * Mathf.Exp(-t * 24f) * (Sin(130f, t) + n(t)));
        Add(start, 0.12f, t => 0.1f * Mathf.Exp(-t * 40f) * Sin(1900f, t));
    }

    private static void ReelRoll(float start, float end)
    {
        float t = start;
        int i = 0;
        while (t < end)
        {
            float progress = (t - start) / (end - start);
            Add(t, 0.04f, x => 0.12f * Mathf.Exp(-x * 120f) * (Sin(2400f, x) + White() * 0.5f), (i % 2 == 0) ? -0.25f : 0.25f);
            t += Mathf.Lerp(0.065f, 0.2f, progress * progress * progress);
            i++;
        }
    }

    private static void Step(float start, float volume)
    {
        Wave n = LowNoise(0.09f);
        Add(start, 0.16f, t => volume * 2.2f * Mathf.Exp(-t * 34f) * n(t), (float)(random.NextDouble() * 0.5 - 0.25));
    }

    private static void Noise(float start, float seconds, float volume, float smooth)
    {
        Wave n = LowNoise(smooth);
        Add(start, seconds, t => volume * n(t) * (random.NextDouble() < 0.7 ? 1f : 0f));
    }

    /// <summary>物が風を切る音</summary>
    private static void Whoosh(float start, float volume)
    {
        const float length = 0.45f;
        float last = 0f;
        Add(start, length, t =>
        {
            float swell = Mathf.Sin(Mathf.PI * t / length);
            last += (White() - last) * Mathf.Lerp(0.03f, 0.3f, swell);
            return volume * swell * swell * last * 2.2f;
        });
    }

    private static void Bass(float start, float f, float volume)
    {
        Add(start, 0.6f, t => volume * Mathf.Min(1f, t * 80f) * Mathf.Exp(-t * 5f) * (Sin(f, t) + 0.4f * Sin(f * 2f, t) + 0.2f * Sin(f * 3f, t)));
    }

    private static void Bell(float start, float f, float volume, float seconds)
    {
        Add(start, seconds, t => volume * Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 3.2f / seconds * 1.6f) * (Sin(f, t) + 0.35f * Sin(f * 2.001f, t) + 0.12f * Sin(f * 3.01f, t)));
    }

    private static void Blip(float start, float f, float volume)
    {
        Add(start, 0.1f, t => volume * Mathf.Exp(-t * 28f) * Sin(f, t));
    }

    private static void Coin(float start, float volume)
    {
        float pan = (float)(random.NextDouble() - 0.5);
        float f = 1750f + (float)random.NextDouble() * 500f;
        Add(start, 0.07f, t => volume * Mathf.Exp(-t * 35f) * Sin(f, t), pan);
        Add(start + 0.055f, 0.35f, t => volume * Mathf.Exp(-t * 11f) * Sin(f * 1.335f, t), pan);
    }

    private static void Heart(float start, float volume)
    {
        Add(start, 0.2f, t => volume * Mathf.Exp(-t * 22f) * Sin(52f, t));
        Add(start + 0.22f, 0.2f, t => volume * 0.7f * Mathf.Exp(-t * 24f) * Sin(46f, t));
    }

    private static void Riser(float start, float end, float volume)
    {
        float length = end - start;
        float last = 0f;
        Add(start, length, t =>
        {
            float k = t / length;
            last += (White() - last) * Mathf.Lerp(0.01f, 0.35f, k * k);
            float tone = Mathf.Sin(2f * Mathf.PI * (160f * t + 320f * t * t / length));
            return volume * k * k * (last * 1.4f + tone * 0.35f) * Mathf.Min(1f, (length - t) / 0.03f);
        });
    }

    private static void Stinger(float start, float volume)
    {
        float[] notes = { 233.1f, 246.9f, 329.6f, 349.2f, 698.5f, 739.9f };
        Add(start, 1.5f, t =>
        {
            float sum = 0f;
            foreach (float f in notes) sum += 2f * ((f * t) % 1f) - 1f; // のこぎり波
            return volume * 0.28f * Mathf.Exp(-t * 2.4f) * (sum + White() * Mathf.Exp(-t * 14f) * 2f);
        });
    }

    private static void Explosion(float start, float volume)
    {
        Wave low = LowNoise(0.025f);
        Wave high = LowNoise(0.4f);
        Add(start, 2.2f, t => volume * (low(t) * 3.2f * Mathf.Exp(-t * 2.2f) + high(t) * Mathf.Exp(-t * 9f) + Sin(38f, t) * Mathf.Exp(-t * 3f)));
    }

    private static void Ring(float start, float seconds, float volume)
    {
        Add(start, seconds, t => volume * Mathf.Min(1f, t * 4f) * Mathf.Min(1f, (seconds - t) * 2f) * Sin(3150f, t));
    }

    /// <summary>大きすぎる所をなだらかに抑えて、最後を消していく</summary>
    private static void Finish(float length)
    {
        for (int i = 0; i < mix.Length; i++)
        {
            float t = (i / 2) / (float)SampleRate;
            float fade = Mathf.Clamp01((length - t) / 0.8f) * Mathf.Clamp01(t / 0.2f);
            mix[i] = (float)Math.Tanh(mix[i] * 1.1f) * 0.92f * Master * fade;
        }
    }
}
