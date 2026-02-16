using UnityEngine;
using GrassSim.Enhancers;

[RequireComponent(typeof(Renderer))]
public class WeaponEmissionController : MonoBehaviour
{
    [Header("Emission Intensity")]
    [SerializeField] private float minIntensity = 0f;
    [SerializeField] private float maxIntensity = 4f;
    [SerializeField] private float smoothSpeed = 8f;

    private WeaponEnhancerSystem enhancerSystem;
    private Renderer rend;
    private MaterialPropertyBlock mpb;

    private Color currentEmission = Color.black;
    private Color targetEmission = Color.black;

    private static readonly int EmissionColorID =
        Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        enhancerSystem = GetComponentInParent<WeaponEnhancerSystem>();
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();

        if (enhancerSystem != null)
            enhancerSystem.OnChanged += RecalculateTargetEmission;

        // start clean
        ApplyEmission(Color.black);
    }

    private void OnDestroy()
    {
        if (enhancerSystem != null)
            enhancerSystem.OnChanged -= RecalculateTargetEmission;
    }

    private void Update()
    {
        // estetyczne wygładzenie
        currentEmission = Color.Lerp(
            currentEmission,
            targetEmission,
            Time.deltaTime * smoothSpeed
        );

        ApplyEmission(currentEmission);
    }

    // ===========================
    // 🔥 CORE LOGIC
    // ===========================

    private void RecalculateTargetEmission()
    {
        if (enhancerSystem == null || enhancerSystem.Active.Count == 0)
        {
            targetEmission = Color.black;
            return;
        }

        float hueX = 0f;
        float hueY = 0f;

        float saturationSum = 0f;
        int colorCount = 0;

        float totalStrength = 0f;

        foreach (var enhancer in enhancerSystem.Active)
        {
            if (enhancer?.Definition == null)
                continue;

            // 🔑 KOLOR: liczymy TYLKO RAZ na enhancer
            Color c = enhancer.Definition.emissionColor;
            Color.RGBToHSV(c, out float h, out float s, out float v);

            float angle = h * Mathf.PI * 2f;
            hueX += Mathf.Cos(angle);
            hueY += Mathf.Sin(angle);

            saturationSum += s;
            colorCount++;

            // 🔥 SIŁA: stacki liczą się TYLKO do intensywności
            totalStrength += enhancer.GetStrength01();
        }

        if (colorCount == 0)
        {
            targetEmission = Color.black;
            return;
        }

        float blendedHue = Mathf.Atan2(hueY, hueX) / (2f * Mathf.PI);
        if (blendedHue < 0f) blendedHue += 1f;

        float blendedSaturation = Mathf.Clamp01(saturationSum / colorCount);

        Color blendedColor = Color.HSVToRGB(
            blendedHue,
            blendedSaturation,
            1f
        );

        float intensity = Mathf.Lerp(
            minIntensity,
            maxIntensity,
            Mathf.Clamp01(totalStrength)
        );

        targetEmission = blendedColor * intensity;
    }

    private void ApplyEmission(Color emission)
    {
        rend.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionColorID, emission);
        rend.SetPropertyBlock(mpb);
    }
}
