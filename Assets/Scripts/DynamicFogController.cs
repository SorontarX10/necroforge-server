using UnityEngine;

[RequireComponent(typeof(Camera))]
public class DynamicFogController : MonoBehaviour
{
    [Header("Atmospheric Fog")]
    [Tooltip("Target fog start distance in meters.")]
    [Min(0f)] public float fogStart = 22f;

    [Tooltip("Distance where fog begins to thicken.")]
    [Min(0f)] public float fogCutoffStart = 38f;

    [Tooltip("Distance where fog reaches full occlusion.")]
    [Min(0f)] public float fogCutoffEnd = 62f;

    [Header("Readability Guardrails")]
    [SerializeField] private FogMode fogMode = FogMode.Linear;
    [SerializeField, Min(0f)] private float fogDensity = 0.02f;
    [SerializeField, Min(0f)] private float minimumFogStartDistance = 18f;
    [SerializeField, Min(0f)] private float minimumFogEndDistance = 44f;

    [Header("Smoothing")]
    [SerializeField, Min(0.01f)] private float smoothSpeed = 3f;

    void Start()
    {
        SanitizeConfig();
        RenderSettings.fog = true;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;

        float targetStart = GetTargetFogStart();
        float targetEnd = GetTargetFogEnd(targetStart);
        RenderSettings.fogStartDistance = targetStart;
        RenderSettings.fogEndDistance = targetEnd;
    }

    void Update()
    {
        SanitizeConfig();

        float targetStart = GetTargetFogStart();
        float targetEnd = GetTargetFogEnd(targetStart);

        RenderSettings.fogStartDistance = Mathf.Lerp(
            RenderSettings.fogStartDistance,
            targetStart,
            Time.deltaTime * smoothSpeed
        );

        RenderSettings.fogEndDistance = Mathf.Lerp(
            RenderSettings.fogEndDistance,
            targetEnd,
            Time.deltaTime * smoothSpeed
        );

        RenderSettings.fogDensity = Mathf.Max(0f, fogDensity);
    }

    private float GetTargetFogStart()
    {
        return Mathf.Max(fogStart, fogCutoffStart, minimumFogStartDistance);
    }

    private float GetTargetFogEnd(float targetStart)
    {
        float end = Mathf.Max(fogCutoffEnd, minimumFogEndDistance);
        return Mathf.Max(end, targetStart + 8f);
    }

    private void SanitizeConfig()
    {
        fogStart = Mathf.Max(0f, fogStart);
        fogCutoffStart = Mathf.Max(0f, fogCutoffStart);
        fogCutoffEnd = Mathf.Max(0f, fogCutoffEnd);
        minimumFogStartDistance = Mathf.Max(0f, minimumFogStartDistance);
        minimumFogEndDistance = Mathf.Max(0f, minimumFogEndDistance);
        fogDensity = Mathf.Max(0f, fogDensity);
        smoothSpeed = Mathf.Max(0.01f, smoothSpeed);
    }

    private void OnValidate()
    {
        SanitizeConfig();
    }
}
