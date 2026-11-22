using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Title3DSystem : MonoBehaviour
{
    [Header("シーン遷移設定")]
    public string singlePlaySceneName = "SampleScene";
    public string multiLobbySceneName = "LobbyScene";

    [Header("UIパネル")]
    public GameObject mainMenuPanel;
    public Image fadeOverlay;

    [Header("3Dカメラ演出")]
    public Transform cameraTransform;
    public float swayAmount = 0.08f;
    public float swaySpeed = 1.0f;
    public float zoomOnPlaySpeed = 2.0f;

    [Header("サウンド（任意）")]
    public AudioSource uiAudioSource;
    public AudioClip hoverSound;
    public AudioClip clickSound;

    private Vector3 initialCamPos;
    private bool isStartingGame = false;
    private bool selectedMultiplayer = false;

    void Start()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Time.timeScale = 1f;

        if (cameraTransform != null)
            initialCamPos = cameraTransform.localPosition;

        // FadeOverlay が画面のクリックを塞がないようにする
        if (fadeOverlay != null)
        {
            fadeOverlay.raycastTarget = false;
            StartCoroutine(FadeScreen(1f, 0f, 1.0f));
        }

        if (mainMenuPanel == null) mainMenuPanel = GameObject.Find("MainMenuPanel");
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    void Update()
    {
        if (cameraTransform == null) return;

        if (!isStartingGame)
        {
            float offsetX = (Mathf.PerlinNoise(Time.time * swaySpeed, 0f) - 0.5f) * swayAmount;
            float offsetY = (Mathf.PerlinNoise(0f, Time.time * swaySpeed) - 0.5f) * swayAmount;
            cameraTransform.localPosition = initialCamPos + new Vector3(offsetX, offsetY, 0f);
        }
        else
        {
            cameraTransform.localPosition += cameraTransform.forward * zoomOnPlaySpeed * Time.deltaTime;
        }
    }

    /// <summary>タイトルの「SINGLE PLAY」：ソロのセーブデータ選択を出す</summary>
    public void OnClickSingleMode()
    {
        PlayClickSound();
        selectedMultiplayer = false;

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (MultiLobbyUI.OpenSolo(ShowMainMenu) == null) ShowMainMenu();
    }

    public void OnClickMultiMode() { OpenMulti(MultiLobbyUI.Step.Menu); }

    /// <summary>タイトルの「HOST GAME」：ルームを作る（セーブ選択から）</summary>
    public void OnClickMultiHost() { OpenMulti(MultiLobbyUI.Step.SelectSave); }

    /// <summary>タイトルの「JOIN GAME」：招待コードを入れて参加する</summary>
    public void OnClickMultiJoin() { OpenMulti(MultiLobbyUI.Step.JoinCode); }

    private void OpenMulti(MultiLobbyUI.Step firstStep)
    {
        PlayClickSound();
        selectedMultiplayer = true;

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (MultiLobbyUI.Open(firstStep, ShowMainMenu) == null) ShowMainMenu();
    }

    /// <summary>シーンのボタンに登録済みの名前（OnClickOpenSettings と同じ）</summary>
    public void OnClickSettings() { OnClickOpenSettings(); }

    /// <summary>タイトルの「SETTING」：設定画面を出す</summary>
    public void OnClickOpenSettings()
    {
        PlayClickSound();

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (MultiLobbyUI.OpenSettings(ShowMainMenu) == null) ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    public void OnClickSelectSlot(int slotNumber)
    {
        if (isStartingGame) return;
        isStartingGame = true;
        PlayClickSound();

        SaveSystem.SelectAndLoadSlot(selectedMultiplayer, slotNumber);
        SaveSystem.CommitNewSlot(); // 新規のセーブデータは、ゲーム開始のこのとき初めて作る

        string targetScene = selectedMultiplayer ? multiLobbySceneName : singlePlaySceneName;
        StartCoroutine(TransitionToScene(targetScene));
    }

    public void OnClickQuit()
    {
        PlayClickSound();
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void PlayHoverSound()
    {
        if (uiAudioSource != null && hoverSound != null)
            uiAudioSource.PlayOneShot(hoverSound, SettingsManager.SeVolume);
    }

    private void PlayClickSound()
    {
        if (uiAudioSource != null && clickSound != null)
            uiAudioSource.PlayOneShot(clickSound, SettingsManager.SeVolume);
    }

    private IEnumerator TransitionToScene(string sceneName)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        if (fadeOverlay != null)
            yield return StartCoroutine(FadeScreen(0f, 1f, 1.2f));
        else
            yield return new WaitForSeconds(0.5f);

        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator FadeScreen(float startAlpha, float endAlpha, float duration)
    {
        float elapsed = 0f;
        Color c = fadeOverlay.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            fadeOverlay.color = c;
            yield return null;
        }

        c.a = endAlpha;
        fadeOverlay.color = c;
    }
}
