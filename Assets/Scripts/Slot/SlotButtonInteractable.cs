using System.Collections;
using UnityEngine;
using TMPro;

public class SlotButtonInteractable : MonoBehaviour, IInteractable
{
    [Header("カジノマシン本体の参照")]
    [SerializeField] private SlotMachineController slotMachine;

    [Header("このボタンのベット金額")]
    [SerializeField] private int customBetAmount = 1000;

    [Header("ボタン上のテキスト表示")]
    [SerializeField] private TextMeshPro buttonText;

    [Header("ボタン押し込み演出")]
    [Tooltip("押したときにボタンが沈み込むローカル移動量")]
    [SerializeField] private Vector3 pressLocalOffset = new Vector3(0f, -0.02f, 0f);
    [SerializeField] private float pressDuration = 0.08f;

    private Vector3 initialLocalPos;
    private bool isAnimatingPress = false;

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
            buttonText.text = $"${customBetAmount}";

            if (slotMachine != null)
            {
                bool canPlay = !slotMachine.IsPlaying && slotMachine.HasEnoughMoney(customBetAmount);
                buttonText.color = canPlay ? Color.white : Color.gray;
            }
        }
    }

    public string GetInteractPrompt() => "";

    public float GetHoldTime() => 0.1f;

    public bool CanInteract()
    {
        if (slotMachine == null) return false;
        return !slotMachine.IsPlaying && slotMachine.HasEnoughMoney(customBetAmount);
    }

    public void OnInteract()
    {
        if (slotMachine != null && CanInteract())
        {
            if (!isAnimatingPress)
            {
                StartCoroutine(AnimateButtonPress());
            }
            slotMachine.OnPressBetButton(customBetAmount);
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