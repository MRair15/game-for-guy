using UnityEngine;
using System.Collections;

public class WizardAppear : MonoBehaviour
{
    [Header("Target")]
    public Transform targetPoint;

    [Header("Motion")]
    public float moveDuration = 1.2f;
    public AnimationCurve ease = null;
    public float offscreenMargin = 0.6f;
    public float fixedZ = 0f;

    [Header("Sorting")]
    public string sortingLayerName = "Characters";
    public int bodyOrder  = 100;   // маг
    public int shadowOrder = 90;   // тень ниже мага

    [Header("Final State")]
    public Sprite finalSprite;
    public bool flipX;

    bool isAppearing;
    SpriteRenderer bodySR;
    SpriteRenderer shadowSR;

    void Awake()
    {
        if (ease == null) ease = AnimationCurve.EaseInOut(0,0,1,1);

        // авто-поиск: тень — объект по имени "Shadow" (как у тебя на скрине)
        shadowSR = transform.Find("Shadow")?.GetComponent<SpriteRenderer>();
        // основной спрайт — любой другой SpriteRenderer (например, на корне или ребёнке)
        if (!bodySR)
        {
            var all = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in all)
            {
                if (r == null) continue;
                if (shadowSR != null && r == shadowSR) continue;
                bodySR = r; break;
            }
        }

        ApplySorting(); // сразу правильные порядки
    }

    void ApplySorting()
    {
        if (bodySR)
        {
            bodySR.sortingLayerName = sortingLayerName;
            bodySR.sortingOrder     = bodyOrder;
            bodySR.enabled = true;
        }
        if (shadowSR)
        {
            shadowSR.sortingLayerName = sortingLayerName;
            shadowSR.sortingOrder     = shadowOrder; // ниже!
            shadowSR.enabled = true;
            // опционально чуть прозрачнее тени:
            var c = shadowSR.color; c.a = Mathf.Clamp01(c.a); shadowSR.color = c;
        }
    }

    public void Appear()
    {
        if (isAppearing) return;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (!enabled) enabled = true;

        // старт справа за экраном
        Vector3 start = GetRightOffscreenPos(targetPoint ? targetPoint.position.y : transform.position.y);
        start.z = fixedZ;
        transform.position = start;

        ApplySorting(); // на всякий случай

        StartCoroutine(AppearRoutine());
    }

    Vector3 GetRightOffscreenPos(float y)
    {
        var cam = Camera.main;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        float right = cam.transform.position.x + halfW;
        return new Vector3(right + offscreenMargin, y, fixedZ);
    }

    IEnumerator AppearRoutine()
    {
        isAppearing = true;

        Vector3 start = transform.position;
        Vector3 end   = targetPoint ? targetPoint.position : transform.position;
        end.z = fixedZ;

        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / moveDuration);
            transform.position = Vector3.LerpUnclamped(start, end, ease.Evaluate(k));
            yield return null;
        }

        transform.position = end;
        isAppearing = false;

        // смена спрайта в конце
        if (finalSprite && bodySR)
        {
            bodySR.sprite = finalSprite;
            bodySR.flipX  = flipX;
        }

        ApplySorting(); // финальная страховка
    }
}
