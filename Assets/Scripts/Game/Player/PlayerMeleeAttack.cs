using UnityEngine;

public class PlayerMeleeAttack : MonoBehaviour
{
    [Header("Refs")]
    public Transform attackPoint;     // пустой объект у лезвия/перед героя
    public SpriteRenderer playerSR;   // чтобы флипать при ударе (если нужно)

    [Header("Attack")]
    public float radius = 0.6f;
    public int damage = 1;
    public float cooldown = 0.25f;
    public float knockback = 4f;
    public LayerMask enemyMask;

    float _nextAttackTime;

    void Update()
    {
        // клавиша атаки — ЛКМ / Space
        if (Time.time >= _nextAttackTime && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
        {
            DoAttack();
            _nextAttackTime = Time.time + cooldown;
        }

        // опционально — поворот/флип в сторону курсора
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 look = mouseWorld - transform.position;
        if (playerSR && Mathf.Abs(look.x) > Mathf.Abs(look.y))
            playerSR.flipX = look.x > 0f; // инверт, если у тебя так заведено
    }

    void DoAttack()
    {
        // Небольшой визуальный «взмах» (поворот) можно добавить позже
        Collider2D[] hits = Physics2D.OverlapCircleAll(attackPoint.position, radius, enemyMask);
        foreach (var c in hits)
        {
            var dmg = c.GetComponentInParent<Damageable>() ?? c.GetComponent<Damageable>();
            if (dmg != null)
            {
                Vector2 dir = (Vector2)(c.transform.position - transform.position);
                dmg.ApplyDamage(damage, dir, knockback);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, radius);
    }
}
