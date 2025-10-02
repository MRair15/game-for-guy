using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;

/// Окно диалога (UI Text + RectTransform) + вопрос (InputField):
/// - Слайд снизу/вверх (по желанию)
/// - Печать (typewriter), skip/next по E или ЛКМ, закрытие по Esc
/// - Блокирует PlayerMovement, отключает меч/объекты на время диалога
/// - В конце может показать InputField + кнопку "Ответить"
[DisallowMultipleComponent]
public class DialogueUI : MonoBehaviour
{
    [Header("UI (existing)")]
    public RectTransform panel;     // рамка/плашка
    public Text textLabel;          // обычный UI Text внутри плашки

    [Header("Question UI (optional)")]
    public GameObject answerRoot;   // контейнер с InputField и кнопкой (Inactive по умолчанию)
    public InputField answerInput;  // поле ввода ответа
    public Button submitButton;     // кнопка "Ответить"

    [Header("Player lock")]
    public PlayerMovement player;   // скрипт движения
    public Rigidbody2D playerRb;    // Rigidbody игрока (обнулить скорость)
    public Behaviour meleeToDisable;      // например PlayerMeleeAttack
    public GameObject[] objectsToHide;    // визуал меча и т.п.

    [Header("Animation")]
    public bool slideEnabled = true;
    public float slideDuration = 0.25f;
    public AnimationCurve slideEase = null;
    public float bottomOffset = 24f;

    [Header("Typing")]
    public float charsPerSecond = 40f;

    [Header("Keys/Mouse")]
    public KeyCode keyAdvance = KeyCode.E;
    public KeyCode keyClose   = KeyCode.Escape;

    // ---- runtime ----
    public bool IsOpen { get; private set; }

    CanvasGroup _cg;
    Vector2 _targetPos;

    string[] _lines;
    int _i;
    Coroutine _typeCo, _slideCo;
    bool _typing;
    bool _acceptInput;

    // question state
    bool _askQuestion;
    string[] _answers;
    bool _caseInsensitive;

    public event Action<string, bool> OnQuestionAnswered; // (ответ, корректность)
    public event Action OnClosed;                          // вызывается при закрытии окна

    void Awake()
    {
        if (slideEase == null) slideEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        if (!panel) { Debug.LogError("[DialogueUI] Panel not assigned"); enabled = false; return; }
        if (!player) player = FindObjectOfType<PlayerMovement>();
        if (!playerRb && player) playerRb = player.GetComponent<Rigidbody2D>();

        EnsureParentsActive(panel.transform);

        var cg = panel.GetComponent<CanvasGroup>();
        _cg = cg ? cg : panel.gameObject.AddComponent<CanvasGroup>();

        _targetPos = panel.anchoredPosition;

        _cg.alpha = 0f;
        _cg.blocksRaycasts = false;
        _cg.interactable = false;

        panel.gameObject.SetActive(true);
        if (answerRoot) answerRoot.SetActive(false);
        if (textLabel) textLabel.text = "";
        if (submitButton) submitButton.onClick.AddListener(SubmitAnswer);
    }

    void Update()
{
    if (!IsOpen) return;

    // Закрыть окно принудительно
    if (Input.GetKeyDown(keyClose))
    {
        // Если открыт вопрос — считаем отказом / неверным ответом
        if (answerRoot && answerRoot.activeSelf)
            OnQuestionAnswered?.Invoke(answerInput ? answerInput.text : "", false);
        Close();
        return;
    }

    // --- РЕЖИМ ВОПРОСА ---
    if (answerRoot && answerRoot.activeSelf)
    {
        // ВАЖНО: клики мышью теперь НЕ сабмитят ответ — можно спокойно кликнуть в InputField
        // Сабмитим только Enter/Return ИЛИ click по кнопке (кнопка уже подписана в Awake).
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SubmitAnswer();
        }
        return; // пока открыт вопрос — больше ничего не обрабатываем
    }

    // --- ОБЫЧНЫЕ РЕПЛИКИ ---
    // Разрешаем листать по E или ЛКМ
    if (!_acceptInput) return;

    bool advancePressed = Input.GetKeyDown(keyAdvance) || Input.GetMouseButtonDown(0);
    if (advancePressed)
    {
        if (_typing) SkipType();
        else NextOrClose();
    }
}


    /// Открыть диалог:
    /// lines — строки (если askQuestion=true, последняя из них — текст вопроса)
    /// answers — список правильных ответов (любой из вариантов)
    public void Open(string[] lines, bool askQuestion, string questionText, string[] answers, bool caseInsensitive)
    {
        if (lines == null || lines.Length == 0) return;

        _lines = lines;
        _i = 0;
        _acceptInput = false;

        _askQuestion = askQuestion;
        _answers = answers ?? Array.Empty<string>();
        _caseInsensitive = caseInsensitive;

        if (IsOpen)
        {
            ShowCurrentLine();
            return;
        }

        // блок управления и отключение оружия
        if (player)
        {
            if (playerRb) playerRb.velocity = Vector2.zero;
            player.enabled = false;
        }
        if (meleeToDisable) meleeToDisable.enabled = false;
        if (objectsToHide != null)
            foreach (var go in objectsToHide) if (go) go.SetActive(false);

        IsOpen = true;

        // показать панель
        _cg.alpha = 1f;
        _cg.blocksRaycasts = true;
        _cg.interactable = true;
        panel.SetAsLastSibling();

        if (slideEnabled) StartSlideInThen(ShowCurrentLine);
        else ShowCurrentLine();
    }

    /// Показать однострочный фидбэк (например, «Правильно!» / «Неверно!») без закрытия сразу.
    /// После показа, по E/ЛКМ окно закроется (т.к. строка одна и _askQuestion=false).
    public void ShowOneLiner(string text)
    {
        _lines = new string[] { text ?? "" };
        _i = 0;
        _askQuestion = false;
        _acceptInput = false;
        if (answerRoot) answerRoot.SetActive(false);
        ShowCurrentLine();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        StopType();

        // вернуть управление и меч
        if (player) player.enabled = true;
        if (meleeToDisable) meleeToDisable.enabled = true;
        if (objectsToHide != null)
            foreach (var go in objectsToHide) if (go) go.SetActive(true);

        if (answerRoot) answerRoot.SetActive(false);

        if (slideEnabled) StartSlideOutThen(() => { HidePanel(); OnClosed?.Invoke(); });
        else { HidePanel(); OnClosed?.Invoke(); }
    }

    // ---- internals ----
    void ShowCurrentLine()
    {
        if (_i >= _lines.Length)
        {
            // если нужен вопрос — вместо закрытия показываем input
            if (_askQuestion)
            {
                ShowQuestionUI();
            }
            else
            {
                Close();
            }
            return;
        }

        string line = _lines[_i] ?? "";
        StartTypewriter(line, Mathf.Max(1f, charsPerSecond));
    }

    void NextOrClose()
    {
        if (_typing) return;
        _i++;
        ShowCurrentLine();
    }

    void StartTypewriter(string full, float cps)
    {
        StopType();
        _typeCo = StartCoroutine(TypeRoutine(full, cps));
    }

    System.Collections.IEnumerator TypeRoutine(string full, float cps)
    {
        _typing = true;
        if (textLabel) textLabel.text = "";

        float delay = 1f / cps;
        for (int k = 1; k <= full.Length; k++)
        {
            if (textLabel) textLabel.text = full.Substring(0, k);
            if (k == 1) { yield return null; _acceptInput = true; } // защита от раннего клика
            yield return new WaitForSecondsRealtime(delay);
        }
        _typing = false;
    }

    void SkipType()
    {
        if (!_typing) return;
        _typing = false;
        if (_typeCo != null) StopCoroutine(_typeCo);
        if (textLabel) textLabel.text = _lines[_i];
    }

    void StopType()
    {
        _typing = false;
        if (_typeCo != null) StopCoroutine(_typeCo);
    }

    void ShowQuestionUI()
    {
        if (textLabel) textLabel.text = _lines[_lines.Length - 1]; // последняя строка — вопрос
        if (!answerRoot) { Close(); return; }

        answerRoot.SetActive(true);
        if (answerInput)
        {
            answerInput.text = "";
            StartCoroutine(FocusInputNextFrame());
        }
    }

    System.Collections.IEnumerator FocusInputNextFrame()
    {
        yield return null;
        if (answerInput) answerInput.Select();
        yield return null;
        if (answerInput) answerInput.ActivateInputField();
    }

    void SubmitAnswer()
    {
        if (!answerRoot || !answerRoot.activeSelf) return;

        string ans = answerInput ? (answerInput.text ?? "") : "";
        bool ok = CheckAnswer(ans, _answers, _caseInsensitive);

        // Сообщаем наружу (маг решит: снять HP, что показать, что делать при конце)
        OnQuestionAnswered?.Invoke(ans, ok);
    }

    static bool CheckAnswer(string s, string[] answers, bool ci)
    {
        if (answers == null || answers.Length == 0) return false;
        if (s == null) s = "";
        string t = ci ? s.Trim().ToLowerInvariant() : s.Trim();

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == null) continue;
            string a = ci ? answers[i].Trim().ToLowerInvariant() : answers[i].Trim();
            if (t == a) return true;
        }
        return false;
    }

    void HidePanel()
    {
        _cg.alpha = 0f;
        _cg.blocksRaycasts = false;
        _cg.interactable = false;
        _acceptInput = false;
        if (textLabel) textLabel.text = "";
    }

    // slide helpers
    void StartSlideInThen(System.Action after)
    {
        if (_slideCo != null) StopCoroutine(_slideCo);
        _slideCo = StartCoroutine(SlideInThen(after));
    }
    void StartSlideOutThen(System.Action after)
    {
        if (_slideCo != null) StopCoroutine(_slideCo);
        _slideCo = StartCoroutine(SlideOutThen(after));
    }

    System.Collections.IEnumerator SlideInThen(System.Action after)
    {
        float h = panel.rect.height;
        if (h <= 1f) { LayoutRebuilder.ForceRebuildLayoutImmediate(panel); h = panel.rect.height; }
        Vector2 start = _targetPos + Vector2.down * (h + bottomOffset);
        panel.anchoredPosition = start;

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.unscaledDeltaTime;
            float e = slideEase.Evaluate(Mathf.Clamp01(t / slideDuration));
            panel.anchoredPosition = Vector2.LerpUnclamped(start, _targetPos, e);
            yield return null;
        }
        panel.anchoredPosition = _targetPos;
        after?.Invoke();
    }

    System.Collections.IEnumerator SlideOutThen(System.Action after)
    {
        float h = panel.rect.height;
        Vector2 start = panel.anchoredPosition;
        Vector2 end = _targetPos + Vector2.down * (h + bottomOffset);

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.unscaledDeltaTime;
            float e = slideEase.Evaluate(Mathf.Clamp01(t / slideDuration));
            panel.anchoredPosition = Vector2.LerpUnclamped(start, end, e);
            yield return null;
        }
        panel.anchoredPosition = end;
        after?.Invoke();
    }

    static void EnsureParentsActive(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
    }
}
