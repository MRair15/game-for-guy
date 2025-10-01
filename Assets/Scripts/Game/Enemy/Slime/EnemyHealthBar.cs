using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
    [Header("Refs")]
    public Damageable damageable;          // если пусто — возьмём из родителей
    public Transform anchor;               // к чему привязан бар (обычно корень врага)
    public SpriteRenderer back;            // фон/рамка (опционально)
    public SpriteRenderer fill;            // заполняемая полоса

    [Header("Layout")]
    public Vector3 worldOffset = new Vector3(0f, 0.9f, 0f);
    public float barLength = 1.2f;         // «длина» в localScale X
    public float barHeight = 0.18f;        // высота в localScale Y

    [Header("Visibility")]
    public float showForSeconds = 1.6f;    // сколько секунд бар держится видимым после удара
    public float fadeSpeed = 10f;          // скорость появления/исчезновения по альфе

    [Header("Animation")]
    public float shrinkDuration = 0.25f;   // длительность «убывания» (ease)
    public AnimationCurve ease = AnimationCurve.EaseInOut(0,0,1,1);

    [Header("Colors")]
    public Gradient hpGradient;            // цвет заливки по проценту (необязательно)
    public Color backColor = new Color(0f,0f,0f,0.55f);

    // ---- runtime ----
    float _lastHitTime = -999f;    // для автоскрытия
    float _alpha = 0f;

    float _fullLenX;               // запомним исходные масштабы
    float _fillBaseX = 1f, _fillBaseY = 1f;
    float _backBaseX = 1f, _backBaseY = 1f;

    float _visualP = 1f;           // что рисуем
    Coroutine _shrinkCR;           // корутина анимации убывания

    void Awake()
    {
        if (!damageable) damageable = GetComponentInParent<Damageable>();
        if (!anchor) anchor = (damageable ? damageable.transform : transform);

        if (fill)
        {
            var ls = fill.transform.localScale;
            _fillBaseX = (ls.x == 0f ? 1f : ls.x);
            _fillBaseY = (ls.y == 0f ? 1f : ls.y);
        }
        if (back)
        {
            var ls = back.transform.localScale;
            _backBaseX = (ls.x == 0f ? 1f : ls.x);
            _backBaseY = (ls.y == 0f ? 1f : ls.y);
            back.color = backColor;
        }

        _fullLenX = _fillBaseX * barLength;

        if (damageable)
        {
            _visualP = Mathf.Clamp01((float)damageable.CurrentHP / Mathf.Max(1, damageable.maxHP));
            ApplyFill(_visualP, instant:true);
            damageable.OnHit += OnHit;
            damageable.OnDeath += OnDeath;
        }

        SetAlpha(0f, instant:true);
        ApplyBackLayout();
        Reposition();
    }

    void OnDestroy()
    {
        if (damageable)
        {
            damageable.OnHit  -= OnHit;
            damageable.OnDeath-= OnDeath;
        }
    }

    void LateUpdate()
    {
        Reposition();

        // автоскрытие
        float wantAlpha = (Time.time <= _lastHitTime + showForSeconds) ? 1f : 0f;
        if (!Mathf.Approximately(_alpha, wantAlpha))
        {
            _alpha = Mathf.MoveTowards(_alpha, wantAlpha, fadeSpeed * Time.deltaTime);
            SetAlpha(_alpha, instant:true);
        }
    }

    void Reposition()
    {
        if (!anchor) return;
        transform.position = anchor.position + worldOffset;
        transform.rotation = Quaternion.identity;
    }

    void ApplyBackLayout()
    {
        if (!back) return;
        var ls = back.transform.localScale;
        ls.x = _backBaseX * barLength;
        ls.y = _backBaseY * barHeight;
        back.transform.localScale = ls;
        var pos = back.transform.localPosition;
        pos.x = 0f; // фон центрируем
        back.transform.localPosition = pos;
    }

    // Левый край фиксирован: уменьшаем «справа налево»
    void ApplyFill(float p, bool instant = false)
    {
        p = Mathf.Clamp01(p);

        var ls = fill.transform.localScale;
        float curLen = _fillBaseX * (barLength * p);
        ls.x = curLen;
        ls.y = _fillBaseY * barHeight;
        fill.transform.localScale = ls;

        // левый край в одной точке: centerX = -fullLen/2 + curLen/2
        var pos = fill.transform.localPosition;
        pos.x = -_fullLenX * 0.5f + curLen * 0.5f;
        fill.transform.localPosition = pos;

        if (hpGradient != null)
        {
            var c = hpGradient.Evaluate(p);
            c.a = fill.color.a;
            fill.color = c;
        }
    }

    void SetAlpha(float a, bool instant)
    {
        if (fill) { var c = fill.color; c.a = a; fill.color = c; }
        if (back) { var c = back.color; c.a = a * 0.9f; back.color = c; }
    }

    // === события от Damageable ===
    void OnHit()
    {
        if (!damageable || !fill) return;

        _lastHitTime = Time.time;
        float targetP = Mathf.Clamp01((float)damageable.CurrentHP / Mathf.Max(1, damageable.maxHP));

        // запускаем плавное «схлопывание» к targetP
        if (_shrinkCR != null) StopCoroutine(_shrinkCR);
        _shrinkCR = StartCoroutine(ShrinkTo(targetP));

        // сразу делаем видимым (если было спрятано)
        SetAlpha(Mathf.Max(_alpha, 1f), instant:false);
    }

    void OnDeath()
    {
        // При смерти быстро схлопнем в ноль
        if (_shrinkCR != null) StopCoroutine(_shrinkCR);
        StartCoroutine(ShrinkTo(0f, shrinkDuration * 0.5f));
        // и затем спрячем
        _lastHitTime = Time.time - showForSeconds + 0.25f;
    }

    IEnumerator ShrinkTo(float targetP, float durationOverride = -1f)
    {
        float startP = _visualP;
        float t = 0f;
        float dur = durationOverride > 0f ? durationOverride : shrinkDuration;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = ease != null ? ease.Evaluate(Mathf.Clamp01(t / dur)) : Mathf.Clamp01(t / dur);

            _visualP = Mathf.Lerp(startP, targetP, k);
            ApplyFill(_visualP);
            yield return null;
        }

        _visualP = targetP;
        ApplyFill(_visualP);
    }
}
