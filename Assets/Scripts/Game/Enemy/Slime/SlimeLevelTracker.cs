using UnityEngine;
using System.Collections.Generic;

/// Считает всех слаймов на уровне и убитых. Когда убиты все — вызывает мага.
public class SlimeLevelTracker : MonoBehaviour
{
    [Header("Who is a slime?")]
    [Tooltip("Ищем слаймов по компоненту SlimeFollow")]
    public bool findByComponent = true;
    [Tooltip("Если findByComponent=false — ищем по тегу")]
    public string slimeTag = "Enemy";

    [Header("Action")]
    public WizardAppear wizard;      // перетащи сюда объект мага
    public bool appearOnce = true;   // чтобы не вызывался повторно

    public int TotalSlimes { get; private set; }
    public int KilledSlimes { get; private set; }

    readonly List<Damageable> _tracked = new();

    void Start()
    {
        // Находим всех слаймов в сцене
        if (findByComponent)
        {
            var followers = FindObjectsOfType<SlimeFollow>(includeInactive: false);
            foreach (var f in followers)
                TryTrack(f.GetComponent<Damageable>());
        }
        else
        {
            var all = GameObject.FindGameObjectsWithTag(slimeTag);
            foreach (var go in all)
                TryTrack(go.GetComponent<Damageable>());
        }

        TotalSlimes = _tracked.Count;
        KilledSlimes = 0;

        // На тот случай, если слаймов нет — сразу вызвать мага.
        TryFinish();
    }

    void OnDestroy()
    {
        // отписываемся
        foreach (var d in _tracked)
            if (d != null) d.OnDeath -= OnSlimeDeath;
    }

    void TryTrack(Damageable d)
    {
        if (d == null) return;
        if (_tracked.Contains(d)) return;

        _tracked.Add(d);
        d.OnDeath += OnSlimeDeath;   // событие уже есть в твоём Damageable
    }

    void OnSlimeDeath()
    {
        KilledSlimes++;
        TryFinish();
    }

    void TryFinish()
    {
        if (TotalSlimes <= 0) { SummonWizard(); return; }
        if (KilledSlimes >= TotalSlimes) SummonWizard();
    }

    void SummonWizard()
{
    if (wizard == null) return;
    if (appearOnce && _alreadySummoned) return;

    if (!wizard.gameObject.activeSelf) wizard.gameObject.SetActive(true); // <—
    wizard.Appear();

    _alreadySummoned = true;
}


    bool _alreadySummoned = false;

    // --- API на будущее: если слаймы спавнятся динамически ---
    public void RegisterSlime(Damageable d)
    {
        TryTrack(d);
        TotalSlimes = _tracked.Count;
    }
}
