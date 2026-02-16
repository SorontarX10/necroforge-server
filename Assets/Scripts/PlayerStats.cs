using UnityEngine;
using System;

public class PlayerStats : MonoBehaviour
{
    [Header("Core")]
    [Range(0.1f, 10f)]
    public float difficulty = 1f;

    public int playerLevel = 1;

    [Header("Runtime modifiers")]
    public float difficultyModifier = 1f;

    public event Action OnStatsChanged;

    public float EffectiveDifficulty =>
        difficulty * difficultyModifier;

    public void SetDifficulty(float value)
    {
        difficulty = Mathf.Max(0.1f, value);
        OnStatsChanged?.Invoke();
    }

    public void AddDifficulty(float delta)
    {
        difficulty = Mathf.Max(0.1f, difficulty + delta);
        OnStatsChanged?.Invoke();
    }
}
