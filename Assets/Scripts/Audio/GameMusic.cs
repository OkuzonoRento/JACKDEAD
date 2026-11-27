using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 曲を流す。タイトルでは子守歌のようなオルゴール。ゲーム中は静かな環境音で、
/// 敵に追われると、追跡の曲と心臓の音が重なる（近いほど大きく・速くなる）。ときどき、遠くで物がきしむ。
/// 大きさは、設定画面の BGM 音量が掛かる。波形は GameSynth が作る（時間がかかるので、裏で作ってから流す）。
/// シーンには「GameAudio」オブジェクトに付けて置く（作成: メニュー JackDead/Game/ハイのオーラと音をシーンに作成）。
/// </summary>
public class GameMusic : MonoBehaviour
{
    public enum Mode { Title, Game }

    public Mode mode = Mode.Game;

    [Header("再生口（くり返し）")]
    public AudioSource titleSource;
    public AudioSource ambienceSource;
    public AudioSource chaseSource;
    public AudioSource heartbeatSource;

    [Header("大きさ")]
    [Range(0f, 1f)] public float titleVolume = 0.6f;
    [Range(0f, 1f)] public float ambienceVolume = 0.55f;
    [Range(0f, 1f)] public float chaseVolume = 0.85f;
    [Range(0f, 1f)] public float heartbeatVolume = 0.75f;

    [Header("追跡の曲")]
    [Tooltip("この距離（m）以内で敵が追ってきていれば、追跡の曲を流す")]
    public float chaseRadius = 25f;
    [Min(0f), Tooltip("振り切ったあと、曲が残る時間（秒）。そのあと、ゆっくり消える")]
    public float chaseHoldSeconds = 4f;
    [Min(0f), Tooltip("見つかったときの一撃の音を、続けて鳴らさない間隔（秒）")]
    public float stingerInterval = 12f;

    [Header("遠くの物音")]
    [Tooltip("次に鳴るまでの時間（秒）。この範囲でばらつく")]
    public Vector2 creakInterval = new Vector2(11f, 26f);

    private readonly Dictionary<AudioSource, Task<float[]>> pending = new Dictionary<AudioSource, Task<float[]>>();
    private readonly Dictionary<AudioSource, GameSound> pendingSound = new Dictionary<AudioSource, GameSound>();

    private PlayerController player;
    private ResultUIController result;
    private float chaserDistance = -1f;
    private float nextCheck;
    private float hold;
    private float chaseLevel;
    private float closeness;
    private float nextStinger;
    private float nextCreak;
    private float ambienceLevel;
    private float titleLevel;

    /// <summary>追跡の曲の出ている度合い（0〜1）</summary>
    public float ChaseLevel => chaseLevel;

    private void Start()
    {
        if (mode == Mode.Title)
        {
            titleSource = Prepare(titleSource, "Music_Title", GameSound.MusicTitle);
        }
        else
        {
            ambienceSource = Prepare(ambienceSource, "Music_Ambience", GameSound.MusicAmbience);
            chaseSource = Prepare(chaseSource, "Music_Chase", GameSound.MusicChase);
            heartbeatSource = Prepare(heartbeatSource, "Music_Heartbeat", GameSound.Heartbeat);
            nextCreak = Time.time + Random.Range(creakInterval.x, creakInterval.y);
        }
    }

    private AudioSource Prepare(AudioSource source, string label, GameSound sound)
    {
        if (source == null)
        {
            GameObject child = new GameObject(label);
            child.transform.SetParent(transform, false);
            source = child.AddComponent<AudioSource>();
        }
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 0; // 効果音が多くても、曲は止めない
        source.volume = 0f;

        AudioClip clip = GameAudio.Cached(sound);
        if (clip != null) source.clip = clip;
        else
        {
            pending[source] = Task.Run(() => GameSynth.Build(sound));
            pendingSound[source] = sound;
        }
        return source;
    }

    private void FinishPending()
    {
        if (pending.Count == 0) return;
        List<AudioSource> done = null;
        foreach (KeyValuePair<AudioSource, Task<float[]>> pair in pending)
        {
            if (!pair.Value.IsCompleted) continue;
            (done ?? (done = new List<AudioSource>())).Add(pair.Key);
        }
        if (done == null) return;
        foreach (AudioSource source in done)
        {
            Task<float[]> task = pending[source];
            if (task.Status == TaskStatus.RanToCompletion) source.clip = GameAudio.Store(pendingSound[source], task.Result);
            else Debug.LogError($"[GameMusic] 曲を作れませんでした: {pendingSound[source]} {task.Exception}");
            pending.Remove(source);
            pendingSound.Remove(source);
        }
    }

    private void Update()
    {
        FinishPending();
        if (mode == Mode.Title)
        {
            titleLevel = Mathf.MoveTowards(titleLevel, titleSource != null && titleSource.clip != null ? 1f : 0f, Time.unscaledDeltaTime * 0.5f);
            Apply(titleSource, titleVolume * titleLevel * SettingsManager.BgmVolume);
            return;
        }
        UpdateGame();
    }

    private void UpdateGame()
    {
        float dt = Time.unscaledDeltaTime;
        if (player == null) player = FindAnyObjectByType<PlayerController>();
        if (result == null) result = FindAnyObjectByType<ResultUIController>();

        // ステージの切り替えやリザルトの間は静かに。全滅したら、曲は止める
        bool quiet = result != null && (result.IsResultShowing || result.IsTransitioning);
        bool over = player != null && player.IsDown && !player.IsSpectating;

        Camera cam = Camera.main;
        Vector3 ear = cam != null ? cam.transform.position : (player != null ? player.transform.position : Vector3.zero);

        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 0.15f;
            chaserDistance = quiet || over ? -1f : EnemyBrain.NearestChaserDistance(ear, chaseRadius);
        }

        bool chasedNow = chaserDistance >= 0f && !quiet && !over;
        if (chasedNow)
        {
            // 見つかった瞬間
            if (hold <= 0f && chaseLevel < 0.1f && Time.time >= nextStinger)
            {
                nextStinger = Time.time + stingerInterval;
                GameAudio.PlayAsMusic(GameSound.ChaseStinger, 0.7f);
                if (chaseSource != null) chaseSource.time = 0f;
            }
            hold = chaseHoldSeconds;
        }
        else
        {
            hold = quiet || over ? 0f : Mathf.Max(0f, hold - dt);
        }

        bool chasing = chasedNow || hold > 0f;
        float rate = chasing ? 2.5f : (quiet || over ? 1.5f : 0.35f);
        chaseLevel = Mathf.MoveTowards(chaseLevel, chasing ? 1f : 0f, dt * rate);

        // 近いほど 1
        float near = chasedNow ? 1f - Mathf.Clamp01((chaserDistance - 3f) / Mathf.Max(1f, chaseRadius - 3f)) : 0f;
        closeness = Mathf.MoveTowards(closeness, near, dt * 0.8f);

        float pause = PauseMenu.IsOpen ? 0.45f : 1f;
        float bgm = SettingsManager.BgmVolume * pause;

        ambienceLevel = Mathf.MoveTowards(ambienceLevel, over ? 0f : (quiet ? 0.35f : 1f), dt * 0.6f);
        Apply(ambienceSource, ambienceVolume * ambienceLevel * Mathf.Lerp(1f, 0.2f, chaseLevel) * bgm);
        Apply(chaseSource, chaseVolume * chaseLevel * Mathf.Lerp(0.65f, 1f, closeness) * bgm);
        Apply(heartbeatSource, heartbeatVolume * chaseLevel * Mathf.Lerp(0.3f, 1f, closeness) * bgm);
        if (heartbeatSource != null) heartbeatSource.pitch = Mathf.Lerp(0.95f, 1.5f, closeness);

        // 静かな間は、ときどき遠くで物がきしむ・鐘が鳴る
        if (Time.time >= nextCreak)
        {
            nextCreak = Time.time + Random.Range(creakInterval.x, creakInterval.y);
            if (!chasing && !quiet && !over && !PauseMenu.IsOpen)
            {
                float roll = Random.value;
                if (roll < 0.15f) GameAudio.Play(GameSound.FarBell, 0.3f, Random.Range(0.9f, 1.05f));
                else
                {
                    Vector2 around = Random.insideUnitCircle.normalized * Random.Range(6f, 13f);
                    GameAudio.PlayAt(roll < 0.6f ? GameSound.Creak1 : GameSound.Creak2,
                        ear + new Vector3(around.x, Random.Range(-0.5f, 1.5f), around.y), 0.55f, Random.Range(0.82f, 1.12f), 30f);
                }
            }
        }
    }

    /// <summary>大きさを反映する。聞こえない間は止め、聞こえるときに鳴らす</summary>
    private static void Apply(AudioSource source, float volume)
    {
        if (source == null || source.clip == null) return;
        source.volume = Mathf.Clamp01(volume);
        if (volume > 0.001f)
        {
            if (!source.isPlaying) source.Play();
        }
        else if (source.isPlaying) source.Pause();
    }
}
