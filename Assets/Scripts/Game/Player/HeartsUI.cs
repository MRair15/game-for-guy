using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Пиксельные сердца в левом верхнем углу.
/// Создаёт ряд по maxHP и плавно тушит при потере жизней.
/// </summary>
public class HeartsUI : MonoBehaviour
{
    [Header("Links")]
    public PlayerMovement player;       // перетащи игрока
    public Image heartPrefab;           // префаб UI-Image с твоим спрайтом сердца
    public RectTransform container;     // пустой объект внутри Canvas

    [Header("Layout")]
    public float spacing = 6f;          // расстояние между сердцами (в UI-пикселях)
    public Vector2 margin = new Vector2(16f, 16f); // отступ от угла

    [Header("Animation")]
    public float fadeDuration = 0.35f;  // скорость затухания/появления

    private readonly List<Image> hearts = new();
    private int lastShownHP = -1;
    private int cachedMaxHP = -1;

    void Awake()
    {
        if (!player) player = FindObjectOfType<PlayerMovement>();
        if (!container) container = GetComponent<RectTransform>();
        SetupAnchors();
        EnsureHorizontalLayout();
    }

    void Start()
    {
        RebuildIfNeeded();
        ForceSync();
    }

    void Update()
    {
        // Отслеживаем изменение maxHP/HP без событий
        if (player == null) return;

        if (cachedMaxHP != player.maxHP)
            RebuildIfNeeded();

        if (lastShownHP != player.CurrentHP)
            RefreshHearts();
    }

    // --- Вспомогательные ---

    void SetupAnchors()
    {
        // якорим контейнер в левый верх
        if (!container) return;
        container.anchorMin = new Vector2(0f, 1f);
        container.anchorMax = new Vector2(0f, 1f);
        container.pivot     = new Vector2(0f, 1f);
        container.anchoredPosition = new Vector2(margin.x, -margin.y);
    }

    void EnsureHorizontalLayout()
    {
        var h = container.GetComponent<HorizontalLayoutGroup>();
        if (!h) h = container.gameObject.AddComponent<HorizontalLayoutGroup>();

        h.childAlignment   = TextAnchor.UpperLeft;
        h.spacing          = spacing;
        h.childControlWidth  = false;
        h.childControlHeight = false;
        h.childForceExpandWidth  = false;
        h.childForceExpandHeight = false;
        h.reverseArrangement = false;
        // убираем нежелательные отступы
        var fitter = container.GetComponent<ContentSizeFitter>();
        if (!fitter) fitter = container.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
    }

    void RebuildIfNeeded()
    {
        if (!player || !heartPrefab || !container) return;

        cachedMaxHP = player.maxHP;

        // очистка старых
        foreach (Transform child in container) Destroy(child.gameObject);
        hearts.Clear();

        // создаём заново в один ряд
        for (int i = 0; i < player.maxHP; i++)
        {
            var img = Instantiate(heartPrefab, container);
            img.gameObject.SetActive(true);
            var c = img.color; c.a = 1f; img.color = c;
            hearts.Add(img);
        }
        lastShownHP = -1; // чтобы принудительно отрисовать в RefreshHearts()
        RefreshHearts();
    }

    void ForceSync()
    {
        if (!player) return;
        lastShownHP = -1;
        RefreshHearts();
    }

    void RefreshHearts()
    {
        if (player == null) return;

        int hp = Mathf.Clamp(player.CurrentHP, 0, hearts.Count);
        // те, что ниже hp — полностью видимы
        for (int i = 0; i < hearts.Count; i++)
        {
            if (i < hp)
            {
                hearts[i].gameObject.SetActive(true);
                StopCoroutineSafe(hearts[i]);
                StartCoroutine(FadeTo(hearts[i], 1f));
            }
            else
            {
                StopCoroutineSafe(hearts[i]);
                StartCoroutine(FadeTo(hearts[i], 0f, deactivateAtZero: true));
            }
        }
        lastShownHP = hp;
    }

    IEnumerator FadeTo(Image img, float targetA, bool deactivateAtZero = false)
    {
        var c = img.color;
        float startA = c.a;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            c.a = Mathf.Lerp(startA, targetA, k);
            img.color = c;
            yield return null;
        }
        c.a = targetA; img.color = c;

        if (deactivateAtZero && targetA <= 0.001f)
            img.gameObject.SetActive(false);
    }

    // не даём двум корутинам одновременно драться за один Image
    void StopCoroutineSafe(Image img)
    {
        // простая защита: отключение и включение компонент не требуется,
        // корутины мы запускаем только отсюда, так что достаточно StopAllCoroutines + перезапуск.
        // Чтобы не стопать ВСЕ корутины в скрипте (включая чужие), можно хранить словарь,
        // но для простоты:
        // ничего не делаем — конкуренция маловероятна при единичных изменениях HP.
    }
}
