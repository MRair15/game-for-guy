using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float maxSpeed = 4f;
    public float accel = 18f;
    public float decel = 22f;
    public float deadZone = 0.05f;

    [Header("Visuals")]
    public Transform spriteTr;
    public float bobAmplitude = 0.06f;
    public float bobFrequency = 9f;
    public float bobLerp = 12f;
    public float tiltFactor = 2f;
    public float maxTilt = 8f;

    // -------- DAMAGE / HEALTH ----------
    [Header("Health")]
    public int maxHP = 5;
    public float invulnTime = 0.6f;
    public float contactCooldown = 0.2f;

    [Header("Contact Damage")]
    public LayerMask enemyMask;
    public string enemyTag = "Enemy";
    public int contactDamage = 1;

    [Header("Knockback / Stun")]
    public float knockbackForce = 6f;     // сила, передаваемая в удар
    public float knockbackToSpeed = 6f;   // перевод силы в стартовую скорость нокбэка
    public float knockbackDamp = 10f;     // затухание внешней скорости
    public float stunTime = 0.12f;        // время, когда управление «оглушено»

    [Header("Hit Flash")]
    public Color flashColor = new Color(1f, 0.25f, 0.25f, 1f);
    public int flashCount = 3;
    public float flashInterval = 0.06f;

    // -------- DEATH ANIMATION ----------
    [Header("Death Animation")]
    public Sprite deathSprite;                 // спрайт «трупа»
    public float deathFallTime = 0.45f;        // длительность падения
    public Vector2 deathOffset = new Vector2(0.08f, -0.18f); // смещение при падении
    public bool disableCollidersOnDeath = true;

    [Header("Tools")]
    public GameObject shadow;
    public GameObject swords;

    // -----------------------------------

    Rigidbody2D rb;
    SpriteRenderer sr;
    Vector2 inputDir, targetVel, currentVel, lastMoveDir = Vector2.right;
    Vector3 spriteStartLP;
    Quaternion spriteStartRot;
    float bobPhase;

    public int CurrentHP { get; private set; }
    bool _invulnerable;
    float _nextAnyContactTime;
    Color _baseColor;

    // защита от повторных хитов одним и тем же врагом
    readonly Dictionary<int, float> _perAttackerCooldown = new();

    // --- Нокбэк/стан игрока ---
    Vector2 externalVel;     // внешняя скорость от нокбэка
    float stunEndTime;       // время конца стана
    public bool IsStunned => Time.time < stunEndTime;

    // --- Death state ---
    bool _isDead;

    public System.Action OnPlayerHit;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (!spriteTr && transform.childCount > 0) spriteTr = transform.GetChild(0);
        if (spriteTr)
        {
            sr = spriteTr.GetComponent<SpriteRenderer>();
            spriteStartLP = spriteTr.localPosition;
            spriteStartRot = spriteTr.localRotation;
            if (sr) _baseColor = sr.color;
        }

        CurrentHP = Mathf.Max(1, maxHP);
    }

    void Update()
    {
        if (_isDead) return;

        // --- ВВОД ---
        float x = 0, y = 0;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            x = (Keyboard.current.dKey.isPressed ? 1 : 0) + (Keyboard.current.aKey.isPressed ? -1 : 0);
            y = (Keyboard.current.wKey.isPressed ? 1 : 0) + (Keyboard.current.sKey.isPressed ? -1 : 0);
        }
        else
#endif
        {
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
        }

        inputDir = new Vector2(x, y);
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();
        if (inputDir.sqrMagnitude > deadZone * deadZone) lastMoveDir = inputDir;

        targetVel = inputDir * maxSpeed;

        // --- Покачивание + наклон ---
        float moveFactor = Mathf.Clamp01(targetVel.magnitude / Mathf.Max(0.0001f, maxSpeed));
        bobPhase += moveFactor * bobFrequency * Time.deltaTime * Mathf.PI * 2f;

        if (spriteTr)
        {
            float bob = Mathf.Sin(bobPhase) * bobAmplitude * moveFactor;
            Vector3 targetLP = spriteStartLP; targetLP.y += bob;
            spriteTr.localPosition = Vector3.Lerp(spriteTr.localPosition, targetLP, Time.deltaTime * bobLerp);

            float tilt = Mathf.Clamp(-currentVel.x * tiltFactor, -maxTilt, maxTilt);
            spriteTr.localRotation = Quaternion.Lerp(
                spriteTr.localRotation,
                Quaternion.Euler(0, 0, tilt),
                Time.deltaTime * 10f
            );
        }

        if (sr && Mathf.Abs(lastMoveDir.x) > Mathf.Abs(lastMoveDir.y))
            sr.flipX = lastMoveDir.x > 0f;
    }

    void FixedUpdate()
    {
        if (_isDead) { rb.velocity = Vector2.zero; return; }

        // Затухание внешней скорости (нокбэк)
        if (externalVel.sqrMagnitude > 0.0001f)
            externalVel = Vector2.MoveTowards(externalVel, Vector2.zero, knockbackDamp * Time.fixedDeltaTime);
        else
            externalVel = Vector2.zero;

        // Плавное движение
        float ax = (targetVel.sqrMagnitude > 0.0001f) ? accel : decel;
        currentVel = Vector2.MoveTowards(currentVel, targetVel, ax * Time.fixedDeltaTime);

        Vector2 move = IsStunned ? externalVel : currentVel + externalVel;
        rb.MovePosition(rb.position + move * Time.fixedDeltaTime);
    }

    // ====== ПОЛУЧЕНИЕ УРОНА ОТ ВРАГОВ ======
    void OnCollisionEnter2D(Collision2D col)  { TryTakeContactDamage(col.collider); }
    void OnCollisionStay2D (Collision2D col)  { TryTakeContactDamage(col.collider); }
    void OnTriggerEnter2D  (Collider2D other) { TryTakeContactDamage(other);        }
    void OnTriggerStay2D   (Collider2D other) { TryTakeContactDamage(other);        }

    void TryTakeContactDamage(Collider2D other)
    {
        if (_invulnerable || _isDead) return;
        if (Time.time < _nextAnyContactTime) return;

        bool isEnemyLayer = (enemyMask.value & (1 << other.gameObject.layer)) != 0;
        bool isEnemyTag   = !string.IsNullOrEmpty(enemyTag) && other.CompareTag(enemyTag);
        if (!isEnemyLayer && !isEnemyTag) return;

        int id = other.attachedRigidbody ? other.attachedRigidbody.GetInstanceID()
                                         : other.gameObject.GetInstanceID();
        if (_perAttackerCooldown.TryGetValue(id, out float readyAt) && Time.time < readyAt)
            return;

        Vector2 fromEnemy = (Vector2)(transform.position - other.transform.position);
        TakeDamage(contactDamage, fromEnemy);

        _nextAnyContactTime = Time.time + contactCooldown;
        _perAttackerCooldown[id] = Time.time + contactCooldown;
    }

    public void TakeDamage(int amount, Vector2 hitDir)
    {
        if (_invulnerable || amount <= 0 || _isDead) return;

        CurrentHP -= amount;
        if (CurrentHP <= 0)
        {
            CurrentHP = 0;
            StartCoroutine(DeathSequence());
            return;
        }

        OnPlayerHit?.Invoke();

        // i-frames + мигание
        StartCoroutine(InvulnRoutine());
        StartCoroutine(FlashRoutine());

        // --- Нокбэк/стан как внешняя скорость (без AddForce) ---
        externalVel = hitDir.normalized * (knockbackForce * knockbackToSpeed * 0.1667f); // ~1/6
        stunEndTime = Time.time + stunTime;
    }

    IEnumerator InvulnRoutine()
    {
        _invulnerable = true;
        yield return new WaitForSeconds(invulnTime);
        _invulnerable = false;
    }

    IEnumerator FlashRoutine()
    {
        if (!sr) yield break;

        for (int i = 0; i < flashCount; i++)
        {
            sr.color = flashColor;
            yield return new WaitForSeconds(flashInterval);
            sr.color = _baseColor;
            yield return new WaitForSeconds(flashInterval);
        }
    }

    // -------- Death animation inside controller --------
   IEnumerator DeathSequence()
{
    _isDead = true;
    rb.velocity = Vector2.zero;

    if (disableCollidersOnDeath)
        foreach (var c in GetComponentsInChildren<Collider2D>(true)) c.enabled = false;

    // куда смотрим: у тебя инверт-флип — flipX == true => смотрим вправо
    bool facingRight = sr ? sr.flipX : (lastMoveDir.x > 0f);
    Destroy(swords);
    float fallAngle = facingRight ? 90f : -90f;
    // смещение «в сторону падения»
    Vector3 signedOffset = new Vector3(
        Mathf.Abs(deathOffset.x) * (facingRight ? 1f : -1f),
        deathOffset.y,
        0f
    );

    // база для интерполяции — текущая локальная позиция, НЕ стартовая
    Quaternion fromR = spriteTr ? spriteTr.localRotation : Quaternion.identity;
    Quaternion toR   = Quaternion.Euler(0, 0, fallAngle);
    Vector3 fromLP   = spriteTr ? spriteTr.localPosition : Vector3.zero;
    Vector3 toLP     = fromLP + signedOffset;

    // родительский трансформ, чтобы зафиксировать мировую точку
    Transform parentTr = spriteTr ? spriteTr.parent : transform;

    // падение
    float t = 0f;
    while (t < 1f)
    {
        t += Time.deltaTime / Mathf.Max(0.0001f, deathFallTime);
        float s = Mathf.SmoothStep(0f, 1f, t);

        if (spriteTr)
        {
            spriteTr.localRotation = Quaternion.Lerp(fromR, toR, s);
            spriteTr.localPosition = Vector3.Lerp(fromLP, toLP, s);
        }
        yield return null;
    }

    // Мировая точка, где должен лежать труп ПОСЛЕ падения
    Vector3 corpseWorldPos = parentTr ? parentTr.TransformPoint(toLP) : toLP;

    // Если есть трупный спрайт — ставим его и СНАЧАЛА сбрасываем поворот,
    // затем возвращаем в ту же мировую точку, чтобы он не «перескочил» на другую сторону.
    if (sr && deathSprite)
    {
        sr.sprite = deathSprite;

            if (spriteTr)
            {
                spriteTr.rotation = Quaternion.identity;  // rotation = 0 (в мировых)
                spriteTr.position = corpseWorldPos;       // фиксация позиции в мире
                                                          // обычно на трупе не нужен флип:

                if (facingRight == false)
                    sr.flipX = true;
                else
                    sr.flipX = false;

                Destroy(shadow);

        }
    }
    // Если deathSprite не задан — оставляем поворот ±90°, он уже «лежит» корректно.
}



}
