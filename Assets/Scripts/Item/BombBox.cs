using UnityEngine;

/// <summary>
/// 時限式爆弾の Box。
/// ・敵に投げられる／プレイヤーが拾って投げられる（Item として拾える）。物理演算で跳ねる
/// ・動いている間にローカルプレイヤーへ直撃すると少量のダメージ（Box は消えない）
/// ・導火線が尽きると爆発し、範囲内のローカルプレイヤーへダメージ（距離で減衰）
/// ・インベントリに入れて持っている間も導火線は進む（PlayerController 側で管理）。残り時間は拾う／落とすときに引き継ぐ
/// マルチプレイでは各PCが同じ Box を同じタイミングで爆発させ、それぞれ自分のプレイヤーだけにダメージを与える。
/// </summary>
public class BombBox : MonoBehaviour
{
    [Header("導火線")]
    [SerializeField, Min(1f), Tooltip("投げられてから（持ち続けてから）爆発するまでの時間（秒）")]
    private float fuseTime = 5f;

    [Header("ダメージ")]
    [SerializeField, Min(0f), Tooltip("動いている Box が直撃したときのダメージ")]
    private float directHitDamage = 8f;
    [SerializeField, Min(0f), Tooltip("爆発の最大ダメージ（中心）。範囲の端に向けて弱まる")]
    private float explosionDamage = 40f;
    [SerializeField, Min(0.5f), Tooltip("爆発の範囲（m）")]
    private float explosionRadius = 5f;
    [SerializeField, Min(0.1f), Tooltip("同じ Box が続けて当たるまでの間隔（秒）")]
    private float hitInterval = 0.8f;
    [SerializeField, Min(0f), Tooltip("この速さ（m/s）以上で動いているときだけ直撃扱いにする")]
    private float hitMinSpeed = 2f;

    [Header("スタン")]
    [SerializeField, Min(0f), Tooltip("動いている Box が当たったとき、相手がスタン（動けなくなる）する時間（秒）")]
    private float stunDuration = 2f;

    [Header("物理")]
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.3f;

    public float FuseTime => fuseTime;
    public float DirectHitDamage => directHitDamage;
    public float ExplosionDamage => explosionDamage;
    public float ExplosionRadius => explosionRadius;
    public float Remaining { get; private set; }

    private Rigidbody rb;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private float nextHitTime;
    private bool exploded;
    private bool blinkOn;
    private PlayerController localPlayer;
    private CharacterController localController;

    private static PhysicsMaterial bouncyMaterial;

    private void Awake()
    {
        Remaining = fuseTime;
        EnsureComponents();
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    /// <summary>プレハブに足りない部品（拾える Item、物理、当たり判定、レイヤー）を実行時に補う</summary>
    private void EnsureComponents()
    {
        int itemsLayer = LayerMask.NameToLayer("Items");
        if (itemsLayer >= 0) SetLayerRecursively(transform, itemsLayer);

        if (GetComponent<Item>() == null) gameObject.AddComponent<Item>();

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Collider col = GetComponentInChildren<Collider>();
        if (col == null) col = AddFittedBoxCollider();

        if (bouncyMaterial == null)
        {
            bouncyMaterial = new PhysicsMaterial("BombBoxBouncy")
            {
                bounciness = bounciness,
                dynamicFriction = 0.5f,
                staticFriction = 0.5f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average
            };
        }
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.sharedMaterial = bouncyMaterial;
    }

    private Collider AddFittedBoxCollider()
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>();
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        if (rends.Length == 0)
        {
            box.size = Vector3.one * 0.5f;
            return box;
        }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        // ワールドの範囲を、このオブジェクトのローカル空間へ直す
        Vector3 scale = transform.lossyScale;
        box.center = transform.InverseTransformPoint(b.center);
        box.size = new Vector3(
            b.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            b.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            b.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        return box;
    }

    private static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayerRecursively(child, layer);
    }

    /// <summary>持ち主が拾っていた間に進んだぶんを引き継ぐ</summary>
    public void SetRemaining(float seconds)
    {
        Remaining = Mathf.Max(0.05f, seconds);
    }

    private void Update()
    {
        if (exploded) return;

        Remaining -= Time.deltaTime;
        UpdateBlink();

        if (Remaining <= 0f)
        {
            exploded = true;
            // 爆心は、見た目の中心（モデルの原点がずれていても正しい位置にする）。
            // 吹き飛ぶ体が、この Box の当たり判定に押し返されないよう、先に判定を切る
            Vector3 center = transform.position;
            Collider[] cols = GetComponentsInChildren<Collider>();
            if (cols.Length > 0)
            {
                Bounds b = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                center = b.center;
            }
            foreach (Collider c in cols) c.enabled = false;
            ExplodeAt(center, explosionRadius, explosionDamage);
            Destroy(gameObject);
            return;
        }

        CheckDirectHit();
    }

    // 爆発が近づくほど、赤く速く点滅する
    private void UpdateBlink()
    {
        if (renderers == null) return;

        float urgency = Mathf.Clamp01(1f - Remaining / Mathf.Max(1f, fuseTime));
        float frequency = Mathf.Lerp(1.5f, 12f, urgency * urgency);
        bool on = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f) > 0f && Remaining < fuseTime * 0.85f;

        // 赤く光るたびに鳴る。爆発が近いほど高い
        if (on && !blinkOn) GameAudio.PlayAt(GameSound.BoxBeep, transform.position, Mathf.Lerp(0.5f, 1f, urgency), Mathf.Lerp(0.9f, 1.4f, urgency), 18f);
        blinkOn = on;

        Color c = on ? new Color(1f, 0.15f, 0.1f) : Color.white;
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        foreach (Renderer r in renderers)
        {
            if (r != null) r.SetPropertyBlock(block);
        }
    }

    private void CheckDirectHit()
    {
        if (bounced || Time.time < nextHitTime) return;
        if (rb == null || rb.linearVelocity.magnitude < hitMinSpeed) return;

        EnsureLocalPlayer();
        if (localPlayer == null || localController == null || localPlayer.IsDown) return;

        // Box の中心がプレイヤーのカプセルに十分近ければ直撃
        Bounds playerBounds = localController.bounds;
        playerBounds.Expand(0.3f);
        if (!playerBounds.Contains(transform.position)) return;

        HitLocalPlayer();
    }

    private void EnsureLocalPlayer()
    {
        if (localPlayer != null) return;
        localPlayer = FindAnyObjectByType<PlayerController>();
        if (localPlayer != null) localController = localPlayer.GetComponent<CharacterController>();
    }

    private void HitLocalPlayer()
    {
        nextHitTime = Time.time + hitInterval;
        bounced = true; // 当たったら、そのあとはもう当たらない（一度バウンドしたのと同じ）
        localPlayer.TakeDamage(directHitDamage, "HIT BY BOX");
        localPlayer.Stun(stunDuration);
    }

    // 一度でも何かに当たって跳ねたら、その Box はダメージもスタンも与えなくなる（爆発だけは残る）
    private bool bounced;
    private float spawnTime;

    // 敵に動いている Box が当たったらスタンさせる（敵・味方どちらが投げた Box でも同じ。判定は敵を動かしているホストだけ）
    private EnemyBrain thrower;
    private float throwTime;

    /// <summary>敵が投げた Box。投げた直後は、投げた本人（自分の体に当たる）をスタンさせない</summary>
    public void SetThrower(EnemyBrain enemy)
    {
        thrower = enemy;
        throwTime = Time.time;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (exploded || rb == null) return;
        float impact = collision.relativeVelocity.magnitude;

        // 敵に当たった：動いていればスタン（自分を投げた敵には、投げた直後は当たらない扱い）
        EnemyBrain enemy = collision.collider.GetComponentInParent<EnemyBrain>();
        if (enemy != null)
        {
            if (enemy == thrower && Time.time - throwTime < 1.5f) return;
            if (!bounced && impact >= hitMinSpeed) enemy.Stun(stunDuration);
            bounced = true;
            return;
        }

        // 自分（ローカルのプレイヤー）に当たった：動いていればダメージとスタン
        PlayerController player = collision.collider.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            if (!bounced && impact >= hitMinSpeed && !player.IsDown)
            {
                localPlayer = player;
                HitLocalPlayer();
            }
            bounced = true;
            return;
        }

        // 床・壁・他のものに当たって跳ねた：以降はダメージもスタンもしない（ぶつかる音は ItemImpactNoise が出す）
        if (impact >= 1f) bounced = true;
    }

    // ------------------------------------------------------------------
    // 爆発（静的）。ローカルプレイヤーだけにダメージを与え、見た目を出す
    // ------------------------------------------------------------------

    public static void ExplodeAt(Vector3 position, float radius, float maxDamage)
    {
        SpawnExplosionEffect(position, radius);
        GameAudio.PlayAt(GameSound.Explosion, position, 1f, Random.Range(0.92f, 1.06f), 70f);
        EnemyNoise.Emit(position, EnemyNoise.Explosion);

        // 敵へのダメージ（敵ごとに無効かどうかを持つ。判定するのはホストだけ）
        EnemyBrain.DamageEnemiesInRadius(position, radius, maxDamage);

        BlastItems(position, radius);

        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc == null || pc.IsDown) return;

        // プレイヤーの体の中心までの距離で減衰させる
        Vector3 bodyCenter = pc.transform.position;
        CharacterController cc = pc.GetComponent<CharacterController>();
        if (cc != null) bodyCenter = cc.bounds.center;

        float distance = Vector3.Distance(position, bodyCenter);
        if (distance > radius) return;

        float falloff = 1f - Mathf.Clamp01(distance / radius);
        float damage = Mathf.Max(1f, maxDamage * Mathf.Lerp(0.25f, 1f, falloff));
        pc.TakeDamage(damage, "BLOWN UP");

        // 爆心から離れる向きへ吹き飛ばす（近いほど強く、少し上へ浮く）。着地するまで操作できない
        // 爆心 → 体の中心 の向き（水平）。ほぼ真上・真下にいる場合は、体が向いている方向の逆へ
        Vector3 away = bodyCenter - position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.04f)
        {
            away = -pc.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
        }
        away = away.normalized;

        Debug.Log($"[Box爆発] 爆心={position} 体の中心={bodyCenter} 飛ぶ向き={away}");
        Debug.DrawRay(position, Vector3.up * 3f, Color.red, 5f);
        Debug.DrawRay(bodyCenter, away * 3f, Color.green, 5f);

        float horizontalSpeed = Mathf.Lerp(3.5f, 8f, falloff);
        float upwardSpeed = Mathf.Lerp(3f, 6f, falloff);
        // 着地してからのスタン時間（近いほど長い）
        float recoveryStun = Mathf.Lerp(2f, 4f, falloff);
        pc.Launch(away * horizontalSpeed + Vector3.up * upwardSpeed, recoveryStun);
    }

    /// <summary>範囲内の落ちているアイテム（他の Box を含む）を吹き飛ばす。近いほど強い</summary>
    private static void BlastItems(Vector3 position, float radius)
    {
        int itemsLayer = LayerMask.NameToLayer("Items");
        if (itemsLayer < 0) return;

        Collider[] hits = Physics.OverlapSphere(position, radius, 1 << itemsLayer, QueryTriggerInteraction.Ignore);
        System.Collections.Generic.HashSet<Rigidbody> blasted = new System.Collections.Generic.HashSet<Rigidbody>();
        foreach (Collider col in hits)
        {
            Rigidbody body = col.attachedRigidbody;
            if (body == null || body.isKinematic || !blasted.Add(body)) continue;
            body.AddExplosionForce(8f, position, radius, 1f, ForceMode.VelocityChange);
        }
    }

    private static void SpawnExplosionEffect(Vector3 position, float radius)
    {
        GameObject fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fx.name = "BoxExplosionFx";
        Destroy(fx.GetComponent<Collider>());
        fx.transform.position = position;
        fx.transform.localScale = Vector3.one * 0.5f;

        RuntimeMaterials.ApplyUnlit(fx.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.1f));

        Light light = fx.AddComponent<Light>();
        light.color = new Color(1f, 0.6f, 0.2f);
        light.range = radius * 2f;
        light.intensity = 8f;

        fx.AddComponent<ExplosionFx>().Setup(radius);
    }

    private class ExplosionFx : MonoBehaviour
    {
        private const float Duration = 0.4f;
        private float radius;
        private float elapsed;
        private Light fxLight;

        public void Setup(float explosionRadius)
        {
            radius = explosionRadius;
            fxLight = GetComponent<Light>();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);
            float scale = Mathf.Lerp(0.5f, radius * 2f, 1f - (1f - t) * (1f - t));
            transform.localScale = Vector3.one * scale;
            if (fxLight != null) fxLight.intensity = Mathf.Lerp(8f, 0f, t);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
