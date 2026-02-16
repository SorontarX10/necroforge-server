using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Stats;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Crimson Wave Sigil",
    fileName = "Relic_CrimsonWaveSigil"
)]
public class CrimsonWaveSigil : RelicEffect
{
    [Header("Wave Timing")]
    public float cooldown = 6f;

    [Header("Wave Shape")]
    public float range = 14f;
    public float radius = 1.25f;

    [Header("Damage")]
    [Tooltip("Base damage multiplier of player's damage (e.g. 0.8 = 80% of damage)")]
    public float damageMultiplier = 0.8f;

    [Tooltip("+ multiplier per stack (e.g. 0.15 => +15% of player damage per stack)")]
    public float extraMultiplierPerStack = 0.15f;

    [Header("Targeting")]
    public LayerMask enemyMask;

    [Header("Visuals")]
    public GameObject waveVfxPrefab;
    public float vfxLifetime = 0.6f;

    [Header("Audio")]
    public AudioClip waveSound;
    [Range(0f, 1f)]
    public float sfxVolume = 1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private CrimsonWaveRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        CrimsonWaveRuntime rt = player.GetComponent<CrimsonWaveRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CrimsonWaveRuntime>();
        return rt;
    }
}

public class CrimsonWaveRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerProgressionController prog;
    private RuntimeStats stats;
    private WeaponController weapon;
    private PlayerRelicController relics;

    private CrimsonWaveSigil cfg;
    private int stacks;
    private float nextFire;

    private readonly HashSet<Combatant> hitCombatants = new();
    private readonly HashSet<Transform> hitRoots = new();

    [Header("Audio")]
    public AudioSource audioSource;

    private void Awake()
    {
        prog = GetComponent<PlayerProgressionController>();
        if (prog != null)
            stats = prog.stats;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        weapon = GetComponentInChildren<WeaponController>(true);
        relics = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public void Configure(CrimsonWaveSigil config, int newStacks)
    {
        cfg = config;
        stacks = Mathf.Max(1, newStacks);
        if (nextFire <= 0f)
            nextFire = Time.time + 1f;

        EnemyQueryService.ConfigureOwnerBudget(this, 64);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && prog != null && stats != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now < nextFire)
            return;

        nextFire = now + Mathf.Max(0.2f, cfg.cooldown);
        FireWave();
    }

    private void FireWave()
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Zombie");

        Vector3 start = transform.position + Vector3.up * 1.2f;
        Vector3 dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;
        dir.Normalize();
        Vector3 end = start + dir * cfg.range;

        SpawnWaveVfx(start, dir, cfg.range);

        Collider[] hits = EnemyQueryService.OverlapCapsule(start, end, cfg.radius, mask, QueryTriggerInteraction.Ignore, this);
        int hitCount = EnemyQueryService.GetLastHitCount(this);
        if (hitCount <= 0)
            return;

        float mult = cfg.damageMultiplier + cfg.extraMultiplierPerStack * (stacks - 1);
        float baseDamage = stats != null ? stats.damage : 1f;

        if (weapon != null)
            baseDamage = weapon.GetDamageMultiplier();
        else if (relics != null)
            baseDamage *= relics.GetDamageMultiplier();

        float dmg = Mathf.Max(1f, baseDamage * mult);

        hitCombatants.Clear();
        hitRoots.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            Combatant combatant = EnemyQueryService.GetCombatant(col);
            if (combatant != null)
            {
                if (hitCombatants.Add(combatant))
                    RelicDamageText.Deal(combatant, dmg, transform, cfg);
                continue;
            }

            Transform root = col.transform.root;
            if (root != null && hitRoots.Add(root))
                col.SendMessage("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
        }
    }

    private void SpawnWaveVfx(Vector3 start, Vector3 dir, float range)
    {
        if (cfg == null || cfg.waveVfxPrefab == null)
            return;

        Transform prefabTransform = cfg.waveVfxPrefab.transform;
        Quaternion baseRot = Quaternion.LookRotation(dir, Vector3.up);
        Quaternion rot = baseRot * prefabTransform.localRotation;
        GameObject vfx = RelicVfxTickSystem.Rent(cfg.waveVfxPrefab, start, rot);
        if (vfx == null)
            return;

        vfx.transform.localScale = new Vector3(cfg.radius * 4f, cfg.radius * 4f, range * 2f);
        AlignVfxStart(vfx.transform, start, dir);

        float duration = cfg.vfxLifetime > 0f ? cfg.vfxLifetime : 0.6f;
        Vector3 alignedStart = vfx.transform.position;

        if (audioSource != null && cfg.waveSound != null)
            audioSource.PlayOneShot(cfg.waveSound, GetSfxVolume(cfg.sfxVolume));

        CrimsonWaveVfxMotion motion = vfx.GetComponent<CrimsonWaveVfxMotion>();
        if (motion == null)
            motion = vfx.AddComponent<CrimsonWaveVfxMotion>();
        motion.Play(alignedStart, dir, range, duration);
    }

    private static void AlignVfxStart(Transform vfx, Vector3 start, Vector3 dir)
    {
        Renderer[] renderers = vfx.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        float min = float.PositiveInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            Bounds b = r.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;

            for (int xi = -1; xi <= 1; xi += 2)
            for (int yi = -1; yi <= 1; yi += 2)
            for (int zi = -1; zi <= 1; zi += 2)
            {
                Vector3 corner = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
                float d = Vector3.Dot(dir, corner);
                if (d < min)
                    min = d;
            }
        }

        if (float.IsPositiveInfinity(min))
            return;

        float target = Vector3.Dot(dir, start);
        float delta = target - min;
        if (Mathf.Abs(delta) > 0.0001f)
            vfx.position += dir * delta;
    }

    private float GetSfxVolume(float baseVolume)
    {
        if (audioSource != null && audioSource.GetComponent<SFXAutoVolume>() != null)
            return baseVolume;

        if (SFXSettings.Instance != null)
            return SFXSettings.Instance.GetVolume(baseVolume);

        return baseVolume * GameSettings.SfxVolume * GameSettings.MasterVolume;
    }
}

public sealed class CrimsonWaveVfxMotion : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Vector3 start;
    private Vector3 end;
    private float startedAt;
    private float duration;
    private bool playing;

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        playing = false;
    }

    public void Play(Vector3 start, Vector3 dir, float range, float duration)
    {
        this.start = start;
        end = start + dir * Mathf.Max(0f, range);
        startedAt = Time.time;
        this.duration = Mathf.Max(0.001f, duration);
        playing = true;
        transform.position = start;
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && playing;

    public float BatchedUpdateInterval => 0.016f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (!playing)
            return;

        float elapsed = now - startedAt;
        if (elapsed >= duration)
        {
            transform.position = end;
            playing = false;
            RelicVfxTickSystem.Return(gameObject);
            return;
        }

        float t = Mathf.Clamp01(elapsed / duration);
        transform.position = Vector3.Lerp(start, end, t);
    }
}
