using System.Collections;
using UnityEngine;

public class SlotReelScroller : MonoBehaviour
{
    private Renderer reelRenderer;
    private float scrollSpeed = 5f;
    private bool isRolling = false;

    private void Awake()
    {
        reelRenderer = GetComponent<Renderer>();
    }

    public void StartRolling(float speed)
    {
        scrollSpeed = speed;
        isRolling = true;
    }

    public IEnumerator StopAt(float targetOffset, float stopDuration)
    {
        // 親モデルの傾きに合わせて、スクロール軸を横方向（X軸）に固定
        float startOffset = reelRenderer.material.mainTextureOffset.x;
        targetOffset = Mathf.Repeat(targetOffset, 1f);

        float elapsed = 0f;

        while (elapsed < stopDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / stopDuration;
            float easeT = Mathf.Sin(t * Mathf.PI * 0.5f);

            // X軸方向へ滑らかにスクロールさせ、徐々に減速して停止位置へ向かう
            float currentX = Mathf.Lerp(startOffset, startOffset + 5f + targetOffset, easeT);
            reelRenderer.material.mainTextureOffset = new Vector2(currentX, 0f);

            yield return null;
        }

        // 最終位置にピタッと合わせる
        reelRenderer.material.mainTextureOffset = new Vector2(targetOffset, 0f);
        isRolling = false;
    }
}