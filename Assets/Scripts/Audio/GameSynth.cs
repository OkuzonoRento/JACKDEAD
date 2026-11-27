using System;

/// <summary>ゲームの効果音と曲の名前。素材に差し替えるときは、Assets/Resources/GameSounds に同じ名前の音声ファイルを置く</summary>
public enum GameSound
{
    // 自分と仲間の体
    Step1, Step2, Step3, Step4, Jump, Land, Hurt, Death,
    // 物
    Pickup, Throw, SlotSwitch, ImpactMetal, ImpactWood, ScrapDamage, BoxBeep, Explosion,
    // サンタ（敵）
    EnemyStep1, EnemyStep2, Bells, EnemyLaugh, EnemyChuckle, EnemyGrunt, EnemyThrow,
    // 場面
    Lever, TruckStart, StageStart, StageClear, GameOver, CountTick,
    // 画面の操作
    UiHover, UiClick,
    // 遠くの物音
    Creak1, Creak2, FarBell,
    // 追跡
    ChaseStinger,
    // ここから下は、くり返す曲
    MusicTitle, MusicAmbience, MusicChase, Heartbeat
}

/// <summary>
/// ゲームの効果音と曲の波形を作る（素材を使わない）。雰囲気は、夜の古い旅館・借金・サンタ：
/// 木のきしみ、錆びた鉄、調子の外れたオルゴール、お寺の鐘、そりの鈴。曲はすべてこのゲーム用に作ったもの。
/// 部品は SynthKit。UnityEngine を使わないので、Unity の外でも音を書き出して確認できる。
/// </summary>
public static class GameSynth
{
    public const int SampleRate = SynthKit.SampleRate;

    public static bool IsLoop(GameSound sound) { return sound >= GameSound.MusicTitle; }

    private static float N(int semitones) { return SynthKit.Note(semitones); }

    public static float[] Build(GameSound sound)
    {
        switch (sound)
        {
            case GameSound.Step1: return Step(101, 150f);
            case GameSound.Step2: return Step(102, 128f);
            case GameSound.Step3: return Step(103, 171f);
            case GameSound.Step4: return Step(104, 139f);
            case GameSound.Jump: return Jump();
            case GameSound.Land: return Land();
            case GameSound.Hurt: return Hurt();
            case GameSound.Death: return Death();
            case GameSound.Pickup: return Pickup();
            case GameSound.Throw: return Throw();
            case GameSound.SlotSwitch: return SlotSwitch();
            case GameSound.ImpactMetal: return ImpactMetal();
            case GameSound.ImpactWood: return ImpactWood();
            case GameSound.ScrapDamage: return ScrapDamage();
            case GameSound.BoxBeep: return BoxBeep();
            case GameSound.Explosion: return Explosion();
            case GameSound.EnemyStep1: return EnemyStep(201, 82f);
            case GameSound.EnemyStep2: return EnemyStep(202, 70f);
            case GameSound.Bells: return Bells();
            case GameSound.EnemyLaugh: return Laugh(301, 3, 118f, 0.27f);
            case GameSound.EnemyChuckle: return Laugh(302, 2, 82f, 0.42f);
            case GameSound.EnemyGrunt: return Grunt();
            case GameSound.EnemyThrow: return EnemyThrow();
            case GameSound.Lever: return Lever();
            case GameSound.TruckStart: return TruckStart();
            case GameSound.StageStart: return StageStart();
            case GameSound.StageClear: return StageClear();
            case GameSound.GameOver: return GameOver();
            case GameSound.CountTick: return CountTick();
            case GameSound.UiHover: return UiHover();
            case GameSound.UiClick: return UiClick();
            case GameSound.Creak1: return Creak(401, 330f, 1.1f);
            case GameSound.Creak2: return Creak(402, 215f, 1.6f);
            case GameSound.FarBell: return FarBell();
            case GameSound.ChaseStinger: return ChaseStinger();
            case GameSound.MusicTitle: return MusicTitle();
            case GameSound.MusicAmbience: return MusicAmbience();
            case GameSound.MusicChase: return MusicChase();
            default: return Heartbeat();
        }
    }

    /// <summary>曲に左右の広がりを付ける（右を少し遅らせる）。くり返しがつながるよう、はみ出た分は先頭へ回す。戻り値は左右が交互</summary>
    public static float[] Widen(float[] mono, float seconds = 0.013f)
    {
        float[] stereo = new float[mono.Length * 2];
        int delay = (int)(seconds * SampleRate);
        for (int i = 0; i < mono.Length; i++)
        {
            float late = mono[(i - delay + mono.Length) % mono.Length];
            stereo[i * 2] = mono[i] * 0.85f + late * 0.15f;
            stereo[i * 2 + 1] = late * 0.85f + mono[i] * 0.15f;
        }
        return stereo;
    }

    // ───────── 自分と仲間の体 ─────────

    private static float[] Step(int seed, float pitch)
    {
        // 古い板張りの床：鈍い音と、小さなきしみ
        float[] d = SynthKit.New(0.28f);
        Random r = new Random(seed);
        SynthKit.Thud(d, 0f, pitch, pitch * 0.5f, 0.12f, 0.9f);
        SynthKit.Knock(d, r, 0.004f, pitch * 1.9f, 0.022f, 0.35f);
        SynthKit.Noise(d, r, 0.002f, 0.07f, 0.22f, 2600f, 300f, 0.002f, 55f);
        SynthKit.Partials(d, 0.035f, pitch * 4.6f + seed % 4 * 40f, 0.025f, 0.07f, new[] { 1f, 1.51f }, new[] { 0.6f, 0.4f }, 0.012f);
        SynthKit.Reverb(d, 0.12f, 0.7f);
        return SynthKit.Finish(d, 0.55f, 1.1f, 0.04f);
    }

    private static float[] Jump()
    {
        float[] d = SynthKit.New(0.3f);
        Random r = new Random(111);
        SynthKit.Thud(d, 0f, 130f, 70f, 0.1f, 0.6f);
        SynthKit.Whoosh(d, r, 0.02f, 0.22f, 0.35f, 200f, 1400f);
        return SynthKit.Finish(d, 0.35f, 1f, 0.05f);
    }

    private static float[] Land()
    {
        float[] d = SynthKit.New(0.55f);
        Random r = new Random(113);
        SynthKit.Thud(d, 0f, 120f, 36f, 0.35f, 1.2f);
        SynthKit.Knock(d, r, 0.003f, 190f, 0.07f, 0.8f);
        SynthKit.Noise(d, r, 0.002f, 0.2f, 0.3f, 1800f, 150f, 0.002f, 18f);
        SynthKit.Reverb(d, 0.2f, 0.74f);
        return SynthKit.Finish(d, 0.7f, 1.2f, 0.08f);
    }

    private static float[] Hurt()
    {
        // 体に響く衝撃と、耳鳴り
        float[] d = SynthKit.New(1.1f);
        Random r = new Random(121);
        SynthKit.Thud(d, 0f, 170f, 34f, 0.45f, 1.3f);
        SynthKit.Noise(d, r, 0f, 0.16f, 0.6f, 3200f, 120f, 0.001f, 22f);
        SynthKit.Voice(d, r, 0.03f, 0.28f, 150f, 105f, new[] { 620f, 1050f }, 0.45f, 0.35f);
        SynthKit.Tone(d, 0.08f, 0.95f, 3150f, 3120f, 0.07f, 0.08f, 2.5f);
        SynthKit.Reverb(d, 0.14f, 0.72f);
        return SynthKit.Finish(d, 0.75f, 1.35f, 0.2f);
    }

    private static float[] Death()
    {
        float[] d = SynthKit.New(3.6f);
        Random r = new Random(123);
        SynthKit.Thud(d, 0f, 180f, 24f, 1.2f, 1.4f);
        SynthKit.Noise(d, r, 0f, 0.6f, 0.45f, 2200f, 60f, 0.001f, 7f);
        SynthKit.Voice(d, r, 0.05f, 0.9f, 175f, 70f, new[] { 560f, 880f, 2400f }, 0.5f, 0.4f);
        SynthKit.Tone(d, 0.2f, 2.6f, 2900f, 2860f, 0.06f, 0.2f, 1f);
        SynthKit.Strings(d, 0.1f, 3.2f, N(-40), 0.35f, 300f, 0.1f, 2f, 0f);
        SynthKit.TempleBell(d, 0.9f, 73f, 1.2f, 0.55f);
        SynthKit.Reverb(d, 0.35f, 0.82f);
        return SynthKit.Finish(d, 0.75f, 1.25f, 0.7f);
    }

    // ───────── 物 ─────────

    private static float[] Pickup()
    {
        float[] d = SynthKit.New(0.45f);
        Random r = new Random(131);
        SynthKit.Whoosh(d, r, 0f, 0.14f, 0.3f, 500f, 2800f);     // つかむ
        SynthKit.Coin(d, r, 0.07f, 0.5f, 0.75f);
        SynthKit.Coin(d, r, 0.13f, 0.35f, 1.0f);
        SynthKit.Knock(d, r, 0.075f, 330f, 0.03f, 0.35f);
        SynthKit.Reverb(d, 0.18f, 0.72f);
        return SynthKit.Finish(d, 0.5f, 1f, 0.08f);
    }

    private static float[] Throw()
    {
        float[] d = SynthKit.New(0.4f);
        Random r = new Random(133);
        SynthKit.Whoosh(d, r, 0f, 0.36f, 0.8f, 180f, 2200f);
        return SynthKit.Finish(d, 0.45f, 1f, 0.05f);
    }

    private static float[] SlotSwitch()
    {
        float[] d = SynthKit.New(0.09f);
        Random r = new Random(135);
        SynthKit.Knock(d, r, 0f, 720f, 0.01f, 0.7f);
        return SynthKit.Finish(d, 0.3f, 1f, 0.02f);
    }

    private static float[] ImpactMetal()
    {
        float[] d = SynthKit.New(0.7f);
        Random r = new Random(141);
        SynthKit.Partials(d, 0f, 390f, 0.12f, 0.7f, new[] { 1f, 1.42f, 2.15f, 3.31f, 4.7f }, new[] { 0.6f, 0.5f, 0.3f, 0.2f, 0.1f }, 0.0008f);
        SynthKit.Thud(d, 0f, 140f, 60f, 0.14f, 0.7f);
        SynthKit.Noise(d, r, 0f, 0.05f, 0.4f, 6000f, 600f, 0.0005f, 70f);
        SynthKit.Reverb(d, 0.22f, 0.76f);
        return SynthKit.Finish(d, 0.65f, 1.2f, 0.1f);
    }

    private static float[] ImpactWood()
    {
        float[] d = SynthKit.New(0.45f);
        Random r = new Random(143);
        SynthKit.Knock(d, r, 0f, 175f, 0.07f, 1f);
        SynthKit.Thud(d, 0f, 110f, 50f, 0.2f, 0.8f);
        SynthKit.Knock(d, r, 0.05f, 320f, 0.02f, 0.3f);
        SynthKit.Reverb(d, 0.2f, 0.74f);
        return SynthKit.Finish(d, 0.65f, 1.15f, 0.08f);
    }

    private static float[] ScrapDamage()
    {
        // 部品が欠ける
        float[] d = SynthKit.New(0.5f);
        Random r = new Random(145);
        for (int i = 0; i < 9; i++)
        {
            float at = i * 0.013f + (float)r.NextDouble() * 0.012f;
            SynthKit.Coin(d, r, at, 0.4f, 0.45f + (float)r.NextDouble() * 0.8f);
        }
        SynthKit.Noise(d, r, 0f, 0.2f, 0.55f, 7000f, 900f, 0.001f, 18f);
        SynthKit.Reverb(d, 0.15f, 0.72f);
        return SynthKit.Finish(d, 0.55f, 1.2f, 0.08f);
    }

    private static float[] BoxBeep()
    {
        // 爆弾の入った箱の、鈴まじりの「チッ」
        float[] d = SynthKit.New(0.16f);
        Random r = new Random(151);
        SynthKit.Partials(d, 0f, 1480f, 0.03f, 0.7f, new[] { 1f, 1.5f, 2.76f }, new[] { 0.7f, 0.35f, 0.15f }, 0.0008f);
        SynthKit.Knock(d, r, 0f, 620f, 0.008f, 0.35f);
        return SynthKit.Finish(d, 0.5f, 1.15f, 0.03f);
    }

    private static float[] Explosion()
    {
        float[] d = SynthKit.New(3.0f);
        Random r = new Random(161);
        SynthKit.Noise(d, r, 0f, 0.12f, 1f, 9000f, 80f, 0.001f, 25f);     // 裂ける音
        SynthKit.Noise(d, r, 0f, 2.6f, 0.9f, 420f, 20f, 0.002f, 2.1f);    // 地響き
        SynthKit.Thud(d, 0f, 140f, 22f, 1.4f, 1.5f);
        for (int i = 0; i < 40; i++)                                      // 破片
        {
            float at = 0.12f + (float)r.NextDouble() * 1.1f;
            SynthKit.Knock(d, r, at, 250f + (float)r.NextDouble() * 900f, 0.012f, 0.18f * (1.3f - at));
        }
        SynthKit.SleighBells(d, r, 0.05f, 0.7f, 0.12f);                    // 贈り物の箱の鈴が飛び散る
        SynthKit.Reverb(d, 0.28f, 0.8f);
        return SynthKit.Finish(d, 0.9f, 1.8f, 0.5f);
    }

    // ───────── サンタ ─────────

    private static float[] EnemyStep(int seed, float pitch)
    {
        // 重いブーツと、床の沈むきしみ
        float[] d = SynthKit.New(0.55f);
        Random r = new Random(seed);
        SynthKit.Thud(d, 0f, pitch * 1.5f, pitch * 0.45f, 0.3f, 1.3f);
        SynthKit.Knock(d, r, 0.006f, pitch * 2.2f, 0.055f, 0.6f);
        SynthKit.Noise(d, r, 0.003f, 0.12f, 0.25f, 1500f, 100f, 0.002f, 30f);
        SynthKit.Partials(d, 0.06f, pitch * 5.2f, 0.06f, 0.08f, new[] { 1f, 1.47f, 2.2f }, new[] { 0.6f, 0.4f, 0.2f }, 0.03f);
        SynthKit.SleighBells(d, r, 0.01f, 0.09f, 0.06f);
        SynthKit.Reverb(d, 0.2f, 0.74f);
        return SynthKit.Finish(d, 0.8f, 1.3f, 0.1f);
    }

    private static float[] Bells()
    {
        float[] d = SynthKit.New(0.5f);
        Random r = new Random(211);
        SynthKit.SleighBells(d, r, 0f, 0.28f, 0.7f);
        SynthKit.Reverb(d, 0.25f, 0.76f);
        return SynthKit.Finish(d, 0.45f, 1f, 0.1f);
    }

    /// <summary>低くしわがれた「ホー、ホー、ホー」</summary>
    private static float[] Laugh(int seed, int times, float pitch, float gap)
    {
        float[] d = SynthKit.New(times * gap + 0.9f);
        Random r = new Random(seed);
        for (int i = 0; i < times; i++)
        {
            float at = 0.02f + i * gap;
            float f = pitch * (1f - i * 0.07f);
            float length = i == times - 1 ? gap * 1.6f : gap * 0.82f;
            SynthKit.Noise(d, r, at, 0.05f, 0.14f, 2000f, 500f, 0.01f, 30f);  // 「ホ」の息
            SynthKit.Voice(d, r, at + 0.02f, length, f * 1.12f, f * 0.82f, new[] { 430f, 760f, 2500f }, 0.8f, 0.18f);
            SynthKit.Voice(d, r, at + 0.02f, length, f * 0.56f, f * 0.41f, new[] { 300f, 620f }, 0.35f, 0.3f); // 下に重なる声
        }
        SynthKit.Reverb(d, 0.4f, 0.8f);
        return SynthKit.Finish(d, 0.75f, 1.4f, 0.2f);
    }

    private static float[] Grunt()
    {
        float[] d = SynthKit.New(0.75f);
        Random r = new Random(311);
        SynthKit.Voice(d, r, 0f, 0.6f, 78f, 132f, new[] { 500f, 900f, 2300f }, 0.9f, 0.35f);
        SynthKit.Reverb(d, 0.22f, 0.76f);
        return SynthKit.Finish(d, 0.7f, 1.5f, 0.1f);
    }

    private static float[] EnemyThrow()
    {
        float[] d = SynthKit.New(0.6f);
        Random r = new Random(313);
        SynthKit.Whoosh(d, r, 0f, 0.42f, 0.9f, 120f, 1600f);
        SynthKit.SleighBells(d, r, 0.05f, 0.2f, 0.35f);
        SynthKit.Voice(d, r, 0f, 0.25f, 150f, 110f, new[] { 700f, 1200f }, 0.4f, 0.4f);
        return SynthKit.Finish(d, 0.65f, 1.2f, 0.1f);
    }

    // ───────── 場面 ─────────

    private static float[] Lever()
    {
        float[] d = SynthKit.New(1.2f);
        Random r = new Random(501);
        for (int i = 0; i < 8; i++) SynthKit.Knock(d, r, i * 0.035f, 480f + i * 35f, 0.01f, 0.4f);
        SynthKit.Partials(d, 0.3f, 180f, 0.3f, 0.8f, new[] { 1f, 1.41f, 2.3f, 3.6f }, new[] { 0.6f, 0.45f, 0.25f, 0.12f }, 0.001f);
        SynthKit.Thud(d, 0.3f, 120f, 34f, 0.5f, 1.2f);
        SynthKit.Reverb(d, 0.3f, 0.78f);
        return SynthKit.Finish(d, 0.75f, 1.3f, 0.2f);
    }

    private static float[] TruckStart()
    {
        // 古いトラックのエンジンがかかる
        float[] d = SynthKit.New(3.2f);
        Random r = new Random(503);
        float at = 0.1f, step = 0.13f;
        while (at < 2.9f)
        {
            float k = at / 3f;
            SynthKit.Thud(d, at, 85f + 40f * k, 38f, step * 1.6f, at < 0.9f ? 0.6f : 0.85f);
            SynthKit.Noise(d, r, at, step, 0.22f, 700f + 600f * k, 50f, 0.003f, 25f);
            at += step;
            if (at > 0.9f) step = Math.Max(0.048f, step * 0.9f);
        }
        SynthKit.Strings(d, 0.9f, 2.2f, 52f, 0.35f, 260f, 0.5f, 0.8f, 0f);
        SynthKit.Reverb(d, 0.15f, 0.72f);
        return SynthKit.Finish(d, 0.6f, 1.4f, 0.6f);
    }

    private static float[] StageStart()
    {
        float[] d = SynthKit.New(4f);
        SynthKit.TempleBell(d, 0f, 110f, 1.4f, 0.8f);
        SynthKit.Thud(d, 0f, 100f, 34f, 0.8f, 0.8f);
        int[] notes = { -5, -2, -6 };
        for (int i = 0; i < notes.Length; i++) SynthKit.MusicBox(d, 0.8f + i * 0.38f, N(notes[i]), 0.7f, 0.4f, -0.2f * i);
        SynthKit.Strings(d, 0.1f, 3.6f, N(-36), 0.22f, 380f, 0.8f, 1.8f, 0f);
        SynthKit.Reverb(d, 0.4f, 0.82f);
        return SynthKit.Finish(d, 0.65f, 1.15f, 0.8f);
    }

    private static float[] StageClear()
    {
        // ほっとするが、まだ終わらない：上がるオルゴールのあと、最後の1音だけずれる
        float[] d = SynthKit.New(4.2f);
        Random r = new Random(511);
        int[] notes = { -9, -5, -2, 3, -2, 3, 7 };
        for (int i = 0; i < notes.Length; i++) SynthKit.MusicBox(d, 0.05f + i * 0.2f, N(notes[i]), i == notes.Length - 1 ? 1f : 0.45f, 0.55f, i == notes.Length - 1 ? -0.1f : 0.08f);
        SynthKit.Strings(d, 0.05f, 2.8f, N(-33), 0.2f, 600f, 0.4f, 1.4f, 0f);
        SynthKit.Strings(d, 0.05f, 2.8f, N(-26), 0.14f, 800f, 0.6f, 1.4f, 0f);
        for (int i = 0; i < 18; i++) SynthKit.Coin(d, r, 1.45f + i * 0.05f, 0.14f, 0.9f);
        SynthKit.MusicBox(d, 2.35f, N(6), 1f, 0.28f, -0.4f);
        SynthKit.Reverb(d, 0.4f, 0.8f);
        return SynthKit.Finish(d, 0.6f, 1.1f, 0.8f);
    }

    private static float[] GameOver()
    {
        // ぜんまいが切れていくオルゴールと、弔いの鐘
        float[] d = SynthKit.New(6.5f);
        int[] notes = { 3, 0, -2, -5, -6, -9, -12 };
        float at = 0.1f, step = 0.3f;
        for (int i = 0; i < notes.Length; i++)
        {
            SynthKit.MusicBox(d, at, N(notes[i]), 0.8f, 0.5f, -i * 0.16f);
            at += step;
            step *= 1.2f;
        }
        SynthKit.TempleBell(d, 0f, 82f, 1.3f, 0.6f);
        SynthKit.TempleBell(d, 3.9f, 65f, 1.6f, 0.75f);
        SynthKit.Strings(d, 0.1f, 6f, N(-40), 0.28f, 260f, 1f, 2.5f, 0f);
        SynthKit.Reverb(d, 0.45f, 0.83f);
        return SynthKit.Finish(d, 0.65f, 1.15f, 1.2f);
    }

    private static float[] CountTick()
    {
        float[] d = SynthKit.New(0.07f);
        Random r = new Random(521);
        SynthKit.Knock(d, r, 0f, 1100f, 0.007f, 0.7f);
        SynthKit.Coin(d, r, 0f, 0.3f, 1.2f);
        return SynthKit.Finish(d, 0.3f, 1f, 0.02f);
    }

    private static float[] UiHover()
    {
        float[] d = SynthKit.New(0.12f);
        SynthKit.MusicBox(d, 0f, N(10), 0.035f, 0.6f, -0.2f);
        return SynthKit.Finish(d, 0.22f, 1f, 0.03f);
    }

    private static float[] UiClick()
    {
        float[] d = SynthKit.New(0.35f);
        Random r = new Random(531);
        SynthKit.Knock(d, r, 0f, 260f, 0.035f, 0.8f);
        SynthKit.MusicBox(d, 0.012f, N(3), 0.1f, 0.45f, -0.15f);
        SynthKit.Reverb(d, 0.18f, 0.72f);
        return SynthKit.Finish(d, 0.45f, 1.1f, 0.06f);
    }

    // ───────── 遠くの物音 ─────────

    private static float[] Creak(int seed, float pitch, float length)
    {
        // どこかで床か戸がきしむ
        float[] d = SynthKit.New(length + 0.8f);
        Random r = new Random(seed);
        float at = 0.02f;
        float gap = 0.055f;
        while (at < length)
        {
            float k = at / length;
            float f = pitch * (1f + 0.35f * (float)Math.Sin(k * 3.6f)) * (0.97f + (float)r.NextDouble() * 0.06f);
            SynthKit.Partials(d, at, f, 0.018f, 0.45f * (float)Math.Sin(Math.PI * Math.Min(0.97f, k + 0.03f)), new[] { 1f, 1.93f, 3.1f }, new[] { 0.6f, 0.4f, 0.2f }, 0.004f);
            at += gap * (0.7f + (float)r.NextDouble() * 0.5f);
            gap = Math.Max(0.016f, gap * 0.95f);
        }
        SynthKit.Reverb(d, 0.5f, 0.82f);
        SynthKit.LowPass(d, 2200f, 0.7f); // 遠くの音は、こもる
        return SynthKit.Finish(d, 0.4f, 1.1f, 0.2f);
    }

    private static float[] FarBell()
    {
        float[] d = SynthKit.New(4.5f);
        SynthKit.TempleBell(d, 0.02f, 131f, 1.4f, 0.6f);
        SynthKit.Reverb(d, 0.6f, 0.85f);
        SynthKit.LowPass(d, 1300f, 0.7f);
        return SynthKit.Finish(d, 0.35f, 1f, 1f);
    }

    private static float[] ChaseStinger()
    {
        // 見つかった瞬間：低い太鼓と、半音でぶつかる弦
        float[] d = SynthKit.New(2.2f);
        foreach (float f in new[] { N(3), N(4), N(-9), N(-8) })
            SynthKit.Strings(d, 0f, 1.5f, f, 0.16f, 2200f, 0.04f, 1.2f, 9f);
        SynthKit.Strings(d, 0f, 1.8f, N(-33), 0.4f, 600f, 0.02f, 1.2f, 0f);
        SynthKit.Thud(d, 0f, 150f, 30f, 0.9f, 1.2f);
        SynthKit.Reverb(d, 0.35f, 0.8f);
        return SynthKit.Finish(d, 0.5f, 1.1f, 0.5f);
    }

    // ───────── 曲（くり返し） ─────────

    /// <summary>タイトル：調子の外れたオルゴールの子守歌が、ゆっくり、まばらに鳴る</summary>
    private static float[] MusicTitle()
    {
        const float beat = 0.75f;
        const int beats = 48; // 36秒
        float[] d = SynthKit.New(beat * beats);
        Random r = new Random(701);

        // 旋律（ラからの半音。99 は休み）。3拍子で 16 小節
        int[] melody =
        {
            0, 3, 7,   6, 99, 99,   5, 99, 2,   3, 99, 99,
            0, 3, 7,   8, 99, 99,   6, 99, 5,   0, 99, 99,
            12, 99, 7, 8, 99, 99,   6, 99, 3,   2, 99, 99,
            0, 3, 7,   6, 99, 99,   2, 99, 2,   0, 99, 99
        };
        for (int i = 0; i < melody.Length; i++)
        {
            if (melody[i] == 99) continue;
            float wobble = (float)Math.Sin(i * 2.3) * 0.18f - 0.08f;
            SynthKit.MusicBox(d, i * beat + (float)r.NextDouble() * 0.025f, N(melody[i]), 0.85f, 0.32f, wobble, true);
        }
        // 伴奏は、2小節に1つの低い音だけ
        int[] roots = { -12, -14, -12, -15, -9, -15, -12, -17 };
        for (int i = 0; i < roots.Length; i++)
        {
            float at = i * 6 * beat;
            SynthKit.MusicBox(d, at, N(roots[i] - 12), 1.3f, 0.14f, -0.15f, true);
            SynthKit.Strings(d, at, beat * 6.5f, N(roots[i] - 24), 0.07f, 260f, 1.5f, 1.8f, 0f, true);
        }
        Drone(d, 55f, 0.035f);
        SynthKit.Reverb(d, 0.45f, 0.83f, true);
        return SynthKit.Finish(d, 0.34f, 1f, 0f);
    }

    /// <summary>探索中：ごく静かな低いうなりと、かすかな隙間風。たまに、遠くでオルゴールが1、2音鳴る</summary>
    private static float[] MusicAmbience()
    {
        const float length = 32f;
        float[] d = SynthKit.New(length);
        Random r = new Random(702);

        Drone(d, 43f, 0.12f);
        float[] wind = SynthKit.New(length);
        for (int i = 0; i < 4; i++)
            SynthKit.Whoosh(wind, r, i * 8f + (float)r.NextDouble() * 2f, 6.5f, 0.1f, 110f, 380f, true);
        SynthKit.Mix(d, wind, 0f, 0.2f);

        SynthKit.Strings(d, 5f, 10f, N(-29), 0.03f, 450f, 4f, 4f, 0f, true);
        SynthKit.Strings(d, 20f, 10f, N(-35), 0.035f, 350f, 4f, 4f, 0f, true);

        float[] far = SynthKit.New(length);
        SynthKit.MusicBox(far, 9.1f, N(0), 1.4f, 0.07f, -0.5f, true);
        SynthKit.MusicBox(far, 26.4f, N(-2), 1.4f, 0.06f, 0.3f, true);
        SynthKit.LowPass(far, 1500f, 0.7f);
        SynthKit.Mix(d, far, 0f, 1f);

        SynthKit.Reverb(d, 0.4f, 0.84f, true);
        return SynthKit.Finish(d, 0.18f, 1f, 0f);
    }

    /// <summary>
    /// 追われているとき：音数を絞って、じわじわ迫る怖さにする。半音で行き来する低い弦と、重い一打。
    /// 後半になると、細い高音が半音でぶつかりながら、ゆっくり現れる
    /// </summary>
    private static float[] MusicChase()
    {
        const float beat = 60f / 112f;
        const int bars = 8;
        const float bar = beat * 4f;
        float[] d = SynthKit.New(bar * bars);

        for (int b = 0; b < bars; b++)
        {
            int shift = b >= 6 ? 1 : 0;
            for (int i = 0; i < 4; i++)
                SynthKit.Strings(d, b * bar + i * beat, beat * 0.85f, N(-32 + (i % 2) + shift), i % 2 == 0 ? 0.34f : 0.26f, 520f, 0.04f, 0.2f, 0f, true);
            SynthKit.Thud(d, b * bar, 105f, 34f, beat * 1.6f, 0.8f, true);
            if (b % 2 == 1) SynthKit.Thud(d, b * bar + beat * 2.5f, 90f, 36f, beat * 1.2f, 0.45f, true);
        }

        // 後半の、細い高音
        SynthKit.Strings(d, bar * 4f, bar * 4f, N(3), 0.035f, 1800f, 3f, 1.5f, 6f, true);
        SynthKit.Strings(d, bar * 5f, bar * 3f, N(4), 0.028f, 1800f, 2.5f, 1.5f, 7f, true);
        // 4小節ごとの、低いうめき
        for (int i = 0; i < 2; i++)
            SynthKit.Strings(d, i * bar * 4f + bar * 2f, bar * 2f, N(-26 + i), 0.14f, 700f, 1.6f, 0.8f, 0f, true);

        SynthKit.Reverb(d, 0.3f, 0.8f, true);
        return SynthKit.Finish(d, 0.42f, 1.05f, 0f);
    }

    /// <summary>心臓の音（追われている間。近いほど速く鳴らす）</summary>
    private static float[] Heartbeat()
    {
        float[] d = SynthKit.New(0.8f);
        SynthKit.Thud(d, 0.02f, 95f, 42f, 0.22f, 1f);
        SynthKit.Thud(d, 0.23f, 78f, 38f, 0.25f, 0.75f);
        return SynthKit.Finish(d, 0.55f, 1.2f, 0f);
    }

    /// <summary>低いうなり。くり返しがつながるよう、長さの中でちょうど整数回震える高さにする</summary>
    private static void Drone(float[] d, float frequency, float volume)
    {
        double length = d.Length / (double)SampleRate;
        double a = Math.Round(frequency * length) / length;
        double b = Math.Round(frequency * 1.008 * length) / length;
        double c = Math.Round(frequency * 1.5 * length) / length;
        double swell = Math.Round(0.125 * length) / length;
        for (int i = 0; i < d.Length; i++)
        {
            double t = i / (double)SampleRate;
            double wave = Math.Sin(2 * Math.PI * a * t) + 0.7 * Math.Sin(2 * Math.PI * b * t)
                        + 0.3 * Math.Sin(2 * Math.PI * c * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * swell * t));
            d[i] += (float)(wave * volume);
        }
    }
}
