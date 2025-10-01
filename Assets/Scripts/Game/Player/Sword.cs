using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class SwordControllerSmoothDamage : MonoBehaviour
{
    [Header("References")]
    public Transform player;                 // вокруг кого вращаемся
    public Transform spriteTr;               // ребёнок со SpriteRenderer (клинок)
    public SpriteRenderer sr;                // можно не задавать — возьмём из spriteTr
    public Camera cam;                       // если null — Camera.main
    [Tooltip("Триггер-хитбокс меча (Box/Circle/PolygonCollider2D), IsTrigger = true")]
    public Collider2D hitbox;                // включается только в активное окно урона

    [Header("Orbit & Aim")]
    public float radius = 0.60f;             // дистанция от игрока
    public float turnSmooth = 0.08f;         // сглаживание наведения (0=мгновенно)
    [Tooltip("Если спрайт меча в текстуре 'смотрит вверх' — поставь -90; если вправо — 0")]
    public float visualAngleOffsetDeg = -90f;

    [Header("Sorting (optional)")]
    public bool sortBehindWhenUp = true;
    public int baseOrder = 0;

    [Header("Attack (LMB)")]
    public float swingAngle = 110f;          // дуга взмаха
    public float swingTime = 0.25f;          // длительность взмаха
    public float returnTime = 0.18f;         // плавный возврат
    public float cooldown = 0.30f;           // КД между ударами
    public AnimationCurve swingEase = null;  // если null — используем EaseInOut
    [Tooltip("Окно урона внутри взмаха (нормализовано 0..1)")]
    [Range(0,1)] public float activeStart = 0.25f;
    [Range(0,1)] public float activeEnd   = 0.80f;

    [Header("Damage")]
    public LayerMask enemyMask;              // слой врагов (Enemy)
    public int damage = 1;
    public float knockback = 6f;

    // ---- runtime ----
    Camera _cam;
    float _aimAngle;                         // сглаженный угол наведения
    float _attackTimer = -999f;              // <0 — нет атаки
    float _lastAttackEnd = -999f;
    int _swingDir = 1;                       // 1 — вправо, -1 — влево
    bool _returning;
    float _returnTimer;
    bool _hitboxOn;
    readonly HashSet<Damageable> _hitThisSwing = new HashSet<Damageable>();

    void Awake()
    {
        _cam = cam != null ? cam : Camera.main;
        if (!spriteTr && transform.childCount > 0) spriteTr = transform.GetChild(0);
        if (!sr && spriteTr) sr = spriteTr.GetComponent<SpriteRenderer>();
        if (hitbox) { hitbox.isTrigger = true; hitbox.enabled = false; }
        if (swingEase == null) swingEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && Time.time >= _lastAttackEnd + cooldown && !IsAttacking() && !_returning)
        {
            // выбираем направление дуги относительно курсора
            Vector3 m = (_cam ? _cam : Camera.main).ScreenToWorldPoint(Input.mousePosition); m.z = 0f;
            _swingDir = (m.x >= player.position.x) ? 1 : -1;

            _attackTimer = 0f;
            _hitThisSwing.Clear();
        }
    }

    void LateUpdate()
    {
        if (!player) return;
        if (_cam == null) _cam = Camera.main;
        if (!sr && spriteTr) sr = spriteTr.GetComponent<SpriteRenderer>();

        // угол к курсору
        Vector3 m = _cam.ScreenToWorldPoint(Input.mousePosition); m.z = 0f;
        Vector2 toMouse = (m - player.position);
        float targetAngle = Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;

        // сглаженное наведение
        if (turnSmooth > 0f)
            _aimAngle = Mathf.LerpAngle(_aimAngle, targetAngle, 1f - Mathf.Exp(-Time.deltaTime / turnSmooth));
        else
            _aimAngle = targetAngle;

        // позиция родителя на орбите (родителя НЕ вращаем)
        Vector2 dir = new Vector2(Mathf.Cos(_aimAngle * Mathf.Deg2Rad), Mathf.Sin(_aimAngle * Mathf.Deg2Rad)).normalized;
        transform.position = player.position + (Vector3)(dir * radius);
        transform.rotation = Quaternion.identity;

        if (sr && sortBehindWhenUp) sr.sortingOrder = baseOrder + (dir.y >= 0f ? -1 : 1);

        if (IsAttacking())
        {
            float t = Mathf.Clamp01(_attackTimer / Mathf.Max(0.0001f, swingTime));
            float eased = swingEase.Evaluate(t);

            float localSwing = Mathf.Lerp(-swingAngle * 0.5f, swingAngle * 0.5f, eased) * _swingDir;

            if (spriteTr) spriteTr.rotation = Quaternion.Euler(0, 0, _aimAngle + localSwing + visualAngleOffsetDeg);
            if (sr) sr.flipY = Mathf.Abs(Normalize180(_aimAngle)) > 90f;

            // окно урона
            SetHitbox(t >= activeStart && t <= activeEnd);

            _attackTimer += Time.deltaTime;
            if (_attackTimer >= swingTime)
            {
                _lastAttackEnd = Time.time;
                _attackTimer = -999f;
                SetHitbox(false);
                _returning = true; _returnTimer = 0f;
            }
        }
        else if (_returning)
        {
            _returnTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_returnTimer / Mathf.Max(0.0001f, returnTime));
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            float endSwing = (_swingDir > 0 ? swingAngle * 0.5f : -swingAngle * 0.5f);
            float fromAngle = _aimAngle + endSwing + visualAngleOffsetDeg;
            float toAngle = _aimAngle + visualAngleOffsetDeg;

            if (spriteTr) spriteTr.rotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(fromAngle, toAngle, smooth));
            if (sr) sr.flipY = Mathf.Abs(Normalize180(_aimAngle)) > 90f;

            if (t >= 1f) _returning = false;
        }
        else
        {
            // обычное наведение
            if (spriteTr) spriteTr.rotation = Quaternion.Euler(0, 0, _aimAngle + visualAngleOffsetDeg);
            if (sr) sr.flipY = Mathf.Abs(Normalize180(_aimAngle)) > 90f;
            SetHitbox(false);
        }
    }

    bool IsAttacking() => _attackTimer >= 0f;

    void SetHitbox(bool state)
    {
        if (!hitbox) return;
        if (_hitboxOn == state) return;
        hitbox.enabled = state;
        _hitboxOn = state;
    }

    // триггер меча → попытка нанести урон
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!_hitboxOn || hitbox == null || other == hitbox) return;
        if ((enemyMask.value & (1 << other.gameObject.layer)) == 0) return;

        // ищем Damageable у цели
        var d = other.GetComponentInParent<Damageable>() ?? other.GetComponent<Damageable>();
        if (d == null || d.IsInvulnerable) return;
        if (_hitThisSwing.Contains(d)) return; // уже били в этом взмахе

        // направление от игрока к цели для нокбэка
        Vector2 dir = (other.transform.position - player.position).normalized;
        d.ApplyDamage(damage, dir, knockback); // твой метод урона :contentReference[oaicite:3]{index=3}
        _hitThisSwing.Add(d);
    }

    static float Normalize180(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }
}
