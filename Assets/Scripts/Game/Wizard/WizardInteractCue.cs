using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// Интеракция с магом: подсказка E, серый оттенок, запуск диалога.
/// В конце вопрос (по галочке), проверка ответа, снятие HP у игрока при ошибке,
/// реплика мага «правильно/неправильно», конец при нуле HP.
[DisallowMultipleComponent]
public class WizardInteract : MonoBehaviour
{
    [Header("Links")]
    public Transform player;                 // если пусто — найдём по PlayerMovement
    public DialogueUI dialogue;              // перетащи объект с DialogueUI
    public GameObject promptRoot;            // ваш объект подсказки "E"
    public KeyCode interactKey = KeyCode.E;

    [Header("Dialogue (editable here)")]
    [TextArea(2,4)] public List<string> lines = new()
    {
        "Привет, путник!",
        "Я задам тебе вопрос."
    };

    [Header("Question at the end")]
    public bool askQuestionAtEnd = false;    // ← галочка
    [TextArea(2,4)] public string questionText = "Сколько будет 2+2?";
    [Tooltip("Любой из вариантов считается правильным")]
    public List<string> correctAnswers = new() { "4", "четыре" };
    public bool caseInsensitive = true;

    [Header("Feedback messages")]
    [TextArea(1,2)] public string correctMsg = "Правильно!";
    [TextArea(1,2)] public string wrongMsg   = "Неправильно! Попробуй ещё раз.";

    [Header("On wrong answer -> damage player")]
    public int damageOnWrong = 1;            // сколько HP снять при ошибке

    [Header("Proximity / Visual")]
    public float radius = 1.6f;
    public Vector3 promptOffset = new Vector3(0f, 1.1f, 0f);
    public string shadowChildName = "Shadow";
    public Color nearTint = new Color(0.82f, 0.82f, 0.82f, 1f);
    public float tintLerp = 12f;

    [Header("Events")]
    public UnityEvent OnPlayerHpZero;        // ← сюда можно повесить Game Over / Restart
    public UnityEvent OnAnswerCorrect;       // реакция на правильный ответ
    public UnityEvent OnAnswerWrong;         // реакция на неверный (например, вспышка)

    // runtime
    Transform _tr;
    readonly List<SpriteRenderer> _body = new();
    readonly List<Color> _base = new();
    bool _near;
    bool _cooldown;

    PlayerMovement _playerPM;

    void Awake()
    {
        _tr = transform;

        if (!player)
        {
            var pm = FindObjectOfType<PlayerMovement>();
            if (pm) player = pm.transform;
        }
        if (player) _playerPM = player.GetComponent<PlayerMovement>();

        if (!dialogue) dialogue = FindObjectOfType<DialogueUI>();
        if (promptRoot) promptRoot.SetActive(false);

        foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!r) continue;
            if (!string.IsNullOrEmpty(shadowChildName) && r.transform.name == shadowChildName) continue;
            _body.Add(r); _base.Add(r.color);
        }
    }

    void OnEnable()
    {
        if (dialogue)
        {
            dialogue.OnQuestionAnswered -= HandleQuestionAnswered;
            dialogue.OnQuestionAnswered += HandleQuestionAnswered;
        }
    }
    void OnDisable()
    {
        if (dialogue) dialogue.OnQuestionAnswered -= HandleQuestionAnswered;

        for (int i = 0; i < _body.Count; i++)
            if (_body[i]) _body[i].color = _base[i];

        if (promptRoot) promptRoot.SetActive(false);
        _near = false;
    }

    void Update()
    {
        if (!player) return;

        bool nowNear = Vector2.Distance(_tr.position, player.position) <= radius;

        if (nowNear != _near)
        {
            _near = nowNear;
            if (promptRoot) promptRoot.SetActive(_near && !(dialogue && dialogue.IsOpen));
        }

        for (int i = 0; i < _body.Count; i++)
        {
            var r = _body[i];
            if (!r) continue;
            var target = _near ? nearTint : _base[i];
            r.color = Color.Lerp(r.color, target, Time.deltaTime * tintLerp);
        }

        if (_near && Input.GetKeyDown(interactKey) && !_cooldown && dialogue && !dialogue.IsOpen)
        {
            _cooldown = true;
            if (promptRoot) promptRoot.SetActive(false);

            var list = new List<string>(lines);
            if (askQuestionAtEnd)
                list.Add(questionText);

            dialogue.Open(
                list.ToArray(),
                askQuestionAtEnd,
                questionText,
                correctAnswers.ToArray(),
                caseInsensitive
            );

            StartCoroutine(ClearCooldownNextFrame());
        }

        if (_near && promptRoot && dialogue && !dialogue.IsOpen && !promptRoot.activeSelf)
            promptRoot.SetActive(true);
    }

    System.Collections.IEnumerator ClearCooldownNextFrame()
    {
        yield return null;
        _cooldown = false;
    }

    void HandleQuestionAnswered(string playerAnswer, bool isCorrect)
    {
        if (!dialogue) return;

        if (isCorrect)
        {
            // фидбэк «правильно» и закрыть после клика
            dialogue.ShowOneLiner(string.IsNullOrEmpty(correctMsg) ? "Правильно!" : correctMsg);
            OnAnswerCorrect?.Invoke();
            // после закрытия можно продолжить квест/сюжет (подписывайся на DialogueUI.OnClosed при желании)
            return;
        }

        // Неверный ответ: снимаем HP у игрока (без нокбэка, передаем Vector2.zero)
        if (_playerPM && damageOnWrong > 0)
        {
            _playerPM.TakeDamage(damageOnWrong, Vector2.zero);
            OnAnswerWrong?.Invoke();

            if (_playerPM.CurrentHP <= 0)
            {
                // игрок умер — финальное сообщение и событие конца
                dialogue.ShowOneLiner("Это конец...");
                // когда окно закроется, триггерим внешний обработчик
                void Once() { dialogue.OnClosed -= Once; OnPlayerHpZero?.Invoke(); }
                dialogue.OnClosed += Once;
                return;
            }
        }

        // Жив — скажем «неправильно», а затем снова зададим вопрос
        dialogue.ShowOneLiner(string.IsNullOrEmpty(wrongMsg) ? "Неправильно!" : wrongMsg);

        // После закрытия фидбэка — заново открыть только вопрос
        void ReopenQuestionOnce()
        {
            dialogue.OnClosed -= ReopenQuestionOnce;
            dialogue.Open(new string[] { questionText }, true, questionText, correctAnswers.ToArray(), caseInsensitive);
        }
        dialogue.OnClosed += ReopenQuestionOnce;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
