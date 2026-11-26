using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// スロットの当たり演出（JACKPOT）を、PV の時刻に合わせて出す。
/// 絵・板・コインはゲームの部品（SlotPixelArt / SlotPixelQuad / SlotCoin）を使い、
/// 動きと時間の割り振りは SlotWinEffects と同じにしている（再生しないとコルーチンが動かないため、ここで組み直す）。
/// SlotWinEffects を変えたら、ここも見直すこと。
/// </summary>
public class PVSlotWin
{
    // SlotWinEffects の JACKPOT の時間と同じ
    private const float StartDelay = 0.18f;
    private const float Step = 0.27f;
    private const float CountTime = 1.3f;
    private const int Reels = 3;
    public const float FinaleStart = StartDelay + Step * (Reels + 2) + CountTime;

    private static readonly Color Pink = new Color(1f, 0.18f, 0.72f);
    private static readonly Color Yellow = new Color(1f, 0.86f, 0.15f);
    private static readonly Color Red = new Color(1f, 0.1f, 0.05f);

    private readonly SlotAnchors anchors;
    private readonly SlotReelScroller[] reels;
    private readonly Transform root;
    private readonly List<Object> owned;
    private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    private readonly SlotPixelQuad[] frames;
    private readonly SlotPixelQuad glow, amount, title, board;
    private readonly List<SlotPixelQuad> stars = new List<SlotPixelQuad>();
    private readonly List<Transform> coins = new List<Transform>();
    private readonly Light light;
    private readonly int reward;
    private readonly float machineMultiplier;

    public PVSlotWin(SlotAnchors slotAnchors, SlotReelScroller[] slotReels, int rewardAmount, float rarity, List<Object> ownedObjects)
    {
        anchors = slotAnchors;
        reels = slotReels;
        reward = rewardAmount;
        machineMultiplier = rarity;
        owned = ownedObjects;

        root = new GameObject("PVSlotWin").transform;
        root.SetPositionAndRotation(anchors.center, Quaternion.LookRotation(-anchors.front, Vector3.up));

        frames = new SlotPixelQuad[reels.Length];
        int pixels = Mathf.RoundToInt(24f * anchors.frameWidth / Mathf.Max(0.01f, anchors.frameHeight));
        for (int i = 0; i < reels.Length; i++)
        {
            frames[i] = Quad("WinFrame", 1);
            frames[i].Texture = Texture("frame", () => SlotPixelArt.Frame(pixels, 24));
        }
        glow = Quad("WindowGlow", 0);
        board = Quad("WinBoard", 2);
        title = Quad("WinTitle", 4);
        amount = Quad("WinAmount", 5);

        int width = Mathf.RoundToInt((anchors.windowWidth + 0.09f) / 0.008f);
        int height = Mathf.RoundToInt((anchors.windowHeight + 0.09f) / 0.008f);
        glow.Texture = Texture("glow", () => SlotPixelArt.Glow(width, height));

        for (int i = 0; i < 46; i++)
        {
            SlotPixelQuad star = Quad("Star", 3);
            star.Texture = Texture("star", SlotPixelArt.Star);
            stars.Add(star);
        }

        Shader coinShader = Resources.Load<Shader>("Shaders/SlotCoin");
        MethodInfo build = typeof(SlotWinEffects).GetMethod("BuildCoinMesh", BindingFlags.Static | BindingFlags.NonPublic);
        if (coinShader != null && build != null)
        {
            Mesh mesh = (Mesh)build.Invoke(null, new object[] { 8, 0.16f });
            Material material = new Material(coinShader);
            owned.Add(mesh);
            owned.Add(material);
            for (int i = 0; i < 100; i++)
            {
                GameObject coin = new GameObject("Coin");
                coin.transform.SetParent(root, false);
                coin.AddComponent<MeshFilter>().sharedMesh = mesh;
                coin.AddComponent<MeshRenderer>().sharedMaterial = material;
                coins.Add(coin.transform);
            }
        }

        light = new GameObject("WinLight").AddComponent<Light>();
        light.transform.SetParent(root, false);
        light.transform.localPosition = new Vector3(0f, 0.35f, -(anchors.face + 0.55f));
        light.type = LightType.Point;
        light.shadows = LightShadows.None;

        Hide();
    }

    private SlotPixelQuad Quad(string name, int order)
    {
        SlotPixelQuad quad = SlotPixelQuad.Create(root, name);
        if (quad == null) throw new Exception("スロットの演出のシェーダーが使えません。");
        quad.Order = order;
        owned.Add(quad.transform.GetComponent<MeshRenderer>().sharedMaterial);
        return quad;
    }

    private Texture2D Texture(string key, Func<SlotPixelArt.Image> draw)
    {
        if (textures.TryGetValue(key, out Texture2D texture)) return texture;
        texture = SlotPixelQuad.ToTexture(draw());
        textures[key] = texture;
        owned.Add(texture);
        return texture;
    }

    private static string Money(int value) => "+$" + value.ToString("N0", CultureInfo.InvariantCulture);

    public void Hide()
    {
        foreach (SlotPixelQuad f in frames) f.Visible = false;
        glow.Visible = board.Visible = title.Visible = amount.Visible = false;
        foreach (SlotPixelQuad s in stars) s.Visible = false;
        foreach (Transform c in coins) c.gameObject.SetActive(false);
        light.enabled = false;
        foreach (SlotReelScroller reel in reels) reel.SetFlash(Color.white, 0f);
    }

    /// <summary>最後のリールが止まってから t 秒後の見た目にする</summary>
    public void Show(float t)
    {
        Hide();
        if (t < StartDelay) return;

        float finale = t - FinaleStart; // 大きな文字が出てからの時間（出る前は負）
        bool alternate = finale >= 0f && Mathf.Repeat(finale * 2.5f, 1f) >= 0.5f;
        float kick = 0f;
        Color kickColor = Pink;

        // 1. そろった目を、左から順に囲む
        for (int i = 0; i < reels.Length; i++)
        {
            float age = t - (StartDelay + Step * i);
            if (age < 0f) continue;
            float slam = Mathf.Lerp(1.8f, 1f, Mathf.Clamp01(age / 0.1f));
            float beat = 1f + 0.04f * Mathf.Sin(age * 16f);
            frames[i].transform.localPosition = new Vector3(anchors.reelOffsets[i], anchors.symbolY, -(anchors.face + 0.015f));
            frames[i].SetSize(anchors.frameWidth * slam * beat, anchors.frameHeight * slam * beat);
            frames[i].Color = age < 0.07f ? Color.white : alternate ? Yellow : Pink;
            frames[i].Visible = true;

            float flash = Mathf.Max(0f, 1f - age * 3f);
            if (finale >= 0f) flash = Mathf.Max(flash, alternate ? 0.7f : 0.25f);
            reels[i].SetFlash(alternate ? Yellow : Pink, flash);
            kick = Mathf.Max(kick, 2.5f - age * 10f);
        }

        // 2. 掲示板に倍率が積み上がり、3. 金額が数え上がる
        float chainStart = StartDelay + Step * reels.Length;
        float countStart = chainStart + Step * 2f;
        string text = null;
        if (t >= chainStart) text = "x7";
        if (t >= chainStart + Step) text += " x" + machineMultiplier.ToString("0.#", CultureInfo.InvariantCulture);
        if (t >= countStart)
        {
            float k = Mathf.Clamp01((t - countStart) / CountTime);
            float stepped = Mathf.Floor(k * 22f) / 22f;
            text = Money(finale >= 0f ? reward : Mathf.RoundToInt(reward * stepped * stepped));
        }
        if (text != null)
        {
            string boardText = text;
            Texture2D picture = Texture("board" + boardText, () => SlotPixelArt.Led(boardText, new SlotPixelArt.Rgba(255, 242, 219)));
            board.Texture = picture;
            board.transform.localPosition = new Vector3(0f, anchors.topY - anchors.center.y + 0.1f, -(anchors.face + 0.03f));
            board.SetWidth(anchors.windowWidth * 0.96f);
            board.Color = Color.white;
            board.Visible = true;
        }
        if (t >= countStart && finale < 0f)
        {
            string counting = text;
            Texture2D picture = Texture("amount" + counting, () => SlotPixelArt.Title(counting,
                new SlotPixelArt.Rgba(255, 90, 205), new SlotPixelArt.Rgba(230, 25, 160),
                new SlotPixelArt.Rgba(255, 244, 220), new SlotPixelArt.Rgba(40, 5, 35)));
            amount.Texture = picture;
            float h = anchors.windowHeight * 0.21f;
            float w = h * picture.width / picture.height;
            float limit = anchors.windowWidth * 0.98f;
            if (w > limit) { h *= limit / w; w = limit; }
            amount.transform.localPosition = new Vector3(0f, anchors.symbolY, -(anchors.face + 0.13f));
            amount.SetSize(w, h);
            amount.Color = Color.white;
            amount.Visible = true;
        }

        if (finale >= 0f)
        {
            // 4. 仕上げ：JACKPOT の見出し、光る窓の枠、星、コイン
            bool second = alternate;
            title.Texture = Texture("title" + second, () => second
                ? SlotPixelArt.Title("JACKPOT", new SlotPixelArt.Rgba(255, 255, 235), new SlotPixelArt.Rgba(255, 225, 40), new SlotPixelArt.Rgba(215, 20, 150), new SlotPixelArt.Rgba(45, 5, 40))
                : SlotPixelArt.Title("JACKPOT", new SlotPixelArt.Rgba(255, 50, 185), new SlotPixelArt.Rgba(215, 20, 150), new SlotPixelArt.Rgba(255, 225, 40), new SlotPixelArt.Rgba(45, 5, 40)));
            float slam = finale < 0.12f ? Mathf.Lerp(2.4f, 0.9f, finale / 0.12f)
                : 1f - 0.1f * Mathf.Exp(-(finale - 0.12f) * 8f) * Mathf.Cos((finale - 0.12f) * 25f);
            title.transform.localPosition = new Vector3(0f, 0f, -(anchors.face + 0.11f));
            title.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(finale * 7f) * 4f);
            title.SetWidth(anchors.windowWidth * 1.02f, slam * (1f + 0.05f * Mathf.Sin(finale * 12f)));
            title.Color = Color.white;
            title.Visible = true;

            float pulse = 0.82f + 0.18f * Mathf.Sin(finale * Mathf.PI * 4f);
            glow.transform.localPosition = new Vector3(0f, 0f, -(anchors.face + 0.082f));
            glow.SetSize(anchors.windowWidth + 0.09f, anchors.windowHeight + 0.09f);
            glow.Color = new Color(1f, 1f, 1f, pulse * Mathf.Clamp01(finale / 0.12f));
            glow.Visible = true;

            Stars(finale);
            Coins(finale);

            light.enabled = true;
            light.range = 7f;
            light.intensity = 5.5f * (0.75f + 0.25f * Mathf.Cos(finale * Mathf.PI * 5f));
            light.color = alternate ? Yellow : Red;
        }
        else if (kick > 0.01f)
        {
            light.enabled = true;
            light.range = 4.5f;
            light.intensity = kick;
            light.color = kickColor;
        }
    }

    private void Stars(float t)
    {
        System.Random random = new System.Random(5);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        for (int i = 0; i < stars.Count; i++)
        {
            float angle = R(0f, Mathf.PI * 2f);
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 start = new Vector3(direction.x * anchors.windowWidth * 0.4f, direction.y * anchors.windowHeight * 0.35f, -(anchors.face + 0.07f + R(0.02f, 0.12f)));
            Vector3 velocity = direction * R(0.3f, 1.1f) + new Vector3(0f, 0.35f, -R(0.05f, 0.35f));
            float size = R(0.045f, 0.11f), spin = R(-240f, 240f), life = R(0.7f, 1.2f);
            Color color = R(0f, 1f) < 0.55f ? Pink : Yellow;

            float age = t - 2.2f * i / stars.Count;
            if (age < 0f || age >= life) continue;
            float k = age / life;
            color.a = 1f - k * k * k;
            stars[i].transform.localPosition = start + velocity * age + Vector3.down * (0.6f * age * age);
            stars[i].transform.localRotation = Quaternion.Euler(0f, 0f, spin * age);
            stars[i].SetSize(size, size);
            stars[i].Color = color;
            stars[i].Visible = true;
        }
    }

    private void Coins(float t)
    {
        const float radius = 0.045f, step = 1f / 60f;
        System.Random random = new System.Random(11);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        Vector3 front = anchors.front;
        Vector3 side = root.right;
        Vector3 origin = anchors.center + front * (anchors.face + 0.12f) + Vector3.down * (anchors.windowHeight * 0.55f);

        for (int i = 0; i < coins.Count; i++)
        {
            Vector3 position = origin + side * R(-0.3f, 0.3f);
            Vector3 velocity = (front * R(0.35f, 1.3f) + side * R(-0.9f, 0.9f) + Vector3.up * R(0.8f, 2.6f)) * 1.25f;
            Quaternion rotation = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
            Vector3 axis = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;
            float spin = R(360f, 1080f), life = R(1.8f, 2.4f);

            float age = t - 2.6f * i / coins.Count;
            if (age < 0f || age >= life) continue;
            for (float s = 0f; s < age; s += step)
            {
                velocity.y -= 9.8f * step;
                position += velocity * step;
                rotation = Quaternion.AngleAxis(spin * step, axis) * rotation;
                if (position.y < anchors.floorY + radius * 0.3f && velocity.y < 0f)
                {
                    position.y = anchors.floorY + radius * 0.3f;
                    velocity = new Vector3(velocity.x * 0.6f, -velocity.y * 0.4f, velocity.z * 0.6f);
                    spin *= 0.5f;
                    if (velocity.y < 0.3f)
                    {
                        velocity = Vector3.zero;
                        spin = 0f;
                        rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                    }
                }
            }
            coins[i].SetPositionAndRotation(position, rotation);
            coins[i].localScale = Vector3.one * (radius * Mathf.Clamp01((life - age) / 0.35f));
            coins[i].gameObject.SetActive(true);
        }
    }
}
