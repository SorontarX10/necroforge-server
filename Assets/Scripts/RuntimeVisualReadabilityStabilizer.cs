using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-9500)]
public sealed class RuntimeVisualReadabilityStabilizer : MonoBehaviour
{
    private static RuntimeVisualReadabilityStabilizer instance;

    [Header("Refresh")]
    [SerializeField, Min(0.1f)] private float applyInterval = 0.5f;

    [Header("Fog Guardrails")]
    [SerializeField, Min(0f)] private float maxFogDensity = 0.03f;
    [SerializeField, Min(0f)] private float minimumFogStartDistance = 18f;
    [SerializeField, Min(0f)] private float minimumFogEndDistance = 44f;
    [SerializeField, Range(0f, 1f)] private float minimumFogColorLuminance = 0.08f;
    [SerializeField, Range(0f, 1f)] private float maximumFogColorLuminance = 0.38f;

    [Header("Environment Lighting Guardrails")]
    [SerializeField] private bool clampEnvironmentLighting = true;
    [SerializeField, Min(0f)] private float minimumAmbientIntensity = 0.85f;
    [SerializeField, Min(0f)] private float maximumAmbientIntensity = 1.35f;
    [SerializeField, Min(0f)] private float minimumReflectionIntensity = 0.65f;
    [SerializeField, Min(0f)] private float maximumReflectionIntensity = 1.25f;
    [SerializeField, Min(0f)] private float minimumDirectionalLightIntensity = 0.75f;
    [SerializeField, Min(0f)] private float maximumDirectionalLightIntensity = 1.6f;
    [SerializeField, Range(0f, 1f)] private float maximumDirectionalShadowStrength = 0.78f;

    [Header("Post Process Guardrails")]
    [SerializeField] private Vector2 postExposureRange = new Vector2(-0.35f, 0.45f);
    [SerializeField] private Vector2 contrastRange = new Vector2(-8f, 15f);
    [SerializeField] private Vector2 bloomIntensityRange = new Vector2(0f, 2.2f);
    [SerializeField] private Vector2 bloomThresholdRange = new Vector2(0.8f, 1.6f);
    [SerializeField] private Vector2 vignetteIntensityRange = new Vector2(0f, 0.35f);
    [SerializeField] private Vector2 vignetteSmoothnessRange = new Vector2(0.2f, 0.75f);

    private readonly HashSet<VolumeProfile> processedProfiles = new(16);
    private float nextApplyAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static RuntimeVisualReadabilityStabilizer EnsureInstance()
    {
        if (instance != null)
            return instance;

        RuntimeVisualReadabilityStabilizer existing = Object.FindFirstObjectByType<RuntimeVisualReadabilityStabilizer>();
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        GameObject go = new GameObject("RuntimeVisualReadabilityStabilizer");
        return go.AddComponent<RuntimeVisualReadabilityStabilizer>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyGuardrails();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now < nextApplyAt)
            return;

        nextApplyAt = now + Mathf.Max(0.1f, applyInterval);
        ApplyGuardrails();
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        nextApplyAt = 0f;
        ApplyGuardrails();
    }

    private void ApplyGuardrails()
    {
        ClampEnvironmentLighting();
        ClampRenderSettingsFog();

        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        processedProfiles.Clear();

        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume == null || !volume.enabled || volume.profile == null)
                continue;

            if (!processedProfiles.Add(volume.profile))
                continue;

            ClampProfile(volume.profile);
        }
    }

    private void ClampRenderSettingsFog()
    {
        if (!RenderSettings.fog)
            return;

        RenderSettings.fogDensity = Mathf.Min(Mathf.Max(0f, RenderSettings.fogDensity), Mathf.Max(0f, maxFogDensity));
        RenderSettings.fogColor = ClampColorLuminance(
            RenderSettings.fogColor,
            minimumFogColorLuminance,
            maximumFogColorLuminance
        );

        if (RenderSettings.fogMode != FogMode.Linear)
            return;

        float start = Mathf.Max(RenderSettings.fogStartDistance, minimumFogStartDistance);
        float end = Mathf.Max(RenderSettings.fogEndDistance, minimumFogEndDistance, start + 8f);
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance = end;
    }

    private void ClampEnvironmentLighting()
    {
        if (!clampEnvironmentLighting)
            return;

        float minAmbient = Mathf.Min(minimumAmbientIntensity, maximumAmbientIntensity);
        float maxAmbient = Mathf.Max(minimumAmbientIntensity, maximumAmbientIntensity);
        RenderSettings.ambientIntensity = Mathf.Clamp(RenderSettings.ambientIntensity, minAmbient, maxAmbient);

        float minReflection = Mathf.Min(minimumReflectionIntensity, maximumReflectionIntensity);
        float maxReflection = Mathf.Max(minimumReflectionIntensity, maximumReflectionIntensity);
        RenderSettings.reflectionIntensity = Mathf.Clamp(RenderSettings.reflectionIntensity, minReflection, maxReflection);

        float minDirectional = Mathf.Min(minimumDirectionalLightIntensity, maximumDirectionalLightIntensity);
        float maxDirectional = Mathf.Max(minimumDirectionalLightIntensity, maximumDirectionalLightIntensity);
        float maxShadowStrength = Mathf.Clamp01(maximumDirectionalShadowStrength);

        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || !light.enabled || light.type != LightType.Directional)
                continue;

            light.intensity = Mathf.Clamp(light.intensity, minDirectional, maxDirectional);
            light.shadowStrength = Mathf.Min(light.shadowStrength, maxShadowStrength);
        }
    }

    private void ClampProfile(VolumeProfile profile)
    {
        if (profile.TryGet(out ColorAdjustments colorAdjustments))
        {
            ClampFloat(colorAdjustments.postExposure, postExposureRange.x, postExposureRange.y);
            ClampFloat(colorAdjustments.contrast, contrastRange.x, contrastRange.y);
        }

        if (profile.TryGet(out Bloom bloom))
        {
            ClampFloat(bloom.intensity, bloomIntensityRange.x, bloomIntensityRange.y);
            ClampFloat(bloom.threshold, bloomThresholdRange.x, bloomThresholdRange.y);
        }

        if (profile.TryGet(out Vignette vignette))
        {
            ClampFloat(vignette.intensity, vignetteIntensityRange.x, vignetteIntensityRange.y);
            ClampFloat(vignette.smoothness, vignetteSmoothnessRange.x, vignetteSmoothnessRange.y);
        }
    }

    private static void ClampFloat(FloatParameter parameter, float min, float max)
    {
        if (parameter == null || !parameter.overrideState)
            return;

        float low = Mathf.Min(min, max);
        float high = Mathf.Max(min, max);
        parameter.value = Mathf.Clamp(parameter.value, low, high);
    }

    private static Color ClampColorLuminance(Color color, float minLuminance, float maxLuminance)
    {
        float low = Mathf.Clamp01(Mathf.Min(minLuminance, maxLuminance));
        float high = Mathf.Clamp01(Mathf.Max(minLuminance, maxLuminance));
        float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;

        if (luminance <= 0.0001f)
        {
            float v = low;
            return new Color(v, v, v, color.a);
        }

        float target = Mathf.Clamp(luminance, low, high);
        float scale = target / luminance;

        return new Color(
            Mathf.Clamp01(color.r * scale),
            Mathf.Clamp01(color.g * scale),
            Mathf.Clamp01(color.b * scale),
            color.a
        );
    }
}
