using System.Collections;
using UnityEngine;

public class SlotReelScroller : MonoBehaviour
{
    private Renderer reelRenderer;
    private float scrollSpeed = 10f;
    private bool isRolling = false;
    private bool isStopping = false;

    private void Awake()
    {
        reelRenderer = GetComponent<Renderer>();
    }

    private void Update()
    {
        if (isRolling && !isStopping)
        {
            Vector2 offset = reelRenderer.material.mainTextureOffset;
            offset.x += scrollSpeed * Time.deltaTime;
            reelRenderer.material.mainTextureOffset = new Vector2(offset.x, 0f);
        }
    }

    public void SetReelTexture(Texture2D tex, float tilingX)
    {
        if (reelRenderer == null) reelRenderer = GetComponent<Renderer>();
        Material mat = reelRenderer.material;

        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
        }
        else
        {
            mat.mainTexture = tex;
        }

        mat.mainTextureScale = new Vector2(tilingX, 1f);
    }

    public void StartRolling(float speed)
    {
        scrollSpeed = speed;
        isRolling = true;
        isStopping = false;
    }

    public IEnumerator StopAt(float targetOffset, float stopDuration)
    {
        isStopping = true;

        float currentX = reelRenderer.material.mainTextureOffset.x;
        targetOffset = Mathf.Repeat(targetOffset, 1f);

        // 現在地から数周（3周）回ったあと、正確に targetOffset の位置にピタッと着地させる
        int extraRounds = 3;
        float baseRound = Mathf.Floor(currentX);
        float finalTargetX = baseRound + extraRounds + targetOffset;

        if (finalTargetX < currentX)
        {
            finalTargetX += 1f;
        }

        float elapsed = 0f;

        while (elapsed < stopDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stopDuration);
            float easeT = Mathf.Sin(t * Mathf.PI * 0.5f); // 綺麗な減速イージング

            float lerpX = Mathf.Lerp(currentX, finalTargetX, easeT);
            reelRenderer.material.mainTextureOffset = new Vector2(lerpX, 0f);

            yield return null;
        }

        // 最終位置を完璧に固定
        reelRenderer.material.mainTextureOffset = new Vector2(targetOffset, 0f);
        isRolling = false;
        isStopping = false;
    }
}