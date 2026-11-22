using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 設定（全体音量・画面の明るさ）を実際のゲームに反映する常駐オブジェクト。
/// ・起動時とシーン切り替え時に、保存済みの設定を自動で適用する（タイトルを通らずにゲームシーンから始めても反映される）
/// ・明るさは URP のグローバル Volume（ColorAdjustments の Post Exposure）で調整する。0.5 が標準で、0 → -2EV、1 → +2EV
/// </summary>
public class GameSettingsRuntime : MonoBehaviour
{
    /// <summary>明るさスライダーの両端での露出補正（EV）</summary>
    private const float MaxExposureEV = 2f;

    private static GameSettingsRuntime instance;

    private ColorAdjustments colorAdjustments;
    private Camera lastCamera;
    private float nextCameraCheckTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureInstance();
        ApplyAll();
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        GameObject go = new GameObject("GameSettingsRuntime");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameSettingsRuntime>();
        instance.CreateBrightnessVolume();    }

    /// <summary>保存済みの設定をすべて適用する</summary>
    public static void ApplyAll()
    {
        ApplyMasterVolume(SettingsManager.MasterVolume);
        ApplyBrightness(SettingsManager.Brightness);
    }

    public static void ApplyMasterVolume(float value)
    {
        AudioListener.volume = Mathf.Clamp01(value);
    }

    public static void ApplyBrightness(float value)
    {
        EnsureInstance();
        if (instance.colorAdjustments == null) return;
        float ev = (Mathf.Clamp01(value) - 0.5f) * 2f * MaxExposureEV;
        instance.colorAdjustments.postExposure.Override(ev);
    }

    private void CreateBrightnessVolume()
    {
        Volume volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f; // 他の Volume より優先して適用する
        volume.weight = 1f;
        colorAdjustments = volume.profile.Add<ColorAdjustments>(true);
        colorAdjustments.postExposure.overrideState = true;
    }

    private void Update()
    {
        // カメラはゲーム中に生成されることがあるので、新しいカメラが現れたらポストプロセスを有効にする
        if (Time.unscaledTime < nextCameraCheckTime) return;
        nextCameraCheckTime = Time.unscaledTime + 0.5f;

        Camera cam = Camera.main;
        if (cam == null || cam == lastCamera) return;
        lastCamera = cam;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = true;    }
}
