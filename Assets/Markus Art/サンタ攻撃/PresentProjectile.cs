using System.Collections;
using UnityEngine;

public class PresentProjectile : MonoBehaviour
{
    [Header("ダメージの設定")]
    public int directHitDamage = 30;       // 地面に落ちる前にPlayerへ直撃した時のダメージ
    public int explosionDamage = 15;       // 地面に落ちて転がった後の爆破ダメージ

    [Header("爆発タイミングの設定")]
    public float fuseTime = 2.0f;          // 地面に着いてから爆発するまでの秒数
    public float maxLifeTime = 8.0f;       // どこにも当たらなかった時の最大寿命（保険）

    [Header("爆発・演出の設定")]
    public float explosionRadius = 3.0f;   // 爆風の届く範囲
    public float explosionForce = 500f;    // 周囲を吹き飛ばす力
    public GameObject explosionPrefab;     // 任意の爆発エフェクト（空欄でも自動演出が出ます）

    private Rigidbody rb;
    private bool hasLanded = false;
    private bool hasExploded = false;
    private bool isDirectHit = false;
    private GameObject directHitPlayerObj;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.angularVelocity = new Vector3(
                Random.Range(6f, 10f),
                Random.Range(-4f, 4f),
                Random.Range(-4f, 4f)
            );
        }

        Invoke(nameof(Explode), maxLifeTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;

        // 1. Playerに当たった時
        if (IsPlayer(collision.gameObject))
        {
            if (!hasLanded)
            {
                isDirectHit = true;
                directHitPlayerObj = collision.gameObject;
            }

            Explode();
            return;
        }

        // 2. 地面や壁に初めて当たったら、そこから2秒後に爆発するタイマーを開始
        if (!hasLanded)
        {
            hasLanded = true;
            Invoke(nameof(Explode), fuseTime);
        }
    }

    bool IsPlayer(GameObject obj)
    {
        if (obj.CompareTag("Player")) return true;
        if (obj.transform.root != null && obj.transform.root.CompareTag("Player")) return true;
        return false;
    }

    void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;
        CancelInvoke();

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            CreateSimpleExplosionEffect();
        }

        bool damagedPlayer = false;

        if (isDirectHit && directHitPlayerObj != null)
        {
            damagedPlayer = true;
            ApplyDamageToPlayer(directHitPlayerObj, directHitDamage, "直撃");
        }

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, explosionRadius);

        foreach (Collider hit in hitColliders)
        {
            if (!damagedPlayer && IsPlayer(hit.gameObject))
            {
                damagedPlayer = true;
                ApplyDamageToPlayer(hit.gameObject, explosionDamage, "転がり爆破");
            }

            Rigidbody targetRb = hit.attachedRigidbody;
            if (targetRb != null && targetRb != rb)
            {
                targetRb.AddExplosionForce(explosionForce, transform.position, explosionRadius);
            }
        }

        Destroy(gameObject);
    }

    void ApplyDamageToPlayer(GameObject hitObj, int damageAmount, string hitType)
    {
        GameObject playerRoot = (hitObj.transform.root != null && hitObj.transform.root.CompareTag("Player"))
            ? hitObj.transform.root.gameObject
            : hitObj;

        Debug.Log($"プレゼント[{hitType}]がPlayerに命中！ ダメージ: {damageAmount}");
        playerRoot.SendMessage("TakeDamage", damageAmount, SendMessageOptions.DontRequireReceiver);
    }

    void CreateSimpleExplosionEffect()
    {
        GameObject fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fx.name = "PresentExplosionFX";
        fx.transform.position = transform.position;
        fx.transform.localScale = Vector3.one * 0.5f;

        Destroy(fx.GetComponent<Collider>());

        Renderer rend = fx.GetComponent<Renderer>();
        if (rend != null)
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            Material mat = new Material(urpShader != null ? urpShader : Shader.Find("Standard"));
            mat.color = new Color(1f, 0.35f, 0.05f, 1f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.05f) * 3f);
            rend.material = mat;
        }

        fx.AddComponent<SimpleExplosionExpand>().targetScale = explosionRadius * 1.4f;
    }
}

public class SimpleExplosionExpand : MonoBehaviour
{
    public float targetScale = 4f;
    private float elapsed = 0f;
    private float duration = 0.28f;

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        transform.localScale = Vector3.Lerp(Vector3.one * 0.5f, Vector3.one * targetScale, t);

        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}