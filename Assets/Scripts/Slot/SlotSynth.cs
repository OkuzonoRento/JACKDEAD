using System;

/// <summary>
/// スロットの効果音の波形を作る（素材を使わない）。鉄の機械の打音と、少し歪んだ電子音。
/// 高さは、重い鉄の音と実機のスロットの音の中間。かわいく聞こえないように、明るい和音は使わない。曲はすべてこのゲーム用に作ったもの。
/// UnityEngine を使わないので、Unity の外でも音を書き出して確認できる。
/// </summary>
public static class SlotSynth
{
    public const int SampleRate = 44100;

    // 全体の高さ。重くしたいときは下げ、軽くしたいときは上げる
    private const float NoteBase = 220f;     // 電子音・和音の基準の高さ（Hz）
    private const float ImpactPitch = 1.55f; // 打音・鉄の音・メダルの高さ（倍率）

    public enum Sound
    {
        Start,      // ボタンを押したとき「てっててっててん」
        Lever,      // リールが回り出す
        SpinLoop,   // 回っている間（つなぎ目なしで繰り返せる）
        ReelStop,   // リールが止まる
        Blip,       // そろった目を数える（高さを変えて鳴らす）
        Tick,       // 払い出しの数え上げ
        Payout,     // 金属の受け皿にメダルがなだれ込む
        Coin,       // コインが床で跳ねる
        Lose,       // ハズレ
        Reroll,     // 再ロール
        Slam,       // 大当たりが出る瞬間
        BigWin,
        Jackpot
    }

    /// <summary>「てっててっててん」の各音が鳴る時刻（秒）</summary>
    public static readonly float[] StartRhythm = { 0f, 0.17f, 0.255f, 0.425f, 0.51f };

    public static float[] Build(Sound sound)
    {
        switch (sound)
        {
            case Sound.Start: return Start();
            case Sound.Lever: return Lever();
            case Sound.SpinLoop: return SpinLoop();
            case Sound.ReelStop: return ReelStop();
            case Sound.Blip: return Blip();
            case Sound.Tick: return Tick();
            case Sound.Payout: return Payout();
            case Sound.Coin: return Coin();
            case Sound.Lose: return Lose();
            case Sound.Reroll: return Reroll();
            case Sound.Slam: return Slam();
            case Sound.BigWin: return BigWin();
            default: return Jackpot();
        }
    }

    // ───────── 回し始め ─────────

    private static float[] Start()
    {
        float[] d = New(1.2f);
        Random r = new Random(7);
        // ラ・ラ・ド・ラ・ミ♭（最後は半端に下がる）
        int[] notes = { 0, 0, 3, 0, 6 };
        float[] lengths = { 0.12f, 0.07f, 0.12f, 0.07f, 0.5f };
        for (int i = 0; i < notes.Length; i++)
        {
            bool last = i == notes.Length - 1;
            float at = StartRhythm[i];
            Chord(d, at, Note(notes[i]), last ? Note(notes[i]) * 0.95f : Note(notes[i]), lengths[i], 0.5f);
            Thud(d, at, 130f, 45f, last ? 0.35f : 0.09f, last ? 1f : 0.6f);
            Clang(d, r, at, last ? 190f : 260f, last ? 0.12f : 0.025f, last ? 0.35f : 0.25f);
        }
        return Finish(d, 0.75f, 0.2f);
    }

    private static float[] Lever()
    {
        float[] d = New(0.55f);
        Random r = new Random(11);
        for (int i = 0; i < 3; i++) Click(d, r, i * 0.028f, 0.35f + i * 0.2f); // 爪がかかる
        Clang(d, r, 0.085f, 150f, 0.07f, 0.8f);                                  // ガシャン
        Thud(d, 0.085f, 140f, 38f, 0.22f, 1.1f);
        Noise(d, r, 0.085f, 0.1f, 0.5f, 0.2f, 0f);
        Grind(d, 0.12f, 80f, 190f, 0.4f, 0.22f, 1.6f, 1800f);                        // ゴゴゴと回り出す
        return Finish(d, 0.75f, 0.16f);
    }

    private static float[] SpinLoop()
    {
        // 0.8 秒ぶん作って、後ろ半分を使う（前の打音の余韻が入るので、つなぎ目が分からない）
        float[] d = New(0.8f);
        for (int i = 0; i < 16; i++)
        {
            Random r = new Random(100 + i % 8);
            float at = i * 0.05f;
            Click(d, r, at, i % 2 == 0 ? 0.35f : 0.2f);
            Clang(d, r, at, i % 4 == 0 ? 230f : 300f, 0.012f, 0.28f);
        }
        for (int i = 0; i < d.Length; i++)
        {
            double t = i / (double)SampleRate;
            // 低いうなり（どれも 0.4 秒でちょうど整数回）
            double motor = Math.Sin(2 * Math.PI * 90 * t) * 0.2 + Math.Sin(2 * Math.PI * 95 * t) * 0.16
                + Math.Tanh(Math.Sin(2 * Math.PI * 180 * t) * 2) * 0.05;
            d[i] += (float)motor;
        }
        float[] loop = new float[d.Length / 2];
        Array.Copy(d, loop.Length, loop, 0, loop.Length);
        Normalize(loop, 0.45f);
        return loop;
    }

    private static float[] ReelStop()
    {
        float[] d = New(0.4f);
        Random r = new Random(31);
        Click(d, r, 0f, 0.9f);
        Thud(d, 0f, 160f, 36f, 0.2f, 1.3f);
        Clang(d, r, 0.002f, 175f, 0.05f, 0.75f);
        Noise(d, r, 0.002f, 0.09f, 0.55f, 0.25f, 0f);
        Click(d, r, 0.052f, 0.3f);
        return Finish(d, 0.85f, 0.14f);
    }

    private static float[] Lose()
    {
        // 低いブザー
        float[] d = New(0.7f);
        Grind(d, 0f, 150f, 138f, 0.4f, 0.5f, 2.5f, 2200f);
        Grind(d, 0f, 159f, 146f, 0.4f, 0.35f, 2.5f, 2200f);
        Thud(d, 0f, 90f, 35f, 0.3f, 0.7f);
        return Finish(d, 0.55f, 0.18f);
    }

    // ───────── 当たり ─────────

    private static float[] Blip()
    {
        // 鉄を打つ「ガンッ」。高さを変えて、半音ずつ追い詰めるように上げる
        float[] d = New(0.4f);
        Random r = new Random(37);
        Click(d, r, 0f, 0.5f);
        Chord(d, 0f, Note(12), Note(12), 0.16f, 0.45f);
        Clang(d, r, 0f, 330f, 0.07f, 0.55f);
        Thud(d, 0f, 150f, 55f, 0.1f, 0.6f);
        return Finish(d, 0.65f, 0.16f);
    }

    private static float[] Tick()
    {
        // 数取り器の「ガチ」
        float[] d = New(0.08f);
        Random r = new Random(39);
        Click(d, r, 0f, 0.8f);
        Clang(d, r, 0f, 420f, 0.009f, 0.6f);
        Beep(d, 0f, Note(12), Note(12), 0.04f, 0.45f);
        return Finish(d, 0.5f, 0.03f);
    }

    private static float[] Payout()
    {
        float[] d = New(1.4f);
        Random r = new Random(41);
        Clang(d, r, 0f, 260f, 0.16f, 0.7f);             // ガシャーン
        Thud(d, 0f, 130f, 40f, 0.25f, 0.9f);
        Noise(d, r, 0f, 0.18f, 0.35f, 0.3f, 0f);
        for (int i = 0; i < 26; i++)                    // ジャラジャラ
        {
            float at = 0.04f + i * 0.034f + (float)r.NextDouble() * 0.025f;
            Medal(d, r, at, 0.5f * (1f - i / 34f), 0.55f);
        }
        return Finish(d, 0.6f, 0.16f);
    }

    private static float[] Coin()
    {
        float[] d = New(0.22f);
        Random r = new Random(43);
        Medal(d, r, 0f, 0.8f, 0.62f);
        return Finish(d, 0.4f, 0.08f);
    }

    private static float[] Reroll()
    {
        // 鎖を巻き戻すような音
        float[] d = New(0.85f);
        Random r = new Random(47);
        float at = 0f;
        for (int i = 0; i < 14; i++)
        {
            Click(d, r, at, 0.4f);
            Clang(d, r, at, 240f + i * 14f, 0.012f, 0.4f);
            at += 0.06f - i * 0.0027f;
        }
        Grind(d, 0f, 110f, 360f, 0.6f, 0.26f, 2f, 3000f);
        Clang(d, r, 0.6f, 200f, 0.08f, 0.8f);
        Thud(d, 0.6f, 130f, 40f, 0.18f, 0.9f);
        return Finish(d, 0.65f, 0.16f);
    }

    private static float[] Slam()
    {
        // 鉄の扉が落ちるような一撃
        float[] d = New(1.2f);
        Random r = new Random(51);
        Click(d, r, 0f, 1f);
        Clang(d, r, 0.002f, 98f, 0.2f, 1f);
        Clang(d, r, 0.002f, 285f, 0.07f, 0.55f);
        Thud(d, 0f, 170f, 27f, 0.9f, 1.6f);
        Noise(d, r, 0f, 0.35f, 0.4f, 0.12f, 0f);
        return Finish(d, 0.52f, 0.25f);
    }

    private static float[] BigWin()
    {
        // 重い太鼓と、歪んだ和音（このゲーム用の曲）
        float[] d = New(2.5f);
        Random r = new Random(61);
        float beat = 0.15f;
        int[] notes = { 0, 0, 3, 0, 5, 6, 7 };
        float[] when = { 0, 1, 2, 3, 4, 5, 6 };
        for (int i = 0; i < notes.Length; i++)
        {
            float at = 0.06f + when[i] * beat;
            float length = i == notes.Length - 1 ? 1.2f : beat * 0.85f;
            Chord(d, at, Note(notes[i] + 12), Note(notes[i] + 12), length, 0.42f);
            Kick(d, r, at);
            if (i % 2 == 1) Hit(d, r, at);
        }
        Clang(d, r, 0.06f + 6f * beat, 210f, 0.3f, 0.6f);
        for (int i = 0; i < 30; i++) Medal(d, r, 0.9f + i * 0.045f + (float)r.NextDouble() * 0.03f, 0.2f, 0.58f);
        return Finish(d, 0.48f, 0.18f);
    }

    private static float[] Jackpot()
    {
        float[] d = New(4.9f);
        Random r = new Random(71);

        // 警報（低い 2 音が交互に鳴る）
        for (int i = 0; i < 6; i++)
        {
            float frequency = i % 2 == 0 ? Note(24) : Note(18);
            Grind(d, i * 0.24f, frequency * 0.97f, frequency, 0.23f, 0.26f, 2f, 4200f);
            Beep(d, i * 0.24f, frequency * 1.015f, frequency * 1.015f, 0.23f, 0.24f);
            Kick(d, r, i * 0.24f);
        }

        // 行進するような、重いくり返し
        float beat = 0.165f, start = 1.48f;
        int[] riff = { 0, 0, 12, 0, 3, 0, 6, 5, 0, 0, 12, 0, 8, 7, 6 };
        for (int i = 0; i < riff.Length; i++)
        {
            float at = start + i * beat;
            Chord(d, at, Note(riff[i] + 12), Note(riff[i] + 12), beat * 0.85f, 0.36f);
            if (i % 2 == 0) Kick(d, r, at);
            else Hit(d, r, at);
        }
        float last = start + riff.Length * beat;
        Chord(d, last, Note(12), Note(12) * 0.94f, 0.85f, 0.45f);
        Chord(d, last, Note(18), Note(18) * 0.94f, 0.85f, 0.25f);
        Clang(d, r, last, 110f, 0.35f, 0.8f);
        Thud(d, last, 150f, 30f, 0.8f, 1.2f);

        for (int i = 0; i < 110; i++) Medal(d, r, 0.2f + i * 0.037f + (float)r.NextDouble() * 0.03f, 0.17f, 0.58f);
        return Finish(d, 0.48f, 0.18f);
    }

    // ───────── 部品 ─────────

    private static float[] New(float seconds)
    {
        return new float[(int)(seconds * SampleRate)];
    }

    /// <summary>基準のラ（NoteBase）から半音いくつ上か → 周波数</summary>
    private static float Note(int semitones)
    {
        return NoteBase * (float)Math.Pow(2.0, semitones / 12.0);
    }

    /// <summary>歪んだ和音（根音・5度・オクターブ）</summary>
    private static void Chord(float[] d, float start, float from, float to, float length, float volume)
    {
        Grind(d, start, from, to, length, volume * 0.8f, 2.2f, 3800f);
        Grind(d, start, from * 1.498f, to * 1.498f, length, volume * 0.4f, 2f, 3800f);
        Beep(d, start, from * 2f, to * 2f, length, volume * 0.5f);
    }

    /// <summary>ざらついた太い音（のこぎり波を歪ませて、高い成分を cutoff より上で削る）。from から to へ高さを動かす</summary>
    private static void Grind(float[] d, float start, float from, float to, float length, float volume, float drive, float cutoff)
    {
        float[] part = New(length);
        double phase = 0;
        for (int i = 0; i < part.Length; i++)
        {
            float k = i / (float)part.Length;
            double frequency = from + (to - from) * k;
            phase += frequency / SampleRate;
            double saw = 0;
            for (int n = 1; n <= 24 && frequency * n < SampleRate * 0.45; n++) saw += Math.Sin(2 * Math.PI * phase * n) / n;
            double envelope = Math.Min(1.0, i / (0.004 * SampleRate)) * Math.Min(1.0, (1 - k) * 6) * (1 - 0.45 * k);
            part[i] = (float)(Math.Tanh(saw * drive) * envelope);
        }
        Biquad(part, cutoff, 0.8f);

        int begin = (int)(start * SampleRate);
        for (int i = 0; i < part.Length && begin + i < d.Length; i++) d[begin + i] += part[i] * volume;
    }

    /// <summary>実機の電子音（角の丸い四角い波）</summary>
    private static void Beep(float[] d, float start, float from, float to, float length, float volume)
    {
        int begin = (int)(start * SampleRate);
        int count = (int)(length * SampleRate);
        double phase = 0;
        for (int i = 0; i < count && begin + i < d.Length; i++)
        {
            float k = i / (float)count;
            phase += (from + (to - from) * k) / SampleRate;
            double wave = Math.Sin(2 * Math.PI * phase) + Math.Sin(6 * Math.PI * phase) * 0.3 + Math.Sin(10 * Math.PI * phase) * 0.15;
            double envelope = Math.Min(1.0, i / (0.003 * SampleRate)) * Math.Min(1.0, (1 - k) * 6) * (1 - 0.35 * k);
            d[begin + i] += (float)(wave * envelope) * volume;
        }
    }

    /// <summary>鉄の音（倍音がそろっていない）</summary>
    private static void Clang(float[] d, Random r, float start, float frequency, float decay, float volume)
    {
        float[] ratio = { 1f, 1.41f, 1.93f, 2.58f, 3.47f, 5.11f };
        for (int n = 0; n < ratio.Length; n++)
            Ring(d, r, start, frequency * ImpactPitch * ratio[n], decay / (1f + n * 0.35f), volume / (1f + n * 0.4f));
    }

    private static void Kick(float[] d, Random r, float start)
    {
        Thud(d, start, 150f, 42f, 0.16f, 1.1f);
        Noise(d, r, start, 0.02f, 0.35f, 0.4f, 0f);
    }

    /// <summary>鉄板をたたく拍</summary>
    private static void Hit(float[] d, Random r, float start)
    {
        Noise(d, r, start, 0.11f, 0.45f, 0.55f, 0.7f);
        Clang(d, r, start, 240f, 0.03f, 0.4f);
    }

    /// <summary>部品がたたかれて響く音（決まった高さで震える）</summary>
    private static void Ring(float[] d, Random r, float start, float frequency, float decay, float volume)
    {
        int begin = (int)(start * SampleRate);
        int count = (int)(decay * 5f * SampleRate);
        int burst = (int)(0.002f * SampleRate);
        double radius = Math.Exp(-1.0 / (decay * SampleRate));
        double a = 2 * radius * Math.Cos(2 * Math.PI * frequency / SampleRate), b = -radius * radius;
        double gain = Math.Sin(2 * Math.PI * frequency / SampleRate) * 0.12;
        double y1 = 0, y2 = 0;
        for (int i = 0; i < count && begin + i < d.Length; i++)
        {
            double x = i < burst ? r.NextDouble() * 2 - 1 : 0;
            double y = a * y1 + b * y2 + x * gain;
            y2 = y1;
            y1 = y;
            d[begin + i] += (float)y * volume;
        }
    }

    /// <summary>メダル 1 枚が金属の皿に当たる音。tone を下げるほど、重い鉄の音になる</summary>
    private static void Medal(float[] d, Random r, float start, float volume, float tone)
    {
        float pitch = tone * ImpactPitch * (0.85f + (float)r.NextDouble() * 0.35f);
        Ring(d, r, start, 2650f * pitch, 0.022f, volume);
        Ring(d, r, start, 4300f * pitch, 0.015f, volume * 0.6f);
        Ring(d, r, start, 1230f * pitch, 0.018f, volume * 0.6f);
        Noise(d, r, start, 0.004f, volume * 0.35f, 0.9f, 0.9f);
    }

    /// <summary>スイッチや爪が当たる「ガチッ」</summary>
    private static void Click(float[] d, Random r, float start, float volume)
    {
        Noise(d, r, start, 0.009f, volume, 0.55f, 0.8f);
        Ring(d, r, start, 1450f * ImpactPitch, 0.005f, volume * 0.5f);
    }

    /// <summary>低い「ドン」。高さを from から to へ下げる</summary>
    private static void Thud(float[] d, float start, float from, float to, float length, float volume)
    {
        int begin = (int)(start * SampleRate);
        int count = (int)(length * SampleRate);
        double phase = 0;
        for (int i = 0; i < count && begin + i < d.Length; i++)
        {
            float k = i / (float)count;
            phase += (to + (from - to) * Math.Exp(-6 * k)) * ImpactPitch / SampleRate;
            float envelope = (float)Math.Exp(-4.5 * k) * (1f - k) * Math.Min(1f, i / 60f);
            d[begin + i] += (float)Math.Sin(2 * Math.PI * phase) * volume * 0.75f * envelope;
        }
    }

    /// <summary>雑音。low は高い成分の残し方（1 でそのまま）、high は低い成分の削り方</summary>
    private static void Noise(float[] d, Random r, float start, float length, float volume, float low, float high)
    {
        int begin = (int)(start * SampleRate);
        int count = (int)(length * SampleRate);
        float smooth = 0f, slow = 0f;
        for (int i = 0; i < count && begin + i < d.Length; i++)
        {
            float k = i / (float)count;
            float white = (float)(r.NextDouble() * 2 - 1);
            smooth += (white - smooth) * low;
            slow += (smooth - slow) * 0.2f;
            float value = smooth - slow * high;
            d[begin + i] += value * volume * (float)Math.Exp(-4 * k) * (low < 0.5f ? 2f : 1f);
        }
    }

    /// <summary>高い成分を削る</summary>
    private static void Biquad(float[] d, float frequency, float q)
    {
        double w = 2 * Math.PI * frequency / SampleRate;
        double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
        double b0 = (1 - cos) / 2, b1 = 1 - cos, a0 = 1 + alpha, a1 = -2 * cos, a2 = 1 - alpha;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < d.Length; i++)
        {
            double x = d[i];
            double y = (b0 * x + b1 * x1 + b0 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            d[i] = (float)y;
        }
    }

    /// <summary>仕上げ：部屋の響き（room は混ぜる量）を足して、大きさをそろえる</summary>
    private static float[] Finish(float[] d, float peak, float room)
    {
        if (room > 0f) Reverb(d, room);
        Biquad(d, 9500f, 0.71f);
        Normalize(d, peak);

        // 終わりでプツッと言わないように、最後を絞る
        int fade = Math.Min(d.Length, SampleRate / 50);
        for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
        return d;
    }

    private static void Normalize(float[] d, float peak)
    {
        float max = 0f;
        foreach (float v in d) max = Math.Max(max, Math.Abs(v));
        if (max <= 0f) return;

        // 少し歪ませて、太い音にする
        for (int i = 0; i < d.Length; i++) d[i] = (float)(Math.Tanh(d[i] / max * 1.4) / Math.Tanh(1.4)) * peak;
    }

    /// <summary>響き。長さの違うくり返しを重ねて、ぼかす</summary>
    private static void Reverb(float[] d, float amount)
    {
        float[] wet = new float[d.Length];
        float[] times = { 0.0297f, 0.0371f, 0.0411f, 0.0437f };
        foreach (float time in times)
        {
            float[] line = new float[(int)(time * SampleRate)];
            float damp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                int at = i % line.Length;
                float echo = line[at];
                damp += (echo - damp) * 0.45f; // 響くほど、こもる
                line[at] = d[i] + damp * 0.78f;
                wet[i] += echo * 0.25f;
            }
        }

        float[] pass = new float[(int)(0.0053f * SampleRate)];
        for (int i = 0; i < d.Length; i++)
        {
            int at = i % pass.Length;
            float delayed = pass[at];
            float value = delayed - wet[i] * 0.6f;
            pass[at] = wet[i] + value * 0.6f;
            d[i] += value * amount;
        }
    }
}
