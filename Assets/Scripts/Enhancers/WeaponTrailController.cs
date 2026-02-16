using UnityEngine;
using GrassSim.Enhancers;
using GrassSim.Combat;

[RequireComponent(typeof(TrailRenderer))]
public class WeaponTrailController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform trailBase;
    [SerializeField] private Transform trailTip;

    [Header("Tuning")]
    [SerializeField] private float maxAlpha = 0.85f;
    [SerializeField] private float intensityMultiplier = 1.2f;
    [SerializeField] private float smooth = 10f;

    private TrailRenderer trail;
    private WeaponEnhancerSystem enhancerSystem;
    private ICombatInput combatInput;

    private Color targetColor = Color.white;
    private Color currentColor = Color.white;

    private float targetAlpha = 0f;
    private float currentAlpha = 0f;

    private void Awake()
    {
        trail = GetComponent<TrailRenderer>();

        enhancerSystem = GetComponentInParent<WeaponEnhancerSystem>();
        combatInput = GetComponentInParent<ICombatInput>();

        if (trailBase == null || trailTip == null)
        {
            Debug.LogError(
                "[WeaponTrailController] Missing Trail_Base or Trail_Tip reference",
                this
            );
            enabled = false;
            return;
        }

        // Trail rysuje TYLKO z tipa
        transform.position = trailTip.position;

        trail.emitting = false;
        trail.enabled = true;

        if (enhancerSystem != null)
        {
            enhancerSystem.OnChanged += RecalculateTarget;
        }

        RecalculateTarget();
    }

    private void OnDestroy()
    {
        if (enhancerSystem != null)
            enhancerSystem.OnChanged -= RecalculateTarget;
    }

    private void LateUpdate()
    {
        // 1️⃣ Trail tylko gdy atakujemy
        bool attacking = combatInput != null && combatInput.IsAttacking();
        trail.emitting = attacking;

        if (!attacking)
            return;

        // 2️⃣ Stabilizacja pozycji i kierunku traila
        Vector3 dir = (trailTip.position - trailBase.position).normalized;
        transform.position = trailTip.position;
        transform.rotation = Quaternion.LookRotation(dir);

        // 3️⃣ Smooth koloru i alphy
        currentColor = Color.Lerp(
            currentColor,
            targetColor,
            Time.deltaTime * smooth
        );

        currentAlpha = Mathf.Lerp(
            currentAlpha,
            targetAlpha,
            Time.deltaTime * smooth
        );

        ApplyTrail(currentColor, currentAlpha);
    }

    private void RecalculateTarget()
    {
        // Domyślnie: biały trail
        Color mixed = Color.white;
        float strength = 0f;
        bool anyEnhancer = false;

        if (enhancerSystem != null)
        {
            mixed = Color.black;
            strength = 0f;

            foreach (var a in enhancerSystem.Active)
            {
                if (a == null || a.Definition == null)
                    continue;

                float s = a.GetStrength01();
                mixed += a.Definition.emissionColor * s;
                strength += s;
                anyEnhancer = true;
            }
        }

        if (!anyEnhancer)
        {
            targetColor = Color.white;
            targetAlpha = maxAlpha * 0.5f;
            return;
        }

        mixed /= Mathf.Max(1f, strength);
        targetColor = mixed;
        targetAlpha = Mathf.Clamp01(strength * intensityMultiplier) * maxAlpha;
    }

    private void ApplyTrail(Color color, float alpha)
    {
        color.a = alpha;

        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
    }
}
