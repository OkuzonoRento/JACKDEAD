using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class Title3DSystem : MonoBehaviour
{
    [Header("シーン遷移設定")]
    public string singlePlaySceneName = "SampleScene";
    public string multiLobbySceneName = "LobbyScene";

    [Header("UIパネル")]
    public GameObject mainMenuPanel;
    public GameObject saveSlotPanel;
    public GameObject settingsPanel;
    public Image fadeOverlay;

    [Header("セーブスロットUI（3枠分）")]
    public TextMeshProUGUI modeHeaderText;
    public TextMeshProUGUI[] slotInfoTexts;
    public GameObject[] deleteSlotButtons;

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

        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (fadeOverlay != null)
            StartCoroutine(FadeScreen(1f, 0f, 1.0f));
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

    public void OnClickSingleMode()
    {
        PlayClickSound();
        selectedMultiplayer = false;
        OpenSaveSlotPanel();
    }

    public void OnClickMultiMode()
    {
        PlayClickSound();
        selectedMultiplayer = true;

        // マルチ：まず「ルームを作る／参加する」を選ぶ。セーブ選択はルームを作る場合のみ（MultiLobbyUI 内で行う）
        if (NetworkSession.Instance != null)
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            MultiLobbyUI.Open(() =>
            {
                if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
            });
            return;
        }
        OpenSaveSlotPanel();
    }

    private void OpenSaveSlotPanel()
    {
        mainMenuPanel.SetActive(false);
        saveSlotPanel.SetActive(true);

        if (modeHeaderText != null)
        {
            modeHeaderText.text = selectedMultiplayer ? "MULTIPLAYER // SELECT SAVE DATA" : "SINGLE PLAYER // SELECT SAVE DATA";
        }

        RefreshSlotUI();
    }

    public void RefreshSlotUI()
    {
        for (int i = 0; i < 3; i++)
        {
            int slotNum = i + 1;
            GameSaveData data = SaveSystem.PeekSaveData(slotNum);

            if (slotInfoTexts != null && i < slotInfoTexts.Length && slotInfoTexts[i] != null)
            {
                if (data != null && data.hasSaveData)
                {
                    slotInfoTexts[i].text = $"SLOT {slotNum}  -  DAY {data.currentDay}  |  MONEY: ${data.currentMoney:N0}";
                }
                else
                {
                    slotInfoTexts[i].text = $"SLOT {slotNum}  -  [ NEW GAME ]";
                }
            }

            if (deleteSlotButtons != null && i < deleteSlotButtons.Length && deleteSlotButtons[i] != null)
            {
                deleteSlotButtons[i].SetActive(data != null && data.hasSaveData);
            }
        }
    }

    public void OnClickSelectSlot(int slotNumber)
    {
        if (isStartingGame) return;
        isStartingGame = true;
        PlayClickSound();

        SaveSystem.SelectAndLoadSlot(selectedMultiplayer, slotNumber);

        string targetScene = selectedMultiplayer ? multiLobbySceneName : singlePlaySceneName;
        StartCoroutine(TransitionToScene(targetScene));
    }

    public void OnClickDeleteSlot(int slotNumber)
    {
        PlayClickSound();
        SaveSystem.DeleteSlot(slotNumber);
        RefreshSlotUI();
    }

    public void OnClickBackFromSlots()
    {
        PlayClickSound();
        saveSlotPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    public void OnClickSettings()
    {
        PlayClickSound();
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void OnClickBackFromSettings()
    {
        PlayClickSound();
        settingsPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
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
            uiAudioSource.PlayOneShot(hoverSound);
    }

    private void PlayClickSound()
    {
        if (uiAudioSource != null && clickSound != null)
            uiAudioSource.PlayOneShot(clickSound);
    }

    private IEnumerator TransitionToScene(string sceneName)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);

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