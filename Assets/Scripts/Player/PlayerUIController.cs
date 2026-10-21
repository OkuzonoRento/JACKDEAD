using UnityEngine;
using UnityEngine.UI;

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

    private void Update()
    {
        // --- 1. ステータスUIの更新 ---
        if (playerController != null)
        {
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

            // --- 2. インベントリスロットUIの更新 ---
            UpdateInventoryUI();
        }

        // スタミナの更新 (PlayerMovementから取得)
        if (staminaSlider != null && playerMovement != null)
        {
            staminaSlider.value = playerMovement.CurrentStamina / 100f;
        }
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