using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Black Chapel Hourglass",
    fileName = "Relic_BlackChapelHourglass"
)]
public class BlackChapelHourglass : RelicEffect
{
    [Header("Charges")]
    public float chargeInterval = 18f;
    public float chargeIntervalReductionPerStack = 1f;
    [Min(1)] public int maxCharges = 2;

    [Header("Afterimage")]
    public float echoDamagePercent = 0.55f;
    public float echoDamagePerStack = 0.08f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private BlackChapelHourglassRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<BlackChapelHourglassRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<BlackChapelHourglassRuntime>();

        return rt;
    }
}

public class BlackChapelHourglassRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color ChargeTextColor = new(0.48f, 0.82f, 1f, 1f);
    private static readonly Color EchoReadyTextColor = new(0.72f, 0.52f, 1f, 1f);
    private static readonly Color EchoTriggerTextColor = new(1f, 0.78f, 0.28f, 1f);
    private const float FloatingTextHeight = 1.95f;
    private const float FloatingTextSize = 30f;
    private const float EchoStrikeDelaySeconds = 0.07f;
    private const float EchoGhostLifetimeSeconds = 0.55f;

    private PlayerRelicController player;
    private BlackChapelHourglass cfg;
    private WeaponController weapon;
    private ICombatInput combatInput;
    private int stacks;
    private bool subscribed;

    private int charges;
    private float nextChargeAt;
    private float lastHitDamage;
    private float pendingEchoDamage;
    private bool applyingEcho;
    private Coroutine echoStrikeRoutine;
    private HourglassEchoSwordGhostVfx ghostSwordVfx;

    public int CurrentCharges => charges;
    public int MaxCharges => cfg != null ? Mathf.Max(1, cfg.maxCharges) : 0;
    public bool HasPendingEcho => pendingEchoDamage > 0f;
    public float PendingEchoDamage => Mathf.Max(0f, pendingEchoDamage);
    public float LastHitDamage => Mathf.Max(0f, lastHitDamage);
    public bool IsEchoVisualActive => ghostSwordVfx != null && ghostSwordVfx.IsPlaying;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
        weapon = GetComponentInChildren<WeaponController>(true);
        combatInput = ResolveCombatInput();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        TrySubscribe();
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        TryUnsubscribe();

        if (echoStrikeRoutine != null)
        {
            StopCoroutine(echoStrikeRoutine);
            echoStrikeRoutine = null;
        }

        applyingEcho = false;
        if (ghostSwordVfx != null)
            ghostSwordVfx.StopImmediately();
    }

    public void Configure(BlackChapelHourglass config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        if (nextChargeAt <= 0f)
        {
            float interval = Mathf.Max(
                2f,
                cfg.chargeInterval - cfg.chargeIntervalReductionPerStack * Mathf.Max(0, stacks - 1)
            );
            nextChargeAt = Time.time + interval;
        }

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        int max = Mathf.Max(1, cfg.maxCharges);
        if (charges >= max)
            return;

        if (now < nextChargeAt)
            return;

        charges = Mathf.Min(max, charges + 1);
        SpawnFloatingText($"+Charge {charges}/{max}", ChargeTextColor);
        float interval = Mathf.Max(
            2f,
            cfg.chargeInterval - cfg.chargeIntervalReductionPerStack * Mathf.Max(0, stacks - 1)
        );
        nextChargeAt = now + interval;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDodged += OnDodged;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDodged -= OnDodged;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnDodged()
    {
        if (cfg == null || charges <= 0 || lastHitDamage <= 0f)
            return;

        charges--;
        float echoMultiplier = cfg.echoDamagePercent + cfg.echoDamagePerStack * Mathf.Max(0, stacks - 1);
        pendingEchoDamage = Mathf.Max(1f, lastHitDamage * Mathf.Max(0f, echoMultiplier));
        SpawnFloatingText($"Echo Ready ({pendingEchoDamage:0})", EchoReadyTextColor);
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        bool attackPressed = IsAttackInputPressed();
        if (damage > 0f && attackPressed)
            lastHitDamage = damage;

        if (!attackPressed)
            return;

        if (applyingEcho)
            return;

        if (pendingEchoDamage <= 0f || target == null || target.IsDead)
            return;

        applyingEcho = true;
        float echoDamage = pendingEchoDamage;
        pendingEchoDamage = 0f;

        SpawnFloatingText("Hourglass Echo", EchoTriggerTextColor);
        PlayEchoGhostSword();

        if (echoStrikeRoutine != null)
            StopCoroutine(echoStrikeRoutine);

        echoStrikeRoutine = StartCoroutine(ResolveEchoStrike(target, echoDamage));
    }

    private void SpawnFloatingText(string text, Color color)
    {
        if (FloatingTextSystem.Instance == null || string.IsNullOrWhiteSpace(text))
            return;

        Vector3 pos = transform.position + Vector3.up * FloatingTextHeight;
        FloatingTextSystem.Instance.SpawnText(pos, text, color, FloatingTextSize);
    }

    private IEnumerator ResolveEchoStrike(Combatant target, float echoDamage)
    {
        float delay = Mathf.Max(0f, EchoStrikeDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (cfg != null && target != null && !target.IsDead && echoDamage > 0f)
            RelicDamageText.Deal(target, echoDamage, transform, cfg);

        applyingEcho = false;
        echoStrikeRoutine = null;
    }

    private void PlayEchoGhostSword()
    {
        Transform followPivot = ResolveWeaponPivot();
        if (followPivot == null)
            return;

        if (ghostSwordVfx == null)
        {
            GameObject go = new("HourglassEchoGhostSwordVfx");
            go.transform.SetParent(transform, false);
            ghostSwordVfx = go.AddComponent<HourglassEchoSwordGhostVfx>();
        }

        ghostSwordVfx.Bind(followPivot);
        ghostSwordVfx.Play(EchoGhostLifetimeSeconds);
    }

    private Transform ResolveWeaponPivot()
    {
        if (weapon == null)
            weapon = GetComponentInChildren<WeaponController>(true);

        if (weapon != null && weapon.weaponPivot != null)
            return weapon.weaponPivot;

        return weapon != null ? weapon.transform : null;
    }

    private bool IsAttackInputPressed()
    {
        combatInput = ResolveCombatInput();
        return combatInput != null && combatInput.IsAttacking();
    }

    private ICombatInput ResolveCombatInput()
    {
        if (weapon == null)
            weapon = GetComponentInChildren<WeaponController>(true);

        if (weapon != null && weapon.combatInputSource != null)
            return weapon.combatInputSource;

        ICombatInput fromSelf = GetComponent<ICombatInput>();
        if (fromSelf != null)
            return fromSelf;

        ICombatInput fromChildren = GetComponentInChildren<ICombatInput>(true);
        if (fromChildren != null)
            return fromChildren;

        return GetComponentInParent<ICombatInput>();
    }
}

public sealed class HourglassEchoSwordGhostVfx : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color GhostColor = new(0.22f, 0.78f, 1f, 1f);

    private const float FollowPositionSharpness = 34f;
    private const float FollowRotationSharpness = 40f;

    private static Material sharedGhostMaterial;

    private MaterialPropertyBlock props;
    private readonly System.Collections.Generic.List<Renderer> renderers = new();

    private Transform followTarget;
    private bool initialized;
    private bool playing;
    private float startedAt;
    private float endsAt;
    private float duration;
    private Vector3 smoothedOffset;

    private readonly Vector3 offsetTarget = new(0.34f, 0.06f, -0.14f);
    private readonly Vector3 offsetStart = new(0.56f, 0.14f, -0.26f);

    public bool IsPlaying => playing;

    public void Bind(Transform target)
    {
        followTarget = target;
        if (followTarget != null)
            gameObject.layer = followTarget.gameObject.layer;
        EnsureInitialized();
    }

    public void Play(float lifetime)
    {
        EnsureInitialized();
        if (followTarget == null)
            return;

        duration = Mathf.Max(0.08f, lifetime);
        startedAt = Time.time;
        endsAt = startedAt + duration;
        smoothedOffset = offsetStart;
        SnapToTarget(smoothedOffset);

        playing = true;
        SetVisible(true);
        UpdateTint(1f);
    }

    public void StopImmediately()
    {
        playing = false;
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!playing)
            return;

        if (followTarget == null)
        {
            StopImmediately();
            return;
        }

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        float posLerp = 1f - Mathf.Exp(-FollowPositionSharpness * dt);
        float rotLerp = 1f - Mathf.Exp(-FollowRotationSharpness * dt);

        smoothedOffset = Vector3.Lerp(smoothedOffset, offsetTarget, posLerp);
        Vector3 worldPos = followTarget.TransformPoint(smoothedOffset);
        transform.position = Vector3.Lerp(transform.position, worldPos, posLerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, followTarget.rotation, rotLerp);

        float t = Mathf.Clamp01((Time.time - startedAt) / Mathf.Max(0.0001f, duration));
        UpdateTint(1f - t);

        if (Time.time >= endsAt)
            StopImmediately();
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        BuildSwordGhostGeometry();
        initialized = true;
        SetVisible(false);
    }

    private void SnapToTarget(Vector3 localOffset)
    {
        if (followTarget == null)
            return;

        transform.position = followTarget.TransformPoint(localOffset);
        transform.rotation = followTarget.rotation;
    }

    private void BuildSwordGhostGeometry()
    {
        Material ghostMaterial = GetSharedGhostMaterial();

        GameObject blade = CreatePrimitiveNoCollider(PrimitiveType.Cube, "GhostBlade", transform);
        blade.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        blade.transform.localScale = new Vector3(0.1f, 1.42f, 0.2f);
        AddRenderer(blade, ghostMaterial);

        GameObject tip = CreatePrimitiveNoCollider(PrimitiveType.Cube, "GhostTip", transform);
        tip.transform.localPosition = new Vector3(0f, 1.74f, 0f);
        tip.transform.localScale = new Vector3(0.08f, 0.24f, 0.16f);
        AddRenderer(tip, ghostMaterial);

        GameObject guard = CreatePrimitiveNoCollider(PrimitiveType.Cube, "GhostGuard", transform);
        guard.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        guard.transform.localScale = new Vector3(0.32f, 0.07f, 0.24f);
        AddRenderer(guard, ghostMaterial);

        GameObject grip = CreatePrimitiveNoCollider(PrimitiveType.Cube, "GhostGrip", transform);
        grip.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        grip.transform.localScale = new Vector3(0.11f, 0.36f, 0.11f);
        AddRenderer(grip, ghostMaterial);
    }

    private void AddRenderer(GameObject go, Material material)
    {
        if (go == null)
            return;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return;

        if (material != null)
            renderer.sharedMaterial = material;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.layer = gameObject.layer;
        renderers.Add(renderer);
    }

    private void SetVisible(bool visible)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    private void UpdateTint(float intensity)
    {
        props ??= new MaterialPropertyBlock();

        float alpha = Mathf.Clamp01(Mathf.Lerp(0.12f, 0.98f, intensity));
        Color tint = new(GhostColor.r, GhostColor.g, GhostColor.b, alpha);
        Color emission = new(
            GhostColor.r * Mathf.Lerp(0.8f, 2.2f, intensity),
            GhostColor.g * Mathf.Lerp(0.9f, 2.4f, intensity),
            GhostColor.b * Mathf.Lerp(1.2f, 3.0f, intensity),
            1f
        );

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(props);
            props.SetColor(BaseColorId, tint);
            props.SetColor(ColorId, tint);
            props.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(props);
        }
    }

    private static GameObject CreatePrimitiveNoCollider(PrimitiveType type, string name, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        if (parent != null)
            go.layer = parent.gameObject.layer;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        return go;
    }

    private static Material GetSharedGhostMaterial()
    {
        if (sharedGhostMaterial != null)
            return sharedGhostMaterial;

        Shader shader = FindSupportedShader(
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Simple Lit",
            "Unlit/Color",
            "Standard"
        );

        if (shader == null)
            return null;

        sharedGhostMaterial = new Material(shader)
        {
            name = "HourglassEchoGhostMaterial",
            enableInstancing = true,
            hideFlags = HideFlags.DontSave
        };

        if (sharedGhostMaterial.HasProperty("_Surface"))
            sharedGhostMaterial.SetFloat("_Surface", 1f);
        if (sharedGhostMaterial.HasProperty("_Blend"))
            sharedGhostMaterial.SetFloat("_Blend", 1f);
        if (sharedGhostMaterial.HasProperty("_ZWrite"))
            sharedGhostMaterial.SetFloat("_ZWrite", 0f);
        if (sharedGhostMaterial.HasProperty("_SrcBlend"))
            sharedGhostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (sharedGhostMaterial.HasProperty("_DstBlend"))
            sharedGhostMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
        if (sharedGhostMaterial.HasProperty("_EmissionColor"))
            sharedGhostMaterial.EnableKeyword("_EMISSION");

        sharedGhostMaterial.renderQueue = (int)RenderQueue.Transparent + 180;
        return sharedGhostMaterial;
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
}
