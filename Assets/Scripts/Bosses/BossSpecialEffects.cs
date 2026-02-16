using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[DisallowMultipleComponent]
public class BossSpecialEffects : MonoBehaviour
{
    private const string PoisonEffectId = "poison";

    [Header("Poison (Zombie Boss)")]
    [SerializeField] private bool poisonEnabled;
    [SerializeField, Range(0f, 1f)] private float poisonChance;
    [SerializeField] private float poisonDuration;
    [SerializeField] private float poisonDps;

    [Header("Root (Dog Boss)")]
    [SerializeField] private bool rootEnabled;
    [SerializeField, Range(0f, 1f)] private float rootChance;
    [SerializeField] private float rootDuration;
    [SerializeField] private float rootPerTargetCooldown;

    [Header("Shared")]
    [SerializeField] private float tickInterval = 1f;

    private readonly System.Collections.Generic.Dictionary<int, float> nextRootTimeByTarget = new();
    private BossEnemyController bossController;

    private void Awake()
    {
        bossController = GetComponent<BossEnemyController>();
    }

    public void ConfigurePoison(float chance, float duration, float dps, float tickInterval)
    {
        poisonEnabled = chance > 0f && dps > 0f && duration > 0f;
        poisonChance = Mathf.Clamp01(chance);
        poisonDuration = Mathf.Max(0.1f, duration);
        poisonDps = Mathf.Max(0f, dps);
        this.tickInterval = Mathf.Max(0.1f, tickInterval);
    }

    public void ConfigureRoot(float chance, float duration, float perTargetCooldown)
    {
        rootEnabled = chance > 0f && duration > 0f;
        rootChance = Mathf.Clamp01(chance);
        rootDuration = Mathf.Max(0.1f, duration);
        rootPerTargetCooldown = Mathf.Max(0f, perTargetCooldown);
    }

    public void ClearAllEffects()
    {
        poisonEnabled = false;
        rootEnabled = false;
        poisonChance = 0f;
        poisonDuration = 0f;
        poisonDps = 0f;
        rootChance = 0f;
        rootDuration = 0f;
        rootPerTargetCooldown = 0f;
        nextRootTimeByTarget.Clear();
    }

    public void ApplyOnHit(Combatant target)
    {
        if (target == null || target.IsDead)
            return;

        if (poisonEnabled && poisonChance > 0f && poisonDps > 0f && Random.value <= poisonChance)
        {
            if (bossController != null && bossController.TryTelegraphPoison(target, poisonDuration, poisonDps, tickInterval))
            {
                // Telegraph routine will apply poison on its own.
            }
            else
            {
                ApplyDot(target, PoisonEffectId, poisonDuration, poisonDps, tickInterval);
            }
        }

        if (rootEnabled)
            TryApplyRoot(target);
    }

    private void TryApplyRoot(Combatant target)
    {
        if (target.GetComponent<PlayerProgressionController>() == null)
            return;

        int key = target.gameObject.GetInstanceID();
        if (nextRootTimeByTarget.TryGetValue(key, out float nextAt) && Time.time < nextAt)
            return;

        if (Random.value > rootChance)
            return;

        bool queuedTelegraph = bossController != null && bossController.TryTelegraphRoot(target, rootDuration);
        if (queuedTelegraph)
        {
            nextRootTimeByTarget[key] = Time.time + rootPerTargetCooldown;
            return;
        }

        ForceApplyRoot(target, rootDuration);
        nextRootTimeByTarget[key] = Time.time + rootPerTargetCooldown;
    }

    public void ForceApplyPoison(Combatant target, float duration, float dps, float interval)
    {
        ApplyDot(target, PoisonEffectId, duration, dps, interval);
    }

    public void ForceApplyRoot(Combatant target, float duration)
    {
        if (target == null || target.IsDead)
            return;

        var root = target.GetComponent<BossRootDebuff>();
        if (root == null)
            root = target.gameObject.AddComponent<BossRootDebuff>();

        root.Apply(duration);
    }

    private void ApplyDot(Combatant target, string effectId, float duration, float dps, float intervalOverride)
    {
        if (duration <= 0f || dps <= 0f)
            return;

        BossDamageOverTimeDebuff debuff = null;
        var existingDebuffs = target.GetComponents<BossDamageOverTimeDebuff>();

        for (int i = 0; i < existingDebuffs.Length; i++)
        {
            if (existingDebuffs[i] != null && existingDebuffs[i].EffectId == effectId)
            {
                debuff = existingDebuffs[i];
                break;
            }
        }

        if (debuff == null)
            debuff = target.gameObject.AddComponent<BossDamageOverTimeDebuff>();

        debuff.Apply(effectId, duration, dps, Mathf.Max(0.1f, intervalOverride));
    }
}
