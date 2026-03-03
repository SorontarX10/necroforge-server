using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.AI;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Stats;
using GrassSim.Telemetry;
using UnityEngine.Rendering;

namespace GrassSim.Enemies
{
    public class EnemyCombatant : MonoBehaviour
    {
        public EnemyStatsData stats;
        public int simId = -1;
        public bool IsElite { get; private set; }
        public bool IsApex { get; private set; }
        public bool CanAct => !emergeInProgress;
        public event System.Action<EnemyCombatant> OnDied;

        public AudioSource audioSource;
        public AudioClip enemyDead1;
        public AudioClip enemyDead2;
        public AudioClip enemyDead3;
        public AudioClip enemyDead4;

        [Header("Death VFX")]
        [SerializeField] private bool enableZombieDustDisintegration = true;

        [Header("Elite Drops")]
        [SerializeField, Range(0f, 1f)] private float eliteChestDropChance = 0.2f;

        [Header("Zombie Spawn Emergence")]
        [SerializeField] private bool zombieSpawnEmergeEnabled = true;
        [SerializeField, Min(0f)] private float zombieSpawnEmergeDepth = 1.1f;
        [SerializeField, Min(0.05f)] private float zombieSpawnEmergeDuration = 0.85f;
        [SerializeField] private AnimationCurve zombieSpawnEmergeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private static PlayerRelicController cachedPlayerRelics;
        private static int cachedPlayerId;
        private static int deathAudioFrame = -1;
        private static int deathAudioPlayedThisFrame;
        private const int MaxDeathAudioPerFrame = 4;
        private const float MaxDeathAudioDistance = 55f;
        private const float MinimumEliteChestDropChance = 0.2f;
        private static readonly Color EliteEyeColor = new Color(1f, 0.92f, 0.18f, 1f);
        private static readonly Color ApexEyeColor = new Color(0.74f, 0.44f, 1f, 1f);
        private static readonly Color ApexAuraColor = new Color(0.73f, 0.32f, 1f, 0.78f);

        private bool handledDeath;
        [System.NonSerialized] public float sharedMeleeHitAvailableAt = -999f;
        private EnemyEliteAuraVfx eliteAuraVfx;
        private Rigidbody cachedRigidbody;
        private Coroutine emergeRoutine;
        private bool emergeInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            cachedPlayerRelics = null;
            cachedPlayerId = 0;
            deathAudioFrame = -1;
            deathAudioPlayedThisFrame = 0;
            EnemyEliteAuraVfx.ResetSharedState();
        }

        private void Awake()
        {
            EnsureEyeEmissionController();
            eliteAuraVfx = GetComponent<EnemyEliteAuraVfx>();
            cachedRigidbody = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            // Pooled enemies must always start in a fresh state.
            handledDeath = false;
            IsElite = false;
            IsApex = false;
            sharedMeleeHitAvailableAt = -999f;
            StopSpawnEmergenceRoutine();
            emergeInProgress = false;
            ApplyEliteEyeOverride(false);
            ApplyEliteAuraOverride(false);
            ReportLifecycle("spawned");
        }

        private void OnDisable()
        {
            StopSpawnEmergenceRoutine();
            emergeInProgress = false;
            ReportLifecycle(handledDeath ? "despawned_after_death" : "despawned");
        }

        public void ApplySpawnVariant(EnemySimState state)
        {
            bool apex = state != null && state.isApex;
            bool elite = state != null && (state.isElite || state.isApex);
            float healthMultiplier = state != null ? state.healthMultiplier : 1f;
            float damageMultiplier = state != null ? state.damageMultiplier : 1f;
            float expMultiplier = state != null ? state.expMultiplier : 1f;
            float eliteMinHealth = state != null ? state.eliteMinHealth : 0f;

            if (stats == null)
            {
                IsElite = elite;
                IsApex = apex;
                ApplyEliteEyeOverride(elite, apex);
                ApplyEliteAuraOverride(elite, apex);
                return;
            }

            float maxHealth = Mathf.Max(1f, stats.maxHealth);
            float damage = Mathf.Max(0f, stats.damage);
            int expReward = Mathf.Max(0, stats.expReward);

            if (elite)
            {
                maxHealth = Mathf.Max(Mathf.Max(1f, eliteMinHealth), maxHealth * Mathf.Max(1f, healthMultiplier));
                damage *= Mathf.Max(1f, damageMultiplier);
                expReward = Mathf.Max(1, Mathf.RoundToInt(expReward * Mathf.Max(1f, expMultiplier)));
            }

            stats.maxHealth = maxHealth;
            stats.damage = damage;
            stats.expReward = expReward;

            IsElite = elite;
            IsApex = apex;
            ApplyEliteEyeOverride(elite, apex);
            ApplyEliteAuraOverride(elite, apex);
        }

        public void BeginSpawnEmergence(Vector3 spawnSurfacePosition)
        {
            if (!ShouldRunSpawnEmergence())
            {
                SetWorldPosition(spawnSurfacePosition);
                emergeInProgress = false;
                return;
            }

            StopSpawnEmergenceRoutine();
            emergeRoutine = StartCoroutine(RunSpawnEmergence(spawnSurfacePosition));
        }

        // 🔥 WOŁANE PRZEZ Combatant.SendMessage
        private void OnCombatantDied()
        {
            if (handledDeath)
                return;
            
            EnemyDiedAudioPlay();
            handledDeath = true;

            TryPlayZombieDisintegrationVfx();
            ReportLifecycle("died");

            if (stats == null) return;

            PlayerProgressionController player = PlayerLocator.GetProgression();
            if (player != null)
            {
                int expReward = Mathf.Max(
                    0,
                    Mathf.RoundToInt(stats.expReward * DifficultyContext.ExpMultiplier)
                );
                PlayerRelicController relics = ResolvePlayerRelics(player);
                if (relics != null)
                    expReward = Mathf.Max(0, Mathf.RoundToInt(expReward * relics.GetExpGainMultiplier()));

                player.AddExp(expReward);
            }

            TryDropEliteChest();
            OnDied?.Invoke(this);

            // Pooled/despawned immediately by activation system.
            if (EnemyActivationController.Instance != null && simId >= 0)
            {
                EnemyActivationController.Instance.OnEnemyKilled(simId);
                return;
            }

            // Fallback for non-streamed enemies.
            DisableEnemy();
            Destroy(gameObject);
        }

        private void TryPlayZombieDisintegrationVfx()
        {
            if (!enableZombieDustDisintegration)
                return;

            if (!CompareTag("Zombie"))
                return;

            ZombieDustDisintegrationVfx.SpawnFrom(gameObject);
        }

        private void TryDropEliteChest()
        {
            if (!IsElite)
                return;

            float dropChance = Mathf.Clamp01(Mathf.Max(MinimumEliteChestDropChance, eliteChestDropChance));
            if (dropChance <= 0f || Random.value > dropChance)
                return;

            RelicChestSpawner spawner = FindFirstObjectByType<RelicChestSpawner>();
            if (spawner == null)
                return;

            spawner.SpawnChestAt(transform.position, "elite_drop");
        }

        private static PlayerRelicController ResolvePlayerRelics(PlayerProgressionController player)
        {
            if (player == null)
                return null;

            int playerId = player.GetInstanceID();
            if (cachedPlayerRelics != null && cachedPlayerId == playerId)
                return cachedPlayerRelics;

            cachedPlayerId = playerId;
            cachedPlayerRelics = player.GetComponent<PlayerRelicController>();
            return cachedPlayerRelics;
        }

        private void DisableEnemy()
        {
            // wyłącz collidery
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;

            // wyłącz AI
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb != this)
                    mb.enabled = false;
            }
        }

        private void EnemyDiedAudioPlay() 
        {
            if (!CanPlayDeathAudio())
                return;

            AudioClip clip = null;
            
            int variant = Random.Range(0, 4);

            switch (variant) {
                case 0:
                    clip = enemyDead1;
                    break;
                case 1:
                    clip = enemyDead2;
                    break;
                case 2:
                    clip = enemyDead3;
                    break;
                case 3:
                    clip = enemyDead4;
                    break;
            }

            AudioUtils.PlayClipAtPoint(clip, transform.position, 0.85f);
        }

        private bool CanPlayDeathAudio()
        {
            int frame = Time.frameCount;
            if (deathAudioFrame != frame)
            {
                deathAudioFrame = frame;
                deathAudioPlayedThisFrame = 0;
            }

            if (deathAudioPlayedThisFrame >= MaxDeathAudioPerFrame)
                return false;

            Transform player = PlayerLocator.GetTransform();
            if (player != null)
            {
                Vector3 delta = transform.position - player.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > MaxDeathAudioDistance * MaxDeathAudioDistance)
                    return false;
            }

            deathAudioPlayedThisFrame++;
            return true;
        }

        private void ReportLifecycle(string lifecycle)
        {
            if (string.IsNullOrWhiteSpace(lifecycle))
                return;

            BossEnemyController boss = GetComponent<BossEnemyController>();
            string enemyType = ResolveEnemyTypeLabel(boss != null);

            GameplayTelemetryHub.ReportEnemyLifecycle(
                new GameplayTelemetryHub.EnemyLifecycleSample(
                    GetRunTimeSeconds(),
                    lifecycle,
                    simId,
                    GetInstanceID(),
                    enemyType,
                    boss != null
                )
            );
        }

        private string ResolveEnemyTypeLabel(bool isBoss)
        {
            string baseName = stats != null ? stats.name : gameObject.name;
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Unknown";

            string normalized = baseName
                .Replace("(Clone)", string.Empty)
                .Replace("_RuntimeDifficulty", string.Empty)
                .Replace("_BossRuntime", string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "Unknown";

            if (isBoss)
                return $"Boss/{normalized}";

            if (IsApex)
                return $"Apex/{normalized}";

            return IsElite ? $"Elite/{normalized}" : $"Enemy/{normalized}";
        }

        private static float GetRunTimeSeconds()
        {
            if (GameTimerController.Instance != null)
                return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

            return 0f;
        }

        private bool ShouldRunSpawnEmergence()
        {
            if (!zombieSpawnEmergeEnabled)
                return false;

            if (!CompareTag("Zombie"))
                return false;

            if (GetComponent<BossEnemyController>() != null)
                return false;

            return zombieSpawnEmergeDepth > 0.01f && zombieSpawnEmergeDuration > 0.01f;
        }

        private IEnumerator RunSpawnEmergence(Vector3 spawnSurfacePosition)
        {
            emergeInProgress = true;
            sharedMeleeHitAvailableAt = Time.time + Mathf.Max(0.05f, zombieSpawnEmergeDuration);

            float depth = Mathf.Max(0.05f, zombieSpawnEmergeDepth);
            float duration = Mathf.Max(0.05f, zombieSpawnEmergeDuration);
            Vector3 start = spawnSurfacePosition - Vector3.up * depth;
            SetWorldPosition(start);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float curveT = zombieSpawnEmergeCurve != null
                    ? Mathf.Clamp01(zombieSpawnEmergeCurve.Evaluate(t))
                    : t;

                SetWorldPosition(Vector3.LerpUnclamped(start, spawnSurfacePosition, curveT));
                yield return null;
            }

            SetWorldPosition(spawnSurfacePosition);
            emergeInProgress = false;
            emergeRoutine = null;
        }

        private void StopSpawnEmergenceRoutine()
        {
            if (emergeRoutine == null)
                return;

            StopCoroutine(emergeRoutine);
            emergeRoutine = null;
        }

        private void SetWorldPosition(Vector3 worldPosition)
        {
            if (cachedRigidbody == null)
                cachedRigidbody = GetComponent<Rigidbody>();

            if (cachedRigidbody != null)
            {
                cachedRigidbody.position = worldPosition;
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
                return;
            }

            transform.position = worldPosition;
        }

        private void ApplyEliteEyeOverride(bool elite, bool apex = false)
        {
            ZombieEyeEmissionController eyeController = GetComponentInChildren<ZombieEyeEmissionController>(true);
            if (eyeController == null)
                return;

            Color eyeColor = apex ? ApexEyeColor : EliteEyeColor;
            eyeController.SetEliteColorOverride(elite, eyeColor);
        }

        private void ApplyEliteAuraOverride(bool elite, bool apex = false)
        {
            if (!elite && eliteAuraVfx == null)
                return;

            if (eliteAuraVfx == null)
                eliteAuraVfx = GetComponent<EnemyEliteAuraVfx>();

            if (eliteAuraVfx == null && elite)
                eliteAuraVfx = gameObject.AddComponent<EnemyEliteAuraVfx>();

            if (eliteAuraVfx != null)
            {
                if (apex)
                    eliteAuraVfx.SetStyle(ApexAuraColor, radiusScale: 1.3f, lightIntensityScale: 1.2f);
                else
                    eliteAuraVfx.SetStyle(EliteEyeColor, radiusScale: 1f, lightIntensityScale: 1f);

                eliteAuraVfx.SetEliteState(elite);
            }
        }

        private void EnsureEyeEmissionController()
        {
            if (!CompareTag("Zombie"))
                return;

            if (GetComponentInChildren<ZombieEyeEmissionController>(true) != null)
                return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                Material[] mats = renderer.sharedMaterials;
                if (mats == null)
                    continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null)
                        continue;

                    string name = mat.name;
                    if (string.IsNullOrEmpty(name) || name.ToLowerInvariant().Contains("eye") == false)
                        continue;

                    ZombieEyeEmissionController controller = gameObject.AddComponent<ZombieEyeEmissionController>();
                    controller.enemyCombatant = this;
                    return;
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public class EnemyEliteAuraVfx : MonoBehaviour
    {
        [Header("Aura")]
        [SerializeField] private bool useParticleAura = false;
        [SerializeField] private Color auraColor = new(1f, 0.86f, 0.2f, 0.7f);
        [SerializeField, Min(0f)] private float auraYOffset = 0.9f;
        [SerializeField, Min(0.05f)] private float auraRadius = 0.7f;
        [SerializeField, Min(0f)] private float particlesPerSecond = 10f;
        [SerializeField, Min(4)] private int maxParticles = 18;
        [SerializeField, Min(0.05f)] private float pulseSpeed = 2.8f;
        [SerializeField, Min(0f)] private float pulseAmplitude = 0.2f;
        [SerializeField] private bool useLightAura = true;
        [SerializeField, Min(0f)] private float auraLightIntensity = 1.4f;
        [SerializeField, Min(0.2f)] private float auraLightRange = 2f;

        private ParticleSystem auraParticles;
        private Transform auraTransform;
        private Light auraLight;
        private float auraLightBaseIntensity;
        private bool eliteActive;
        private float pulseSeed;
        private bool hasRuntimeStyle;
        private Color runtimeAuraColor;
        private float runtimeRadiusScale = 1f;
        private float runtimeLightIntensityScale = 1f;
        private static Material sharedAuraMaterial;
        private static bool missingAuraShaderWarningLogged;

        private void Awake()
        {
            pulseSeed = Random.Range(0f, 100f);
        }

        private void OnEnable()
        {
            if (eliteActive)
                SetEliteState(true);
        }

        public void SetStyle(Color color, float radiusScale = 1f, float lightIntensityScale = 1f)
        {
            hasRuntimeStyle = true;
            runtimeAuraColor = color;
            runtimeRadiusScale = Mathf.Max(0.35f, radiusScale);
            runtimeLightIntensityScale = Mathf.Max(0.1f, lightIntensityScale);

            if (auraParticles != null)
                ConfigureParticleSystem(auraParticles);

            if (auraLight != null)
            {
                auraLightBaseIntensity = Mathf.Max(0f, GetAuraLightIntensity());
                auraLight.intensity = auraLightBaseIntensity;
                auraLight.color = GetAuraLightColor();
            }
        }

        private void OnDisable()
        {
            if (auraParticles != null)
                auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (auraLight != null && auraLight.gameObject.activeSelf)
                auraLight.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!eliteActive)
                return;

            if (auraTransform == null && auraLight == null)
                return;

            float speed = Mathf.Max(0.05f, pulseSpeed);
            float amplitude = Mathf.Max(0f, pulseAmplitude);
            float pulse = 1f + Mathf.Sin((Time.time + pulseSeed) * speed) * amplitude;
            float scale = Mathf.Max(0.2f, pulse);

            if (auraTransform != null)
            {
                auraTransform.localPosition = Vector3.up * auraYOffset;
                auraTransform.localScale = new Vector3(scale, scale, scale);
            }

            if (auraLight != null)
            {
                auraLight.transform.localPosition = Vector3.up * auraYOffset;
                float lightPulse = 0.86f + Mathf.Sin((Time.time + pulseSeed) * speed) * 0.14f;
                auraLight.intensity = Mathf.Max(0f, auraLightBaseIntensity * lightPulse);
            }
        }

        public void SetEliteState(bool elite)
        {
            eliteActive = elite;

            if (!elite)
            {
                if (auraParticles != null)
                {
                    auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (auraParticles.gameObject.activeSelf)
                        auraParticles.gameObject.SetActive(false);
                }

                if (auraLight != null && auraLight.gameObject.activeSelf)
                    auraLight.gameObject.SetActive(false);

                return;
            }

            if (useParticleAura)
            {
                EnsureAuraParticles();
                if (auraParticles != null)
                {
                    if (!auraParticles.gameObject.activeSelf)
                        auraParticles.gameObject.SetActive(true);

                    if (!auraParticles.isPlaying)
                        auraParticles.Play(true);
                }
            }
            else if (auraParticles != null)
            {
                auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (auraParticles.gameObject.activeSelf)
                    auraParticles.gameObject.SetActive(false);
            }

            if (useLightAura)
            {
                EnsureAuraLight();
                if (auraLight != null && !auraLight.gameObject.activeSelf)
                    auraLight.gameObject.SetActive(true);
            }
        }

        private void EnsureAuraParticles()
        {
            if (auraParticles != null)
                return;

            GameObject aura = new("EliteAuraVfx");
            aura.hideFlags = HideFlags.DontSave;
            aura.transform.SetParent(transform, false);
            aura.transform.localPosition = Vector3.up * auraYOffset;
            auraTransform = aura.transform;

            auraParticles = aura.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(auraParticles);

            ParticleSystemRenderer renderer = aura.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material auraMaterial = GetAuraMaterial();
                if (auraMaterial != null)
                {
                    renderer.sharedMaterial = auraMaterial;
                    renderer.enabled = true;
                }
                else
                {
                    renderer.enabled = false;
                }
            }
        }

        private void EnsureAuraLight()
        {
            if (auraLight != null)
                return;

            GameObject auraLightObject = new("EliteAuraLight");
            auraLightObject.hideFlags = HideFlags.DontSave;
            auraLightObject.transform.SetParent(transform, false);
            auraLightObject.transform.localPosition = Vector3.up * auraYOffset;

            auraLight = auraLightObject.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.range = Mathf.Max(0.2f, auraLightRange);
            auraLightBaseIntensity = Mathf.Max(0f, GetAuraLightIntensity());
            auraLight.intensity = auraLightBaseIntensity;
            auraLight.color = GetAuraLightColor();
            auraLight.shadows = LightShadows.None;
            auraLight.renderMode = LightRenderMode.Auto;
        }

        private void ConfigureParticleSystem(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.maxParticles = Mathf.Max(4, maxParticles);
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startColor = GetAuraColor();

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, particlesPerSecond);

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.05f, auraRadius * GetAuraRadiusScale());
            shape.alignToDirection = false;

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ParticleSystem.MinMaxCurve lateral = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            ParticleSystem.MinMaxCurve up = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            ParticleSystem.MinMaxCurve radial = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            ParticleSystem.MinMaxCurve zero = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.MinMaxCurve unit = new ParticleSystem.MinMaxCurve(1f, 1f);

            // Unity requires all Velocity-over-Lifetime curves to share the same mode.
            velocity.x = lateral;
            velocity.y = up;
            velocity.z = lateral;
            velocity.radial = radial;
            velocity.orbitalX = zero;
            velocity.orbitalY = zero;
            velocity.orbitalZ = zero;
            velocity.speedModifier = unit;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = BuildColorGradient();

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.2f;
        }

        private ParticleSystem.MinMaxGradient BuildColorGradient()
        {
            Color midColor = GetAuraColor();
            midColor.a = Mathf.Clamp01(midColor.a);

            Gradient gradient = new();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(midColor, 0f),
                    new GradientColorKey(midColor, 0.55f),
                    new GradientColorKey(midColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(midColor.a, 0.2f),
                    new GradientAlphaKey(midColor.a * 0.5f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                }
            );

            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private Color GetAuraColor()
        {
            return hasRuntimeStyle ? runtimeAuraColor : auraColor;
        }

        private float GetAuraRadiusScale()
        {
            return hasRuntimeStyle ? runtimeRadiusScale : 1f;
        }

        private float GetAuraLightIntensity()
        {
            float intensity = auraLightIntensity;
            if (hasRuntimeStyle)
                intensity *= runtimeLightIntensityScale;

            return intensity;
        }

        private Color GetAuraLightColor()
        {
            Color color = GetAuraColor();
            return new Color(color.r, color.g, color.b, 1f);
        }

        private static Material GetAuraMaterial()
        {
            if (sharedAuraMaterial != null)
                return sharedAuraMaterial;

            Shader shader = null;
            RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();

            if (pipeline != null)
            {
                shader = FindSupportedShader(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Particles/Lit"
                );

                if (shader == null)
                {
                    Material defaultParticle = pipeline.defaultParticleMaterial;
                    if (defaultParticle != null && defaultParticle.shader != null && defaultParticle.shader.isSupported)
                    {
                        string shaderName = defaultParticle.shader.name;
                        if (!string.IsNullOrWhiteSpace(shaderName) && shaderName.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal))
                        {
                            sharedAuraMaterial = defaultParticle;
                            return sharedAuraMaterial;
                        }
                    }
                }

                if (shader == null)
                {
                    if (!missingAuraShaderWarningLogged)
                    {
                        missingAuraShaderWarningLogged = true;
                        Debug.LogWarning("Elite aura VFX disabled: no URP-compatible particle shader/material found.");
                    }

                    return null;
                }
            }
            else
            {
                shader = FindSupportedShader(
                    "Particles/Standard Unlit",
                    "Particles/Additive"
                );
            }

            if (shader == null)
                return null;

            sharedAuraMaterial = new Material(shader)
            {
                name = "EliteAuraVfxMaterial",
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };

            return sharedAuraMaterial;
        }

        private static RenderPipelineAsset ResolveActiveRenderPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
                return pipeline;

            return GraphicsSettings.defaultRenderPipeline;
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

        internal static void ResetSharedState()
        {
            sharedAuraMaterial = null;
            missingAuraShaderWarningLogged = false;
        }

    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ApexSkeletonZombieController : MonoBehaviour
    {
        [Header("Distance Control")]
        [SerializeField, Min(5f)] private float minimumApproachDistance = 20f;
        [SerializeField, Min(5f)] private float preferredFireDistance = 26f;
        [SerializeField, Min(8f)] private float maxFireDistance = 36f;
        [SerializeField, Min(0.1f)] private float moveSpeed = 3.1f;
        [SerializeField, Min(0.1f)] private float rotationSpeed = 7.5f;

        [Header("Shooting")]
        [SerializeField, Min(0.05f)] private float aimDuration = 0.45f;
        [SerializeField, Min(0.1f)] private float shootCooldown = 2.2f;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 10f;
        [SerializeField, Min(0.1f)] private float projectileLifetime = 5.4f;
        [SerializeField, Min(0.01f)] private float projectileRadius = 0.18f;
        [SerializeField, Min(0.1f)] private float projectileDamageMultiplier = 1f;
        [SerializeField, Min(0f)] private float friendlyFireDamageScale = 1f;
        [SerializeField, Min(0f)] private float muzzleHeight = 1.4f;
        [SerializeField, Min(0f)] private float muzzleForwardOffset = 0.65f;
        [SerializeField] private Color projectileColor = new(0.78f, 0.35f, 1f, 1f);
        [SerializeField] private Color projectileTrailColor = new(0.72f, 0.3f, 1f, 0.9f);

        [Header("Skull Head")]
        [SerializeField] private string skullResourcePath = "FBX/21337_Skull_v1";
        [SerializeField, Min(0.05f)] private float skullScale = 0.38f;
        [SerializeField, Min(0f)] private float skullWorldOffset = 0.1f;
        [SerializeField, Min(1f)] private float skullFollowLerp = 16f;
        [SerializeField] private Vector3 skullLocalEuler = new(-90f, 0f, 0f);

        [Header("Targeting")]
        [SerializeField, Min(0.05f)] private float targetRefreshInterval = 0.3f;

        private Rigidbody rb;
        private Animator animator;
        private EnemyCombatant enemyCombatant;
        private Combatant ownerCombatant;
        private Transform playerTarget;
        private float nextTargetRefreshAt;
        private float nextShootAt;
        private float aimEndsAt = -1f;
        private Vector3 aimDirection;
        private bool apexActive;
        private GameObject skullRoot;
        private bool skullHasWorldPosition;
        private readonly List<Behaviour> disabledBehaviours = new(12);
        private readonly List<Collider> disabledMeleeColliders = new(6);
        private readonly List<Renderer> hiddenHeadRenderers = new(2);

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>();
        }

        private void OnDisable()
        {
            if (rb != null)
            {
                Vector3 vel = rb.linearVelocity;
                vel.x = 0f;
                vel.z = 0f;
                rb.linearVelocity = vel;
            }

            if (animator != null)
                animator.SetFloat("Speed", 0f);

            skullHasWorldPosition = false;
        }

        private void LateUpdate()
        {
            if (!apexActive || skullRoot == null || !skullRoot.activeSelf)
                return;

            UpdateSkullWorldPlacement(forceInstant: false);
        }

        private void FixedUpdate()
        {
            if (!apexActive)
                return;

            if (enemyCombatant == null || ownerCombatant == null || ownerCombatant.IsDead)
            {
                StopHorizontalMotion();
                return;
            }

            if (!enemyCombatant.CanAct)
            {
                StopHorizontalMotion();
                return;
            }

            RefreshTargetIfNeeded();
            if (playerTarget == null)
            {
                StopHorizontalMotion();
                return;
            }

            Vector3 toTarget = playerTarget.position - transform.position;
            toTarget.y = 0f;
            float sqrDistance = toTarget.sqrMagnitude;
            if (sqrDistance < 0.0001f)
            {
                StopHorizontalMotion();
                return;
            }

            float distance = Mathf.Sqrt(sqrDistance);
            Vector3 toTargetDir = toTarget / distance;
            RotateTowards(toTargetDir);

            if (aimEndsAt > 0f)
            {
                StopHorizontalMotion();
                if (Time.time >= aimEndsAt)
                {
                    aimEndsAt = -1f;
                    FireProjectile(aimDirection);
                    nextShootAt = Time.time + Mathf.Max(0.1f, shootCooldown);
                }

                return;
            }

            bool inFireWindow = distance >= Mathf.Max(1f, minimumApproachDistance) && distance <= Mathf.Max(minimumApproachDistance + 1f, maxFireDistance);
            if (inFireWindow && Time.time >= nextShootAt)
            {
                aimDirection = toTargetDir;
                aimEndsAt = Time.time + Mathf.Max(0.05f, aimDuration);
                StopHorizontalMotion();
                return;
            }

            Vector3 desiredDirection = Vector3.zero;
            float minDistance = Mathf.Max(1f, minimumApproachDistance);
            float desiredDistance = Mathf.Max(minDistance + 0.5f, preferredFireDistance);
            if (distance < minDistance)
                desiredDirection = -toTargetDir;
            else if (distance > desiredDistance)
                desiredDirection = toTargetDir;

            MoveHorizontal(desiredDirection);
        }

        public void EnableApexMode(EnemyCombatant enemy, Combatant owner)
        {
            enemyCombatant = enemy;
            ownerCombatant = owner;
            apexActive = true;
            enabled = true;
            nextShootAt = Time.time + Random.Range(0.45f, 1.25f);
            aimEndsAt = -1f;

            EnsureApexPresentation();
            DisableMeleeStack();
            RefreshTarget(force: true);
        }

        public void DisableApexMode()
        {
            apexActive = false;
            aimEndsAt = -1f;
            playerTarget = null;
            skullHasWorldPosition = false;
            RestoreDisabledBehaviours();
            RestoreHiddenHeadRenderers();
            if (skullRoot != null && skullRoot.activeSelf)
                skullRoot.SetActive(false);

            StopHorizontalMotion();
            enabled = false;
        }

        private void RefreshTargetIfNeeded()
        {
            if (playerTarget != null && playerTarget.gameObject.activeInHierarchy)
                return;

            if (Time.time < nextTargetRefreshAt)
                return;

            RefreshTarget(force: false);
        }

        private void RefreshTarget(bool force)
        {
            if (!force && Time.time < nextTargetRefreshAt)
                return;

            nextTargetRefreshAt = Time.time + Mathf.Max(0.05f, targetRefreshInterval);
            playerTarget = PlayerLocator.GetTransform();
        }

        private void RotateTowards(Vector3 lookDirection)
        {
            if (lookDirection.sqrMagnitude < 0.0001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            float lerp = Mathf.Clamp01(Mathf.Max(0.1f, rotationSpeed) * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lerp);
        }

        private void MoveHorizontal(Vector3 direction)
        {
            if (rb == null)
                return;

            if (direction.sqrMagnitude < 0.0001f)
            {
                StopHorizontalMotion();
                return;
            }

            direction.Normalize();
            Vector3 velocity = rb.linearVelocity;
            velocity.x = direction.x * Mathf.Max(0f, moveSpeed);
            velocity.z = direction.z * Mathf.Max(0f, moveSpeed);
            rb.linearVelocity = velocity;

            if (animator != null)
                animator.SetFloat("Speed", new Vector2(velocity.x, velocity.z).magnitude);
        }

        private void StopHorizontalMotion()
        {
            if (rb != null)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                rb.linearVelocity = velocity;
            }

            if (animator != null)
                animator.SetFloat("Speed", 0f);
        }

        private void FireProjectile(Vector3 direction)
        {
            if (enemyCombatant == null || ownerCombatant == null || direction.sqrMagnitude < 0.0001f)
                return;

            float damage = enemyCombatant.stats != null
                ? Mathf.Max(1f, enemyCombatant.stats.damage * Mathf.Max(0.1f, projectileDamageMultiplier))
                : 12f;

            Vector3 fireDirection = direction.normalized;
            Vector3 muzzlePos = transform.position
                + Vector3.up * Mathf.Max(0f, muzzleHeight)
                + fireDirection * Mathf.Max(0f, muzzleForwardOffset);

            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "ApexSkeletonProjectile";
            projectile.transform.position = muzzlePos;
            projectile.transform.rotation = Quaternion.LookRotation(fireDirection, Vector3.up);
            float diameter = Mathf.Max(0.08f, projectileRadius * 2f);
            projectile.transform.localScale = new Vector3(diameter, diameter, diameter);

            Collider primitiveCollider = projectile.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
                Destroy(primitiveCollider);
            }

            MeshRenderer meshRenderer = projectile.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = ApexSkeletonProjectile.GetProjectileMaterial(projectileColor);
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            TrailRenderer trail = projectile.AddComponent<TrailRenderer>();
            trail.time = 0.34f;
            trail.minVertexDistance = 0.03f;
            trail.startWidth = Mathf.Max(0.03f, projectileRadius * 1.2f);
            trail.endWidth = 0f;
            trail.autodestruct = false;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 1;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = ApexSkeletonProjectile.GetTrailMaterial(projectileTrailColor);
            trail.startColor = projectileTrailColor;
            trail.endColor = new Color(projectileTrailColor.r, projectileTrailColor.g, projectileTrailColor.b, 0f);
            trail.emitting = true;

            ApexSkeletonProjectile projectileRuntime = projectile.AddComponent<ApexSkeletonProjectile>();
            projectileRuntime.Initialize(
                ownerCombatant,
                damage,
                fireDirection * Mathf.Max(0.1f, projectileSpeed),
                Mathf.Max(0.01f, projectileRadius),
                Mathf.Max(0.1f, projectileLifetime),
                Mathf.Max(0f, friendlyFireDamageScale)
            );
        }

        private void EnsureApexPresentation()
        {
            EnsureSkullVisual();
            UpdateSkullWorldPlacement(forceInstant: true);
            HideDefaultHeadRenderers();
        }

        private void EnsureSkullVisual()
        {
            if (skullRoot == null)
            {
                skullRoot = new GameObject("ApexSkeletonHead");
                skullRoot.transform.SetParent(transform, true);

                GameObject skullSource = Resources.Load<GameObject>(skullResourcePath);
                if (skullSource != null)
                {
                    GameObject skullModel = Instantiate(skullSource, skullRoot.transform);
                    skullModel.name = "SkullModel";
                    skullModel.transform.localPosition = Vector3.zero;
                    skullModel.transform.localRotation = Quaternion.identity;
                    skullModel.transform.localScale = Vector3.one;
                    Collider[] colliders = skullModel.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        if (colliders[i] != null)
                            Destroy(colliders[i]);
                    }
                }
                else
                {
                    skullRoot.AddComponent<RelicSkullResourceVisual>();
                }
            }

            skullRoot.transform.SetParent(transform, true);
            skullRoot.transform.localScale = Vector3.one * Mathf.Max(0.05f, skullScale);
            if (!skullRoot.activeSelf)
                skullRoot.SetActive(true);
            skullHasWorldPosition = false;
        }

        private void UpdateSkullWorldPlacement(bool forceInstant)
        {
            if (skullRoot == null)
                return;

            if (!TryResolveHeadWorldPosition(out Vector3 desiredPos))
                return;

            Quaternion desiredRot = ResolveSkullWorldRotation();
            if (forceInstant || !skullHasWorldPosition)
            {
                skullRoot.transform.SetPositionAndRotation(desiredPos, desiredRot);
                skullHasWorldPosition = true;
                return;
            }

            float lerpFactor = 1f - Mathf.Exp(-Mathf.Max(1f, skullFollowLerp) * Time.deltaTime);
            Vector3 currentPos = skullRoot.transform.position;
            skullRoot.transform.position = Vector3.Lerp(currentPos, desiredPos, lerpFactor);
            skullRoot.transform.rotation = Quaternion.Slerp(skullRoot.transform.rotation, desiredRot, lerpFactor);
        }

        private bool TryResolveHeadWorldPosition(out Vector3 worldPosition)
        {
            Bounds headBounds = default;
            bool hasBounds = false;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (skullRoot != null && renderer.transform != null && renderer.transform.IsChildOf(skullRoot.transform))
                    continue;

                if (IsExcludedHeadSourceRenderer(renderer))
                    continue;

                if (!hasBounds)
                {
                    headBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    headBounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                worldPosition = new Vector3(headBounds.center.x, headBounds.max.y + Mathf.Max(0f, skullWorldOffset), headBounds.center.z);
                return true;
            }

            worldPosition = transform.position + Vector3.up * 1.6f;
            return true;
        }

        private Quaternion ResolveSkullWorldRotation()
        {
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;

            Quaternion baseRotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            return baseRotation * Quaternion.Euler(skullLocalEuler);
        }

        private static bool IsExcludedHeadSourceRenderer(Renderer renderer)
        {
            string lower = renderer.name.ToLowerInvariant();
            if (lower.Contains("hitbox") || lower.Contains("weapon") || lower.Contains("sword"))
                return true;

            if (lower.Contains("aura") || lower.Contains("trail") || lower.Contains("projectile"))
                return true;

            if (lower.Contains("vfx") || lower.Contains("fx"))
                return true;

            return false;
        }

        private void HideDefaultHeadRenderers()
        {
            if (hiddenHeadRenderers.Count > 0)
                return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                string lower = renderer.name.ToLowerInvariant();
                if (!lower.Contains("head"))
                    continue;

                renderer.enabled = false;
                hiddenHeadRenderers.Add(renderer);
                if (hiddenHeadRenderers.Count >= 2)
                    break;
            }
        }

        private void RestoreHiddenHeadRenderers()
        {
            for (int i = 0; i < hiddenHeadRenderers.Count; i++)
            {
                Renderer renderer = hiddenHeadRenderers[i];
                if (renderer != null)
                    renderer.enabled = true;
            }

            hiddenHeadRenderers.Clear();
        }

        private void DisableMeleeStack()
        {
            RestoreDisabledBehaviours();

            DisableBehaviour(GetComponent<ZombieAI>());
            DisableBehaviour(GetComponent<ZombieCombatInput>());
            DisableBehaviour(GetComponent<EnemyCombatInput>());

            WeaponController[] weaponControllers = GetComponentsInChildren<WeaponController>(true);
            for (int i = 0; i < weaponControllers.Length; i++)
                DisableBehaviour(weaponControllers[i]);

            EnemyDamageDealer[] damageDealers = GetComponentsInChildren<EnemyDamageDealer>(true);
            for (int i = 0; i < damageDealers.Length; i++)
            {
                EnemyDamageDealer dealer = damageDealers[i];
                if (dealer == null)
                    continue;

                DisableBehaviour(dealer);

                Collider hitCollider = dealer.GetComponent<Collider>();
                if (hitCollider != null && hitCollider.enabled)
                {
                    hitCollider.enabled = false;
                    disabledMeleeColliders.Add(hitCollider);
                }
            }
        }

        private void DisableBehaviour(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled || behaviour == this)
                return;

            behaviour.enabled = false;
            disabledBehaviours.Add(behaviour);
        }

        private void RestoreDisabledBehaviours()
        {
            for (int i = 0; i < disabledBehaviours.Count; i++)
            {
                Behaviour behaviour = disabledBehaviours[i];
                if (behaviour != null)
                    behaviour.enabled = true;
            }

            disabledBehaviours.Clear();

            for (int i = 0; i < disabledMeleeColliders.Count; i++)
            {
                Collider col = disabledMeleeColliders[i];
                if (col != null)
                    col.enabled = true;
            }

            disabledMeleeColliders.Clear();
        }
    }

    [DisallowMultipleComponent]
    public sealed class ApexSkeletonProjectile : MonoBehaviour
    {
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[10];
        private static Material sharedProjectileMaterial;
        private static Material sharedTrailMaterial;

        private Combatant owner;
        private Transform ownerTransform;
        private float damage;
        private float friendlyFireDamageScale;
        private float radius;
        private Vector3 velocity;
        private float dieAt;
        private bool initialized;
        private LayerMask hitMask = ~0;

        public void Initialize(
            Combatant owner,
            float damage,
            Vector3 velocity,
            float radius,
            float lifeTime,
            float friendlyFireDamageScale
        )
        {
            this.owner = owner;
            ownerTransform = owner != null ? owner.transform : null;
            this.damage = Mathf.Max(0f, damage);
            this.velocity = velocity;
            this.radius = Mathf.Max(0.01f, radius);
            this.friendlyFireDamageScale = Mathf.Max(0f, friendlyFireDamageScale);
            dieAt = Time.time + Mathf.Max(0.05f, lifeTime);
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 start = transform.position;
            Vector3 step = velocity * dt;
            float stepDistance = step.magnitude;
            if (stepDistance > 0.0001f)
            {
                Vector3 direction = step / stepDistance;
                int hitCount = Physics.SphereCastNonAlloc(
                    start,
                    radius,
                    direction,
                    HitBuffer,
                    stepDistance,
                    hitMask,
                    QueryTriggerInteraction.Ignore
                );

                Collider bestCollider = null;
                float bestDistance = float.PositiveInfinity;
                Vector3 bestPoint = start + direction * stepDistance;

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = HitBuffer[i];
                    Collider col = hit.collider;
                    if (col == null || IsIgnoredCollider(col))
                        continue;

                    if (hit.distance < bestDistance)
                    {
                        bestDistance = hit.distance;
                        bestCollider = col;
                        bestPoint = hit.point;
                    }
                }

                if (bestCollider != null)
                {
                    transform.position = bestPoint;
                    HandleImpact(bestCollider);
                    return;
                }

                transform.position = start + step;
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            if (Time.time >= dieAt)
                Destroy(gameObject);
        }

        private bool IsIgnoredCollider(Collider col)
        {
            if (col == null)
                return true;

            if (ownerTransform != null && col.transform != null && col.transform.IsChildOf(ownerTransform))
                return true;

            return false;
        }

        private void HandleImpact(Collider col)
        {
            if (col == null)
            {
                Destroy(gameObject);
                return;
            }

            Combatant target = col.GetComponentInParent<Combatant>();
            if (target != null && !target.IsDead && target != owner)
            {
                float appliedDamage = damage;
                if (!target.IsPlayer)
                    appliedDamage *= friendlyFireDamageScale;

                if (appliedDamage > 0f)
                    target.TakeDamage(appliedDamage, ownerTransform);
            }

            Destroy(gameObject);
        }

        public static Material GetProjectileMaterial(Color color)
        {
            if (sharedProjectileMaterial == null)
            {
                Shader shader = FindSupportedShader(
                    "Universal Render Pipeline/Unlit",
                    "Unlit/Color"
                );

                if (shader != null)
                {
                    sharedProjectileMaterial = new Material(shader)
                    {
                        name = "ApexSkeletonProjectileMaterial",
                        enableInstancing = true,
                        hideFlags = HideFlags.DontSave
                    };
                }
            }

            if (sharedProjectileMaterial != null)
            {
                if (sharedProjectileMaterial.HasProperty("_BaseColor"))
                    sharedProjectileMaterial.SetColor("_BaseColor", color);
                if (sharedProjectileMaterial.HasProperty("_Color"))
                    sharedProjectileMaterial.SetColor("_Color", color);
                if (sharedProjectileMaterial.HasProperty("_EmissionColor"))
                    sharedProjectileMaterial.SetColor("_EmissionColor", color * 1.4f);
            }

            return sharedProjectileMaterial;
        }

        public static Material GetTrailMaterial(Color color)
        {
            if (sharedTrailMaterial == null)
            {
                Shader shader = FindSupportedShader(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Unlit",
                    "Particles/Standard Unlit",
                    "Unlit/Color"
                );

                if (shader != null)
                {
                    sharedTrailMaterial = new Material(shader)
                    {
                        name = "ApexSkeletonProjectileTrailMaterial",
                        enableInstancing = true,
                        hideFlags = HideFlags.DontSave
                    };
                }
            }

            if (sharedTrailMaterial != null)
            {
                if (sharedTrailMaterial.HasProperty("_BaseColor"))
                    sharedTrailMaterial.SetColor("_BaseColor", color);
                if (sharedTrailMaterial.HasProperty("_Color"))
                    sharedTrailMaterial.SetColor("_Color", color);
                if (sharedTrailMaterial.HasProperty("_EmissionColor"))
                    sharedTrailMaterial.SetColor("_EmissionColor", color * 1.3f);
            }

            return sharedTrailMaterial;
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
}
