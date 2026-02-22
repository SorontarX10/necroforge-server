using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Telemetry;

public static class RelicDamageText
{
    private static readonly Color DefaultRelicColor = new(0.72f, 0.5f, 1f, 1f);
    private static readonly Dictionary<int, float> LastEffectTextAt = new();
    private static readonly Dictionary<int, float> LastProcPresentationAt = new();
    private static readonly Queue<float> ProcDamageEventTimes = new();
    private static readonly Dictionary<int, float> LastTargetProcAt = new();
    private static readonly List<int> StaleTargetProcIds = new();
    private static readonly string[] ProcBudgetLimitedRelicTokens =
    {
        "choir",
        "ossuary",
        "mortwarden",
        "ledger",
        "executioner"
    };

    private const float EffectTextCooldown = 0.35f;
    private const float ProcPresentationCooldown = 0.06f;
    private const int MaxProcDamageEventsPerFrame = 4;
    private const int MaxProcDamageEventsPerSecond = 40;
    private const float TargetProcCooldownSeconds = 0.1f;
    private const float TargetProcCleanupInterval = 2f;
    private const float TargetProcStaleAfterSeconds = 8f;

    private static int procDamageFrame = -1;
    private static int procDamageEventsThisFrame;
    private static float nextTargetProcCleanupAt;

    public static void Deal(
        Combatant target,
        float damage,
        Transform source,
        RelicEffect effect = null,
        string fallbackLabel = "Relic",
        float damageFontSize = 34f
    )
    {
        if (target == null || damage <= 0f)
            return;

        ResolvePresentation(
            source,
            effect,
            fallbackLabel,
            out Color color,
            out string label,
            out RelicRarity rarity,
            out string relicId
        );
        if (!TryConsumeProcBudget(target, relicId, label))
            return;

        string effectText = ShouldShowEffectText(target, label) ? label : null;
        float effectFontSize = Mathf.Max(20f, damageFontSize * 0.72f);
        bool wasAlive = !target.IsDead;
        float clampedDamage = Mathf.Max(0f, damage);

        target.TakeDamageWithText(
            clampedDamage,
            source,
            color,
            damageFontSize,
            effectText,
            color,
            effectFontSize
        );
        bool causedKill = wasAlive && target.IsDead;
        ReportRelicProcTelemetry(target, clampedDamage, relicId, label, rarity, causedKill);

        TrySpawnProcPresentation(target, source, color, rarity);
    }

    private static bool TryConsumeProcBudget(Combatant target, string relicId, string label)
    {
        if (!ShouldLimitProcBudget(relicId, label))
            return true;

        float now = Time.unscaledTime;
        int frame = Time.frameCount;

        if (procDamageFrame != frame)
        {
            procDamageFrame = frame;
            procDamageEventsThisFrame = 0;
        }

        if (procDamageEventsThisFrame >= MaxProcDamageEventsPerFrame)
            return false;

        float threshold = now - 1f;
        while (ProcDamageEventTimes.Count > 0 && ProcDamageEventTimes.Peek() < threshold)
            ProcDamageEventTimes.Dequeue();

        if (ProcDamageEventTimes.Count >= MaxProcDamageEventsPerSecond)
            return false;

        if (target != null)
        {
            int targetId = target.GetInstanceID();
            if (LastTargetProcAt.TryGetValue(targetId, out float lastProcAt) && now - lastProcAt < TargetProcCooldownSeconds)
                return false;

            LastTargetProcAt[targetId] = now;
        }

        CleanupStaleTargetProcTimestamps(now);

        ProcDamageEventTimes.Enqueue(now);
        procDamageEventsThisFrame++;
        return true;
    }

    private static bool ShouldLimitProcBudget(string relicId, string label)
    {
        string id = string.IsNullOrWhiteSpace(relicId) ? string.Empty : relicId.ToLowerInvariant();
        string display = string.IsNullOrWhiteSpace(label) ? string.Empty : label.ToLowerInvariant();

        for (int i = 0; i < ProcBudgetLimitedRelicTokens.Length; i++)
        {
            string token = ProcBudgetLimitedRelicTokens[i];
            if ((id.Length > 0 && id.Contains(token)) || (display.Length > 0 && display.Contains(token)))
                return true;
        }

        return false;
    }

    private static void CleanupStaleTargetProcTimestamps(float now)
    {
        if (now < nextTargetProcCleanupAt || LastTargetProcAt.Count == 0)
            return;

        float staleThreshold = now - TargetProcStaleAfterSeconds;
        StaleTargetProcIds.Clear();

        foreach (KeyValuePair<int, float> kv in LastTargetProcAt)
        {
            if (kv.Value < staleThreshold)
                StaleTargetProcIds.Add(kv.Key);
        }

        for (int i = 0; i < StaleTargetProcIds.Count; i++)
            LastTargetProcAt.Remove(StaleTargetProcIds[i]);

        nextTargetProcCleanupAt = now + TargetProcCleanupInterval;
    }

    public static GameObject CreateGeneratedAuraCircle(
        string name,
        float radius,
        RelicRarity rarity,
        float yThickness = 0.02f
    )
    {
        return RelicGeneratedPresentation.CreateAuraCircle(name, radius, RelicRarityColors.Get(rarity), yThickness);
    }

    public static GameObject CreateGeneratedCloud(
        string name,
        float radius,
        RelicRarity rarity
    )
    {
        return RelicGeneratedPresentation.CreateCloud(name, radius, RelicRarityColors.Get(rarity));
    }

    public static GameObject CreateGeneratedSkull(
        string name,
        RelicRarity rarity,
        float scale = 0.35f
    )
    {
        return RelicGeneratedPresentation.CreateSkull(name, RelicRarityColors.Get(rarity), scale);
    }

    public static GameObject CreateGeneratedScribe(
        string name,
        RelicRarity rarity,
        float scale = 0.45f
    )
    {
        return RelicGeneratedPresentation.CreateScribe(name, RelicRarityColors.Get(rarity), scale);
    }

    public static GameObject CreateGeneratedStandard(
        string name,
        float auraRadius,
        RelicRarity rarity,
        bool withBanner
    )
    {
        return RelicGeneratedPresentation.CreateStandard(name, auraRadius, RelicRarityColors.Get(rarity), withBanner);
    }

    public static GameObject CreateGeneratedMinionBody(
        string name,
        RelicRarity rarity
    )
    {
        return RelicGeneratedPresentation.CreateMinionBody(name, RelicRarityColors.Get(rarity));
    }

    public static void StyleSpawnedSkullVisual(
        GameObject skullRoot,
        RelicRarity rarity,
        float uniformScale = 0.08f,
        Vector3 rotationEuler = default
    )
    {
        if (skullRoot == null)
            return;

        skullRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, uniformScale);
        skullRoot.transform.localRotation = Quaternion.Euler(rotationEuler);

        Color color = RelicRarityColors.Get(rarity);
        RelicGeneratedPresentation.ApplySolidTintHierarchy(
            skullRoot,
            color,
            emission: 0.55f,
            disableShadows: true
        );
    }

    public static void PlayGeneratedEventFeedback(
        Transform source,
        RelicRarity rarity,
        float burstScale = 1.2f
    )
    {
        if (source == null)
            return;

        Color color = RelicRarityColors.Get(rarity);
        Vector3 pos = source.position + Vector3.up * 0.95f;
        RelicGeneratedPresentation.SpawnEventBurst(pos, color, rarity, burstScale);
    }

    private static void ResolvePresentation(
        Transform source,
        RelicEffect effect,
        string fallbackLabel,
        out Color color,
        out string label,
        out RelicRarity rarity,
        out string relicId
    )
    {
        color = DefaultRelicColor;
        label = fallbackLabel;
        rarity = RelicRarity.Common;
        relicId = null;

        PlayerRelicController relics = source != null
            ? source.GetComponentInParent<PlayerRelicController>()
            : null;

        if (relics != null && effect != null && relics.TryGetRelicDefinitionByEffect(effect, out RelicDefinition def))
        {
            color = RelicRarityColors.Get(def.rarity);
            rarity = def.rarity;
            relicId = def.id;
            if (!string.IsNullOrWhiteSpace(def.displayName))
                label = def.displayName;
        }
        else if (relics != null && effect == null && !string.IsNullOrWhiteSpace(fallbackLabel))
        {
            foreach (var kvp in relics.Relics)
            {
                RelicDefinition candidate = kvp.Value;
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.displayName))
                    continue;

                if (!string.Equals(candidate.displayName, fallbackLabel, StringComparison.OrdinalIgnoreCase))
                    continue;

                color = RelicRarityColors.Get(candidate.rarity);
                rarity = candidate.rarity;
                label = candidate.displayName;
                relicId = candidate.id;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(label))
            label = HumanizeEffectName(effect);
        if (string.IsNullOrWhiteSpace(relicId) && effect != null)
            relicId = effect.name;
    }

    private static void ReportRelicProcTelemetry(
        Combatant target,
        float damage,
        string relicId,
        string displayName,
        RelicRarity rarity,
        bool causedKill
    )
    {
        if (target == null || damage <= 0f)
            return;

        float runTime = GameTimerController.Instance != null
            ? Mathf.Max(0f, GameTimerController.Instance.elapsedTime)
            : 0f;

        GameplayTelemetryHub.ReportRelicProc(
            new GameplayTelemetryHub.RelicProcSample(
                runTime,
                relicId,
                displayName,
                rarity.ToString(),
                damage,
                causedKill,
                target.GetInstanceID(),
                NormalizeTargetName(target.gameObject != null ? target.gameObject.name : "Unknown")
            )
        );
    }

    private static string NormalizeTargetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        string normalized = value.Replace("(Clone)", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Unknown" : normalized;
    }

    private static bool ShouldShowEffectText(Combatant target, string effectLabel)
    {
        if (target == null || string.IsNullOrWhiteSpace(effectLabel))
            return false;

        int key = (target.GetInstanceID() * 397) ^ effectLabel.GetHashCode();
        float now = Time.unscaledTime;

        if (LastEffectTextAt.TryGetValue(key, out float lastAt) && now - lastAt < EffectTextCooldown)
            return false;

        LastEffectTextAt[key] = now;
        return true;
    }

    private static void TrySpawnProcPresentation(Combatant target, Transform source, Color color, RelicRarity rarity)
    {
        if (target == null)
            return;

        int key = (target.GetInstanceID() * 397) ^ (int)rarity;
        float now = Time.unscaledTime;
        if (LastProcPresentationAt.TryGetValue(key, out float lastAt) && now - lastAt < ProcPresentationCooldown)
            return;

        LastProcPresentationAt[key] = now;

        Vector3 pos = target.transform.position + Vector3.up * ResolveEffectPopupHeight(target);
        RelicGeneratedPresentation.SpawnProcPulse(pos, color, rarity);
    }

    private static float ResolveEffectPopupHeight(Combatant target)
    {
        if (target == null)
            return 1.25f;

        float height = 1.25f;
        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
            height = Mathf.Max(height, rend.bounds.size.y * 0.6f);

        CapsuleCollider capsule = target.GetComponentInChildren<CapsuleCollider>();
        if (capsule != null)
            height = Mathf.Max(height, capsule.height * 0.55f);

        CharacterController cc = target.GetComponentInChildren<CharacterController>();
        if (cc != null)
            height = Mathf.Max(height, cc.height * 0.55f);

        return height;
    }

    private static string HumanizeEffectName(RelicEffect effect)
    {
        if (effect == null)
            return "Relic";

        if (!string.IsNullOrWhiteSpace(effect.displayName))
            return effect.displayName;

        string raw = string.IsNullOrWhiteSpace(effect.name)
            ? effect.GetType().Name
            : effect.name;

        raw = raw.Replace("Relic_", string.Empty).Replace("Runtime", string.Empty).Trim();
        if (raw.Length == 0)
            return "Relic";

        var sb = new StringBuilder(raw.Length + 8);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(raw[i - 1]) && !char.IsUpper(raw[i - 1]))
                sb.Append(' ');

            sb.Append(c);
        }

        return sb.ToString();
    }
}

internal static class RelicGeneratedPresentation
{
    // Sphere pulses read as noisy "pink balls" in dense combat; keep disabled for cleaner readability.
    private static readonly bool EnablePulseSpheres = false;
    private const int MaxPulsePoolSize = 64;
    private const int PulsePrewarmCount = 12;
    private static readonly Queue<GameObject> PulsePool = new();
    private static readonly Dictionary<RelicRarity, AudioClip> ProcClips = new();
    private static readonly Dictionary<RelicRarity, float> LastSfxAt = new();

    private static Material solidMaterial;
    private static Material translucentMaterial;
    private static Material darkMaterial;
    private static Material areaRingMaterial;
    private static Material areaFogMaterial;
    private static RelicOneShotAudioHub audioHub;
    private static bool missingMaterialWarningLogged;

    private const string AreaOverlayShaderName = "Necroforge/Relic/AreaOverlay";
    private static Transform pulsePoolRoot;
    private static bool pulsePoolPrewarmed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        PulsePool.Clear();
        ProcClips.Clear();
        LastSfxAt.Clear();
        pulsePoolPrewarmed = false;

        solidMaterial = null;
        translucentMaterial = null;
        darkMaterial = null;
        areaRingMaterial = null;
        areaFogMaterial = null;
        audioHub = null;
        missingMaterialWarningLogged = false;

        if (pulsePoolRoot != null)
            UnityEngine.Object.Destroy(pulsePoolRoot.gameObject);

        pulsePoolRoot = null;
    }

    public static void SpawnProcPulse(Vector3 worldPos, Color color, RelicRarity rarity)
    {
        if (EnablePulseSpheres)
            SpawnPulse(worldPos, color, 0.42f, 0.15f, 0.8f);

        TryPlaySfx(worldPos, rarity);
    }

    public static void SpawnEventBurst(Vector3 worldPos, Color color, RelicRarity rarity, float burstScale)
    {
        if (EnablePulseSpheres)
        {
            float scale = Mathf.Clamp(burstScale, 0.6f, 2.5f);
            SpawnPulse(worldPos, color, 0.56f * scale, 0.2f, 1.35f * scale);
            SpawnPulse(worldPos + Vector3.up * 0.04f, new Color(color.r, color.g, color.b, color.a * 0.75f), 0.42f * scale, 0.17f, 1.08f * scale);
        }

        TryPlaySfx(worldPos, rarity);
    }

    public static GameObject CreateAuraCircle(string name, float radius, Color color, float yThickness)
    {
        GameObject root = new(name);
        float safeRadius = Mathf.Max(0.35f, radius);
        float yOffset = Mathf.Max(0.003f, yThickness);

        // Ground-locked area quad with very faint fill and a strong border generated in shader.
        GameObject area = CreatePrimitiveNoCollider(PrimitiveType.Quad, "Area", root.transform);
        area.transform.localPosition = new Vector3(0f, yOffset, 0f);
        area.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        area.transform.localScale = new Vector3(safeRadius * 2f, safeRadius * 2f, 1f);
        Color auraColor = new(color.r, color.g, color.b, Mathf.Clamp(color.a * 0.35f, 0.2f, 0.36f));
        Tint(
            area.GetComponent<Renderer>(),
            auraColor,
            translucent: true,
            emission: 0.22f,
            overrideMaterial: GetAreaRingMaterial()
        );

        return root;
    }

    public static GameObject CreateCloud(string name, float radius, Color color)
    {
        GameObject root = new(name);
        float safeRadius = Mathf.Max(0.6f, radius);
        Color fogColor = new(
            Mathf.Lerp(color.r, 0.72f, 0.45f),
            Mathf.Lerp(color.g, 0.72f, 0.45f),
            Mathf.Lerp(color.b, 0.72f, 0.45f),
            0.34f
        );

        // Two subtle layered fog quads keep the center readable while preserving zone readability.
        GameObject fogMain = CreatePrimitiveNoCollider(PrimitiveType.Quad, "FogAreaMain", root.transform);
        fogMain.transform.localPosition = new Vector3(0f, 0.045f, 0f);
        fogMain.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        fogMain.transform.localScale = new Vector3(safeRadius * 2.2f, safeRadius * 2.2f, 1f);
        Tint(
            fogMain.GetComponent<Renderer>(),
            fogColor,
            translucent: true,
            emission: 0.07f,
            overrideMaterial: GetAreaFogMaterial()
        );

        GameObject fogSoft = CreatePrimitiveNoCollider(PrimitiveType.Quad, "FogAreaSoft", root.transform);
        fogSoft.transform.localPosition = new Vector3(0f, 0.055f, 0f);
        fogSoft.transform.localRotation = Quaternion.Euler(90f, 35f, 0f);
        fogSoft.transform.localScale = new Vector3(safeRadius * 1.75f, safeRadius * 1.75f, 1f);
        Tint(
            fogSoft.GetComponent<Renderer>(),
            new Color(fogColor.r, fogColor.g, fogColor.b, fogColor.a * 0.72f),
            translucent: true,
            emission: 0.045f,
            overrideMaterial: GetAreaFogMaterial()
        );

        return root;
    }

    public static GameObject CreateSkull(string name, Color color, float scale)
    {
        GameObject root = new(name);
        root.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

        // Keep fallback visuals compact and non-spherical to avoid "floating orb" read in dense fights.
        GameObject head = CreatePrimitiveNoCollider(PrimitiveType.Capsule, "Head", root.transform);
        head.transform.localScale = new Vector3(0.74f, 0.46f, 0.74f);
        head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Tint(head.GetComponent<Renderer>(), color, translucent: false, emission: 0.28f);

        GameObject jaw = CreatePrimitiveNoCollider(PrimitiveType.Cube, "Jaw", root.transform);
        jaw.transform.localPosition = new Vector3(0f, -0.26f, 0f);
        jaw.transform.localScale = new Vector3(0.56f, 0.22f, 0.5f);
        Tint(jaw.GetComponent<Renderer>(), color * 0.86f, translucent: false, emission: 0.16f);

        GameObject eyeLeft = CreatePrimitiveNoCollider(PrimitiveType.Cube, "EyeL", root.transform);
        eyeLeft.transform.localPosition = new Vector3(-0.16f, 0.03f, 0.28f);
        eyeLeft.transform.localScale = new Vector3(0.12f, 0.1f, 0.08f);
        Tint(eyeLeft.GetComponent<Renderer>(), new Color(0.05f, 0.05f, 0.06f, 1f), translucent: false, emission: 0f, dark: true);

        GameObject eyeRight = CreatePrimitiveNoCollider(PrimitiveType.Cube, "EyeR", root.transform);
        eyeRight.transform.localPosition = new Vector3(0.16f, 0.03f, 0.28f);
        eyeRight.transform.localScale = new Vector3(0.12f, 0.1f, 0.08f);
        Tint(eyeRight.GetComponent<Renderer>(), new Color(0.05f, 0.05f, 0.06f, 1f), translucent: false, emission: 0f, dark: true);

        return root;
    }

    public static GameObject CreateScribe(string name, Color color, float scale)
    {
        GameObject root = new(name);
        root.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

        GameObject core = CreatePrimitiveNoCollider(PrimitiveType.Cube, "Core", root.transform);
        core.transform.localScale = new Vector3(0.72f, 0.72f, 0.72f);
        core.transform.localRotation = Quaternion.Euler(0f, 34f, 0f);
        Tint(core.GetComponent<Renderer>(), color, translucent: false, emission: 0.28f);

        GameObject halo = CreatePrimitiveNoCollider(PrimitiveType.Cylinder, "Halo", root.transform);
        halo.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        halo.transform.localScale = new Vector3(1.2f, 0.02f, 1.2f);
        Tint(halo.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, 0.8f), translucent: true, emission: 0.45f);

        GameObject quill = CreatePrimitiveNoCollider(PrimitiveType.Cylinder, "Quill", root.transform);
        quill.transform.localPosition = new Vector3(0.35f, 0.05f, 0f);
        quill.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
        quill.transform.localScale = new Vector3(0.08f, 0.45f, 0.08f);
        Tint(quill.GetComponent<Renderer>(), color * 0.85f, translucent: false, emission: 0.25f);

        return root;
    }

    public static GameObject CreateStandard(string name, float auraRadius, Color color, bool withBanner)
    {
        GameObject root = new(name);

        GameObject pole = CreatePrimitiveNoCollider(PrimitiveType.Cylinder, "Pole", root.transform);
        pole.transform.localScale = new Vector3(0.08f, 1.15f, 0.08f);
        pole.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        Tint(pole.GetComponent<Renderer>(), color * 0.82f, translucent: false, emission: 0.25f);

        GameObject finial = CreatePrimitiveNoCollider(PrimitiveType.Cube, "Finial", root.transform);
        finial.transform.localScale = new Vector3(0.18f, 0.18f, 0.18f);
        finial.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        finial.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        Tint(finial.GetComponent<Renderer>(), color, translucent: false, emission: 0.22f);

        if (withBanner)
        {
            Vector3 bannerPos = new(0.28f, 1.7f, 0f);
            Vector3 bannerScale = new(0.6f, 0.95f, 1f);
            Color bannerColor = new(color.r, color.g, color.b, 0.62f);

            GameObject bannerFront = CreatePrimitiveNoCollider(PrimitiveType.Quad, "Banner_Front", root.transform);
            bannerFront.transform.localPosition = bannerPos;
            bannerFront.transform.localScale = bannerScale;
            bannerFront.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Tint(bannerFront.GetComponent<Renderer>(), bannerColor, translucent: true, emission: 0.34f);

            // Back face so the flag remains visible from both sides.
            GameObject bannerBack = CreatePrimitiveNoCollider(PrimitiveType.Quad, "Banner_Back", root.transform);
            bannerBack.transform.localPosition = bannerPos;
            bannerBack.transform.localScale = bannerScale;
            bannerBack.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Tint(bannerBack.GetComponent<Renderer>(), bannerColor, translucent: true, emission: 0.34f);
        }

        GameObject aura = CreateAuraCircle("Aura", auraRadius, color, 0.02f);
        aura.transform.SetParent(root.transform, false);
        aura.transform.localPosition = new Vector3(0f, 0.02f, 0f);

        return root;
    }

    public static GameObject CreateMinionBody(string name, Color color)
    {
        GameObject root = new(name);

        GameObject body = CreatePrimitiveNoCollider(PrimitiveType.Capsule, "Body", root.transform);
        body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
        Tint(body.GetComponent<Renderer>(), color * 0.9f, translucent: false, emission: 0.2f);

        GameObject head = CreatePrimitiveNoCollider(PrimitiveType.Cube, "Head", root.transform);
        head.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        head.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
        head.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
        Tint(head.GetComponent<Renderer>(), color, translucent: false, emission: 0.18f);

        GameObject blade = CreatePrimitiveNoCollider(PrimitiveType.Cube, "Blade", root.transform);
        blade.transform.localPosition = new Vector3(0.38f, 0.72f, 0f);
        blade.transform.localScale = new Vector3(0.08f, 0.7f, 0.08f);
        blade.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
        Tint(blade.GetComponent<Renderer>(), color * 0.75f, translucent: false, emission: 0.05f);

        return root;
    }

    private static GameObject GetPulseObject()
    {
        EnsurePulsePoolWarm();

        GameObject go;
        if (PulsePool.Count > 0)
        {
            go = PulsePool.Dequeue();
            go.SetActive(true);
            return go;
        }

        go = CreatePulseObject();
        go.SetActive(true);
        return go;
    }

    private static void EnsurePulsePoolWarm()
    {
        if (pulsePoolPrewarmed)
            return;

        pulsePoolPrewarmed = true;
        for (int i = 0; i < PulsePrewarmCount; i++)
        {
            GameObject go = CreatePulseObject();
            if (go == null)
                break;

            go.SetActive(false);
            PulsePool.Enqueue(go);
        }
    }

    private static GameObject CreatePulseObject()
    {
        GameObject go = CreatePrimitiveNoCollider(PrimitiveType.Sphere, "RelicProcPulse", null);
        if (go == null)
            return null;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = GetTranslucentMaterial();
            if (mat != null)
                renderer.sharedMaterial = mat;
            else
                renderer.enabled = false;
        }

        RelicPulseVfx pulse = go.AddComponent<RelicPulseVfx>();
        pulse.Bind(renderer);
        EnsurePulsePoolRoot();
        go.transform.SetParent(pulsePoolRoot, false);
        return go;
    }

    private static void SpawnPulse(Vector3 pos, Color color, float startScale, float duration, float endScale)
    {
        GameObject go = GetPulseObject();
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;

        var pulse = go.GetComponent<RelicPulseVfx>();
        if (pulse != null)
            pulse.Play(color, startScale, duration, endScale);
    }

    internal static void RecyclePulse(GameObject go)
    {
        if (go == null)
            return;

        if (PulsePool.Count >= MaxPulsePoolSize)
        {
            UnityEngine.Object.Destroy(go);
            return;
        }

        go.SetActive(false);
        EnsurePulsePoolRoot();
        go.transform.SetParent(pulsePoolRoot, false);
        PulsePool.Enqueue(go);
    }

    private static void EnsurePulsePoolRoot()
    {
        if (pulsePoolRoot != null)
            return;

        GameObject existing = GameObject.Find("RelicGeneratedVfxPool");
        if (existing != null)
        {
            pulsePoolRoot = existing.transform;
            return;
        }

        GameObject root = new("RelicGeneratedVfxPool");
        root.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(root);
        pulsePoolRoot = root.transform;
    }

    private static void TryPlaySfx(Vector3 position, RelicRarity rarity)
    {
        float now = Time.unscaledTime;
        if (LastSfxAt.TryGetValue(rarity, out float lastAt) && now - lastAt < 0.04f)
            return;

        LastSfxAt[rarity] = now;

        var hub = EnsureAudioHub();
        if (hub == null)
            return;

        AudioClip clip = GetOrBuildProcClip(rarity);
        if (clip == null)
            return;

        float volume = rarity switch
        {
            RelicRarity.Common => 0.16f,
            RelicRarity.Uncommon => 0.18f,
            RelicRarity.Rare => 0.2f,
            RelicRarity.Legendary => 0.24f,
            RelicRarity.Mythic => 0.28f,
            _ => 0.18f
        };

        float pitchJitter = 1f + (Mathf.PerlinNoise(now * 9f, (int)rarity * 0.1f) - 0.5f) * 0.08f;
        hub.Play(clip, position, volume, pitchJitter);
    }

    private static AudioClip GetOrBuildProcClip(RelicRarity rarity)
    {
        if (ProcClips.TryGetValue(rarity, out AudioClip cached) && cached != null)
            return cached;

        (float startHz, float endHz, float duration, float noise) tune = rarity switch
        {
            RelicRarity.Common => (210f, 160f, 0.09f, 0.09f),
            RelicRarity.Uncommon => (255f, 180f, 0.1f, 0.1f),
            RelicRarity.Rare => (320f, 220f, 0.11f, 0.11f),
            RelicRarity.Legendary => (430f, 280f, 0.12f, 0.12f),
            RelicRarity.Mythic => (560f, 320f, 0.13f, 0.13f),
            _ => (260f, 180f, 0.1f, 0.1f)
        };

        int sampleRate = 22050;
        int count = Mathf.Max(1, Mathf.CeilToInt(tune.duration * sampleRate));
        float[] data = new float[count];

        float phase = 0f;
        float invCount = 1f / count;
        for (int i = 0; i < count; i++)
        {
            float t = i * invCount;
            float envA = Mathf.Clamp01(t / 0.12f);
            float envB = Mathf.Clamp01((1f - t) / 0.88f);
            float env = envA * envB;

            float hz = Mathf.Lerp(tune.startHz, tune.endHz, t);
            phase += (2f * Mathf.PI * hz) / sampleRate;
            float tone = Mathf.Sin(phase);
            float overtone = Mathf.Sin(phase * 1.97f) * 0.35f;
            float noise = (Mathf.PerlinNoise(i * 0.031f, tune.startHz * 0.001f) * 2f - 1f) * tune.noise;

            data[i] = (tone + overtone + noise) * env * 0.24f;
        }

        AudioClip clip = AudioClip.Create($"RelicProc_{rarity}", count, 1, sampleRate, false);
        clip.SetData(data, 0);
        ProcClips[rarity] = clip;
        return clip;
    }

    private static RelicOneShotAudioHub EnsureAudioHub()
    {
        if (audioHub != null)
            return audioHub;

        var existing = UnityEngine.Object.FindFirstObjectByType<RelicOneShotAudioHub>();
        if (existing != null)
        {
            audioHub = existing;
            return audioHub;
        }

        GameObject go = new("RelicOneShotAudioHub");
        UnityEngine.Object.DontDestroyOnLoad(go);
        audioHub = go.AddComponent<RelicOneShotAudioHub>();
        audioHub.Initialize(10);
        return audioHub;
    }

    private static GameObject CreatePrimitiveNoCollider(PrimitiveType type, string name, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (parent != null)
            go.transform.SetParent(parent, false);

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            UnityEngine.Object.Destroy(col);

        return go;
    }

    private static void Tint(
        Renderer renderer,
        Color color,
        bool translucent,
        float emission,
        bool dark = false,
        Material overrideMaterial = null
    )
    {
        if (renderer == null)
            return;

        Material mat = overrideMaterial ?? (dark
            ? GetDarkMaterial()
            : translucent ? GetTranslucentMaterial() : GetSolidMaterial());
        if (mat == null)
        {
            renderer.enabled = false;
            return;
        }

        if (!renderer.enabled)
            renderer.enabled = true;

        renderer.sharedMaterial = mat;

        var props = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(props);

        if (mat.HasProperty("_BaseColor"))
            props.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            props.SetColor("_Color", color);
        if (mat.HasProperty("_EmissionColor"))
            props.SetColor("_EmissionColor", color * emission);

        renderer.SetPropertyBlock(props);
    }

    public static void ApplySolidTintHierarchy(
        GameObject root,
        Color color,
        float emission,
        bool disableShadows
    )
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        Material material = GetSolidMaterial();
        if (material == null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }

            return;
        }

        var props = new MaterialPropertyBlock();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!renderer.enabled)
                renderer.enabled = true;

            renderer.sharedMaterial = material;
            renderer.GetPropertyBlock(props);

            if (material.HasProperty("_BaseColor"))
                props.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                props.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
                props.SetColor("_EmissionColor", color * Mathf.Max(0f, emission));

            renderer.SetPropertyBlock(props);

            if (disableShadows)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }

    private static Material GetSolidMaterial()
    {
        if (solidMaterial != null)
            return solidMaterial;

        Shader shader = null;
        RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();
        if (pipeline != null)
        {
            shader = FindSupportedShader(
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit"
            );

            if (shader == null && pipeline.defaultMaterial != null && pipeline.defaultMaterial.shader != null && pipeline.defaultMaterial.shader.isSupported)
                shader = pipeline.defaultMaterial.shader;
        }
        else
        {
            shader = FindSupportedShader(
                "Standard",
                "Sprites/Default"
            );
        }

        if (shader == null)
        {
            LogMissingMaterialWarning();
            return null;
        }

        solidMaterial = new Material(shader)
        {
            name = "RelicGeneratedSolidMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (solidMaterial.HasProperty("_Glossiness"))
            solidMaterial.SetFloat("_Glossiness", 0.1f);

        return solidMaterial;
    }

    private static Material GetDarkMaterial()
    {
        if (darkMaterial != null)
            return darkMaterial;

        Shader shader = null;
        RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();
        if (pipeline != null)
        {
            shader = FindSupportedShader(
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit"
            );

            if (shader == null && pipeline.defaultMaterial != null && pipeline.defaultMaterial.shader != null && pipeline.defaultMaterial.shader.isSupported)
                shader = pipeline.defaultMaterial.shader;
        }
        else
        {
            shader = FindSupportedShader(
                "Standard",
                "Sprites/Default"
            );
        }

        if (shader == null)
        {
            LogMissingMaterialWarning();
            return null;
        }

        darkMaterial = new Material(shader)
        {
            name = "RelicGeneratedDarkMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (darkMaterial.HasProperty("_Glossiness"))
            darkMaterial.SetFloat("_Glossiness", 0f);

        return darkMaterial;
    }

    private static Material GetAreaRingMaterial()
    {
        if (areaRingMaterial != null)
            return areaRingMaterial;

        areaRingMaterial = CreateAreaOverlayMaterial();
        if (areaRingMaterial.HasProperty("_InnerAlpha"))
            areaRingMaterial.SetFloat("_InnerAlpha", 0.012f);
        if (areaRingMaterial.HasProperty("_EdgeAlpha"))
            areaRingMaterial.SetFloat("_EdgeAlpha", 0.88f);
        if (areaRingMaterial.HasProperty("_EdgeWidth"))
            areaRingMaterial.SetFloat("_EdgeWidth", 0.072f);
        if (areaRingMaterial.HasProperty("_EdgeSoftness"))
            areaRingMaterial.SetFloat("_EdgeSoftness", 0.03f);
        if (areaRingMaterial.HasProperty("_NoiseScale"))
            areaRingMaterial.SetFloat("_NoiseScale", 3.2f);
        if (areaRingMaterial.HasProperty("_NoiseStrength"))
            areaRingMaterial.SetFloat("_NoiseStrength", 0.09f);
        if (areaRingMaterial.HasProperty("_FlowSpeed"))
            areaRingMaterial.SetFloat("_FlowSpeed", 0.16f);
        if (areaRingMaterial.HasProperty("_Emission"))
            areaRingMaterial.SetFloat("_Emission", 0.65f);

        return areaRingMaterial;
    }

    private static Material GetAreaFogMaterial()
    {
        if (areaFogMaterial != null)
            return areaFogMaterial;

        areaFogMaterial = CreateAreaOverlayMaterial();
        if (areaFogMaterial.HasProperty("_InnerAlpha"))
            areaFogMaterial.SetFloat("_InnerAlpha", 0.025f);
        if (areaFogMaterial.HasProperty("_EdgeAlpha"))
            areaFogMaterial.SetFloat("_EdgeAlpha", 0.22f);
        if (areaFogMaterial.HasProperty("_EdgeWidth"))
            areaFogMaterial.SetFloat("_EdgeWidth", 0.16f);
        if (areaFogMaterial.HasProperty("_EdgeSoftness"))
            areaFogMaterial.SetFloat("_EdgeSoftness", 0.08f);
        if (areaFogMaterial.HasProperty("_NoiseScale"))
            areaFogMaterial.SetFloat("_NoiseScale", 5.8f);
        if (areaFogMaterial.HasProperty("_NoiseStrength"))
            areaFogMaterial.SetFloat("_NoiseStrength", 0.2f);
        if (areaFogMaterial.HasProperty("_FlowSpeed"))
            areaFogMaterial.SetFloat("_FlowSpeed", 0.28f);
        if (areaFogMaterial.HasProperty("_Emission"))
            areaFogMaterial.SetFloat("_Emission", 0.08f);

        return areaFogMaterial;
    }

    private static Material CreateAreaOverlayMaterial()
    {
        Shader shader = null;
        RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();
        if (pipeline != null)
        {
            shader = FindSupportedShader(
                AreaOverlayShaderName,
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit"
            );

            if (shader == null && pipeline.defaultMaterial != null && pipeline.defaultMaterial.shader != null && pipeline.defaultMaterial.shader.isSupported)
                shader = pipeline.defaultMaterial.shader;
        }
        else
        {
            shader = FindSupportedShader(
                AreaOverlayShaderName,
                "Unlit/Color",
                "Standard"
            );
        }

        if (shader == null)
        {
            LogMissingMaterialWarning();
            return null;
        }

        Material mat = new(shader)
        {
            name = "RelicAreaOverlayMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);

        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    private static Material GetTranslucentMaterial()
    {
        if (translucentMaterial != null)
            return translucentMaterial;

        Shader shader = null;
        RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();
        if (pipeline != null)
        {
            shader = FindSupportedShader(
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit"
            );

            if (shader == null && pipeline.defaultMaterial != null && pipeline.defaultMaterial.shader != null && pipeline.defaultMaterial.shader.isSupported)
                shader = pipeline.defaultMaterial.shader;
        }
        else
        {
            shader = FindSupportedShader(
                "Unlit/Color",
                "Standard"
            );
        }

        if (shader == null)
        {
            LogMissingMaterialWarning();
            return null;
        }

        translucentMaterial = new Material(shader)
        {
            name = "RelicGeneratedTranslucentMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (translucentMaterial.HasProperty("_Surface"))
            translucentMaterial.SetFloat("_Surface", 1f);
        if (translucentMaterial.HasProperty("_Blend"))
            translucentMaterial.SetFloat("_Blend", 0f);
        if (translucentMaterial.HasProperty("_ZWrite"))
            translucentMaterial.SetFloat("_ZWrite", 0f);

        if (translucentMaterial.HasProperty("_SrcBlend"))
            translucentMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (translucentMaterial.HasProperty("_DstBlend"))
            translucentMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        translucentMaterial.renderQueue = (int)RenderQueue.Transparent;
        return translucentMaterial;
    }

    private static Shader FindSupportedShader(params string[] shaderNames)
    {
        if (shaderNames == null)
            return null;

        for (int i = 0; i < shaderNames.Length; i++)
        {
            string shaderName = shaderNames[i];
            if (string.IsNullOrWhiteSpace(shaderName))
                continue;

            Shader shader = Shader.Find(shaderName);
            if (shader != null && shader.isSupported)
                return shader;
        }

        return null;
    }

    private static RenderPipelineAsset ResolveActiveRenderPipeline()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null)
            return pipeline;

        return GraphicsSettings.defaultRenderPipeline;
    }

    private static void LogMissingMaterialWarning()
    {
        if (missingMaterialWarningLogged)
            return;

        missingMaterialWarningLogged = true;
        Debug.LogWarning("Relic generated VFX material disabled: no compatible shader/material found.");
    }
}

internal sealed class RelicPulseVfx : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Renderer cachedRenderer;
    private float elapsed;
    private float lifetime;
    private float fromScale;
    private float toScale;
    private Color baseColor;
    private MaterialPropertyBlock props;

    private void Awake()
    {
        EnsurePropertyBlock();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public void Bind(Renderer cachedRenderer)
    {
        this.cachedRenderer = cachedRenderer;
    }

    public void Play(Color color, float startScale, float duration, float endScale)
    {
        EnsurePropertyBlock();

        if (cachedRenderer == null)
            cachedRenderer = GetComponent<Renderer>();

        baseColor = color;
        elapsed = 0f;
        lifetime = Mathf.Max(0.03f, duration);
        fromScale = Mathf.Max(0.01f, startScale);
        toScale = Mathf.Max(fromScale + 0.01f, endScale);
        transform.localScale = Vector3.one * fromScale;
        SetColor(color);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && lifetime > 0f;

    public float BatchedUpdateInterval => 0.016f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        elapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(elapsed / lifetime);
        transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, t);

        Color c = baseColor;
        c.a = Mathf.Lerp(baseColor.a, 0f, t);
        SetColor(c);

        if (t >= 1f)
            RelicGeneratedPresentation.RecyclePulse(gameObject);
    }

    private void SetColor(Color color)
    {
        EnsurePropertyBlock();

        if (cachedRenderer == null)
            cachedRenderer = GetComponent<Renderer>();

        if (cachedRenderer == null)
            return;

        cachedRenderer.GetPropertyBlock(props);
        Material mat = cachedRenderer.sharedMaterial;
        if (mat != null && mat.HasProperty("_BaseColor"))
            props.SetColor("_BaseColor", color);
        if (mat != null && mat.HasProperty("_Color"))
            props.SetColor("_Color", color);
        if (mat != null && mat.HasProperty("_EmissionColor"))
            props.SetColor("_EmissionColor", color * 0.4f);
        cachedRenderer.SetPropertyBlock(props);
    }

    private void EnsurePropertyBlock()
    {
        if (props == null)
            props = new MaterialPropertyBlock();
    }
}

internal sealed class RelicOneShotAudioHub : MonoBehaviour
{
    private readonly List<AudioSource> sources = new();
    private int roundRobinIndex;

    public void Initialize(int sourceCount)
    {
        int count = Mathf.Max(4, sourceCount);
        for (int i = 0; i < count; i++)
            sources.Add(CreateSource(i));
    }

    public void Play(AudioClip clip, Vector3 worldPos, float volume, float pitch)
    {
        if (clip == null)
            return;

        AudioSource src = NextSource();
        if (src == null)
            return;

        src.transform.position = worldPos;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Clamp(pitch, 0.8f, 1.25f);
        src.Play();
    }

    private AudioSource NextSource()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            int idx = (roundRobinIndex + i) % sources.Count;
            if (sources[idx] != null && !sources[idx].isPlaying)
            {
                roundRobinIndex = (idx + 1) % sources.Count;
                return sources[idx];
            }
        }

        int fallback = roundRobinIndex % Mathf.Max(1, sources.Count);
        roundRobinIndex = (fallback + 1) % Mathf.Max(1, sources.Count);
        return sources.Count > 0 ? sources[fallback] : null;
    }

    private AudioSource CreateSource(int index)
    {
        GameObject go = new($"RelicSfx_{index + 1}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0.65f;
        src.minDistance = 1.5f;
        src.maxDistance = 20f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.dopplerLevel = 0f;
        return src;
    }
}
