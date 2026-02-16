using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class BossEnemyController : MonoBehaviour
{
    [Header("Presentation Pooling")]
    [SerializeField, Min(1)] private int telegraphPoolCapacity = 10;
    [SerializeField, Min(1)] private int shockwavePoolCapacity = 6;

    [Header("Readability Telemetry")]
    [SerializeField] private bool enableReadabilityTelemetry = true;
    [SerializeField, Range(0.2f, 1f)] private float readabilityTargetDodgeRate = 0.58f;
    [SerializeField, Min(0.05f)] private float readabilityTargetReactionSeconds = 0.28f;

    private readonly Queue<GameObject> telegraphPool = new();
    private readonly Queue<GameObject> shockwavePool = new();

    private readonly Dictionary<int, TelegraphTelemetryEntry> telegraphTelemetry = new();
    private readonly Dictionary<BossArchetype, ReadabilityTelemetryState> readabilityStates = new();
    private readonly List<int> telemetryIdsBuffer = new(16);
    private int nextTelegraphTelemetryId;

    private static readonly int BossPulseId = Shader.PropertyToID("_BossPulse");
    private static readonly int BossGlowId = Shader.PropertyToID("_BossGlow");
    private static readonly int BossArchetypeId = Shader.PropertyToID("_BossArchetype");

    private const string DefaultTelegraphPrefabResourcePath = "BossTelegraphMarker_SG";
    private const string DefaultShockwavePrefabResourcePath = "BossShockwave_SG";
    private const string DefaultEmissiveLutProfileResourcePath = "BossEmissiveLut_Default";

    private GameObject cachedDefaultTelegraphPrefab;
    private GameObject cachedDefaultShockwavePrefab;
    private BossEmissiveLutProfile cachedDefaultEmissiveLutProfile;
    private bool defaultTelegraphPrefabResolved;
    private bool defaultShockwavePrefabResolved;
    private bool defaultEmissiveLutResolved;
    private bool telegraphPrefabMissingWarningShown;
    private bool shockwavePrefabMissingWarningShown;

    private struct TelegraphTelemetryEntry
    {
        public BossArchetype archetype;
        public Vector3 origin;
        public Vector3 playerStart;
        public float radius;
        public float startedAt;
        public float duration;
        public bool reacted;
        public float reactedAt;
    }

    private struct ReadabilityTelemetryState
    {
        public int samples;
        public int dodged;
        public float avgReaction;
        public float avgDuration;
        public float avgMovedDistance;
        public float durationMultiplier;
    }

    private void SetupAuraVisual()
    {
        CleanupAuraVisual();

        if (auraPrefabOverride != null)
            auraVisual = Instantiate(auraPrefabOverride, transform.position, Quaternion.identity, transform);
        else
        {
            auraVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            auraVisual.name = "BossAura";
            auraVisual.transform.SetParent(transform, worldPositionStays: true);
            RemoveAllColliders(auraVisual);
        }

        if (auraVisual == null)
            return;

        auraTransform = auraVisual.transform;
        auraRenderers = auraVisual.GetComponentsInChildren<Renderer>(true);
        auraBaseScale = auraTransform.localScale;
        auraSeed = Random.Range(0f, 100f);
        nextDogAuraGrowlAt = Time.time + Random.Range(dogAuraGrowlIntervalMin, dogAuraGrowlIntervalMax);
        UpdateAuraVisual(forceImmediate: true);
    }

    private void UpdateAuraVisual(bool forceImmediate = false)
    {
        if (auraVisual == null || auraTransform == null)
            return;

        GetAuraStyle(out Color auraColor, out float radius, out float thickness, out float pulseAmp, out float pulseSpeed, out float rotSpeed);
        float pulse01 = 1f;

        if (!forceImmediate)
        {
            float phase = (Time.time + auraSeed) * pulseSpeed;
            float pulse = 1f + Mathf.Sin(phase) * pulseAmp;
            pulse01 = Mathf.Clamp01((pulse - (1f - pulseAmp)) / Mathf.Max(0.001f, pulseAmp * 2f));

            Vector3 pos = transform.position + Vector3.up * auraYOffset;
            if (archetype == BossArchetype.Dog)
            {
                float jitterX = Mathf.PerlinNoise(phase, auraSeed) - 0.5f;
                float jitterZ = Mathf.PerlinNoise(auraSeed, phase) - 0.5f;
                pos.x += jitterX * 0.18f;
                pos.z += jitterZ * 0.18f;
            }

            auraTransform.position = pos;
            auraTransform.rotation = Quaternion.Euler(0f, Time.time * rotSpeed, 0f);

            if (auraPrefabOverride != null)
                auraTransform.localScale = auraBaseScale * Mathf.Lerp(0.9f, 1.2f, pulse);
            else
                auraTransform.localScale = new Vector3(radius * 2f * pulse, thickness, radius * 2f * (archetype == BossArchetype.Quick ? Mathf.Lerp(0.82f, 1.15f, pulse) : pulse));
        }
        else
        {
            auraTransform.position = transform.position + Vector3.up * auraYOffset;
            if (auraPrefabOverride != null)
                auraTransform.localScale = auraBaseScale;
            else
                auraTransform.localScale = new Vector3(radius * 2f, thickness, radius * 2f);
        }

        TintRenderers(
            auraRenderers,
            auraColor,
            auraMaterialOverride,
            pulse01,
            GetArchetypeGlowScale()
        );

        if (archetype == BossArchetype.Dog && dogAuraGrowlSfx != null && Time.time >= nextDogAuraGrowlAt)
        {
            PlayOneShot(dogAuraGrowlSfx, transform.position, presentationSfxVolume);
            nextDogAuraGrowlAt = Time.time + Random.Range(dogAuraGrowlIntervalMin, dogAuraGrowlIntervalMax);
        }
    }

    private void GetAuraStyle(out Color color, out float radius, out float thickness, out float pulseAmp, out float pulseSpeed, out float rotationSpeed)
    {
        switch (archetype)
        {
            case BossArchetype.Zombie:
                color = new Color(0.2f, 1f, 0.25f, 0.68f); radius = 2.15f; thickness = 0.03f; pulseAmp = 0.14f; pulseSpeed = 2f; rotationSpeed = 30f; break;
            case BossArchetype.Quick:
                color = new Color(0.25f, 0.95f, 1f, 0.72f); radius = 1.85f; thickness = 0.025f; pulseAmp = 0.2f; pulseSpeed = 4.8f; rotationSpeed = 220f; break;
            case BossArchetype.Tank:
                color = new Color(0.35f, 0.1f, 0.1f, 0.75f); radius = 2.85f; thickness = 0.09f; pulseAmp = 0.08f; pulseSpeed = 1.5f; rotationSpeed = 22f; break;
            case BossArchetype.Dog:
                color = new Color(1f, 0.68f, 0.2f, 0.78f); radius = 1.7f; thickness = 0.03f; pulseAmp = 0.22f; pulseSpeed = 6f; rotationSpeed = 110f; break;
            default:
                color = new Color(1f, 0f, 0f, 0.65f); radius = 2f; thickness = 0.03f; pulseAmp = 0.12f; pulseSpeed = 2.5f; rotationSpeed = 40f; break;
        }

        if (enrageApplied)
        {
            color = Color.Lerp(color, new Color(1f, 0f, 0f, 0.92f), 0.6f);
            radius *= 1.12f;
            pulseAmp *= 1.2f;
            pulseSpeed *= 1.1f;
        }
    }

    private void CleanupAuraVisual()
    {
        if (auraVisual != null)
            Destroy(auraVisual);

        auraVisual = null;
        auraTransform = null;
        auraRenderers = null;
    }

    private void PlaySpawnPresentation()
    {
        SpawnTransientVfx(spawnVfxPrefab, transform.position);
        SpawnShockwave(transform.position, spawnShockwaveRadius, spawnShockwaveDuration, spawnShockwaveColor);
        PlayOneShot(spawnSfx, transform.position, presentationSfxVolume);
    }

    private void PlayTeleportPresentation(Vector3 at)
    {
        SpawnTransientVfx(teleportVfxPrefab, at);
        SpawnShockwave(at, teleportShockwaveRadius, teleportShockwaveDuration, teleportShockwaveColor);
        PlayOneShot(teleportSfx, at, presentationSfxVolume);
    }

    private void PlayEnragePresentation()
    {
        SpawnTransientVfx(enrageVfxPrefab, transform.position);
        SpawnShockwave(transform.position, enrageShockwaveRadius, enrageShockwaveDuration, enrageShockwaveColor);
        PlayOneShot(enrageSfx, transform.position, presentationSfxVolume);
    }

    private void SpawnTransientVfx(GameObject prefab, Vector3 at, float fallbackLifetime = 6f)
    {
        if (prefab == null)
            return;

        GameObject instance = Instantiate(prefab, at, Quaternion.identity);
        if (instance != null)
            Destroy(instance, Mathf.Max(0.1f, fallbackLifetime));
    }

    private void SpawnGroundTelegraph(Vector3 worldPos, float radius, float duration, Color color, AudioClip warningClip)
    {
        if (radius <= 0f || duration <= 0f)
            return;

        Vector3 markerPos = worldPos;
        if (TrySnapToGround(worldPos, out Vector3 snapped))
            markerPos = snapped;

        markerPos.y += telegraphYOffset;

        GameObject marker = CreateTelegraphVisual(markerPos, radius, color);
        if (marker != null)
        {
            int telemetryId = RegisterTelegraphTelemetry(markerPos, radius, duration);
            StartCoroutine(TelegraphPulseRoutine(marker, duration, color, telemetryId));
        }

        PlayOneShot(warningClip, markerPos, warningSfxVolume);
    }

    private GameObject CreateTelegraphVisual(Vector3 position, float radius, Color color)
    {
        GameObject marker = RentTelegraphVisual();
        if (marker == null)
            return null;

        marker.transform.position = position;
        marker.transform.rotation = Quaternion.identity;
        marker.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
        TintRenderers(marker.GetComponentsInChildren<Renderer>(true), color, telegraphMaterialOverride, 1f, 1f);
        return marker;
    }

    private IEnumerator TelegraphPulseRoutine(GameObject marker, float duration, Color baseColor, int telemetryId)
    {
        if (marker == null)
            yield break;

        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        Vector3 baseScale = marker.transform.localScale;
        float endTime = Time.time + duration;

        while (marker != null && Time.time < endTime)
        {
            float t = 1f - ((endTime - Time.time) / Mathf.Max(0.01f, duration));
            float scalePulse = Mathf.Lerp(0.55f, 1f, t);
            marker.transform.localScale = new Vector3(baseScale.x * scalePulse, baseScale.y, baseScale.z * scalePulse);

            Color frameColor = baseColor;
            frameColor.a = Mathf.Lerp(baseColor.a, 0f, t);
            TintRenderers(renderers, frameColor, null, t, 1f);

            yield return null;
        }

        if (telemetryId >= 0)
            CompleteTelegraphTelemetry(telemetryId);

        if (marker != null)
            ReturnTelegraphVisual(marker);
    }

    private void SpawnShockwave(Vector3 at, float radius, float duration, Color color)
    {
        if (radius <= 0f || duration <= 0f)
            return;

        StartCoroutine(ShockwaveRoutine(at, radius, duration, color));
    }

    private IEnumerator ShockwaveRoutine(Vector3 at, float radius, float duration, Color color)
    {
        GameObject wave = RentShockwaveVisual();
        if (wave == null)
            yield break;

        Vector3 pos = at;
        if (TrySnapToGround(at, out Vector3 snapped))
            pos = snapped;

        pos.y += 0.03f;
        wave.transform.position = pos;
        wave.transform.rotation = Quaternion.identity;
        wave.transform.localScale = new Vector3(0.2f, 0.02f, 0.2f);

        Renderer[] renderers = wave.GetComponentsInChildren<Renderer>(true);
        TintRenderers(renderers, color, telegraphMaterialOverride, 0f, 1f);

        float endTime = Time.time + duration;
        while (wave != null && Time.time < endTime)
        {
            float t = 1f - ((endTime - Time.time) / duration);
            float currentRadius = Mathf.Lerp(0.12f, radius, t);
            wave.transform.localScale = new Vector3(currentRadius * 2f, 0.02f, currentRadius * 2f);

            Color frameColor = color;
            frameColor.a = Mathf.Lerp(color.a, 0f, t);
            TintRenderers(renderers, frameColor, null, t, 1f);

            yield return null;
        }

        if (wave != null)
            ReturnShockwaveVisual(wave);
    }

    private static void RemoveAllColliders(GameObject go)
    {
        if (go == null)
            return;

        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                Destroy(colliders[i]);
        }
    }

    private void TintRenderers(Renderer[] renderers, Color color, Material materialOverride, float pulse01 = 1f, float glowScale = 1f)
    {
        if (renderers == null)
            return;

        tintPropertyBlock ??= new MaterialPropertyBlock();
        Color lutColor = GetArchetypeLutColor();
        Color emission = Color.Lerp(color, lutColor, 0.35f) * Mathf.Lerp(0.45f, 1.1f, pulse01) * Mathf.Max(0.1f, glowScale);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            if (materialOverride != null && r.sharedMaterial != materialOverride)
                r.sharedMaterial = materialOverride;

            Material shared = r.sharedMaterial;
            if (shared != null)
                shared.EnableKeyword("_EMISSION");

            r.GetPropertyBlock(tintPropertyBlock);
            tintPropertyBlock.SetColor(BaseColorId, color);
            tintPropertyBlock.SetColor(ColorId, color);
            tintPropertyBlock.SetColor(EmissionColorId, emission);
            tintPropertyBlock.SetFloat(BossPulseId, pulse01);
            tintPropertyBlock.SetFloat(BossGlowId, glowScale);
            tintPropertyBlock.SetFloat(BossArchetypeId, (float)archetype);
            r.SetPropertyBlock(tintPropertyBlock);
        }
    }

    private void PlayOneShot(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null)
            return;

        AudioUtils.PlayClipAtPoint(clip, position, Mathf.Clamp01(volume));
    }

    private GameObject RentTelegraphVisual()
    {
        while (telegraphPool.Count > 0)
        {
            GameObject cached = telegraphPool.Dequeue();
            if (cached != null)
            {
                cached.SetActive(true);
                return cached;
            }
        }

        GameObject sourcePrefab = ResolveTelegraphPrefab();
        if (sourcePrefab == null)
        {
            if (!telegraphPrefabMissingWarningShown)
            {
                Debug.LogWarning(
                    "[BossEnemyController] Missing telegraph prefab. Assign telegraphMarkerPrefab or create Resources/BossTelegraphMarker_SG.",
                    this
                );
                telegraphPrefabMissingWarningShown = true;
            }

            return null;
        }

        GameObject marker = Instantiate(sourcePrefab);
        RemoveAllColliders(marker);
        return marker;
    }

    private void ReturnTelegraphVisual(GameObject marker)
    {
        if (marker == null)
            return;

        if (telegraphPool.Count >= Mathf.Max(1, telegraphPoolCapacity))
        {
            Destroy(marker);
            return;
        }

        marker.SetActive(false);
        telegraphPool.Enqueue(marker);
    }

    private GameObject RentShockwaveVisual()
    {
        while (shockwavePool.Count > 0)
        {
            GameObject cached = shockwavePool.Dequeue();
            if (cached != null)
            {
                cached.SetActive(true);
                return cached;
            }
        }

        GameObject sourcePrefab = ResolveShockwavePrefab();
        if (sourcePrefab == null)
        {
            if (!shockwavePrefabMissingWarningShown)
            {
                Debug.LogWarning(
                    "[BossEnemyController] Missing shockwave prefab. Assign shockwavePrefab or create Resources/BossShockwave_SG.",
                    this
                );
                shockwavePrefabMissingWarningShown = true;
            }

            return null;
        }

        GameObject wave = Instantiate(sourcePrefab);
        RemoveAllColliders(wave);
        return wave;
    }

    private void ReturnShockwaveVisual(GameObject wave)
    {
        if (wave == null)
            return;

        if (shockwavePool.Count >= Mathf.Max(1, shockwavePoolCapacity))
        {
            Destroy(wave);
            return;
        }

        wave.SetActive(false);
        shockwavePool.Enqueue(wave);
    }

    private void CleanupPresentationPools()
    {
        while (telegraphPool.Count > 0)
        {
            GameObject go = telegraphPool.Dequeue();
            if (go != null)
                Destroy(go);
        }

        while (shockwavePool.Count > 0)
        {
            GameObject go = shockwavePool.Dequeue();
            if (go != null)
                Destroy(go);
        }
    }

    private Color GetArchetypeLutColor()
    {
        BossEmissiveLutProfile profile = ResolveEmissiveLutProfile();
        if (profile != null && profile.TryGet(archetype, out Color profileLutColor, out _))
            return profileLutColor;

        return archetype switch
        {
            BossArchetype.Zombie => new Color(0.24f, 0.95f, 0.38f, 1f),
            BossArchetype.Quick => new Color(0.32f, 0.82f, 1f, 1f),
            BossArchetype.Tank => new Color(1f, 0.34f, 0.28f, 1f),
            BossArchetype.Dog => new Color(1f, 0.76f, 0.27f, 1f),
            _ => Color.white
        };
    }

    private float GetArchetypeGlowScale()
    {
        BossEmissiveLutProfile profile = ResolveEmissiveLutProfile();
        if (profile != null && profile.TryGet(archetype, out _, out float profileGlowScale))
            return profileGlowScale;

        return archetype switch
        {
            BossArchetype.Zombie => 0.95f,
            BossArchetype.Quick => 1.2f,
            BossArchetype.Tank => 0.9f,
            BossArchetype.Dog => 1.12f,
            _ => 1f
        };
    }

    private GameObject ResolveTelegraphPrefab()
    {
        if (telegraphMarkerPrefab != null)
            return telegraphMarkerPrefab;

        if (!defaultTelegraphPrefabResolved)
        {
            cachedDefaultTelegraphPrefab = Resources.Load<GameObject>(DefaultTelegraphPrefabResourcePath);
            defaultTelegraphPrefabResolved = true;
        }

        return cachedDefaultTelegraphPrefab;
    }

    private GameObject ResolveShockwavePrefab()
    {
        if (shockwavePrefab != null)
            return shockwavePrefab;

        if (!defaultShockwavePrefabResolved)
        {
            cachedDefaultShockwavePrefab = Resources.Load<GameObject>(DefaultShockwavePrefabResourcePath);
            defaultShockwavePrefabResolved = true;
        }

        return cachedDefaultShockwavePrefab;
    }

    private BossEmissiveLutProfile ResolveEmissiveLutProfile()
    {
        if (emissiveLutProfile != null)
            return emissiveLutProfile;

        if (!defaultEmissiveLutResolved)
        {
            cachedDefaultEmissiveLutProfile = Resources.Load<BossEmissiveLutProfile>(DefaultEmissiveLutProfileResourcePath);
            defaultEmissiveLutResolved = true;
        }

        return cachedDefaultEmissiveLutProfile;
    }

    private int RegisterTelegraphTelemetry(Vector3 origin, float radius, float duration)
    {
        if (!enableReadabilityTelemetry || player == null)
            return -1;

        int id = ++nextTelegraphTelemetryId;
        telegraphTelemetry[id] = new TelegraphTelemetryEntry
        {
            archetype = archetype,
            origin = origin,
            playerStart = player.position,
            radius = Mathf.Max(0.1f, radius),
            startedAt = Time.time,
            duration = Mathf.Max(0.01f, duration),
            reacted = false,
            reactedAt = -1f
        };

        return id;
    }

    private void UpdateReadabilityTelemetry()
    {
        if (!enableReadabilityTelemetry || telegraphTelemetry.Count == 0 || player == null)
            return;

        telemetryIdsBuffer.Clear();
        foreach (KeyValuePair<int, TelegraphTelemetryEntry> kv in telegraphTelemetry)
            telemetryIdsBuffer.Add(kv.Key);

        for (int i = 0; i < telemetryIdsBuffer.Count; i++)
        {
            int id = telemetryIdsBuffer[i];
            if (!telegraphTelemetry.TryGetValue(id, out TelegraphTelemetryEntry entry))
                continue;

            if (entry.reacted)
                continue;

            float distance = Vector3.Distance(player.position, entry.origin);
            if (distance > entry.radius * 1.05f)
            {
                entry.reacted = true;
                entry.reactedAt = Time.time;
                telegraphTelemetry[id] = entry;
            }
        }
    }

    private void CompleteTelegraphTelemetry(int id)
    {
        if (!enableReadabilityTelemetry || id < 0)
            return;

        if (!telegraphTelemetry.TryGetValue(id, out TelegraphTelemetryEntry entry))
            return;

        telegraphTelemetry.Remove(id);

        float reactionTime = entry.reacted ? Mathf.Max(0f, entry.reactedAt - entry.startedAt) : entry.duration;
        bool dodged = entry.reacted && reactionTime <= entry.duration;
        float movedDistance = player != null ? Vector3.Distance(player.position, entry.playerStart) : 0f;

        if (!readabilityStates.TryGetValue(entry.archetype, out ReadabilityTelemetryState state))
            state = new ReadabilityTelemetryState { durationMultiplier = 1f };

        state.samples++;
        if (dodged)
            state.dodged++;

        float blend = state.samples <= 1 ? 1f : 0.25f;
        state.avgReaction = Mathf.Lerp(state.avgReaction, reactionTime, blend);
        state.avgDuration = Mathf.Lerp(state.avgDuration, entry.duration, blend);
        state.avgMovedDistance = Mathf.Lerp(state.avgMovedDistance, movedDistance, blend);
        state.durationMultiplier = EvaluateDurationMultiplier(state);
        readabilityStates[entry.archetype] = state;
    }

    private float EvaluateDurationMultiplier(ReadabilityTelemetryState state)
    {
        float baseMul = Mathf.Max(0.75f, state.durationMultiplier <= 0f ? 1f : state.durationMultiplier);
        if (state.samples < 3)
            return baseMul;

        float dodgeRate = state.dodged / (float)Mathf.Max(1, state.samples);
        float reactionRatio = state.avgDuration > 0.01f ? (state.avgReaction / state.avgDuration) : 1f;
        float score = 0f;

        score += (readabilityTargetDodgeRate - dodgeRate) * 0.65f;
        score += (reactionRatio - 0.72f) * 0.45f;
        score += (state.avgReaction - readabilityTargetReactionSeconds) * 0.25f;

        return Mathf.Clamp(baseMul + score * 0.12f, 0.8f, 1.35f);
    }

    private float GetReadabilityAdjustedTelegraphDuration(float baseDuration)
    {
        if (!enableReadabilityTelemetry)
            return baseDuration;

        if (!readabilityStates.TryGetValue(archetype, out ReadabilityTelemetryState state))
            return baseDuration;

        float mul = state.durationMultiplier <= 0f ? 1f : state.durationMultiplier;
        return Mathf.Max(0.05f, baseDuration * mul);
    }

    public bool TryGetReadabilitySnapshot(
        out float dodgeRate,
        out float avgReactionSeconds,
        out float durationMultiplier)
    {
        dodgeRate = 0f;
        avgReactionSeconds = 0f;
        durationMultiplier = 1f;

        if (!readabilityStates.TryGetValue(archetype, out ReadabilityTelemetryState state) || state.samples <= 0)
            return false;

        dodgeRate = state.dodged / (float)Mathf.Max(1, state.samples);
        avgReactionSeconds = state.avgReaction;
        durationMultiplier = state.durationMultiplier <= 0f ? 1f : state.durationMultiplier;
        return true;
    }
}
