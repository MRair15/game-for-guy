using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class DeathScreenController : MonoBehaviour
{
    // ---------- Assign in Inspector ----------
    [Header("Links (panel may be inactive)")]
    [Tooltip("Корень панели смерти (может быть SetActive(false) в начале).")]
    public GameObject panelRoot;
    [Tooltip("CanvasGroup на корне панели (если не указать — добавится автоматически).")]
    public CanvasGroup panelCg;
    public Text subtitleText;   // «Ничего страшного.» — меняется по массиву
    public Text hintText;       // «Нажмите на экран…» — появляется позже

    [Header("Timing")]
    public float delayBeforeShow = 3.0f;
    public float panelFade = 0.6f;
    public float hintDelay = 1.2f;
    public float hintFade = 0.5f;

    [Header("Messages (cycled)")]
    public string[] messages = { "Ничего страшного.", "Бывает и хуже.", "Ещё попытка — ещё опыт.", "Главное — не сдавайся!" };

    [Header("Behaviour")]
    public bool pauseTimeOnShow = true;
    public bool resumeTimeOnHide = true;

    [Header("Events")]
    public UnityEvent OnShown;
    public UnityEvent OnHintShown;
    public UnityEvent OnRestartRequested; // подпишите сюда перезапуск сцены

    // ---------- Runtime ----------
    static DeathScreenController _instance;
    int _msgIndex;
    bool _isShowing;

    void Awake()
    {
        _instance = this;

        if (panelRoot) panelRoot.SetActive(false); // можно держать скрытой
        if (messages == null || messages.Length == 0) messages = new[] { "Ничего страшного." };
        _msgIndex = PlayerPrefs.GetInt("death_msg_index", 0) % messages.Length;
    }

    void Update()
    {
        if (!_isShowing) return;

        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnRestartRequested?.Invoke();
        }
    }

    // ======== PUBLIC API ========

    /// Вызвать показ со стандартной задержкой (безопасно вызывать откуда угодно).
    public static void Show()
    {
        if (_instance == null) { Debug.LogError("[DeathScreen] No instance in scene."); return; }
        CoroutineRunner.Run(_instance.ShowRoutine());
    }

    /// Вызвать показ с переопределением задержки.
    public static void Show(float delayOverride)
    {
        if (_instance == null) { Debug.LogError("[DeathScreen] No instance in scene."); return; }
        _instance.delayBeforeShow = delayOverride;
        CoroutineRunner.Run(_instance.ShowRoutine());
    }

    /// Мгновенно скрыть (перед рестартом и т.п.)
    public static void HideImmediate()
    {
        if (_instance == null) return;
        _instance.InternalHideImmediate();
    }

    // ======== INTERNAL ========

    IEnumerator ShowRoutine()
    {
        // ждём реальным временем (независимо от timeScale)
        float t = 0f;
        while (t < delayBeforeShow) { t += Time.unscaledDeltaTime; yield return null; }

        // активируем панель
        if (panelRoot) panelRoot.SetActive(true);
        if (!panelCg && panelRoot) panelCg = panelRoot.GetComponent<CanvasGroup>();
        if (!panelCg && panelRoot) panelCg = panelRoot.AddComponent<CanvasGroup>();

        // начальные состояния
        panelCg.alpha = 0f;
        panelCg.blocksRaycasts = true;
        panelCg.interactable = true;

        if (subtitleText)
        {
            subtitleText.text = messages[_msgIndex];
            _msgIndex = (_msgIndex + 1) % messages.Length;
            PlayerPrefs.SetInt("death_msg_index", _msgIndex);
        }
        if (hintText) hintText.canvasRenderer.SetAlpha(0f);

        if (pauseTimeOnShow) Time.timeScale = 0f;
        _isShowing = true;

        // фейд панели
        float dur = Mathf.Max(0.01f, panelFade);
        float a = 0f;
        while (a < dur)
        {
            a += Time.unscaledDeltaTime;
            panelCg.alpha = Mathf.Lerp(0f, 1f, a / dur);
            yield return null;
        }
        panelCg.alpha = 1f;
        OnShown?.Invoke();

        // задержка и фейд подсказки
        t = 0f;
        while (t < hintDelay) { t += Time.unscaledDeltaTime; yield return null; }
        if (hintText) hintText.CrossFadeAlpha(1f, Mathf.Max(0.01f, hintFade), true);
        OnHintShown?.Invoke();
    }

    void InternalHideImmediate()
    {
        _isShowing = false;
        if (panelRoot) panelRoot.SetActive(false);
        if (resumeTimeOnHide) Time.timeScale = 1f;
    }

    // Внутренний «раннер» корутин, гарантированно активный
    class CoroutineRunner : MonoBehaviour
    {
        static CoroutineRunner _runner;
        public static void Run(IEnumerator co)
        {
            if (_runner == null)
            {
                var go = new GameObject("DeathScreenRunner");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<CoroutineRunner>();
            }
            _runner.StartCoroutine(co);
        }
    }

    public void ReloadScene()
{
    if (Time.timeScale == 0f) Time.timeScale = 1f; // вернуть время
    var scene = SceneManager.GetActiveScene().name;
    SceneManager.LoadScene(scene);
}
}
