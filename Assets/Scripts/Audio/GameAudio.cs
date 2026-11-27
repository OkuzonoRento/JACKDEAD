using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ゲーム全体の効果音を鳴らす。波形は GameSynth がコードで作る（一度作ったら使い回す）。
/// 素材の音に差し替えるには、Assets/Resources/GameSounds に、GameSound と同じ名前の音声ファイルを置く（例: Explosion.wav）。
/// 大きさは、設定画面の SE 音量が掛かる。シーンには「GameAudio」オブジェクトとして置く
/// （作成: メニュー JackDead/Game/ハイのオーラと音をシーンに作成）。置いていないシーンでは、鳴らすときに自動で作る。
/// 画面のボタンの音（重ねたとき・押したとき）と、ダメージの音もここで鳴らす。
/// </summary>
public class GameAudio : MonoBehaviour
{
    [Range(0f, 1f), Tooltip("効果音全体の大きさ（設定画面の SE 音量も掛かる）")]
    public float effectVolume = 0.8f;
    [Tooltip("自分の音・画面の音（場所を持たない）")]
    public AudioSource[] flatSources;
    [Tooltip("場所のある音")]
    public AudioSource[] spatialSources;

    private const int FlatCount = 6;
    private const int SpatialCount = 20;

    private static GameAudio instance;
    private static AudioClip[] clips;
    private static float lastStep = -1f;

    private int nextFlat;
    private int nextSpatial;
    private Selectable hovered;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

    public static GameAudio Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<GameAudio>();
            if (instance == null && Application.isPlaying) instance = new GameObject("GameAudio").AddComponent<GameAudio>();
            return instance;
        }
    }

    /// <summary>音のデータ。素材があればそれを、無ければ作ったものを返す</summary>
    public static AudioClip Clip(GameSound sound)
    {
        AudioClip clip = Cached(sound);
        if (clip != null) return clip;
        return Store(sound, GameSynth.Build(sound));
    }

    /// <summary>作り終えた音・素材の音があれば返す（無ければ null）</summary>
    public static AudioClip Cached(GameSound sound)
    {
        if (clips == null) clips = new AudioClip[System.Enum.GetValues(typeof(GameSound)).Length];
        int index = (int)sound;
        if (clips[index] == null) clips[index] = Resources.Load<AudioClip>("GameSounds/" + sound);
        return clips[index];
    }

    /// <summary>作った波形を音のデータにして覚える（曲は左右に広げる）。メインスレッドで呼ぶ</summary>
    public static AudioClip Store(GameSound sound, float[] data)
    {
        AudioClip clip = Cached(sound);
        if (clip != null) return clip;

        bool wide = GameSynth.IsLoop(sound) && sound != GameSound.Heartbeat;
        float[] samples = wide ? GameSynth.Widen(data) : data;
        int channels = wide ? 2 : 1;
        clip = AudioClip.Create("Game_" + sound, samples.Length / channels, channels, GameSynth.SampleRate, false);
        clip.SetData(samples, 0);
        clips[(int)sound] = clip;
        return clip;
    }

    /// <summary>場所を持たない音（自分の体・画面）</summary>
    public static void Play(GameSound sound, float volume = 1f, float pitch = 1f)
    {
        GameAudio audio = Instance;
        if (audio == null) return;
        audio.PlayFlat(sound, volume * audio.effectVolume * SettingsManager.SeVolume, pitch);
    }

    /// <summary>曲の一部として鳴らす音（BGM 音量が掛かる）</summary>
    public static void PlayAsMusic(GameSound sound, float volume = 1f)
    {
        GameAudio audio = Instance;
        if (audio != null) audio.PlayFlat(sound, volume * SettingsManager.BgmVolume, 1f);
    }

    /// <summary>その場所から聞こえる音。range より遠いと聞こえない</summary>
    public static void PlayAt(GameSound sound, Vector3 position, float volume = 1f, float pitch = 1f, float range = 20f)
    {
        GameAudio audio = Instance;
        if (audio == null) return;

        // 遠くて聞こえない音は、鳴らさない（数に限りのある再生口を取らない）
        Camera ear = Camera.main;
        if (ear != null && (ear.transform.position - position).sqrMagnitude > range * range) return;

        AudioSource source = audio.NextSpatial();
        source.transform.position = position;
        source.maxDistance = range;
        source.pitch = pitch;
        source.clip = Clip(sound);
        source.volume = Mathf.Clamp01(volume * audio.effectVolume * SettingsManager.SeVolume);
        source.Play();
    }

    /// <summary>足音（4種類から選ぶ）。position を指定すると、その場所から聞こえる</summary>
    public static void PlayStep(float volume, Vector3? position = null)
    {
        GameSound step = GameSound.Step1 + Random.Range(0, 4);
        float pitch = Random.Range(0.9f, 1.1f);
        if (position.HasValue) PlayAt(step, position.Value, volume, pitch, 18f);
        else if (Time.time - lastStep > 0.15f)
        {
            lastStep = Time.time;
            Play(step, volume, pitch);
        }
    }

    /// <summary>物がぶつかる音（箱は木、ほかは金属）</summary>
    public static void PlayImpact(GameObject thrown, Vector3 point, float speed)
    {
        GameSound sound = thrown.GetComponent<BombBox>() != null ? GameSound.ImpactWood : GameSound.ImpactMetal;
        PlayAt(sound, point, Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(2f, 10f, speed)), Random.Range(0.88f, 1.15f), 22f);
    }

    // ------------------------------------------------------------------

    private void Awake()
    {
        if (instance == null) instance = this;
        if (flatSources == null || flatSources.Length == 0) flatSources = MakeSources("Flat", FlatCount, 0f);
        if (spatialSources == null || spatialSources.Length == 0) spatialSources = MakeSources("Spatial", SpatialCount, 1f);
        foreach (AudioSource s in flatSources) Setup(s, 0f);
        foreach (AudioSource s in spatialSources) Setup(s, 1f);
    }

    private AudioSource[] MakeSources(string label, int count, float spatial)
    {
        AudioSource[] result = new AudioSource[count];
        for (int i = 0; i < count; i++)
        {
            GameObject child = new GameObject($"{label}_{i + 1:00}");
            child.transform.SetParent(transform, false);
            result[i] = child.AddComponent<AudioSource>();
            Setup(result[i], spatial);
        }
        return result;
    }

    private static void Setup(AudioSource source, float spatial)
    {
        if (source == null) return;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = spatial;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.dopplerLevel = 0f;
    }

    private void OnEnable() { PlayerController.DamageTaken += OnDamage; }

    private void OnDisable() { PlayerController.DamageTaken -= OnDamage; }

    private void OnDamage(float amount)
    {
        Play(GameSound.Hurt, Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(amount / 30f)), Random.Range(0.93f, 1.07f));
    }

    private void PlayFlat(GameSound sound, float volume, float pitch)
    {
        if (flatSources == null || flatSources.Length == 0) return;

        // 鳴っていない再生口を使う。全部鳴っていれば、順番に上書きする
        AudioSource source = null;
        for (int i = 0; i < flatSources.Length && source == null; i++)
            if (flatSources[(nextFlat + i) % flatSources.Length] is AudioSource s && !s.isPlaying) source = s;
        if (source == null) source = flatSources[nextFlat % flatSources.Length];
        nextFlat = (nextFlat + 1) % flatSources.Length;

        source.pitch = pitch;
        source.clip = Clip(sound);
        source.volume = Mathf.Clamp01(volume);
        source.Play();
    }

    private AudioSource NextSpatial()
    {
        for (int i = 0; i < spatialSources.Length; i++)
        {
            int at = (nextSpatial + i) % spatialSources.Length;
            if (spatialSources[at].isPlaying) continue;
            nextSpatial = at + 1;
            return spatialSources[at];
        }
        AudioSource oldest = spatialSources[nextSpatial % spatialSources.Length];
        nextSpatial++;
        return oldest;
    }

    // ───────── 画面のボタン ─────────

    private void Update()
    {
        EventSystem events = EventSystem.current;
        Mouse mouse = Mouse.current;
        if (events == null || mouse == null || !Cursor.visible)
        {
            hovered = null;
            return;
        }

        PointerEventData pointer = new PointerEventData(events) { position = mouse.position.ReadValue() };
        hits.Clear();
        events.RaycastAll(pointer, hits);
        Selectable current = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<Selectable>() : null;
        if (current != null && !current.IsInteractable()) current = null;

        if (current != hovered)
        {
            hovered = current;
            if (current != null) Play(GameSound.UiHover, 0.8f);
        }
        if (current != null && mouse.leftButton.wasPressedThisFrame) Play(GameSound.UiClick);
    }
}
