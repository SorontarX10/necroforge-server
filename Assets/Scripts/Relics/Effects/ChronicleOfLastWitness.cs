using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Chronicle Of Last Witness",
    fileName = "Relic_ChronicleOfLastWitness"
)]
public class ChronicleOfLastWitness : RelicEffect, IDamageModifier, ISpeedModifier, IDamageReductionModifier
{
    public enum VerseType
    {
        Offense = 0,
        Defense = 1,
        Speed = 2
    }

    [Header("Verses")]
    public float verseDuration = 20f;
    public float combatDuration = 8f;
    public float offensePerVerse = 0.08f;
    public float defensePerVerse = 0.08f;
    public float speedPerVerse = 0.08f;
    [Min(1)] public int baseMaxVerses = 4;
    [Min(0)] public int maxVersesPerStack = 1;
    public float bonusAmplificationPerStack = 0.2f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ChronicleOfLastWitnessRuntime>() : null;
        if (rt == null)
            return 1f;

        float amp = 1f + bonusAmplificationPerStack * Mathf.Max(0, stacks - 1);
        return 1f + offensePerVerse * amp * rt.CountVerses(VerseType.Offense);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ChronicleOfLastWitnessRuntime>() : null;
        if (rt == null)
            return 0f;

        float amp = 1f + bonusAmplificationPerStack * Mathf.Max(0, stacks - 1);
        return speedPerVerse * amp * rt.CountVerses(VerseType.Speed);
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ChronicleOfLastWitnessRuntime>() : null;
        if (rt == null)
            return 0f;

        float amp = 1f + bonusAmplificationPerStack * Mathf.Max(0, stacks - 1);
        return defensePerVerse * amp * rt.CountVerses(VerseType.Defense);
    }

    private ChronicleOfLastWitnessRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ChronicleOfLastWitnessRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ChronicleOfLastWitnessRuntime>();

        return rt;
    }
}

public class ChronicleOfLastWitnessRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private struct Verse
    {
        public ChronicleOfLastWitness.VerseType type;
        public float expiresAt;
    }

    private readonly List<Verse> verses = new();

    private PlayerRelicController player;
    private ChronicleOfLastWitness cfg;
    private int stacks;
    private bool subscribed;
    private float combatEndsAt;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        TrySubscribe();
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        TryUnsubscribe();
    }

    public void Configure(ChronicleOfLastWitness config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.2f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupExpiredVerses(now);
    }

    public int CountVerses(ChronicleOfLastWitness.VerseType type)
    {
        int count = 0;
        for (int i = 0; i < verses.Count; i++)
        {
            if (verses[i].type == type)
                count++;
        }

        return count;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        player.OnMeleeKill += OnMeleeKill;
        player.OnDamageTaken += OnDamageTaken;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        player.OnMeleeKill -= OnMeleeKill;
        player.OnDamageTaken -= OnDamageTaken;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        combatEndsAt = Time.time + Mathf.Max(0.5f, cfg.combatDuration);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        combatEndsAt = Time.time + Mathf.Max(0.5f, cfg.combatDuration);
        AddRandomVerse();
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null)
            return;

        combatEndsAt = Time.time + Mathf.Max(0.5f, cfg.combatDuration);
        if (verses.Count <= 0)
            return;

        int index = Random.Range(0, verses.Count);
        verses.RemoveAt(index);

        float newExpiry = Time.time + Mathf.Max(0.1f, cfg.verseDuration);
        for (int i = 0; i < verses.Count; i++)
        {
            var verse = verses[i];
            verse.expiresAt = newExpiry;
            verses[i] = verse;
        }

        player?.Progression?.NotifyStatsChanged();
    }

    private void AddRandomVerse()
    {
        int maxVerses = Mathf.Max(1, cfg.baseMaxVerses + cfg.maxVersesPerStack * Mathf.Max(0, stacks - 1));
        if (verses.Count >= maxVerses)
            verses.RemoveAt(0);

        var type = (ChronicleOfLastWitness.VerseType)Random.Range(0, 3);
        verses.Add(new Verse
        {
            type = type,
            expiresAt = Time.time + Mathf.Max(0.1f, cfg.verseDuration)
        });

        player?.Progression?.NotifyStatsChanged();
    }

    private void CleanupExpiredVerses(float now)
    {
        if (verses.Count == 0)
            return;

        bool removed = false;
        for (int i = verses.Count - 1; i >= 0; i--)
        {
            if (now >= verses[i].expiresAt)
            {
                verses.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            player?.Progression?.NotifyStatsChanged();
    }
}
