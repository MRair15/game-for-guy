using UnityEngine;
using System;
using System.Collections;
using System.Linq;

[DisallowMultipleComponent]
public class Damageable : MonoBehaviour
{
    [Header("Health")]
    public int maxHP = 3;
    [Tooltip("Время неуязвимости (i-frames) после попадания")]
    public float invulnTime = 0.25f;

    [Header("Feedback (on hit)")]
    public SpriteRenderer sr;                    // можно не задавать — возьмётся из детей
    [Tooltip("Сколько раз мигнуть за invulnTime")]
    public int flashCount = 4;
    public Color hitColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Knockback / Stun")]
    [Tooltip("Перевод силы knockbackForce в стартовую скорость отброса")]
    public float knockbackToSpeed = 6f;
    [Tooltip("Затухание отброса (чем больше — тем быстрее останавливается)")]
    public float knockbackDamp = 10f;
    [Tooltip("Сколько длится стан после удара")]
    public float stunTime = 0.12f;

    [Header("Death Animation")]
    [Tooltip("Играть анимацию уменьшения/растворения перед уничтожением")]
    public bool animateOnDeath = true;
    [Tooltip("Кого масштабировать (если пусто — сам объект)")]
    public Transform deathScaleTarget;
    [Tooltip("Длительность анимации смерти")]
    public float deathDuration = 0.25f;
    [Tooltip("Кривая плавности (0→1)")]
    public AnimationCurve deathEase = null; // если null — EaseInOut
    [Tooltip("Параллельно уводить альфу в ноль")]
    public bool deathFadeOut = true;
    [Tooltip("Отключить все Collider2D сразу при смерти")]
    public bool deathDisableColliders = true;
    [Tooltip("Выключить симуляцию Rigidbody2D (если есть)")]
    public bool deathDisableRigidbodySimulation = true;
    [Tooltip("Отключить все MonoBehaviour у потомков (кроме этого)")]
    public bool deathDisableBehaviours = true;

    [Header("Spawn Animation")]
    [Tooltip("Играть анимацию появления при создании/активации")]
    public bool animateOnSpawn = true;
    [Tooltip("Кого масштабировать (если пусто — сам объект)")]
    public Transform spawnScaleTarget;
    [Tooltip("Длительность анимации появления")]
    public float spawnDuration = 0.25f;
    [Tooltip("Кривая плавности появления")]
    public AnimationCurve spawnEase = null; // если null — EaseInOut
    [Tooltip("Параллельно увеличивать альфу из 0 в 1")]
    public bool spawnFadeIn = true;

    [Header("Destroy")]
    [Tooltip("Удалять объект после смерти (после анимации, если она включена)")]
    public bool destroyOnDeath = true;

    // -------- Runtime state --------
    public int CurrentHP { get; private set; }
    public bool IsInvulnerable => Time.time < _invEnd;
    public bool IsStunned => Time.time < _stunEnd;
    /// <summary> Внешняя скорость от нокбэка: прибавляй её к своему MovePosition. </summary>
    public Vector2 ExternalVelocity => _kbVel;

    public event Action OnHit;
    public event Action OnDeath;  // Вызывается при начале смерти (до анимации)

    Rigidbody2D _rb;
    Color _baseColor;
    float _invEnd;
    float _stunEnd;
    Vector2 _kbVel;
    Coroutine _flashCo;
    bool _isDying;
    SpriteRenderer[] _allRenderers;   // для fade
    Vector3 _startScale;

    void Awake()
    {
        CurrentHP = Mathf.Max(1, maxHP);
        _rb = GetComponent<Rigidbody2D>();
        if (!sr) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr) _baseColor = sr.color;

        if (!deathScaleTarget) deathScaleTarget = transform;
        _startScale = deathScaleTarget.localScale;

        if (!spawnScaleTarget) spawnScaleTarget = transform;
        if (spawnEase == null) spawnEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        _allRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
        if (deathEase == null) deathEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    void Start()
    {
        if (animateOnSpawn && spawnDuration > 0f)
            StartCoroutine(SpawnRoutine());
    }

    void Update()
    {
        // Экспоненциальное затухание нокбэка.
        if (_kbVel.sqrMagnitude > 0.0001f)
            _kbVel = Vector2.MoveTowards(_kbVel, Vector2.zero, knockbackDamp * Time.deltaTime);
        else
            _kbVel = Vector2.zero;
    }

    /// <summary>
    /// amount — урон; hitDir — направление ОТ атакующего К нам; knockbackForce — сила.
    /// </summary>
    public void ApplyDamage(int amount, Vector2 hitDir, float knockbackForce)
    {
        if (_isDying || IsInvulnerable || amount <= 0) return;

        CurrentHP -= amount;
        if (CurrentHP < 0) CurrentHP = 0;
        _invEnd = Time.time + invulnTime;

        // Нокбэк и стан.
        if (knockbackForce > 0f)
        {
            _kbVel = hitDir.normalized * (knockbackForce * knockbackToSpeed * 0.1667f); // ~1/6 для правдоподобия
            _stunEnd = Time.time + stunTime;
        }

        // Красное мигание на время i-frames.
        if (_flashCo != null) StopCoroutine(_flashCo);
        if (sr) _flashCo = StartCoroutine(FlashRoutine());

        OnHit?.Invoke();

        if (CurrentHP <= 0)
            StartDeath();
    }

    void StartDeath()
    {
        if (_isDying) return;
        _isDying = true;

        // Сигнал наружу (лут, счёт и т.п.)
        OnDeath?.Invoke();

        // Заморозить активность
        if (deathDisableColliders)
        {
            foreach (var c in GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;
        }
        if (deathDisableRigidbodySimulation && _rb)
        {
            _rb.simulated = false;
        }
        if (deathDisableBehaviours)
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == this) continue; // себя не выключаем
                mb.enabled = false;
            }
        }

        if (animateOnDeath && deathDuration > 0f)
            StartCoroutine(DeathRoutine());
        else
            FinalDestroy();
    }

    IEnumerator DeathRoutine()
    {
        float t = 0f;
        // Запомним исходные альфы, если делаем fade
        float[] startAlpha = null;
        if (deathFadeOut && _allRenderers != null && _allRenderers.Length > 0)
        {
            startAlpha = _allRenderers.Select(r => r.color.a).ToArray();
        }

        while (t < deathDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / deathDuration);
            float eased = deathEase.Evaluate(k);

            // Scale → 0
            if (deathScaleTarget)
            {
                deathScaleTarget.localScale = Vector3.LerpUnclamped(_startScale, Vector3.zero, eased);
            }

            // Fade → 0
            if (deathFadeOut && _allRenderers != null)
            {
                for (int i = 0; i < _allRenderers.Length; i++)
                {
                    var r = _allRenderers[i];
                    if (!r) continue;
                    var col = r.color;
                    float a0 = (startAlpha != null && i < startAlpha.Length) ? startAlpha[i] : col.a;
                    col.a = Mathf.Lerp(a0, 0f, eased);
                    r.color = col;
                }
            }

            yield return null;
        }

        FinalDestroy();
    }

    void FinalDestroy()
    {
        if (sr) sr.color = _baseColor;

        if (destroyOnDeath)
            Destroy(gameObject);
        else
            enabled = false;
    }

    IEnumerator FlashRoutine()
    {
        if (!sr || flashCount <= 0 || invulnTime <= 0f)
            yield break;

        float perHalf = invulnTime / (flashCount * 2f);
        for (int i = 0; i < flashCount; i++)
        {
            sr.color = hitColor;
            yield return new WaitForSeconds(perHalf);
            sr.color = _baseColor;
            yield return new WaitForSeconds(perHalf);
        }
        sr.color = _baseColor;
    }

    IEnumerator SpawnRoutine()
    {
        if (!spawnScaleTarget) yield break;

        Vector3 targetScale = spawnScaleTarget.localScale;
        spawnScaleTarget.localScale = Vector3.zero;

        // Подготовим альфы
        Color[] finalColors = _allRenderers.Select(r => r.color).ToArray();
        if (spawnFadeIn)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                var col = finalColors[i];
                col.a = 0f;
                _allRenderers[i].color = col;
            }
        }

        float t = 0f;
        while (t < spawnDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / spawnDuration);
            float eased = spawnEase.Evaluate(k);

            spawnScaleTarget.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale, eased);

            if (spawnFadeIn)
            {
                for (int i = 0; i < _allRenderers.Length; i++)
                {
                    var col = finalColors[i];
                    col.a = Mathf.Lerp(0f, finalColors[i].a, eased);
                    _allRenderers[i].color = col;
                }
            }

            yield return null;
        }

        spawnScaleTarget.localScale = targetScale;
        if (spawnFadeIn)
        {
            for (int i = 0; i < _allRenderers.Length; i++)
                _allRenderers[i].color = finalColors[i];
        }
    }
}
