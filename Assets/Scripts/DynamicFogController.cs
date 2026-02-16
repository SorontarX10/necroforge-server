using UnityEngine;

[RequireComponent(typeof(Camera))]
public class DynamicFogController : MonoBehaviour
{
    [Header("Atmospheric fog (near)")]
    [Tooltip("Odległość, od której zaczyna być widoczna mgła")]
    public float fogStart = 10f;

    [Tooltip("Odległość, gdzie mgła zaczyna mocno gęstnieć")]
    public float fogCutoffStart = 35f;

    [Tooltip("Odległość, gdzie mgła jest w 100% nieprzenikalna")]
    public float fogCutoffEnd = 45f;

    [Header("Smoothing")]
    public float smoothSpeed = 3f;

    void Start()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.045f;
    }

    void Update()
    {
        RenderSettings.fogStartDistance = Mathf.Lerp(
            RenderSettings.fogStartDistance,
            fogCutoffStart,
            Time.deltaTime * smoothSpeed
        );

        RenderSettings.fogEndDistance = Mathf.Lerp(
            RenderSettings.fogEndDistance,
            fogCutoffEnd,
            Time.deltaTime * smoothSpeed
        );
    }
}
