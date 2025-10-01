using UnityEngine;
using System.Collections;

public class SlimeFollow : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 2f;            // скорость движения
    public float stopDistance = 0.5f;   // дистанция остановки
    public Transform player;            // цель (если пусто — найдём по имени)

    private Rigidbody2D rb;
    private Damageable dmg;

    // ---------- SLIME TRAIL ----------
    [Header("Slime Trail")]
    [Tooltip("Спрайт лужицы слайма (маленький кружок/капля)")]
    public Sprite trailSprite;
    [Tooltip("Как часто роняем лужицу при движении")]
    public float trailInterval = 0.16f;
    [Tooltip("Минимальная пройденная дистанция между лужицами")]
    public float trailMinStep = 0.08f;
    [Tooltip("Сколько секунд живёт одна лужица")]
    public float trailLifetime = 2.0f;
    [Tooltip("Случайный масштаб лужицы (min..max)")]
    public Vector2 trailScale = new Vector2(0.8f, 1.2f);
    [Tooltip("Смещение назад относительно движения (чтобы была позади)")]
    public float trailBackOffset = 0.10f;
    [Tooltip("Небольшой случайный дрожь по X/Y")]
    public float trailJitter = 0.04f;
    [Tooltip("Слой отрисовки (Sorting Layer) лужиц")]
    public string trailSortingLayer = "Default";
    [Tooltip("Порядок в слое. Ставь ниже слайма, чтобы было под ним")]
    public int trailOrderInLayer = -1;

    float _nextTrailTime;
    Vector3 _lastTrailPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        dmg = GetComponent<Damageable>();

        if (!player)
        {
            GameObject found = GameObject.Find("Player");
            if (found) player = found.transform;
        }

        _lastTrailPos = transform.position;
    }

    void FixedUpdate()
    {
        if (!player) return;

        // Если оглушён — только скользим по нокбэку
        if (dmg && dmg.IsStunned)
        {
            Vector2 vel = dmg.ExternalVelocity;
            rb.MovePosition(rb.position + vel * Time.fixedDeltaTime);
            // даже в стане можно оставлять хвост — по желанию. Включи следующую строку:
            // TryDropTrail(vel);
            return;
        }

        // Обычное следование к игроку
        Vector2 to = (player.position - transform.position);
        float dist = to.magnitude;

        Vector2 move = Vector2.zero;
        if (dist > stopDistance)
        {
            to.Normalize();
            move = to * speed;
        }

        // Прибавляем внешнюю скорость отброса
        if (dmg) move += dmg.ExternalVelocity;

        rb.MovePosition(rb.position + move * Time.fixedDeltaTime);

        // Пытаемся ронять лужицы, если реально двигаемся
        TryDropTrail(move);
    }

    // --------- TRAIL LOGIC ----------
    void TryDropTrail(Vector2 velocity)
    {
        if (!trailSprite) return;

        // достаточно ли быстро движемся?
        if (velocity.sqrMagnitude < 0.0001f) return;

        // по таймеру + по пройденной дистанции
        if (Time.time < _nextTrailTime) return;
        if ((transform.position - _lastTrailPos).sqrMagnitude < trailMinStep * trailMinStep) return;

        _nextTrailTime = Time.time + trailInterval;
        _lastTrailPos = transform.position;

        // Позиция чуть позади движения + небольшой jitter
        Vector2 dir = velocity.normalized;
        Vector3 pos = transform.position - (Vector3)(dir * trailBackOffset);
        pos.x += Random.Range(-trailJitter, trailJitter);
        pos.y += Random.Range(-trailJitter, trailJitter);

        // Создаём GO с SpriteRenderer (никаких дополнительных скриптов)
        var go = new GameObject("SlimeTrail");
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * Random.Range(trailScale.x, trailScale.y);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = trailSprite;
        sr.sortingLayerName = trailSortingLayer;
        sr.sortingOrder = trailOrderInLayer;

        // Альфа сначала полная; плавно потухает
        StartCoroutine(FadeAndDie(sr, trailLifetime));
    }

    IEnumerator FadeAndDie(SpriteRenderer r, float life)
    {
        float t = 0f;
        var c0 = r.color;
        while (t < life)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            var c = c0;
            c.a = 1f - k;  // линейно гасим
            r.color = c;
            yield return null;
        }
        if (r) Destroy(r.gameObject);
    }
}
