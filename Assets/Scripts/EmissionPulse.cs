using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class EmissionPulse : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float minIntensity = 0.3f;
    public float maxIntensity = 1.0f;
    public float pulseSpeed = 1.2f;

    [Header("Randomization")]
    public bool randomPhase = true;

    private Material mat;
    private Color baseEmissionColor;
    private float phaseOffset;

    void Start()
    {
        mat = GetComponent<Renderer>().material;
        baseEmissionColor = mat.GetColor("_EmissionColor");

        phaseOffset = randomPhase ? Random.Range(0f, 100f) : 0f;
    }

    void Update()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed + phaseOffset);
        pulse = Mathf.InverseLerp(-1f, 1f, pulse); // 0–1

        float intensity = Mathf.Lerp(minIntensity, maxIntensity, pulse);
        mat.SetColor("_EmissionColor", baseEmissionColor * intensity);
    }
}
