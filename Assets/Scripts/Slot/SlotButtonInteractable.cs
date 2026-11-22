using System.Collections;
using UnityEngine;
using TMPro;

public class SlotButtonInteractable : MonoBehaviour, IInteractable
{
    [Header("カジノマシン本体の参照")]
    [SerializeField] private SlotMachineController slotMachine;

    [Header("ベット金額・長押し時間設定")]
    [SerializeField] private int customBetAmount = 1000;
    [Tooltip("ボタンを長押ししてゲージが満タンになるまでの時間（秒）")]
    [SerializeField, Min(0.05f)] private float holdTime = 0.5f;

    [Header("ボタン上のテキスト表示")]
    [SerializeField] private TextMeshPro buttonText;
    [SerializeField] private Color availableTextColor = Color.white;
    [SerializeField] private Color unavailableTextColor = Color.gray;

    [Header("ボタン押し込み演出")]
    [Tooltip("押したときにボタンが沈み込むローカル移動量")]
    [SerializeField] private Vector3 pressLocalOffset = new Vector3(0f, -0.02f, 0f);
    [SerializeField] private float pressDuration = 0.08f;

    private Vector3 initialLocalPos;
    private bool isAnimatingPress = false;
    private MapGenerator mapGenerator;

    /// <summary>
    /// customBetAmount（レベル1基準）に現在のレベルの価格補正を掛けた実際のベット額
    /// </summary>
    private int CurrentBetAmount
    {
        get
        {
            if (mapGenerator == null) mapGenerator = FindAnyObjectByType<MapGenerator>();
            return mapGenerator != null ? mapGenerator.ScaleToCurrentPrice(customBetAmount) : customBetAmount;
        }
    }

    private PlayerController player;

    /// <summary>実際に払う掛け金。正気度が低いと上がる（もらえる配当は、上がる前の額が基準のまま）</summary>
    private int CurrentCost
    {
        get
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            float m = player != null ? player.BetPriceMultiplier : 1f;
            return Mathf.RoundToInt(CurrentBetAmount * m);
        }
    }

    private void Awake()
    {
        initialLocalPos = transform.localPosition;

        if (slotMachine == null)
        {
            slotMachine = GetComponentInParent<SlotMachineController>();
        }
    }

    private void Start()
    {
        UpdateTextDisplay();
    }

    private void Update()
    {
        UpdateTextDisplay();
    }

    private void UpdateTextDisplay()
    {
        if (buttonText != null)
        {
            buttonText.text = $"${CurrentCost:N0}";

            if (slotMachine != null)
            {
                bool canPlay = CanInteract();
                buttonText.color = canPlay ? availableTextColor : unavailableTextColor;
            }
        }
    }

    public string GetInteractPrompt() => "";

    public float GetHoldTime() => holdTime;

    public bool CanInteract()
    {
        if (slotMachine == null) return false;
        return !slotMachine.IsPlaying && slotMachine.HasEnoughMoney(CurrentCost);
    }

    public void OnInteract()
    {
        if (slotMachine != null && CanInteract())
        {
            if (!isAnimatingPress)
            {
                StartCoroutine(AnimateButtonPress());
            }
            slotMachine.OnPressBetButton(CurrentCost, CurrentBetAmount);
        }
    }

    private IEnumerator AnimateButtonPress()
    {
        isAnimatingPress = true;
        Vector3 targetPos = initialLocalPos + pressLocalOffset;

        float t = 0f;
        while (t < pressDuration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(initialLocalPos, targetPos, t / pressDuration);
            yield return null;
        }

        t = 0f;
        while (t < pressDuration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(targetPos, initialLocalPos, t / pressDuration);
            yield return null;
        }

        transform.localPosition = initialLocalPos;
        isAnimatingPress = false;
    }
}