using System.Collections;
using UnityEngine;

public class SlotReelScroller : MonoBehaviour
{
    private Renderer reelRenderer;
    private Material reelMaterial;
    private float scrollSpeed = 3.5f;
    private bool isRolling = false;
    private bool isStopping = false;

    private void Awake()
    {
        InitMaterial();
    }

    private void InitMaterial()
    {
        if (reelMaterial != null) return;
        reelRenderer = GetComponent<Renderer>();
        if (reelRenderer != null)
        {
            reelMaterial = reelRenderer.material;
        }
    }

    private void Update()
    {
        if (isRolling && !isStopping && reelMaterial != null)
        {
            Vector2 offset = reelMaterial.mainTextureOffset;
            offset.y = Mathf.Repeat(offset.y + scrollSpeed * Time.deltaTime, 1f);
            reelMaterial.mainTextureOffset = new Vector2(0f, offset.y);
        }
    }

    public void SetReelTexture(Texture2D tex, float tilingY)
    {
        InitMaterial();
        if (reelMaterial == null) return;

        if (reelMaterial.HasProperty("_BaseMap"))
        {
            reelMaterial.SetTexture("_BaseMap", tex);
        }
        else
        {
            reelMaterial.mainTexture = tex;
        }

        reelMaterial.mainTextureScale = new Vector2(1f, tilingY);
    }

    public void SetOffset(float offsetY)
    {
        InitMaterial();
        if (reelMaterial != null)
        {
            reelMaterial.mainTextureOffset = new Vector2(0f, offsetY);
        }
    }

    public void StartRolling(float speed)
    {
        scrollSpeed = speed;
        isRolling = true;
        isStopping = false;
    }

    public IEnumerator StopAt(float targetOffsetY, float stopDuration)
    {
        InitMaterial();
        isStopping = true;

        float currentY = reelMaterial.mainTextureOffset.y;
        targetOffsetY = Mathf.Repeat(targetOffsetY, 1f);

        int extraRounds = 2;
        float finalTargetY = Mathf.Floor(currentY) + extraRounds + targetOffsetY;
        if (finalTargetY < currentY)
        {
            finalTargetY += 1f;
        }

        float elapsed = 0f;
        while (elapsed < stopDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stopDuration);
            float easeT = Mathf.Sin(t * Mathf.PI * 0.5f);

            float lerpY = Mathf.Lerp(currentY, finalTargetY, easeT);
            reelMaterial.mainTextureOffset = new Vector2(0f, lerpY);
            yield return null;
        }

        reelMaterial.mainTextureOffset = new Vector2(0f, targetOffsetY);
        isRolling = false;
        isStopping = false;
    }
}