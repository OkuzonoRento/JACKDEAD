using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUIController : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerMovement playerMovement; // スタミナ参照用

    [Header("UI スライダー")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Slider sanitySlider;
    [SerializeField] private Slider dopamineSlider;
    [SerializeField] private Slider staminaSlider;

    [Header("所持金UI")]
    [SerializeField] private TextMeshProUGUI moneyText;
    [Tooltip("この金額以上になったら『K』単位（例: $ 1.5 K）に省略表示する")]
    [SerializeField] private int kUnitThreshold = 1000;

    [Header("所持金アニメーション設定")]
    [Tooltip("金額が変化しきるまでの時間（秒）")]
    [SerializeField] private float moneyAnimDuration = 0.6f;
    [Tooltip("金額変化時にテキストが弾む最大スケール倍率")]
    [SerializeField] private float moneyPunchScale = 1.3f;
    [Tooltip("お金が増えたときの一時的な文字色")]
    [SerializeField] private Color moneyGainColor = new Color(0.3f, 1f, 0.4f);
    [Tooltip("お金が減ったときの一時的な文字色")]
    [SerializeField] private Color moneyLossColor = new Color(1f, 0.35f, 0.35f);

    [System.Serializable]
    public struct InventorySlotUI
    {
        public RectTransform slotRectTransform; // スロット自体のRectTransform（サイズ変更用）
        public Image slotBackgroundImage;       // スロット自体の背景（選択中でSpriteを変更）
        public Image itemIconImage;             // アイテムのアイコンを表示するImage
    }

    [Header("インベントリ スロットUI (3つ分)")]
    [SerializeField] private InventorySlotUI[] inventorySlots = new InventorySlotUI[3];

    [Header("スロット選択時の見た目設定")]
    [SerializeField] private Sprite normalSlotSprite;     // 通常時のスロット画像
    [SerializeField] private Sprite selectedSlotSprite;   // 選択中のスロット画像
    [SerializeField] private float normalScale = 1.0f;    // 通常時のサイズ倍率
    [SerializeField] private float selectedScale = 1.2f;  // 選択中のサイズ倍率
    [SerializeField] private float scaleLerpSpeed = 15f;  // サイズ変更のスムーズさ

    private int targetMoney = 0;
    private float currentDisplayedMoney = 0f;
    private bool isMoneyInitialized = false;

    private Coroutine moneyAnimCoroutine;
    private Color defaultMoneyColor = Color.white;
    private Vector3 defaultMoneyScale = Vector3.one;

    private void Awake()
    {
        if (moneyText != null)
        {
            defaultMoneyColor = moneyText.color;
            defaultMoneyScale = moneyText.rectTransform.localScale;
        }
    }

    private void Update()
    {
        if (playerController != null)
        {
            // --- 1. ステータスUIの更新 ---
            if (healthSlider != null)
            {
                healthSlider.value = playerController.Health / 100f;
            }
            if (sanitySlider != null)
            {
                sanitySlider.value = playerController.Sanity / 100f;
            }
            if (dopamineSlider != null)
            {
                dopamineSlider.value = playerController.Dopamine / 100f;
            }

            // --- 2. 所持金UIの更新 ---
            UpdateMoneyUI();

            // --- 3. インベントリスロットUIの更新 ---
            UpdateInventoryUI();
        }

        // スタミナの更新 (PlayerMovementから取得)
        if (staminaSlider != null && playerMovement != null)
        {
            staminaSlider.value = playerMovement.CurrentStamina / 100f;
        }
    }

    private void UpdateMoneyUI()
    {
        if (moneyText == null) return;

        int actualMoney = playerController.PlayerMoney;

        // 初回フレームはアニメーションなしで即座に反映
        if (!isMoneyInitialized)
        {
            isMoneyInitialized = true;
            targetMoney = actualMoney;
            currentDisplayedMoney = actualMoney;
            moneyText.text = FormatMoney(actualMoney);
            return;
        }

        // 所持金が変動した瞬間にアニメーションを開始
        if (actualMoney != targetMoney)
        {
            bool isGain = actualMoney > targetMoney;
            targetMoney = actualMoney;

            if (moneyAnimCoroutine != null)
            {
                StopCoroutine(moneyAnimCoroutine);
            }
            moneyAnimCoroutine = StartCoroutine(AnimateMoneyChange(isGain));
        }
    }

    private IEnumerator AnimateMoneyChange(bool isGain)
    {
        float startMoney = currentDisplayedMoney;
        float elapsed = 0f;

        Color flashColor = isGain ? moneyGainColor : moneyLossColor;
        RectTransform textRect = moneyText.rectTransform;

        while (elapsed < moneyAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moneyAnimDuration);

            // イージング（最初は速く、目標値に近づくほどゆっくり止まる）
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            // 1. 数値のカウントアップ／ダウン
            currentDisplayedMoney = Mathf.Lerp(startMoney, targetMoney, easeOut);
            moneyText.text = FormatMoney(Mathf.RoundToInt(currentDisplayedMoney));

            // 2. スケールのバウンス演出（前半で膨らみ、後半で元のサイズに戻る）
            float bounce = Mathf.Sin(t * Mathf.PI);
            float scaleMultiplier = Mathf.Lerp(1f, moneyPunchScale, bounce);
            textRect.localScale = defaultMoneyScale * scaleMultiplier;

            // 3. 文字色のフラッシュ（増減色から元の色へ徐々に戻す）
            moneyText.color = Color.Lerp(flashColor, defaultMoneyColor, t);

            yield return null;
        }

        // 最終値をピタッと確定
        currentDisplayedMoney = targetMoney;
        moneyText.text = FormatMoney(targetMoney);
        textRect.localScale = defaultMoneyScale;
        moneyText.color = defaultMoneyColor;
        moneyAnimCoroutine = null;
    }

    private string FormatMoney(int amount)
    {
        int absAmount = Mathf.Abs(amount);
        string sign = amount < 0 ? "-" : "";

        // 100万以上は M 単位 (例: $ 1.2 M)
        if (absAmount >= 1000000)
        {
            float mValue = Mathf.Floor(absAmount / 100000f) / 10f;
            return $"{sign}$ {mValue:0.#} M";
        }

        // 指定しきい値（例: 1000 または 10000）以上は K 単位 (例: $ 1.5 K)
        if (absAmount >= kUnitThreshold && kUnitThreshold >= 1000)
        {
            float kValue = Mathf.Floor(absAmount / 100f) / 10f;
            return $"{sign}$ {kValue:0.#} K";
        }

        // それ未満はそのまま表示 (例: $ 500)
        return $"{sign}$ {absAmount}";
    }

    private void UpdateInventoryUI()
    {
        int selectedIndex = playerController.SelectedSlotIndex;

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            bool isSelected = (i == selectedIndex);

            // A. スロットの選択状態に応じた背景Spriteの切り替え
            if (inventorySlots[i].slotBackgroundImage != null)
            {
                if (isSelected)
                {
                    if (selectedSlotSprite != null)
                        inventorySlots[i].slotBackgroundImage.sprite = selectedSlotSprite;
                }
                else
                {
                    if (normalSlotSprite != null)
                        inventorySlots[i].slotBackgroundImage.sprite = normalSlotSprite;
                }
            }

            // B. 選択中スロットのサイズ（スケール）を滑らかに変更
            if (inventorySlots[i].slotRectTransform != null)
            {
                float targetScale = isSelected ? selectedScale : normalScale;
                Vector3 targetV3 = new Vector3(targetScale, targetScale, 1f);
                inventorySlots[i].slotRectTransform.localScale = Vector3.Lerp(
                    inventorySlots[i].slotRectTransform.localScale,
                    targetV3,
                    Time.deltaTime * scaleLerpSpeed
                );
            }

            // C. インベントリ内のアイテムに応じたアイコンの切り替え
            if (inventorySlots[i].itemIconImage != null)
            {
                ItemDataAsset itemData = playerController.Inventory[i];
                if (itemData != null && itemData.ItemIcon != null)
                {
                    inventorySlots[i].itemIconImage.sprite = itemData.ItemIcon;
                    inventorySlots[i].itemIconImage.gameObject.SetActive(true);
                }
                else
                {
                    inventorySlots[i].itemIconImage.sprite = null;
                    inventorySlots[i].itemIconImage.gameObject.SetActive(false);
                }
            }
        }
    }
}