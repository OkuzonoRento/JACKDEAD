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
        // プレハブアセット（シーン外）では Renderer.material を使えないので何もしない
        if (!gameObject.scene.IsValid()) return;
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

    private string tintProperty;
    private Color baseTint;

    /// <summary>当たりの演出でリールを光らせる。amount が 0 なら元の色</summary>
    public void SetFlash(Color color, float amount)
    {
        if (amount <= 0f)
        {
            ResetTint();
            return;
        }
        if (!PrepareTint()) return;

        Color bright = baseTint * Color.Lerp(Color.white, color, 0.7f) * 2.2f;
        bright.a = baseTint.a;
        reelMaterial.SetColor(tintProperty, Color.Lerp(baseTint, bright, Mathf.Clamp01(amount)));
    }

    /// <summary>元の色に戻す</summary>
    public void ResetTint()
    {
        if (reelMaterial != null && !string.IsNullOrEmpty(tintProperty)) reelMaterial.SetColor(tintProperty, baseTint);
    }

    private bool PrepareTint()
    {
        InitMaterial();
        if (reelMaterial == null) return false;

        if (tintProperty == null)
        {
            tintProperty = reelMaterial.HasProperty("_BaseColor") ? "_BaseColor"
                : reelMaterial.HasProperty("_Color") ? "_Color" : "";
            if (tintProperty.Length > 0) baseTint = reelMaterial.GetColor(tintProperty);
        }
        return tintProperty.Length > 0;
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

    /// <summary>回転を即座に止めて、指定の位置に置く（演出の中断用）</summary>
    public void Halt(float offsetY)
    {
        isRolling = false;
        isStopping = false;
        SetOffset(Mathf.Repeat(offsetY, 1f));
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