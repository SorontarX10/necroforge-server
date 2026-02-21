using UnityEngine;
using GrassSim.AI;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Stats;
using GrassSim.Telemetry;

namespace GrassSim.Enemies
{
    public class EnemyCombatant : MonoBehaviour
    {
        public EnemyStatsData stats;
        public int simId = -1;
        public event System.Action<EnemyCombatant> OnDied;

        public AudioSource audioSource;
        public AudioClip enemyDead1;
        public AudioClip enemyDead2;
        public AudioClip enemyDead3;
        public AudioClip enemyDead4;

        [Header("Death VFX")]
        [SerializeField] private bool enableZombieDustDisintegration = true;

        private static PlayerRelicController cachedPlayerRelics;
        private static int cachedPlayerId;
        private static int deathAudioFrame = -1;
        private static int deathAudioPlayedThisFrame;
        private const int MaxDeathAudioPerFrame = 4;
        private const float MaxDeathAudioDistance = 55f;

        private bool handledDeath;
        [System.NonSerialized] public float sharedMeleeHitAvailableAt = -999f;

        private void Awake()
        {
            EnsureEyeEmissionController();
        }

        private void OnEnable()
        {
            // Pooled enemies must always start in a fresh state.
            handledDeath = false;
            sharedMeleeHitAvailableAt = -999f;
            ReportLifecycle("spawned");
        }

        private void OnDisable()
        {
            ReportLifecycle(handledDeath ? "despawned_after_death" : "despawned");
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

            return isBoss ? $"Boss/{normalized}" : $"Enemy/{normalized}";
        }

        private static float GetRunTimeSeconds()
        {
            if (GameTimerController.Instance != null)
                return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

            return 0f;
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
}
