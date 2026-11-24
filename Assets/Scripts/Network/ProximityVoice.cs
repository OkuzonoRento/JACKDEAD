#if JD_VIVOX
using System;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 近接ボイスチャット（Unity Vivox）。
/// ・生きている人：部屋のコードごとの「位置つきチャンネル」。自分のカメラの位置と向きを伝えると、相手の声が距離に応じて小さくなり、方向から聞こえる
/// ・倒れて観戦している人：別の「観戦者だけのチャンネル」（距離に関係なく、観戦者どうしだけで話せる）。生きている人にも敵にも聞こえない
///   （倒れた瞬間に、生きている人のチャンネルから抜けて、観戦者のチャンネルへ移る。復活すると戻る）
/// ・マイクの使い方は、設定（キー設定）の「マイクの使い方」で、自動で拾う / プッシュトゥトーク
///     - 自動：声を出すと拾う
///     - プッシュトゥトーク：「PushToTalk」のキー（初期は V）を押している間だけ話せる
///   「MicMute」のキー（初期は B）で、どちらの使い方でもミュートを切り替えられる（ミュート中は、押しても話せない）
/// ・相手の声の大きさは、設定の「ボイスチャット音量」つまみ
/// ・敵は、生きている人の声に反応する（声の大きさに応じて、遠くまで聞こえる）。観戦者の声には反応しない
/// 使うには：Unity Cloud のダッシュボードで、このプロジェクトの Vivox を有効にしておく。
/// </summary>
public class ProximityVoice : MonoBehaviour
{
    [Header("聞こえる距離（生きている人どうし）")]
    [Tooltip("この距離（m）より遠い相手の声は、まったく聞こえない")]
    [SerializeField, Min(2)] private int audibleDistance = 24;
    [Tooltip("この距離（m）までは、声が小さくならない")]
    [SerializeField, Min(1)] private int conversationalDistance = 3;
    [Tooltip("遠いほど小さくなる強さ（大きいほど、すぐに小さくなる）")]
    [SerializeField, Range(0.1f, 3f)] private float fadeIntensity = 1.2f;

    [Header("位置の送信")]
    [SerializeField, Min(0.05f)] private float positionInterval = 0.1f;

    [Header("敵への音")]
    [Tooltip("声が出ている間、この間隔（秒）で、敵に声を聞かせる")]
    [SerializeField, Min(0.1f)] private float noiseInterval = 0.3f;

    private const string PrefsMuted = "jd.micMuted";
    private const float JoinDelayAfterSceneLoad = 4f;

    private enum Mode { None, Alive, Dead }

    private static ProximityVoice instance;

    private bool busy;
    private Mode currentMode = Mode.None;
    private string currentChannel = "";
    private float nextPositionTime;
    private float nextNoiseTime;
    private float retryAt;
    private bool micMuted;
    private bool loggedIn;
    private bool initialized;
    private bool pttHeld;
    private bool selfSpeaking;
    private bool lastInputMuted;
    private bool inputMuteKnown;
    private PlayerController localPlayer;

    private InputAction muteAction;
    private InputAction pttAction;

    /// <summary>マイクがミュートされているか</summary>
    public static bool MicMuted { get; private set; }
    /// <summary>ボイスチャットに参加しているか</summary>
    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        MicMuted = false;
        IsActive = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<ProximityVoice>() != null) return;
        GameObject go = new GameObject("ProximityVoice");
        DontDestroyOnLoad(go);
        go.AddComponent<ProximityVoice>();
    }

    private void Awake()
    {
        instance = this;
        micMuted = PlayerPrefs.GetInt(PrefsMuted, 0) == 1;
        MicMuted = micMuted;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        IsActive = false;
    }

    // 設定画面から呼ばれる
    public static void ApplyOutputVolume()
    {
        if (instance != null) instance.ApplyOutputVolumeInternal();
    }

    public static void ApplyMicState()
    {
        if (instance != null) instance.inputMuteKnown = false; // 次のフレームで、マイクの状態を決め直す
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        // マップの生成で画面が止まっている間は、参加しない（止まっている間に音声の通信が途切れて、外されることがあるため）
        bool online = net != null && net.IsOnline && net.State == NetworkSessionState.InGame && !string.IsNullOrEmpty(net.RoomCode)
                      && Time.timeSinceLevelLoad >= JoinDelayAfterSceneLoad;

        if (localPlayer == null) localPlayer = FindAnyObjectByType<PlayerController>();
        bool dead = localPlayer != null && localPlayer.IsDown;

        Mode wantedMode = !online ? Mode.None : (dead ? Mode.Dead : Mode.Alive);
        string wantedChannel = wantedMode == Mode.Alive ? "jd_" + net.RoomCode.ToLowerInvariant()
                             : wantedMode == Mode.Dead ? "jd_dead_" + net.RoomCode.ToLowerInvariant()
                             : "";

        if (!busy && (wantedMode != currentMode || wantedChannel != currentChannel))
        {
            if (wantedMode == Mode.None) _ = LeaveAsync();
            else if (Time.unscaledTime >= retryAt) _ = SwitchAsync(wantedMode, wantedChannel, net.LocalPlayerName);
        }

        // 他の場所（同じアカウントでの別のログインなど）で抜けさせられていたら、状態を戻して、少し待ってから入り直す
        if (currentMode != Mode.None && !busy && !IsInChannel(currentChannel))
        {
            Debug.LogWarning($"[ボイス] チャンネルから外れていました。少し待って、入り直します: {currentChannel}");
            currentMode = Mode.None;
            currentChannel = "";
            IsActive = false;
            selfSpeaking = false;
            inputMuteKnown = false;
            loggedIn = SafeIsLoggedIn();
            retryAt = Time.unscaledTime + 5f;
            return;
        }

        if (currentMode == Mode.None || busy) return;

        ReadInputs();
        UpdateMic();
        if (currentMode == Mode.Alive)
        {
            SendPosition();
            EmitVoiceNoise();
        }
    }

    // ------------------------------------------------------------------
    // 参加・退出
    // ------------------------------------------------------------------
    private static bool IsInChannel(string channel)
    {
        if (string.IsNullOrEmpty(channel)) return false;
        try { return VivoxService.Instance.ActiveChannels.ContainsKey(channel); }
        catch (Exception) { return false; }
    }

    private static bool SafeIsLoggedIn()
    {
        try { return VivoxService.Instance.IsLoggedIn; }
        catch (Exception) { return false; }
    }


    private async Task SwitchAsync(Mode mode, string channel, string displayName)
    {
        busy = true;
        try
        {
            // いまのチャンネルから抜ける（生きている人 ⇔ 観戦者の切り替えのときも、先に抜ける）
            await LeaveChannelOnlyAsync(keepLogin: true);

            if (!initialized)
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                await VivoxService.Instance.InitializeAsync();
                initialized = true;
            }

            if (!loggedIn)
            {
                LoginOptions options = new LoginOptions { DisplayName = SafeName(displayName), EnableTTS = false };
                await VivoxService.Instance.LoginAsync(options);
                loggedIn = true;
            }

            if (mode == Mode.Alive)
            {
                Channel3DProperties props = new Channel3DProperties(
                    audibleDistance, conversationalDistance, fadeIntensity, AudioFadeModel.InverseByDistance);
                await VivoxService.Instance.JoinPositionalChannelAsync(channel, ChatCapability.AudioOnly, props);
            }
            else
            {
                // 観戦者だけのチャンネル。位置は関係なく、同じチャンネルの人どうしで話せる
                await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
            }

            currentMode = mode;
            currentChannel = channel;
            IsActive = true;
            inputMuteKnown = false;
            ApplyOutputVolumeInternal();
            Debug.Log($"[ボイス] 参加しました: {channel}（{(mode == Mode.Alive ? "生きている人" : "観戦者")}）");
        }
        catch (Exception e)
        {
            // Vivox が有効でない、マイクが無い、など。ゲームは続けられるので、少し待って、やり直す
            Debug.LogWarning($"[ボイス] ボイスチャットに参加できませんでした: {e.Message}");
            currentMode = Mode.None;
            currentChannel = "";
            IsActive = false;
            retryAt = Time.unscaledTime + 15f;
        }
        finally
        {
            busy = false;
        }
    }

    private async Task LeaveAsync()
    {
        busy = true;
        try
        {
            await LeaveChannelOnlyAsync(keepLogin: false);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task LeaveChannelOnlyAsync(bool keepLogin)
    {
        string channel = currentChannel;
        currentMode = Mode.None;
        currentChannel = "";
        IsActive = false;
        selfSpeaking = false;
        try
        {
            if (!string.IsNullOrEmpty(channel) && initialized && IsInChannel(channel)) await VivoxService.Instance.LeaveChannelAsync(channel);
            if (!keepLogin && loggedIn && SafeIsLoggedIn())
            {
                await VivoxService.Instance.LogoutAsync();
                loggedIn = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ボイス] 退出中にエラー: {e.Message}");
        }
    }

    /// <summary>表示名に使えない文字を取り除く</summary>
    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Player";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "Player";
    }

    // ------------------------------------------------------------------
    // 入力（ミュート・プッシュトゥトーク）
    // ------------------------------------------------------------------

    private void ReadInputs()
    {
        InputActionAsset asset = KeyBindings.Asset;
        if (asset != null)
        {
            if (muteAction == null) { muteAction = asset.FindAction(KeyBindings.MapName + "/MicMute", false); muteAction?.Enable(); }
            if (pttAction == null) { pttAction = asset.FindAction(KeyBindings.MapName + "/PushToTalk", false); pttAction?.Enable(); }
        }

        bool blocked = PauseMenu.IsOpen || KeyBindings.Busy;

        if (!blocked && muteAction != null && muteAction.WasPressedThisFrame())
        {
            micMuted = !micMuted;
            MicMuted = micMuted;
            PlayerPrefs.SetInt(PrefsMuted, micMuted ? 1 : 0);
        }

        pttHeld = !blocked && pttAction != null && pttAction.IsPressed();
    }

    /// <summary>マイクを開けるかどうかを決めて、変わったときだけ Vivox に伝える</summary>
    private void UpdateMic()
    {
        bool shouldMute = micMuted || (SettingsManager.PushToTalk && !pttHeld);
        if (inputMuteKnown && shouldMute == lastInputMuted) return;

        try
        {
            if (shouldMute) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
            lastInputMuted = shouldMute;
            inputMuteKnown = true;
        }
        catch (Exception) { }
    }

    private void ApplyOutputVolumeInternal()
    {
        if (!initialized) return;
        try
        {
            float v = SettingsManager.VoiceVolume; // 0〜2（1 が標準）
            if (v <= 0.001f)
            {
                VivoxService.Instance.MuteOutputDevice();
                return;
            }
            VivoxService.Instance.UnmuteOutputDevice();
            int vol = v <= 1f ? Mathf.RoundToInt(Mathf.Lerp(-50f, 0f, v)) : Mathf.RoundToInt(Mathf.Lerp(0f, 50f, v - 1f));
            VivoxService.Instance.SetOutputDeviceVolume(Mathf.Clamp(vol, -50, 50));
        }
        catch (Exception) { }
    }

    // ------------------------------------------------------------------
    // 位置・敵への音
    // ------------------------------------------------------------------

    private void SendPosition()
    {
        if (Time.unscaledTime < nextPositionTime) return;
        nextPositionTime = Time.unscaledTime + positionInterval;

        Camera cam = Camera.main;
        if (cam == null) return;

        Transform t = cam.transform;
        try
        {
            // 声を出す位置と、聞く位置は、どちらも自分の目の位置（相手の位置は、相手が自分で送る）
            VivoxService.Instance.Set3DPosition(t.position, t.position, t.forward, t.up, currentChannel, true);
        }
        catch (Exception) { }
    }

    /// <summary>自分が話している間、敵に声を聞かせる（生きているときだけ。声が大きいほど遠くまで聞こえる）</summary>
    private void EmitVoiceNoise()
    {
        if (Time.unscaledTime < nextNoiseTime) return;
        nextNoiseTime = Time.unscaledTime + noiseInterval;

        if (lastInputMuted) return; // ミュート中（プッシュトゥトークで押していないときも含む）は、声は出ない

        float energy = 0f;
        bool speaking = false;
        try
        {
            if (VivoxService.Instance.ActiveChannels.TryGetValue(currentChannel, out var participants))
            {
                foreach (VivoxParticipant p in participants)
                {
                    if (!p.IsSelf) continue;
                    speaking = p.SpeechDetected;
                    energy = (float)p.AudioEnergy;
                    break;
                }
            }
        }
        catch (Exception) { }

        selfSpeaking = speaking;
        if (!speaking) return;

        Camera cam = Camera.main;
        Vector3 pos = localPlayer != null ? localPlayer.transform.position : (cam != null ? cam.transform.position : Vector3.zero);
        float loudness = Mathf.Lerp(EnemyNoise.VoiceMin, EnemyNoise.VoiceMax, Mathf.Clamp01(energy * 2f));
        GameplaySync.NotifyVoiceNoise(pos, loudness);
    }

    // ------------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------------

    private static string KeyName(string action)
    {
        foreach (KeyBindings.Entry e in KeyBindings.Entries)
        {
            if (e.action != action) continue;
            string shown = KeyBindings.GetDisplay(e, KeyBindings.Scheme.KeyboardMouse);
            return string.IsNullOrEmpty(shown) ? "-" : shown;
        }
        return "-";
    }

    private void OnGUI()
    {
        if (currentMode == Mode.None) return;

        // 右下に、小さな状態表示
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerRight };
        string text;
        Color color;
        if (micMuted)
        {
            text = Localization.T($"マイク: ミュート中 [{KeyName("MicMute")}]", $"MIC: MUTED [{KeyName("MicMute")}]");
            color = new Color(1f, 0.4f, 0.4f);
        }
        else if (SettingsManager.PushToTalk)
        {
            text = pttHeld
                ? Localization.T($"マイク: 話し中 [{KeyName("PushToTalk")}]", $"MIC: TALKING [{KeyName("PushToTalk")}]")
                : Localization.T($"マイク: 押して話す [{KeyName("PushToTalk")}]", $"MIC: PUSH TO TALK [{KeyName("PushToTalk")}]");
            color = pttHeld ? new Color(0.7f, 1f, 0.7f) : new Color(0.85f, 0.85f, 0.85f);
        }
        else
        {
            text = Localization.T($"マイク: オン [{KeyName("MicMute")}でミュート]", $"MIC: ON [{KeyName("MicMute")} TO MUTE]");
            color = new Color(0.7f, 1f, 0.7f);
        }
        if (currentMode == Mode.Dead) text = Localization.T("観戦者チャット  ", "SPECTATOR CHAT  ") + text;

        style.normal.textColor = color;
        GUI.Label(new Rect(Screen.width - 560f, Screen.height - 40f, 540f, 30f), text, style);
    }
}
#else
using UnityEngine;

/// <summary>
/// 近接ボイスチャットの代わりの空の実装（Vivox のパッケージが入っていないとき）。
/// Package Manager で「Vivox」（com.unity.services.vivox）を入れると、自動で本物（上の実装）に切り替わる
/// （Editor の VivoxDefineSetter が JD_VIVOX を付ける）。
/// </summary>
public class ProximityVoice : MonoBehaviour
{
    public static bool MicMuted => false;
    public static bool IsActive => false;
    public static void ApplyOutputVolume() { }
    public static void ApplyMicState() { }
}
#endif
