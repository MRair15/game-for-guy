using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Game/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [Serializable]
    public class Line
    {
        [TextArea(2,4)] public string text;
        [Tooltip("Если > 0 — перекроет глобальную скорость печати")]
        public float overrideCharsPerSecond = 0f;
    }

    [Header("Lines")]
    public List<Line> lines = new List<Line>();

    [Header("Question (optional)")]
    public bool askQuestionAtEnd = false;
    [TextArea(2,4)] public string questionText = "Ваш ответ?";
    public string inputPlaceholder = "Введите ответ...";
}
