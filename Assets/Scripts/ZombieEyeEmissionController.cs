using System.Collections.Generic;
using GrassSim.Combat;
using GrassSim.Enemies;
using UnityEngine;

[DisallowMultipleComponent]
public class ZombieEyeEmissionController : MonoBehaviour
{
    [Header("Enemy References")]
    public EnemyCombatant enemyCombatant; // optional assignment

    [Header("Emission Settings")]
    [Tooltip("When enabled, full-health emission hue is derived from selected eye materials.")]
    [SerializeField] private bool deriveColorFromEyeMaterial = true;
    public Color fullHealthEmissionColor = Color.green;
    public Color zeroHealthEmissionColor = Color.black;
    [Tooltip("HDR intensity at full health")]
    public float maxEmissionIntensity = 3f;
    [Tooltip("Minimum visible intensity near zero health")]
    public float minEmissionIntensity = 0f;
    [SerializeField, Min(0.1f)] private float globalEmissionIntensityCap = 4.5f;
    [SerializeField, Min(0f)] private float globalMinimumIntensityFloor = 0.08f;
    [SerializeField, Min(0.1f)] private float emissionSmoothing = 3.5f;

    [Header("Targeting (optional overrides)")]
    [SerializeField] private Renderer[] eyeRenderers;
    [SerializeField] private Material[] explicitEyeMaterials;
    [SerializeField] private string[] eyeNameHints = { "eye", "glow", "pupil", "socket" };
    [SerializeField] private bool allowRendererNameHintFallback;
    [SerializeField] private bool allowEmissionScoreFallback = true;
    [SerializeField, Min(0.05f)] private float fallbackPollInterval = 0.12f;

    private struct EmissionTarget
    {
        public Renderer renderer;
        public int materialIndex;
        public MaterialPropertyBlock props;
    }

    private struct Candidate
    {
        public Renderer renderer;
        public int materialIndex;
        public float score;
        public bool explicitMatch;
        public bool materialHinted;
        public bool rendererHinted;
    }

    private readonly List<EmissionTarget> targets = new(4);
    private readonly List<Candidate> candidates = new(16);

    private Combatant combatant;
    private int emissionColorID;
    private float lastHealth01 = -1f;
    private float currentEmissionIntensity = -1f;
    private float nextPollAt;
    private bool subscribed;
    private bool hasEliteColorOverride;
    private Color eliteOverrideColor = Color.yellow;

    private void Awake()
    {
        emissionColorID = Shader.PropertyToID("_EmissionColor");
        ResolveCombatant();
        ResolveTargets();
        ForceRefresh();
    }

    private void OnEnable()
    {
        ResolveCombatant();
        ResolveTargets();
        TrySubscribe();
        ForceRefresh();
    }

    private void OnDisable()
    {
        TryUnsubscribe();
    }

    private void Update()
    {
        // Fallback for HP changes made outside Combatant.OnHealthChanged.
        float now = Time.time;
        if (now < nextPollAt)
            return;

        nextPollAt = now + Mathf.Max(0.05f, fallbackPollInterval);
        RefreshIfChanged();
    }

    private void OnDestroy()
    {
        TryUnsubscribe();
    }

    public void SetEliteColorOverride(bool enabled, Color color)
    {
        hasEliteColorOverride = enabled;
        eliteOverrideColor = color;
        ForceRefresh();
    }

    private void ResolveCombatant()
    {
        if (enemyCombatant == null)
            enemyCombatant = GetComponentInParent<EnemyCombatant>();

        if (enemyCombatant != null)
            combatant = enemyCombatant.GetComponent<Combatant>();

        if (combatant == null)
            combatant = GetComponentInParent<Combatant>();

        if (combatant == null)
            Debug.LogWarning("[ZombieEyeEmission] Combatant not found!", this);
    }

    private void TrySubscribe()
    {
        if (subscribed || combatant == null)
            return;

        combatant.OnHealthChanged += OnHealthChanged;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || combatant == null)
            return;

        combatant.OnHealthChanged -= OnHealthChanged;
        subscribed = false;
    }

    private void OnHealthChanged()
    {
        RefreshIfChanged();
    }

    private void ResolveTargets()
    {
        targets.Clear();
        candidates.Clear();

        Renderer[] renderers = (eyeRenderers != null && eyeRenderers.Length > 0)
            ? eyeRenderers
            : GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("[ZombieEyeEmission] No Renderer found in children!", this);
            return;
        }

        bool hasExplicitCandidate = false;
        bool hasMaterialHintedCandidate = false;
        bool hasRendererHintedCandidate = false;
        float bestScore = float.MinValue;

        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;

            bool rendererHinted = IsHinted(renderer.name);
            if (rendererHinted)
                hasRendererHintedCandidate = true;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null || !mat.HasProperty("_EmissionColor"))
                    continue;

                Color baseColor = mat.GetColor("_EmissionColor");
                float score = Mathf.Max(0.001f, baseColor.maxColorComponent);
                bool explicitMatch = MatchesExplicitEyeMaterial(mat);
                bool materialHinted = IsHinted(mat.name);

                if (explicitMatch)
                    hasExplicitCandidate = true;

                if (materialHinted)
                    hasMaterialHintedCandidate = true;

                bestScore = Mathf.Max(bestScore, score);

                candidates.Add(new Candidate
                {
                    renderer = renderer,
                    materialIndex = i,
                    score = score,
                    explicitMatch = explicitMatch,
                    materialHinted = materialHinted,
                    rendererHinted = rendererHinted
                });
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[ZombieEyeEmission] Could not determine eye material!", this);
            return;
        }

        float threshold = Mathf.Max(0.001f, bestScore * 0.9f);
        Vector3 accumulatedColor = Vector3.zero;
        int accumulatedCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate c = candidates[i];
            if (hasExplicitCandidate)
            {
                if (!c.explicitMatch)
                    continue;
            }
            else if (hasMaterialHintedCandidate)
            {
                if (!c.materialHinted)
                    continue;
            }
            else if (allowRendererNameHintFallback && hasRendererHintedCandidate)
            {
                if (!c.rendererHinted)
                    continue;
            }
            else if (allowEmissionScoreFallback)
            {
                if (c.score < threshold)
                    continue;
            }
            else
            {
                continue;
            }

            Material[] runtimeMats = c.renderer.materials;
            if (runtimeMats == null || c.materialIndex < 0 || c.materialIndex >= runtimeMats.Length)
                continue;

            Material runtimeMat = runtimeMats[c.materialIndex];
            if (runtimeMat == null || !runtimeMat.HasProperty("_EmissionColor"))
                continue;

            runtimeMat.EnableKeyword("_EMISSION");

            if (deriveColorFromEyeMaterial && TryGetMaterialColorSeed(runtimeMat, out Color seed))
            {
                accumulatedColor += new Vector3(seed.r, seed.g, seed.b);
                accumulatedCount++;
            }

            targets.Add(new EmissionTarget
            {
                renderer = c.renderer,
                materialIndex = c.materialIndex,
                props = new MaterialPropertyBlock()
            });
        }

        if (deriveColorFromEyeMaterial && accumulatedCount > 0)
        {
            Vector3 avg = accumulatedColor / accumulatedCount;
            fullHealthEmissionColor = new Color(avg.x, avg.y, avg.z, 1f);
        }

        if (targets.Count == 0)
            Debug.LogWarning("[ZombieEyeEmission] No valid eye targets selected. Assign explicitEyeMaterials or eyeRenderers.", this);
    }

    private static bool TryGetMaterialColorSeed(Material mat, out Color seed)
    {
        seed = Color.black;
        if (mat == null)
            return false;

        if (mat.HasProperty("_EmissionColor"))
        {
            Color emission = mat.GetColor("_EmissionColor");
            if (TryNormalizeColor(emission, out seed))
                return true;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            Color baseColor = mat.GetColor("_BaseColor");
            if (TryNormalizeColor(baseColor, out seed))
                return true;
        }

        if (mat.HasProperty("_Color"))
        {
            Color color = mat.GetColor("_Color");
            if (TryNormalizeColor(color, out seed))
                return true;
        }

        return false;
    }

    private static bool TryNormalizeColor(Color input, out Color normalized)
    {
        float max = Mathf.Max(input.r, Mathf.Max(input.g, input.b));
        if (max <= 0.0001f)
        {
            normalized = Color.black;
            return false;
        }

        normalized = new Color(input.r / max, input.g / max, input.b / max, 1f);
        return true;
    }

    private bool MatchesExplicitEyeMaterial(Material mat)
    {
        if (mat == null || explicitEyeMaterials == null || explicitEyeMaterials.Length == 0)
            return false;

        for (int i = 0; i < explicitEyeMaterials.Length; i++)
        {
            Material explicitMat = explicitEyeMaterials[i];
            if (explicitMat != null && ReferenceEquals(explicitMat, mat))
                return true;
        }

        return false;
    }

    private bool IsHinted(string text)
    {
        string normalized = text != null ? text.ToLowerInvariant() : string.Empty;
        if (eyeNameHints == null || eyeNameHints.Length == 0)
            return false;

        for (int i = 0; i < eyeNameHints.Length; i++)
        {
            string hint = eyeNameHints[i];
            if (string.IsNullOrWhiteSpace(hint))
                continue;

            string key = hint.ToLowerInvariant().Trim();
            if (key.Length == 0)
                continue;

            if (normalized.Contains(key))
                return true;
        }

        return false;
    }

    private void ForceRefresh()
    {
        lastHealth01 = -1f;
        currentEmissionIntensity = -1f;
        RefreshIfChanged();
    }

    private void RefreshIfChanged()
    {
        if (combatant == null)
            return;

        if (targets.Count == 0 || TargetsMissing())
            ResolveTargets();

        if (targets.Count == 0)
            return;

        float health01 = Mathf.Clamp01(
            combatant.CurrentHealth / Mathf.Max(1f, combatant.MaxHealth)
        );

        lastHealth01 = health01;
        ApplyEmission(health01);
    }

    private bool TargetsMissing()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].renderer == null)
                return true;
        }

        return false;
    }

    private void ApplyEmission(float health01)
    {
        Color fullColor = hasEliteColorOverride ? eliteOverrideColor : fullHealthEmissionColor;
        Color color = Color.Lerp(zeroHealthEmissionColor, fullColor, health01);
        float minIntensity = Mathf.Max(globalMinimumIntensityFloor, minEmissionIntensity);
        float maxIntensity = Mathf.Min(Mathf.Max(minIntensity, maxEmissionIntensity), globalEmissionIntensityCap);
        float targetIntensity = Mathf.Lerp(
            minIntensity,
            maxIntensity,
            health01
        );

        if (currentEmissionIntensity < 0f)
        {
            currentEmissionIntensity = targetIntensity;
        }
        else
        {
            float step = Mathf.Max(0.01f, emissionSmoothing) * Mathf.Max(0.05f, fallbackPollInterval);
            currentEmissionIntensity = Mathf.MoveTowards(currentEmissionIntensity, targetIntensity, step);
        }

        Color emission = color * currentEmissionIntensity;

        for (int i = 0; i < targets.Count; i++)
        {
            EmissionTarget target = targets[i];
            if (target.renderer == null)
                continue;

            target.renderer.GetPropertyBlock(target.props, target.materialIndex);
            target.props.SetColor(emissionColorID, emission);
            target.renderer.SetPropertyBlock(target.props, target.materialIndex);
        }
    }

    private void OnValidate()
    {
        maxEmissionIntensity = Mathf.Max(0f, maxEmissionIntensity);
        minEmissionIntensity = Mathf.Max(0f, minEmissionIntensity);
        globalEmissionIntensityCap = Mathf.Max(0.1f, globalEmissionIntensityCap);
        globalMinimumIntensityFloor = Mathf.Max(0f, globalMinimumIntensityFloor);
        emissionSmoothing = Mathf.Max(0.1f, emissionSmoothing);
        fallbackPollInterval = Mathf.Max(0.05f, fallbackPollInterval);
    }
}
