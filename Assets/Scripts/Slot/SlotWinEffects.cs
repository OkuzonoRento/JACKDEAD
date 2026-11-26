using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>当たりの大きさ（演出の派手さが変わる）</summary>
public enum SlotWinTier { Reroll, Win, BigWin, Jackpot }

/// <summary>当たり演出の設定（調整は SlotMachineController のインスペクター）</summary>
[Serializable]
public class SlotCelebrationSettings
{
    [Tooltip("オフにすると、音と画面の演出を出さず、台が一度ふくらむだけになる")]
    public bool enabled = true;

    [Header("大当たりの決め方（どちらかを満たせばよい）")]
    [Tooltip("BIG WIN：出目の倍率がこれ以上")]
    public float bigSymbolMultiplier = 3f;
    [Tooltip("BIG WIN：台の倍率×出目の倍率がこれ以上")]
    public float bigTotalMultiplier = 6f;
    [Tooltip("JACKPOT：出目の倍率がこれ以上")]
    public float jackpotSymbolMultiplier = 7f;
    [Tooltip("JACKPOT：台の倍率×出目の倍率がこれ以上")]
    public float jackpotTotalMultiplier = 15f;

    [Header("金額が出たあと、台の前で見ている時間（秒）。回し始めてからここまで動けず、敵にも音が聞こえる")]
    [Min(0f)] public float winLinger = 0.3f;
    [Min(0f)] public float bigWinLinger = 0.8f;
    [Min(0f)] public float jackpotLinger = 1.4f;

    [Tooltip("リールの表面より、窓の枠が手前に出ている距離（m）。電飾と大きな文字は、その手前に出す。台のモデルを変えたら合わせる")]
    [Min(0f)] public float windowFrameDepth = 0.07f;

    [Header("音・揺れ・コイン")]
    [Range(0f, 1f), Tooltip("スロットの音の大きさ（設定画面の SE 音量も掛かる）")]
    public float soundVolume = 0.7f;
    [Tooltip("BIG WIN と JACKPOT で、自分のカメラを揺らす")]
    public bool cameraShake = true;
    [Range(0f, 2f), Tooltip("コインの量（1 が標準、0 で出さない）")]
    public float coinAmount = 1f;

    public SlotWinTier GetTier(float rarityMultiplier, float symbolMultiplier)
    {
        float total = rarityMultiplier * symbolMultiplier;
        if (symbolMultiplier >= jackpotSymbolMultiplier || total >= jackpotTotalMultiplier) return SlotWinTier.Jackpot;
        if (symbolMultiplier >= bigSymbolMultiplier || total >= bigTotalMultiplier) return SlotWinTier.BigWin;
        return SlotWinTier.Win;
    }

    public float GetLinger(SlotWinTier tier)
    {
        return tier == SlotWinTier.Jackpot ? jackpotLinger : tier == SlotWinTier.BigWin ? bigWinLinger : winLinger;
    }
}

/// <summary>当たりの内容</summary>
public struct SlotWinInfo
{
    public SlotWinTier tier;
    public int reward;
    public float symbolMultiplier;
    public float machineMultiplier;
    public float buffMultiplier; // ドーパミンによる上乗せ（1 なら無し）
}

/// <summary>演出を置く場所。台が揺れていないときに測る</summary>
public struct SlotAnchors
{
    public Vector3 center;       // リールの窓の中心
    public Vector3 front;        // 台の正面の向き（水平）
    public float[] reelOffsets;  // 各リールの中心。正面から見て右向きの距離
    public float frameWidth;     // そろった目を囲む枠の大きさ
    public float frameHeight;
    public float symbolY;        // 止まった目の中心の高さ（窓の中心から）
    public float windowWidth;
    public float windowHeight;
    public float face;           // 中心からリールの表面までの、手前への距離
    public float floorY;
    public float topY;
}

/// <summary>
/// スロット台の音と演出。台そのものの画面に出すので、周りの人にも同じように見えて聞こえる。
/// 当たりの流れ：そろった目を左から順にピンクの枠で囲む（音が上がる）→ 台の上の掲示板に倍率が積み上がる →
/// リールの上で「+金額」が数え上がる → 黄色く光る窓の枠・大きな文字・星・コイン。
/// 見た目だけで当たり判定は持たない（床の計測や照準の邪魔をしない）。
/// </summary>
public class SlotWinEffects : MonoBehaviour
{
    private const float CoinRadius = 0.045f;
    private const float Gravity = 9.8f;
    private const float Bounce = 0.4f;
    private const float StartDelay = 0.18f; // 3つ目が止まってから、数え始めるまでの「ため」

    /// <summary>ボタンを押してから、リールが回り出すまでの時間（「てっててっててん」を聞かせる）</summary>
    public const float StartWait = 0.85f;

    private static readonly Color Pink = new Color(1f, 0.18f, 0.72f);
    private static readonly Color Cream = new Color(1f, 0.95f, 0.86f); // 掲示板の文字
    private static readonly Color Yellow = new Color(1f, 0.86f, 0.15f);
    private static readonly Color Orange = new Color(1f, 0.5f, 0.08f);
    private static readonly Color Cyan = new Color(0.25f, 0.9f, 1f);
    private static readonly Color Red = new Color(1f, 0.1f, 0.05f);

    private class Coin
    {
        public Transform body;
        public Vector3 velocity;
        public Vector3 spinAxis;
        public float spinSpeed;
        public float delay;
        public float age;
        public float life;
    }

    private class Star
    {
        public SlotPixelQuad quad;
        public Vector3 position;
        public Vector3 velocity;
        public float size;
        public float spin;
        public float delay;
        public float age;
        public float life;
        public Color color;
    }

    private static Mesh coinMesh;
    private static Material coinMaterial;
    private static bool coinShaderMissing;

    private readonly List<Coin> coins = new List<Coin>();
    private readonly List<Star> stars = new List<Star>();

    private SlotAnchors anchors;
    private SlotReelScroller[] reels;
    private SlotCelebrationSettings settings;

    private AudioSource loopSource;
    private AudioSource jingleSource;
    private AudioSource startSource;
    private AudioSource[] shotSources;
    private int nextShot;
    private float lastCoinSound;

    private Light flashLight;
    private SlotPixelQuad[] frames;
    private SlotPixelQuad glow;
    private SlotPixelQuad amount;
    private Texture2D amountTexture;
    private float amountBump;
    private SlotPixelQuad title;
    private SlotPixelQuad led;
    private Texture2D ledTexture;
    private float[] frameAge;
    private float[] reelFlash;

    private SlotWinTier tier;
    private bool showing;       // 当たりの演出中
    private float finaleAge;    // 大きな文字が出てからの時間（出る前は負）
    private float fade = 1f;
    private float ledBump;
    private float lightKick;
    private Color lightColor;

    private float Volume => settings.soundVolume * SettingsManager.SeVolume;

    // ───────── 時間の割り振り ─────────

    private static float StepTime(SlotWinTier tier)
    {
        return tier == SlotWinTier.Jackpot ? 0.27f : tier == SlotWinTier.BigWin ? 0.21f : tier == SlotWinTier.Win ? 0.17f : 0.12f;
    }

    private static float CountTime(SlotWinTier tier)
    {
        return tier == SlotWinTier.Jackpot ? 1.3f : tier == SlotWinTier.BigWin ? 0.65f : tier == SlotWinTier.Win ? 0.3f : 0f;
    }

    private static float FinaleTime(SlotWinTier tier)
    {
        return tier == SlotWinTier.Jackpot ? 3.4f : tier == SlotWinTier.BigWin ? 2.2f : tier == SlotWinTier.Win ? 1.1f : 0.6f;
    }

    private static int ChainLength(SlotWinInfo info)
    {
        if (info.tier == SlotWinTier.Reroll) return 0;
        return info.buffMultiplier > 1.001f ? 3 : 2;
    }

    /// <summary>当たりが出てから、金額（大きな文字）が出るまでの時間</summary>
    public float GetRevealTime(SlotWinInfo info)
    {
        int reelCount = reels != null ? reels.Length : 3;
        return StartDelay + StepTime(info.tier) * (reelCount + ChainLength(info)) + CountTime(info.tier);
    }

    // ───────── 準備 ─────────

    public void Setup(SlotAnchors newAnchors, SlotReelScroller[] machineReels, SlotCelebrationSettings newSettings)
    {
        anchors = newAnchors;
        reels = machineReels;
        settings = newSettings;

        // x = 正面から見て右、y = 上、z = 台の奥
        transform.SetPositionAndRotation(anchors.center, Quaternion.LookRotation(-anchors.front, Vector3.up));

        loopSource = CreateSource();
        loopSource.loop = true;
        jingleSource = CreateSource();
        startSource = CreateSource();
        shotSources = new AudioSource[8];
        for (int i = 0; i < shotSources.Length; i++) shotSources[i] = CreateSource();

        GameObject lightObj = new GameObject("WinLight");
        lightObj.transform.SetParent(transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.35f, -(anchors.face + 0.55f));
        flashLight = lightObj.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.shadows = LightShadows.None;
        flashLight.range = 4.5f;
        flashLight.enabled = false;

        int count = reels.Length;
        frames = new SlotPixelQuad[count];
        frameAge = new float[count];
        reelFlash = new float[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = SlotPixelQuad.Create(transform, "WinFrame");
            if (frames[i] == null) continue;
            int pixels = Mathf.RoundToInt(24f * anchors.frameWidth / Mathf.Max(0.01f, anchors.frameHeight));
            frames[i].Texture = SlotPixelQuad.Cached($"frame{pixels}", () => SlotPixelArt.Frame(pixels, 24));
            frames[i].Order = 1;
        }
        glow = SlotPixelQuad.Create(transform, "WindowGlow");
        amount = SlotPixelQuad.Create(transform, "WinAmount");
        title = SlotPixelQuad.Create(transform, "WinTitle");
        led = SlotPixelQuad.Create(transform, "WinBoard");
        if (glow != null) glow.Order = 0;
        if (amount != null) amount.Order = 5;
        if (title != null) title.Order = 4;
        if (led != null) led.Order = 2;
    }

    private AudioSource CreateSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f; // 台の場所から聞こえる
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 20f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void Play(SlotSynth.Sound sound, float semitones = 0f, float volumeScale = 1f)
    {
        AudioSource source = shotSources[nextShot];
        nextShot = (nextShot + 1) % shotSources.Length;
        source.Stop();
        source.pitch = Mathf.Pow(2f, semitones / 12f);
        source.PlayOneShot(SlotWinSounds.Get(sound), Volume * volumeScale);
    }

    // ───────── 回している間の音 ─────────

    /// <summary>ボタンを押したときの「てっててっててん」</summary>
    public void PlayStart()
    {
        startSource.clip = SlotWinSounds.Get(SlotSynth.Sound.Start);
        startSource.volume = Volume;
        startSource.Play();
    }

    public void StartSpin()
    {
        Play(SlotSynth.Sound.Lever);
        loopSource.clip = SlotWinSounds.Get(SlotSynth.Sound.SpinLoop);
        loopSource.pitch = 1f;
        loopSource.volume = Volume * 0.6f;
        loopSource.Play();
    }

    /// <param name="matched">左からここまで同じ目が並んでいれば、その数（並んでいなければ 0）</param>
    public void ReelStopped(int index, int matched)
    {
        Play(SlotSynth.Sound.ReelStop, -index);

        // そろいかけると、音が鳴って期待させる
        if (matched >= 2) Play(SlotSynth.Sound.Blip, -7f + (matched - 2) * 4f, 0.55f);

        if (index >= reels.Length - 1) loopSource.Stop();
        else loopSource.pitch = 1f - 0.12f * (index + 1);
    }

    public void PlayLose()
    {
        Play(SlotSynth.Sound.Lose, 0f, 0.7f);
    }

    // ───────── 当たり ─────────

    /// <param name="beat">台をはねさせる（引数は強さ）</param>
    public void PlayWin(SlotWinInfo info, bool shakeCamera, Action<float> beat)
    {
        StopVisuals(); // 最後のリールが止まった音は、鳴らしたままにする
        jingleSource.Stop();
        tier = info.tier;
        showing = true;
        finaleAge = -1f;
        fade = 1f;
        StartCoroutine(RunWin(info, shakeCamera, beat));
    }

    private IEnumerator RunWin(SlotWinInfo info, bool shakeCamera, Action<float> beat)
    {
        float step = StepTime(tier);
        bool reroll = tier == SlotWinTier.Reroll;
        yield return new WaitForSeconds(StartDelay);

        // 1. そろった目を、左から順に囲む。音がだんだん上がる
        int[] notes = { -5, -4, -2, 0, 1 }; // 半音ずつ追い詰めるように上げる
        for (int i = 0; i < reels.Length; i++)
        {
            if (frames[i] != null)
            {
                frames[i].transform.localPosition = new Vector3(anchors.reelOffsets[i], anchors.symbolY, -(anchors.face + 0.015f));
                frames[i].SetSize(anchors.frameWidth * 1.8f, anchors.frameHeight * 1.8f);
                frames[i].Color = Color.white;
                frames[i].Visible = true;
            }
            frameAge[i] = 0f;
            reelFlash[i] = 1f;
            Kick(reroll ? Cyan : Pink, 2.5f);
            Play(SlotSynth.Sound.Blip, notes[Mathf.Min(i, notes.Length - 1)]);
            beat?.Invoke(0.03f);
            yield return new WaitForSeconds(step);
        }

        // 2. 掲示板に倍率が積み上がる
        string chain = "";
        float[] values = { info.symbolMultiplier, info.machineMultiplier, info.buffMultiplier };
        for (int i = 0; i < ChainLength(info); i++)
        {
            chain += (i > 0 ? " " : "") + "x" + values[i].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            ShowBoard(chain, Cream);
            Kick(Yellow, 3f);
            Play(SlotSynth.Sound.Blip, 3f + i);
            beat?.Invoke(0.045f);
            yield return new WaitForSeconds(step);
        }

        // 3. 金額が数え上がる。だんだん速く、高くなる
        float count = CountTime(tier);
        float nextTick = 0f;
        for (float t = 0f; t < count; t += Time.deltaTime)
        {
            if (t >= nextTick)
            {
                float k = t / count;
                nextTick = t + Mathf.Lerp(0.075f, 0.035f, k);
                int shown = Mathf.RoundToInt(info.reward * k * k);
                ShowBoard(Money(shown), Cream, false);
                ShowAmount(Money(shown));
                Play(SlotSynth.Sound.Tick, Mathf.Lerp(-3f, 12f, k), 0.7f);
            }
            yield return null;
        }

        // 4. 仕上げ
        finaleAge = 0f;
        ShowBoard(reroll ? "REROLL!" : Money(info.reward), reroll ? Cyan : Cream);
        // 小さい当たりは「+金額」だけ。大きい当たりは見出しを出し、JACKPOT は見出しだけにする
        if (tier != SlotWinTier.Win) ShowTitle();
        if (tier == SlotWinTier.Win || tier == SlotWinTier.BigWin)
        {
            ShowAmount(Money(info.reward));
            amountBump = 1f;
        }
        else if (amount != null)
        {
            amount.Visible = false;
        }
        if (reroll)
        {
            Play(SlotSynth.Sound.Reroll);
        }
        else
        {
            Play(SlotSynth.Sound.Payout, 0f, tier == SlotWinTier.Win ? 1f : 0.55f); // 大きい当たりは、曲と一撃が重なるので控えめに
            if (tier != SlotWinTier.Win)
            {
                Play(SlotSynth.Sound.Slam);
                jingleSource.clip = SlotWinSounds.Get(tier == SlotWinTier.Jackpot ? SlotSynth.Sound.Jackpot : SlotSynth.Sound.BigWin);
                jingleSource.volume = Volume;
                jingleSource.Play();
                if (shakeCamera && settings.cameraShake)
                {
                    HitFeedback feedback = FindAnyObjectByType<HitFeedback>();
                    if (feedback != null) feedback.Impact(tier == SlotWinTier.Jackpot ? 0.35f : 0.18f);
                }
            }
            ShowGlow();
            SpawnCoins();
            SpawnStars();
        }

        float finale = FinaleTime(tier);
        float nextBeat = 0f;
        int beats = tier == SlotWinTier.Jackpot ? 8 : tier == SlotWinTier.BigWin ? 3 : 1;
        for (float t = 0f; t < finale; t += Time.deltaTime)
        {
            if (beats > 0 && t >= nextBeat)
            {
                beats--;
                nextBeat = t + 0.28f;
                beat?.Invoke(0.08f);
            }
            fade = 1f - Mathf.Clamp01((t - (finale - 0.3f)) / 0.3f);
            yield return null;
        }
        HideVisuals();
    }

    private static string Money(int value)
    {
        return "+$" + value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ShowBoard(string text, Color color, bool bump = true)
    {
        if (led == null) return;
        Color32 c = color;
        ledTexture = SlotPixelQuad.ToTexture(SlotPixelArt.Led(text, new SlotPixelArt.Rgba(c.r, c.g, c.b)), ledTexture);
        led.Texture = ledTexture;
        if (bump) ledBump = 1f;
        PlaceBoard();
        led.Visible = true;
    }

    private void PlaceBoard()
    {
        led.transform.localPosition = new Vector3(0f, anchors.topY - anchors.center.y + 0.1f, -(anchors.face + 0.03f));
        led.SetWidth(anchors.windowWidth * 0.96f, 1f + 0.18f * ledBump);
        led.Color = new Color(1f, 1f, 1f, fade);
    }

    /// <summary>リールの上に出す「+金額」</summary>
    private void ShowAmount(string text)
    {
        if (amount == null) return;
        amountTexture = SlotPixelQuad.ToTexture(SlotPixelArt.Title(text,
            new SlotPixelArt.Rgba(255, 90, 205), new SlotPixelArt.Rgba(230, 25, 160),
            new SlotPixelArt.Rgba(255, 244, 220), new SlotPixelArt.Rgba(40, 5, 35)), amountTexture);
        amount.Texture = amountTexture;
        PlaceAmount();
        amount.Visible = true;
    }

    private void PlaceAmount()
    {
        float height = anchors.windowHeight * (finaleAge >= 0f && tier == SlotWinTier.Win ? 0.27f : 0.21f);
        float width = height * amountTexture.width / amountTexture.height;
        float limit = anchors.windowWidth * 0.98f;
        if (width > limit)
        {
            height *= limit / width;
            width = limit;
        }

        // BIG WIN は見出しの下に出す
        float y = finaleAge >= 0f && tier == SlotWinTier.BigWin ? -anchors.windowHeight * 0.33f : anchors.symbolY;
        float pop = 1f + 0.45f * amountBump;
        amount.transform.localPosition = new Vector3(0f, y, -(anchors.face + settings.windowFrameDepth + 0.06f));
        amount.SetSize(width * pop, height * pop);
        amount.Color = new Color(1f, 1f, 1f, fade);
    }

    private void ShowGlow()
    {
        if (glow == null) return;
        int width = Mathf.RoundToInt((anchors.windowWidth + 0.09f) / 0.008f);
        int height = Mathf.RoundToInt((anchors.windowHeight + 0.09f) / 0.008f);
        glow.Texture = SlotPixelQuad.Cached($"glow{width}x{height}", () => SlotPixelArt.Glow(width, height));
        glow.transform.localPosition = new Vector3(0f, 0f, -(anchors.face + settings.windowFrameDepth + 0.012f));
        glow.SetSize(anchors.windowWidth + 0.09f, anchors.windowHeight + 0.09f);
        glow.Color = new Color(1f, 1f, 1f, 0f);
        glow.Visible = true;
    }

    private void ShowTitle()
    {
        if (title == null) return;
        title.Texture = TitleTexture(false);
        title.transform.localScale = Vector3.zero; // 大きさは次のフレームから
        title.Visible = true;
    }

    private Texture2D TitleTexture(bool alternate)
    {
        return SlotPixelQuad.Cached($"title{tier}{alternate}", () =>
        {
            SlotPixelArt.Rgba dark = new SlotPixelArt.Rgba(45, 5, 40);
            SlotPixelArt.Rgba white = new SlotPixelArt.Rgba(255, 255, 235);
            SlotPixelArt.Rgba pink = new SlotPixelArt.Rgba(255, 50, 185);
            SlotPixelArt.Rgba deepPink = new SlotPixelArt.Rgba(215, 20, 150);
            SlotPixelArt.Rgba yellow = new SlotPixelArt.Rgba(255, 225, 40);
            SlotPixelArt.Rgba orange = new SlotPixelArt.Rgba(255, 140, 20);

            switch (tier)
            {
                case SlotWinTier.Jackpot:
                    return alternate
                        ? SlotPixelArt.Title("JACKPOT", white, yellow, deepPink, dark)
                        : SlotPixelArt.Title("JACKPOT", pink, deepPink, yellow, dark);
                case SlotWinTier.BigWin:
                    return SlotPixelArt.Title("BIG WIN!", pink, deepPink, yellow, dark);
                case SlotWinTier.Win:
                    return SlotPixelArt.Title("WIN!", white, yellow, orange, dark);
                default:
                    return SlotPixelArt.Title("REROLL!", white, new SlotPixelArt.Rgba(60, 220, 255), new SlotPixelArt.Rgba(20, 80, 190), dark);
            }
        });
    }

    private void Kick(Color color, float intensity)
    {
        lightColor = color;
        lightKick = intensity;
    }

    /// <summary>演出と音を打ち切る（攻撃を受けた・次の回転を始めた）</summary>
    public void Stop()
    {
        if (shotSources == null) return;

        startSource.Stop();
        Clear();
    }

    /// <summary>ボタンを押したときの音は残して、ほかを打ち切る</summary>
    public void Clear()
    {
        if (shotSources == null) return;

        loopSource.Stop();
        jingleSource.Stop();
        foreach (AudioSource source in shotSources) source.Stop();
        StopVisuals();
    }

    private void StopVisuals()
    {
        StopAllCoroutines();
        foreach (Coin coin in coins)
        {
            if (coin.body != null) Destroy(coin.body.gameObject);
        }
        coins.Clear();
        foreach (Star star in stars) star.quad.Destroy();
        stars.Clear();
        HideVisuals();
    }

    private void HideVisuals()
    {
        showing = false;
        finaleAge = -1f;
        lightKick = 0f;
        if (flashLight != null) flashLight.enabled = false;
        if (frames != null)
        {
            foreach (SlotPixelQuad frame in frames)
            {
                if (frame != null) frame.Visible = false;
            }
        }
        if (glow != null) glow.Visible = false;
        if (amount != null) amount.Visible = false;
        if (title != null) title.Visible = false;
        if (led != null) led.Visible = false;
        RestoreReels();
    }

    private void OnDestroy()
    {
        RestoreReels();
        if (frames != null)
        {
            foreach (SlotPixelQuad frame in frames) frame?.Destroy();
        }
        glow?.Destroy();
        amount?.Destroy();
        if (amountTexture != null) Destroy(amountTexture);
        title?.Destroy();
        led?.Destroy();
        foreach (Star star in stars) star.quad.Destroy();
        if (ledTexture != null) Destroy(ledTexture);
    }

    private void RestoreReels()
    {
        if (reels == null) return;
        if (reelFlash != null) Array.Clear(reelFlash, 0, reelFlash.Length);
        foreach (SlotReelScroller reel in reels)
        {
            if (reel != null) reel.SetFlash(Color.white, 0f);
        }
    }

    // ───────── 毎フレームの動き ─────────

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateCoins(dt);
        UpdateStars(dt);
        if (!showing) return;

        bool finale = finaleAge >= 0f;
        if (finale) finaleAge += dt;
        bool reroll = tier == SlotWinTier.Reroll;
        // 色の切り替えは、目に悪くないように速くしすぎない（1秒に2.5回）
        bool alternate = finale && Mathf.Repeat(finaleAge * 2.5f, 1f) >= 0.5f;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] == null || !frames[i].Visible) continue;
            frameAge[i] += dt;
            float slam = Mathf.Lerp(1.8f, 1f, Mathf.Clamp01(frameAge[i] / 0.1f));
            float beat = 1f + 0.04f * Mathf.Sin(frameAge[i] * 16f);
            frames[i].SetSize(anchors.frameWidth * slam * beat, anchors.frameHeight * slam * beat);
            Color color = reroll ? Cyan : alternate ? Yellow : Pink;
            if (frameAge[i] < 0.07f) color = Color.white;
            color.a = fade;
            frames[i].Color = color;
        }

        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] == null) continue;
            reelFlash[i] = Mathf.MoveTowards(reelFlash[i], 0f, dt * 3f);
            float amount = reelFlash[i];
            if (finale) amount = Mathf.Max(amount, (alternate ? 0.7f : 0.25f) * fade);
            reels[i].SetFlash(reroll ? Cyan : alternate ? Yellow : Pink, amount);
        }

        if (led != null && led.Visible)
        {
            ledBump = Mathf.MoveTowards(ledBump, 0f, dt * 6f);
            PlaceBoard();
        }

        if (finale && title != null && title.Visible)
        {
            // 勢いよく飛び込んで、はずむ
            float slam = finaleAge < 0.12f ? Mathf.Lerp(2.4f, 0.9f, finaleAge / 0.12f)
                : 1f - 0.1f * Mathf.Exp(-(finaleAge - 0.12f) * 8f) * Mathf.Cos((finaleAge - 0.12f) * 25f);
            float pulse = 1f + (reroll ? 0f : 0.05f * Mathf.Sin(finaleAge * 12f));
            float widthRate = tier == SlotWinTier.Jackpot ? 1.02f : tier == SlotWinTier.BigWin ? 0.9f : 0.8f;
            // 小さい当たりは、そろった目が見えるように下へ寄せる
            float y = tier == SlotWinTier.BigWin ? anchors.windowHeight * 0.08f : 0f;

            if (tier == SlotWinTier.Jackpot) title.Texture = TitleTexture(alternate);
            title.transform.localPosition = new Vector3(0f, y, -(anchors.face + settings.windowFrameDepth + 0.04f));
            title.transform.localRotation = Quaternion.Euler(0f, 0f, tier == SlotWinTier.Jackpot ? Mathf.Sin(finaleAge * 7f) * 4f : 0f);
            title.SetWidth(anchors.windowWidth * widthRate, slam * pulse);
            title.Color = new Color(1f, 1f, 1f, fade);
        }

        if (amount != null && amount.Visible)
        {
            amountBump = Mathf.MoveTowards(amountBump, 0f, dt * 5f);
            PlaceAmount();
        }

        if (glow != null && glow.Visible)
        {
            // ふわっと点いて、脈打つ。大きい当たりほど濃い
            float strength = tier == SlotWinTier.Jackpot ? 1f : tier == SlotWinTier.BigWin ? 0.85f : 0.55f;
            float pulse = 0.82f + 0.18f * Mathf.Sin(finaleAge * Mathf.PI * 4f);
            glow.Color = new Color(1f, 1f, 1f, strength * pulse * fade * Mathf.Clamp01(finaleAge / 0.12f));
        }

        UpdateLight(dt, finale, alternate);
    }

    private void UpdateLight(float dt, bool finale, bool alternate)
    {
        lightKick = Mathf.MoveTowards(lightKick, 0f, dt * 10f);
        float intensity = lightKick;
        Color color = lightColor;

        if (finale && tier != SlotWinTier.Reroll)
        {
            float peak = tier == SlotWinTier.Jackpot ? 5.5f : tier == SlotWinTier.BigWin ? 3.5f : 1.8f;
            intensity = peak * fade * (0.75f + 0.25f * Mathf.Cos(finaleAge * Mathf.PI * 5f));
            color = tier == SlotWinTier.Jackpot ? (alternate ? Yellow : Red)
                : tier == SlotWinTier.BigWin ? Orange : Yellow;
        }

        flashLight.enabled = intensity > 0.01f;
        flashLight.range = tier == SlotWinTier.Jackpot ? 7f : 4.5f;
        flashLight.intensity = intensity;
        flashLight.color = color;
    }

    // ───────── 星 ─────────

    private void SpawnStars()
    {
        int count = tier == SlotWinTier.Jackpot ? 46 : tier == SlotWinTier.BigWin ? 20 : 5;
        float spread = tier == SlotWinTier.Jackpot ? 2.2f : tier == SlotWinTier.BigWin ? 0.7f : 0.1f;

        for (int i = 0; i < count; i++)
        {
            SlotPixelQuad quad = SlotPixelQuad.Create(transform, "Star");
            if (quad == null) return;
            quad.Texture = SlotPixelQuad.Cached("star", SlotPixelArt.Star);
            quad.Order = 3;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            stars.Add(new Star
            {
                quad = quad,
                position = new Vector3(direction.x * anchors.windowWidth * 0.4f, direction.y * anchors.windowHeight * 0.35f,
                    -(anchors.face + settings.windowFrameDepth + Random.Range(0.02f, 0.12f))),
                velocity = direction * Random.Range(0.3f, 1.1f) + new Vector3(0f, 0.35f, -Random.Range(0.05f, 0.35f)),
                size = Random.Range(0.045f, 0.11f),
                spin = Random.Range(-240f, 240f),
                delay = spread * i / count,
                life = Random.Range(0.7f, 1.2f),
                color = Random.value < 0.55f ? Pink : Yellow
            });
        }
    }

    private void UpdateStars(float dt)
    {
        for (int i = stars.Count - 1; i >= 0; i--)
        {
            Star star = stars[i];
            if (star.delay > 0f)
            {
                star.delay -= dt;
                continue;
            }

            star.age += dt;
            if (star.age >= star.life)
            {
                star.quad.Destroy();
                stars.RemoveAt(i);
                continue;
            }

            star.velocity.y -= 1.2f * dt;
            star.position += star.velocity * dt;
            float k = star.age / star.life;

            star.quad.Visible = true;
            star.quad.transform.localPosition = star.position;
            star.quad.transform.localRotation = Quaternion.Euler(0f, 0f, star.spin * star.age);
            star.quad.SetSize(star.size, star.size);
            Color color = star.color;
            color.a = 1f - k * k * k;
            star.quad.Color = color;
        }
    }

    // ───────── コイン ─────────

    private void SpawnCoins()
    {
        int count = tier == SlotWinTier.Jackpot ? 100 : tier == SlotWinTier.BigWin ? 40 : 10;
        count = Mathf.RoundToInt(count * settings.coinAmount);
        if (count <= 0 || !EnsureCoinAssets()) return;

        float spread = tier == SlotWinTier.Jackpot ? 2.6f : tier == SlotWinTier.BigWin ? 1f : 0.25f; // 出し続ける秒数
        float power = tier == SlotWinTier.Jackpot ? 1.25f : 1f;
        Vector3 front = anchors.front;
        Vector3 side = transform.right;
        Vector3 origin = anchors.center + front * (anchors.face + 0.12f) + Vector3.down * (anchors.windowHeight * 0.55f);

        for (int i = 0; i < count; i++)
        {
            GameObject obj = new GameObject("Coin");
            obj.transform.SetParent(transform, false);
            obj.transform.position = origin + side * Random.Range(-0.3f, 0.3f);
            obj.transform.rotation = Random.rotation;
            obj.transform.localScale = Vector3.one * CoinRadius;
            obj.AddComponent<MeshFilter>().sharedMesh = coinMesh;

            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = coinMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.enabled = false;

            coins.Add(new Coin
            {
                body = obj.transform,
                velocity = (front * Random.Range(0.35f, 1.3f) + side * Random.Range(-0.9f, 0.9f) + Vector3.up * Random.Range(0.8f, 2.6f)) * power,
                spinAxis = Random.onUnitSphere,
                spinSpeed = Random.Range(360f, 1080f),
                delay = spread * i / count,
                life = Random.Range(1.8f, 2.4f)
            });
        }
    }

    private void UpdateCoins(float dt)
    {
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            Coin coin = coins[i];
            if (coin.body == null)
            {
                coins.RemoveAt(i);
                continue;
            }

            if (coin.delay > 0f)
            {
                coin.delay -= dt;
                if (coin.delay > 0f) continue;
                coin.body.GetComponent<MeshRenderer>().enabled = true;
            }

            coin.age += dt;
            if (coin.age >= coin.life)
            {
                Destroy(coin.body.gameObject);
                coins.RemoveAt(i);
                continue;
            }

            coin.velocity.y -= Gravity * dt;
            Vector3 position = coin.body.position + coin.velocity * dt;

            if (position.y < anchors.floorY + CoinRadius * 0.3f && coin.velocity.y < 0f)
            {
                position.y = anchors.floorY + CoinRadius * 0.3f;
                if (coin.velocity.y < -1.5f && Time.time - lastCoinSound > 0.06f)
                {
                    lastCoinSound = Time.time;
                    Play(SlotSynth.Sound.Coin, Random.Range(-3f, 4f), 0.35f);
                }
                coin.velocity.y = -coin.velocity.y * Bounce;
                coin.velocity.x *= 0.6f;
                coin.velocity.z *= 0.6f;
                coin.spinSpeed *= 0.5f;

                // 跳ねなくなったら、床に寝かせる
                if (coin.velocity.y < 0.3f)
                {
                    coin.velocity = Vector3.zero;
                    coin.spinSpeed = 0f;
                    coin.body.rotation = Quaternion.Euler(0f, coin.body.eulerAngles.y, 0f);
                }
            }

            coin.body.position = position;
            coin.body.Rotate(coin.spinAxis, coin.spinSpeed * dt, Space.World);
            coin.body.localScale = Vector3.one * (CoinRadius * Mathf.Clamp01((coin.life - coin.age) / 0.35f));
        }
    }

    private static bool EnsureCoinAssets()
    {
        if (coinMesh != null && coinMaterial != null) return true;
        if (coinShaderMissing) return false;

        Shader shader = Resources.Load<Shader>("Shaders/SlotCoin");
        if (shader == null || !shader.isSupported)
        {
            coinShaderMissing = true;
            Debug.LogWarning("[スロット] コインのシェーダー（Resources/Shaders/SlotCoin）が使えません。");
            return false;
        }

        coinMaterial = new Material(shader) { name = "SlotCoin" };
        coinMesh = BuildCoinMesh(8, 0.16f);
        return true;
    }

    /// <summary>半径 1 の薄い円柱（コイン）</summary>
    private static Mesh BuildCoinMesh(int segments, float halfThickness)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2f * i / segments;
            float a1 = Mathf.PI * 2f * (i + 1) / segments;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 up = Vector3.up * halfThickness;

            // 上の面・下の面
            AddTriangle(vertices, normals, triangles, up, p1 + up, p0 + up, Vector3.up, Vector3.up, Vector3.up);
            AddTriangle(vertices, normals, triangles, -up, p0 - up, p1 - up, Vector3.down, Vector3.down, Vector3.down);
            // 側面
            AddTriangle(vertices, normals, triangles, p0 - up, p0 + up, p1 + up, p0, p0, p1);
            AddTriangle(vertices, normals, triangles, p0 - up, p1 + up, p1 - up, p0, p1, p1);
        }

        Mesh mesh = new Mesh { name = "SlotCoin" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddTriangle(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        normals.Add(na); normals.Add(nb); normals.Add(nc);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    }
}
