using System.Collections;
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
            bool elite = state != null && state.isElite;
            float healthMultiplier = state != null ? state.healthMultiplier : 1f;
            float damageMultiplier = state != null ? state.damageMultiplier : 1f;
            float expMultiplier = state != null ? state.expMultiplier : 1f;
            float eliteMinHealth = state != null ? state.eliteMinHealth : 0f;

            if (stats == null)
            {
                IsElite = elite;
                ApplyEliteEyeOverride(elite);
                ApplyEliteAuraOverride(elite);
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
            ApplyEliteEyeOverride(elite);
            ApplyEliteAuraOverride(elite);
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

        private void ApplyEliteEyeOverride(bool elite)
        {
            ZombieEyeEmissionController eyeController = GetComponentInChildren<ZombieEyeEmissionController>(true);
            if (eyeController == null)
                return;

            eyeController.SetEliteColorOverride(elite, EliteEyeColor);
        }

        private void ApplyEliteAuraOverride(bool elite)
        {
            if (!elite && eliteAuraVfx == null)
                return;

            if (eliteAuraVfx == null)
                eliteAuraVfx = GetComponent<EnemyEliteAuraVfx>();

            if (eliteAuraVfx == null && elite)
                eliteAuraVfx = gameObject.AddComponent<EnemyEliteAuraVfx>();

            if (eliteAuraVfx != null)
                eliteAuraVfx.SetEliteState(elite);
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
            auraLightBaseIntensity = Mathf.Max(0f, auraLightIntensity);
            auraLight.intensity = auraLightBaseIntensity;
            auraLight.color = new Color(auraColor.r, auraColor.g, auraColor.b, 1f);
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
            main.startColor = auraColor;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, particlesPerSecond);

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.05f, auraRadius);
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
            Color midColor = auraColor;
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
}
