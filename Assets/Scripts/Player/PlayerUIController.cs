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

    private void Update()
    {
        if (playerController != null)
        {
            // 体力スライダーの更新 (上限を100として正規化)
            if (healthSlider != null)
            {
                healthSlider.value = playerController.Health / 100f;
            }

            // 正気度スライダーの更新
            if (sanitySlider != null)
            {
                sanitySlider.value = playerController.Sanity / 100f;
            }

            // ドーパミンスライダーの更新
            if (dopamineSlider != null)
            {
                dopamineSlider.value = playerController.Dopamine / 100f;
            }
        }

        // スタミナの更新 (PlayerMovementから取得)
        if (staminaSlider != null && playerMovement != null)
        {
            staminaSlider.value = playerMovement.CurrentStamina / 100f;
        }
    }
}