using UnityEngine;
using TMPro;

public class SlotButtonInteractable : MonoBehaviour, IInteractable
{
    [Header("カジノマシン本体の参照")]
    [SerializeField] private SlotMachineController slotMachine;

    [Header("このボタンのベット金額")]
    [SerializeField] private int customBetAmount = 1000;

    [Header("ボタン上のテキスト表示")]
    [SerializeField] private TextMeshPro buttonText; // または TextMeshPro (3D) の場合は TextMeshPro に変更

    private void Awake()
    {
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
        // 所持金の増減や状態変化に応じてテキストの色や内容をリアルタイムに更新（オプション）
        UpdateTextDisplay();
    }

    private void UpdateTextDisplay()
    {
        if (buttonText != null)
        {
            buttonText.text = $"${customBetAmount}";

            // 所持金が足りない場合はテキストを灰色にするなどして「押せない状態」を視覚的に伝える
            if (slotMachine != null)
            {
                bool canPlay = !slotMachine.IsPlaying && slotMachine.HasEnoughMoney(customBetAmount);
                buttonText.color = canPlay ? Color.white : Color.gray;
            }
        }
    }

    // --- IInteractable の実装 ---
    public string GetInteractPrompt()
    {
        // 画面中央のUIプロンプトは出さない（空文字を返す、またはPlayerInteraction側で非表示にする）
        return "";
    }

    public float GetHoldTime()
    {
        return 0.1f; // すぐに反応
    }

    public bool CanInteract()
    {
        if (slotMachine == null) return false;
        return !slotMachine.IsPlaying && slotMachine.HasEnoughMoney(customBetAmount);
    }

    public void OnInteract()
    {
        if (slotMachine != null)
        {
            slotMachine.OnPressBetButton(customBetAmount);
        }
    }
}