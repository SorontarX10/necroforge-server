using System;
using System.Collections;
using System.Collections.Generic;
using GrassSim.AI;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;
using GrassSim.Enhancers;
using GrassSim.Stats;
using GrassSim.Upgrades;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GrassSim.Testing
{
    [DefaultExecutionOrder(-10000)]
    public sealed class TestSceneSandboxController : MonoBehaviour
    {
        private const string SandboxSceneName = "TestScene";
        private const float SpawnHeightFallback = 1.2f;
        private const float DefaultRelicDescriptionDurationSeconds = 10f;
        private const string AnvilOfNightOathsRelicId = "legendary_anvil_of_night_oaths";
        private const string OssuaryChoirRelicId = "legendary_ossuary_choir";
        private const string BlackChapelHourglassRelicId = "rare_black_chapel_hourglass";
        private const string BlackMassCenserRelicId = "mythic_black_mass_censer";
        private const string FloatingTextPrefabPath = "Assets/Prefabs/UI/FloatingText.prefab";

        private static TestSceneSandboxController instance;

        private readonly Dictionary<string, RelicLoadoutEntry> selectedRelics = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnhancerLoadoutEntry> selectedEnhancers = new(StringComparer.Ordinal);
        private readonly List<UpgradeLoadoutEntry> selectedUpgrades = new();
        private readonly List<SpawnedZombieEntry> spawnedZombies = new();

        private readonly List<RelicDefinition> relicCatalog = new();
        private readonly List<EnhancerDefinition> enhancerCatalog = new();
        private readonly List<UpgradeCatalogEntry> upgradeCatalog = new();

        private GameObject playerPrefab;
        private GameObject zombiePrefab;
        private RelicLibrary relicLibrary;

        private GameObject playerInstance;
        private PlayerProgressionController playerProgression;
        private PlayerRelicController playerRelics;
        private WeaponEnhancerSystem playerEnhancers;

        private Rect windowRect = new(16f, 16f, 620f, 880f);
        private Vector2 relicScroll;
        private Vector2 enhancerScroll;
        private Vector2 upgradesScroll;
        private Vector2 activeLoadoutScroll;

        private int selectedRelicIndex = -1;
        private int selectedEnhancerIndex = -1;
        private int selectedUpgradeIndex = -1;
        private float upgradeValueScale = 1f;
        private string relicSearch = string.Empty;
        private string enhancerSearch = string.Empty;
        private RelicDefinition selectedRelicPreview;
        private float selectedRelicPreviewVisibleUntil = -1f;

        private bool lockCursorForGameplay;
        private bool isRebuildInFlight;
        private bool showPlayerHud = true;
        private bool showPlayerStatsSnapshot = true;

        private ZombieMode zombieMode = ZombieMode.Walking;
        private int zombieSpawnCount = 8;
        private float zombieSpawnRadius = 16f;
        private float zombieMinSpawnDistance = 6f;
        private LayerMask groundMask;

        [Header("Sandbox UX")]
        [SerializeField] private KeyCode cursorToggleHotkey = KeyCode.F1;
        [SerializeField, Min(0.5f)] private float relicDescriptionDurationSeconds = DefaultRelicDescriptionDurationSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !string.Equals(activeScene.name, SandboxSceneName, StringComparison.Ordinal))
                return;

            if (instance != null)
                return;

            GameObject root = new("TestSceneSandbox");
            instance = root.AddComponent<TestSceneSandboxController>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            groundMask = LayerMask.GetMask("Ground");
        }

        private void Start()
        {
            if (!IsSandboxSceneActive())
            {
                Destroy(gameObject);
                return;
            }

            DisableAutonomousSpawners();
            ResolveAssetReferences();
            ConfigureFloatingTextSystem();
            SpawnFreshSandboxPlayer();
            BuildAllCatalogs();
            SetCursorLockMode(false);
        }

        private void Update()
        {
            if (!IsSandboxSceneActive())
                return;

            HandleCursorToggleHotkey();
            CleanupZombieList();
        }

        private void LateUpdate()
        {
            if (!IsSandboxSceneActive())
                return;

            // Keep final cursor state deterministic even if another script changes it in Update.
            EnforceCursorMode();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            ClearSpawnedZombies();
        }

        private void OnGUI()
        {
            if (!IsSandboxSceneActive() || !BuildProfileResolver.IsDevelopmentToolsEnabled)
                return;

            DrawPlayerHudOverlay();

            windowRect = GUILayout.Window(
                GetInstanceID(),
                windowRect,
                DrawWindow,
                GetSandboxWindowTitle()
            );
        }

        private void DrawWindow(int id)
        {
            DrawPlayerSection();
            if (showPlayerStatsSnapshot)
            {
                GUILayout.Space(8f);
                DrawPlayerStatsSnapshot();
            }
            GUILayout.Space(8f);
            DrawZombieSection();
            GUILayout.Space(8f);
            DrawUpgradeSection();
            GUILayout.Space(8f);
            DrawRelicSection();
            GUILayout.Space(8f);
            DrawEnhancerSection();
            GUILayout.Space(8f);
            DrawActiveLoadoutSection();
            GUILayout.Space(8f);

            if (GUILayout.Button("Clear Entire Loadout and Respawn"))
            {
                selectedRelics.Clear();
                selectedEnhancers.Clear();
                selectedUpgrades.Clear();
                RebuildPlayerFromLoadout();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 26f));
        }

        private void DrawPlayerSection()
        {
            GUILayout.Label("Player");
            GUILayout.Label($"Hotkey: {cursorToggleHotkey} toggles cursor lock");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Respawn (Keep Loadout)"))
                RebuildPlayerFromLoadout();

            if (GUILayout.Button("Full Heal and Stamina"))
                RefillPlayerResources();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(lockCursorForGameplay ? "Unlock Cursor for UI" : "Lock Cursor for Gameplay"))
                SetCursorLockMode(!lockCursorForGameplay);

            if (GUILayout.Button("Rebuild Catalogs"))
                BuildAllCatalogs();
            GUILayout.EndHorizontal();

            if (BuildProfileResolver.IsDevelopmentToolsEnabled)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(GameSettings.GodMode ? "Godmode: ON" : "Godmode: OFF"))
                {
                    bool enableGodMode = !GameSettings.GodMode;
                    GameSettings.SetGodMode(enableGodMode);
                    if (enableGodMode)
                        RefillPlayerResources();
                }

                if (GUILayout.Button("Refill (Godmode Safety)"))
                    RefillPlayerResources();
                GUILayout.EndHorizontal();
            }

            if (playerProgression != null)
            {
                GUILayout.BeginHorizontal();
                showPlayerHud = GUILayout.Toggle(showPlayerHud, "Show HUD", "Button");
                showPlayerStatsSnapshot = GUILayout.Toggle(showPlayerStatsSnapshot, "Show Stats Snapshot", "Button");
                GUILayout.EndHorizontal();

                GUILayout.Label($"HP: {playerProgression.CurrentHealth:0}/{playerProgression.MaxHealth:0}");
                GUILayout.Label($"Stamina: {playerProgression.CurrentStamina:0}/{playerProgression.MaxStamina:0}");
                GUILayout.Label($"Level: {playerProgression.xp.level}  EXP: {playerProgression.xp.exp}/{playerProgression.xp.expToNext}");
            }
            else
            {
                GUILayout.Label("Player not spawned.");
            }
        }

        private void DrawPlayerStatsSnapshot()
        {
            if (playerProgression == null || playerProgression.stats == null)
            {
                GUILayout.Label("Player stats unavailable.");
                return;
            }

            PlayerProgressionController progression = playerProgression;
            PlayerRelicController relics = playerRelics != null ? playerRelics : progression.GetComponent<PlayerRelicController>();
            WeaponEnhancerSystem enhancers = playerEnhancers != null ? playerEnhancers : progression.GetComponentInChildren<WeaponEnhancerSystem>(true);

            float damage = progression.stats.damage;
            if (relics != null)
                damage *= relics.GetDamageMultiplier();
            if (enhancers != null)
                damage = enhancers.GetEffectiveValue(StatType.Damage, damage);

            float critChance = progression.stats.critChance + (relics != null ? relics.GetCritChanceBonus() : 0f);
            float critMultiplier = progression.stats.critMultiplier + (relics != null ? relics.GetCritMultiplierBonus() : 0f);
            float lifeSteal = progression.stats.lifeSteal + (relics != null ? relics.GetLifeStealBonus() : 0f);
            float speed = progression.stats.speed + (relics != null ? relics.GetSpeedBonus() : 0f);
            float swingSpeed = progression.stats.swingSpeed + (relics != null ? relics.GetSwingSpeedBonus() : 0f);
            float damageReduction = progression.stats.damageReduction + (relics != null ? relics.GetDamageReductionBonus() : 0f);
            float dodgeChance = progression.stats.dodgeChance + (relics != null ? relics.GetDodgeChanceBonus() : 0f);
            float healthRegen = progression.stats.healthRegen;
            float staminaRegen = progression.stats.staminaRegen + (relics != null ? relics.GetStaminaRegenBonus() : 0f);

            if (enhancers != null)
            {
                critChance = enhancers.GetEffectiveValue(StatType.CritChance, critChance);
                critMultiplier = enhancers.GetEffectiveValue(StatType.CritMultiplier, critMultiplier);
                lifeSteal = enhancers.GetEffectiveValue(StatType.LifeSteal, lifeSteal);
                speed = enhancers.GetEffectiveValue(StatType.Speed, speed);
                swingSpeed = enhancers.GetEffectiveValue(StatType.SwingSpeed, swingSpeed);
                damageReduction = enhancers.GetEffectiveValue(StatType.DamageReduction, damageReduction);
                dodgeChance = enhancers.GetEffectiveValue(StatType.DodgeChance, dodgeChance);
                healthRegen = enhancers.GetEffectiveValue(StatType.HealthRegen, healthRegen);
                staminaRegen = enhancers.GetEffectiveValue(StatType.StaminaRegen, staminaRegen);
            }

            critChance = Mathf.Clamp01(critChance);
            lifeSteal = Mathf.Clamp01(lifeSteal);
            damageReduction = CombatBalanceCaps.ClampDamageReduction(damageReduction);
            dodgeChance = CombatBalanceCaps.ClampDodgeChance(dodgeChance);
            critMultiplier = CombatBalanceCaps.ClampCritMultiplier(critMultiplier);

            GUILayout.Label("Player Stats Snapshot");
            GUILayout.Label($"Damage: {damage:0.##}");
            GUILayout.Label($"Crit Chance: {critChance * 100f:0.##}%");
            GUILayout.Label($"Crit Multiplier: x{critMultiplier:0.##}");
            GUILayout.Label($"Life Steal: {lifeSteal * 100f:0.##}%");
            GUILayout.Label($"Move Speed: {speed:0.##}");
            GUILayout.Label($"Swing Speed: {swingSpeed:0.##}");
            GUILayout.Label($"Damage Reduction: {damageReduction * 100f:0.##}%");
            GUILayout.Label($"Dodge Chance: {dodgeChance * 100f:0.##}%");
            GUILayout.Label($"Health Regen: {healthRegen:0.##}/s");
            GUILayout.Label($"Stamina Regen: {staminaRegen:0.##}/s");
            GUILayout.Label($"Relics Active: {selectedRelics.Count} | Enhancers Active: {selectedEnhancers.Count}");
            DrawRelicRuntimeDiagnostics(progression, relics);
        }

        private static void DrawRelicRuntimeDiagnostics(PlayerProgressionController progression, PlayerRelicController relics)
        {
            if (progression == null || relics == null)
                return;

            int anvilStacks = relics.GetStacks(AnvilOfNightOathsRelicId);
            int choirStacks = relics.GetStacks(OssuaryChoirRelicId);
            int hourglassStacks = relics.GetStacks(BlackChapelHourglassRelicId);
            int censerStacks = relics.GetStacks(BlackMassCenserRelicId);
            if (anvilStacks <= 0 && choirStacks <= 0 && hourglassStacks <= 0 && censerStacks <= 0)
                return;

            GUILayout.Space(4f);
            GUILayout.Label("Relic Runtime Debug");

            if (anvilStacks > 0)
            {
                AnvilOfNightOathsRuntime anvilRuntime = progression.GetComponent<AnvilOfNightOathsRuntime>();
                if (anvilRuntime == null)
                {
                    GUILayout.Label("Anvil: runtime missing (effect not attached).");
                }
                else if (anvilRuntime.IsForgedActive)
                {
                    GUILayout.Label($"Anvil x{anvilStacks}: FORGED active ({anvilRuntime.ForgedTimeRemaining:0.0}s left)");
                }
                else
                {
                    int hitsToForge = Mathf.Max(1, anvilRuntime.HitsToForge);
                    int hitProgress = Mathf.Clamp(anvilRuntime.ForgedHitProgress, 0, hitsToForge);
                    GUILayout.Label($"Anvil x{anvilStacks}: forging progress {hitProgress}/{hitsToForge} hits");
                }
            }

            if (choirStacks > 0)
            {
                OssuaryChoirRuntime choirRuntime = progression.GetComponent<OssuaryChoirRuntime>();
                if (choirRuntime == null)
                {
                    GUILayout.Label("Ossuary Choir: runtime missing (effect not attached).");
                }
                else if (choirRuntime.IsChoirActiveNow)
                {
                    GUILayout.Label(
                        $"Ossuary Choir x{choirStacks}: ACTIVE ({choirRuntime.ChoirTimeRemaining:0.0}s left, skulls {choirRuntime.ActiveSkullCount})"
                    );
                }
                else
                {
                    int requiredKills = Mathf.Max(1, choirRuntime.RequiredKills);
                    int killProgress = Mathf.Clamp(choirRuntime.KillProgress, 0, requiredKills);
                    GUILayout.Label(
                        $"Ossuary Choir x{choirStacks}: kill chain {killProgress}/{requiredKills} in {choirRuntime.KillWindowSeconds:0.0}s"
                    );
                }
            }

            if (hourglassStacks > 0)
            {
                BlackChapelHourglassRuntime hourglassRuntime = progression.GetComponent<BlackChapelHourglassRuntime>();
                if (hourglassRuntime == null)
                {
                    GUILayout.Label("Black Chapel Hourglass: runtime missing (effect not attached).");
                }
                else
                {
                    GUILayout.Label(
                        $"Black Chapel Hourglass x{hourglassStacks}: charges {hourglassRuntime.CurrentCharges}/{Mathf.Max(1, hourglassRuntime.MaxCharges)}"
                    );
                    GUILayout.Label($"  Echo sword active: {(hourglassRuntime.IsEchoVisualActive ? "yes" : "no")}");
                    if (hourglassRuntime.HasPendingEcho)
                        GUILayout.Label($"  Echo ready: {hourglassRuntime.PendingEchoDamage:0} dmg");
                    else
                        GUILayout.Label($"  Last hit stored: {hourglassRuntime.LastHitDamage:0.##}");
                }
            }

            if (censerStacks > 0)
            {
                BlackMassCenserRuntime censerRuntime = progression.GetComponent<BlackMassCenserRuntime>();
                if (censerRuntime == null)
                {
                    GUILayout.Label("Black Mass Censer: runtime missing (effect not attached).");
                }
                else
                {
                    GUILayout.Label(
                        $"Black Mass Censer x{censerStacks}: {censerRuntime.CurrentRite} ({censerRuntime.RiteTimeRemaining:0.0}s left)"
                    );
                }
            }
        }

        private void DrawPlayerHudOverlay()
        {
            if (!showPlayerHud || playerProgression == null)
                return;

            int level = playerProgression.xp != null ? Mathf.Max(1, playerProgression.xp.level) : 1;
            int exp = playerProgression.xp != null ? Mathf.Max(0, playerProgression.xp.exp) : 0;
            int expToNext = playerProgression.xp != null ? Mathf.Max(1, playerProgression.xp.expToNext) : 1;

            float maxHealth = Mathf.Max(1f, playerProgression.MaxHealth);
            float currentHealth = Mathf.Clamp(playerProgression.CurrentHealth, 0f, maxHealth);

            float maxStamina = Mathf.Max(1f, playerProgression.MaxStamina);
            float currentStamina = Mathf.Clamp(playerProgression.CurrentStamina, 0f, maxStamina);

            const float hudWidth = 300f;
            const float hudHeight = 168f;
            const float margin = 16f;
            float hudX = Mathf.Max(margin, Screen.width - hudWidth - margin);
            Rect hudRect = new(hudX, margin, hudWidth, hudHeight);

            GUILayout.BeginArea(hudRect, "Sandbox HUD", GUI.skin.window);
            GUILayout.Label($"LVL {level}");
            DrawReadOnlyProgressBar("EXP", exp, expToNext, new Color(0.92f, 0.78f, 0.18f, 1f));
            DrawReadOnlyProgressBar("HP", currentHealth, maxHealth, new Color(0.86f, 0.17f, 0.22f, 1f));
            DrawReadOnlyProgressBar("Stamina", currentStamina, maxStamina, new Color(0.20f, 0.74f, 0.86f, 1f));
            GUILayout.EndArea();
        }

        private static void DrawReadOnlyProgressBar(string label, float current, float max, Color fillColor)
        {
            float safeMax = Mathf.Max(0.0001f, max);
            float clampedCurrent = Mathf.Clamp(current, 0f, safeMax);
            float normalized = Mathf.Clamp01(clampedCurrent / safeMax);

            GUILayout.Label($"{label}: {clampedCurrent:0}/{safeMax:0}");
            Rect barRect = GUILayoutUtility.GetRect(10f, 14f, GUILayout.ExpandWidth(true));
            GUI.Box(barRect, GUIContent.none);

            float fillWidth = Mathf.Max(0f, (barRect.width - 2f) * normalized);
            if (fillWidth <= 0f)
                return;

            Rect fillRect = new(
                barRect.x + 1f,
                barRect.y + 1f,
                fillWidth,
                Mathf.Max(1f, barRect.height - 2f)
            );

            Color previousColor = GUI.color;
            GUI.color = fillColor;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawZombieSection()
        {
            GUILayout.Label("Zombie Arena");

            string[] modeOptions = { "Walking", "Standing" };
            zombieMode = (ZombieMode)GUILayout.Toolbar((int)zombieMode, modeOptions);

            zombieSpawnCount = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(zombieSpawnCount, 1f, 60f)
            );
            GUILayout.Label($"Spawn Count: {zombieSpawnCount}");

            zombieSpawnRadius = GUILayout.HorizontalSlider(zombieSpawnRadius, 4f, 60f);
            GUILayout.Label($"Spawn Radius: {zombieSpawnRadius:0.0}");

            zombieMinSpawnDistance = GUILayout.HorizontalSlider(zombieMinSpawnDistance, 2f, 20f);
            GUILayout.Label($"Min Spawn Distance: {zombieMinSpawnDistance:0.0}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn Zombies"))
                SpawnZombies(zombieSpawnCount, zombieMode);

            if (GUILayout.Button("Reconfigure Spawned"))
                ReconfigureSpawnedZombies(zombieMode);

            if (GUILayout.Button("Clear Spawned"))
                ClearSpawnedZombies();
            GUILayout.EndHorizontal();

            GUILayout.Label($"Spawned by sandbox: {spawnedZombies.Count}");
        }

        private void DrawUpgradeSection()
        {
            GUILayout.Label("Upgrades");

            if (upgradeCatalog.Count == 0)
            {
                GUILayout.Label("No upgrades found. Check UpgradeLibrary assignment.");
                return;
            }

            upgradeValueScale = GUILayout.HorizontalSlider(upgradeValueScale, 0.25f, 4f);
            GUILayout.Label($"Value Scale: {upgradeValueScale:0.00}x");

            upgradesScroll = GUILayout.BeginScrollView(upgradesScroll, GUILayout.Height(120f));
            for (int i = 0; i < upgradeCatalog.Count; i++)
            {
                UpgradeCatalogEntry entry = upgradeCatalog[i];
                string title = $"{entry.label} ({entry.baseValue:0.##})";
                if (GUILayout.Toggle(selectedUpgradeIndex == i, title, "Button"))
                    selectedUpgradeIndex = i;
            }
            GUILayout.EndScrollView();

            if (selectedUpgradeIndex >= 0 && selectedUpgradeIndex < upgradeCatalog.Count)
            {
                if (GUILayout.Button("Add Selected Upgrade"))
                {
                    UpgradeCatalogEntry entry = upgradeCatalog[selectedUpgradeIndex];
                    selectedUpgrades.Add(
                        new UpgradeLoadoutEntry
                        {
                            label = entry.label,
                            stat = entry.stat,
                            value = entry.baseValue * upgradeValueScale
                        }
                    );
                    RebuildPlayerFromLoadout();
                }
            }
        }

        private void DrawRelicSection()
        {
            GUILayout.Label("Relics");
            relicSearch = GUILayout.TextField(relicSearch ?? string.Empty);

            List<RelicDefinition> filtered = GetFilteredRelics(relicSearch);
            relicScroll = GUILayout.BeginScrollView(relicScroll, GUILayout.Height(145f));
            for (int i = 0; i < filtered.Count; i++)
            {
                RelicDefinition relic = filtered[i];
                string label = $"{GetRelicDisplayName(relic)} [{relic.rarity}]";
                bool selected = selectedRelicIndex >= 0
                    && selectedRelicIndex < relicCatalog.Count
                    && relicCatalog[selectedRelicIndex] == relic;

                if (GUILayout.Toggle(selected, label, "Button"))
                    SetSelectedRelicIndex(relicCatalog.IndexOf(relic));
            }
            GUILayout.EndScrollView();

            if (selectedRelicIndex >= 0 && selectedRelicIndex < relicCatalog.Count)
            {
                if (GUILayout.Button("Add Selected Relic"))
                    AddRelicToLoadout(relicCatalog[selectedRelicIndex]);
            }

            if (selectedRelicPreview != null && Time.unscaledTime <= selectedRelicPreviewVisibleUntil)
            {
                float timeLeft = Mathf.Max(0f, selectedRelicPreviewVisibleUntil - Time.unscaledTime);
                GUILayout.Space(4f);
                GUILayout.Label($"Relic description ({timeLeft:0.0}s):");
                GUILayout.TextArea(GetRelicDescription(selectedRelicPreview), GUILayout.MinHeight(72f));
            }
        }

        private void DrawEnhancerSection()
        {
            GUILayout.Label("Enhancers");
            enhancerSearch = GUILayout.TextField(enhancerSearch ?? string.Empty);

            List<EnhancerDefinition> filtered = GetFilteredEnhancers(enhancerSearch);
            enhancerScroll = GUILayout.BeginScrollView(enhancerScroll, GUILayout.Height(145f));
            for (int i = 0; i < filtered.Count; i++)
            {
                EnhancerDefinition enhancer = filtered[i];
                string label = GetEnhancerDisplayName(enhancer);
                bool selected = selectedEnhancerIndex >= 0
                    && selectedEnhancerIndex < enhancerCatalog.Count
                    && enhancerCatalog[selectedEnhancerIndex] == enhancer;

                if (GUILayout.Toggle(selected, label, "Button"))
                    selectedEnhancerIndex = enhancerCatalog.IndexOf(enhancer);
            }
            GUILayout.EndScrollView();

            if (selectedEnhancerIndex >= 0 && selectedEnhancerIndex < enhancerCatalog.Count)
            {
                if (GUILayout.Button("Add Selected Enhancer"))
                    AddEnhancerToLoadout(enhancerCatalog[selectedEnhancerIndex]);
            }
        }

        private void DrawActiveLoadoutSection()
        {
            GUILayout.Label("Active Loadout");
            activeLoadoutScroll = GUILayout.BeginScrollView(activeLoadoutScroll, GUILayout.Height(205f));

            string relicKeyToIncrease = null;
            string relicKeyToDecrease = null;
            string enhancerKeyToIncrease = null;
            string enhancerKeyToDecrease = null;
            int upgradeIndexToRemove = -1;

            GUILayout.Label("Relics:");
            if (selectedRelics.Count == 0)
            {
                GUILayout.Label("  - none");
            }
            else
            {
                foreach (KeyValuePair<string, RelicLoadoutEntry> pair in selectedRelics)
                {
                    RelicLoadoutEntry entry = pair.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{GetRelicDisplayName(entry.definition)} x{entry.count}");
                    if (GUILayout.Button("+", GUILayout.Width(28f)))
                        relicKeyToIncrease = pair.Key;
                    if (GUILayout.Button("-", GUILayout.Width(28f)))
                        relicKeyToDecrease = pair.Key;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label("Enhancers:");
            if (selectedEnhancers.Count == 0)
            {
                GUILayout.Label("  - none");
            }
            else
            {
                foreach (KeyValuePair<string, EnhancerLoadoutEntry> pair in selectedEnhancers)
                {
                    EnhancerLoadoutEntry entry = pair.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{GetEnhancerDisplayName(entry.definition)} x{entry.count}");
                    if (GUILayout.Button("+", GUILayout.Width(28f)))
                        enhancerKeyToIncrease = pair.Key;
                    if (GUILayout.Button("-", GUILayout.Width(28f)))
                        enhancerKeyToDecrease = pair.Key;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label("Upgrades:");
            if (selectedUpgrades.Count == 0)
            {
                GUILayout.Label("  - none");
            }
            else
            {
                for (int i = 0; i < selectedUpgrades.Count; i++)
                {
                    UpgradeLoadoutEntry entry = selectedUpgrades[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{entry.label}: +{entry.value:0.##}");
                    if (GUILayout.Button("Remove", GUILayout.Width(68f)))
                        upgradeIndexToRemove = i;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();

            if (relicKeyToIncrease != null && selectedRelics.TryGetValue(relicKeyToIncrease, out RelicLoadoutEntry relicPlus))
                AddRelicToLoadout(relicPlus.definition);
            else if (relicKeyToDecrease != null)
                RemoveRelicFromLoadout(relicKeyToDecrease);

            if (enhancerKeyToIncrease != null && selectedEnhancers.TryGetValue(enhancerKeyToIncrease, out EnhancerLoadoutEntry enhancerPlus))
                AddEnhancerToLoadout(enhancerPlus.definition);
            else if (enhancerKeyToDecrease != null)
                RemoveEnhancerFromLoadout(enhancerKeyToDecrease);

            if (upgradeIndexToRemove >= 0 && upgradeIndexToRemove < selectedUpgrades.Count)
            {
                selectedUpgrades.RemoveAt(upgradeIndexToRemove);
                RebuildPlayerFromLoadout();
            }
        }

        private void DisableAutonomousSpawners()
        {
            SpawnManager[] legacySpawners = FindObjectsByType<SpawnManager>(FindObjectsSortMode.None);
            for (int i = 0; i < legacySpawners.Length; i++)
                legacySpawners[i].enabled = false;

            SpawnManagerML[] legacyMlSpawners = FindObjectsByType<SpawnManagerML>(FindObjectsSortMode.None);
            for (int i = 0; i < legacyMlSpawners.Length; i++)
                legacyMlSpawners[i].enabled = false;

            EnemySimulationManager[] simulationManagers = FindObjectsByType<EnemySimulationManager>(FindObjectsSortMode.None);
            for (int i = 0; i < simulationManagers.Length; i++)
                simulationManagers[i].enabled = false;

            EnemyActivationController[] activationControllers = FindObjectsByType<EnemyActivationController>(FindObjectsSortMode.None);
            for (int i = 0; i < activationControllers.Length; i++)
                activationControllers[i].enabled = false;

            HordeAISystem[] hordeSystems = FindObjectsByType<HordeAISystem>(FindObjectsSortMode.None);
            for (int i = 0; i < hordeSystems.Length; i++)
                hordeSystems[i].enabled = true;

            GameObject legacySpawnManagerRoot = GameObject.Find("SpawnManager");
            if (legacySpawnManagerRoot != null)
                legacySpawnManagerRoot.SetActive(false);
        }

        private void ResolveAssetReferences()
        {
#if UNITY_EDITOR
            playerPrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bob.prefab");
            zombiePrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie.prefab");
            relicLibrary ??= AssetDatabase.LoadAssetAtPath<RelicLibrary>("Assets/Data/Relics/RelicLibrary.asset");
#endif

            if (relicLibrary == null)
            {
                RelicLibrary[] loadedLibraries = Resources.FindObjectsOfTypeAll<RelicLibrary>();
                if (loadedLibraries != null && loadedLibraries.Length > 0)
                    relicLibrary = loadedLibraries[0];
            }
        }

        private void ConfigureFloatingTextSystem()
        {
            FloatingTextSystem system = FloatingTextSystem.EnsureInstance();
            if (system == null)
                return;

            FloatingText floatingTextPrefab = Resources.Load<FloatingText>("UI/FloatingText");
#if UNITY_EDITOR
            if (floatingTextPrefab == null)
                floatingTextPrefab = AssetDatabase.LoadAssetAtPath<FloatingText>(FloatingTextPrefabPath);
#endif
            if (floatingTextPrefab == null)
            {
                Debug.LogWarning("[TestSceneSandbox] Missing FloatingText prefab for sandbox configuration.", this);
                return;
            }

            system.prefab = floatingTextPrefab;
        }

        private void BuildAllCatalogs()
        {
            BuildRelicCatalog();
            BuildEnhancerCatalog();
            BuildUpgradeCatalog();
            ClampSelectionIndices();
        }

        private void BuildRelicCatalog()
        {
            relicCatalog.Clear();

            if (relicLibrary != null && relicLibrary.relics != null)
            {
                for (int i = 0; i < relicLibrary.relics.Count; i++)
                {
                    RelicDefinition def = relicLibrary.relics[i];
                    if (def == null || relicCatalog.Contains(def))
                        continue;

                    relicCatalog.Add(def);
                }
            }

#if UNITY_EDITOR
            if (relicCatalog.Count == 0)
            {
                List<RelicDefinition> fromAssets = LoadAllAssetsInEditor<RelicDefinition>();
                for (int i = 0; i < fromAssets.Count; i++)
                {
                    RelicDefinition def = fromAssets[i];
                    if (def != null && !relicCatalog.Contains(def))
                        relicCatalog.Add(def);
                }
            }
#endif

            relicCatalog.Sort((a, b) => string.Compare(
                GetRelicDisplayName(a),
                GetRelicDisplayName(b),
                StringComparison.OrdinalIgnoreCase
            ));
        }

        private void BuildEnhancerCatalog()
        {
            enhancerCatalog.Clear();

#if UNITY_EDITOR
            List<EnhancerDefinition> fromAssets = LoadAllAssetsInEditor<EnhancerDefinition>();
            for (int i = 0; i < fromAssets.Count; i++)
            {
                EnhancerDefinition def = fromAssets[i];
                if (def != null && !enhancerCatalog.Contains(def))
                    enhancerCatalog.Add(def);
            }
#endif

            if (enhancerCatalog.Count == 0)
            {
                EnhancerDefinition[] loadedEnhancers = Resources.FindObjectsOfTypeAll<EnhancerDefinition>();
                for (int i = 0; i < loadedEnhancers.Length; i++)
                {
                    EnhancerDefinition def = loadedEnhancers[i];
                    if (def != null && !enhancerCatalog.Contains(def))
                        enhancerCatalog.Add(def);
                }
            }

            enhancerCatalog.Sort((a, b) => string.Compare(
                GetEnhancerDisplayName(a),
                GetEnhancerDisplayName(b),
                StringComparison.OrdinalIgnoreCase
            ));
        }

        private void BuildUpgradeCatalog()
        {
            upgradeCatalog.Clear();
            if (playerProgression == null || playerProgression.upgradeLibrary == null || playerProgression.upgradeLibrary.entries == null)
                return;

            List<UpgradeLibrary.Entry> entries = playerProgression.upgradeLibrary.entries;
            for (int i = 0; i < entries.Count; i++)
            {
                UpgradeLibrary.Entry entry = entries[i];
                if (entry == null)
                    continue;

                upgradeCatalog.Add(
                    new UpgradeCatalogEntry
                    {
                        label = entry.stat.ToString(),
                        stat = entry.stat,
                        baseValue = entry.baseValue
                    }
                );
            }
        }

#if UNITY_EDITOR
        private static List<T> LoadAllAssetsInEditor<T>() where T : UnityEngine.Object
        {
            List<T> result = new();
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || result.Contains(asset))
                    continue;

                result.Add(asset);
            }

            return result;
        }
#endif

        private void SpawnFreshSandboxPlayer()
        {
            PlayerProgressionController[] existingPlayers = FindObjectsByType<PlayerProgressionController>(FindObjectsSortMode.None);
            for (int i = 0; i < existingPlayers.Length; i++)
            {
                PlayerProgressionController existing = existingPlayers[i];
                if (existing != null)
                    Destroy(existing.gameObject);
            }

            Vector3 spawnPosition = ResolveGroundedPosition(Vector3.zero, SpawnHeightFallback);
            SpawnPlayer(spawnPosition, Quaternion.identity);
            ApplyLoadoutToCurrentPlayer();
            DisableNonPlayerMainCameras();
        }

        private void SpawnPlayer(Vector3 position, Quaternion rotation)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[TestSceneSandbox] Missing player prefab (expected Assets/Prefabs/Bob.prefab).", this);
                return;
            }

            playerInstance = Instantiate(playerPrefab, position, rotation);
            playerInstance.name = "SandboxPlayer";

            playerProgression = playerInstance.GetComponent<PlayerProgressionController>();
            playerRelics = playerInstance.GetComponent<PlayerRelicController>();
            playerEnhancers = playerInstance.GetComponentInChildren<WeaponEnhancerSystem>(true);

            if (playerProgression != null)
                playerProgression.LevelingEnabled = false;

            PlayerLocator.Invalidate();
            RefillPlayerResources();
        }

        private void RebuildPlayerFromLoadout()
        {
            if (isRebuildInFlight)
                return;

            StartCoroutine(RebuildPlayerFromLoadoutRoutine());
        }

        private IEnumerator RebuildPlayerFromLoadoutRoutine()
        {
            isRebuildInFlight = true;

            Vector3 position = playerInstance != null ? playerInstance.transform.position : ResolveGroundedPosition(Vector3.zero, SpawnHeightFallback);
            Quaternion rotation = playerInstance != null ? playerInstance.transform.rotation : Quaternion.identity;

            if (playerInstance != null)
            {
                if (playerInstance.CompareTag("Player"))
                    playerInstance.tag = "Untagged";

                playerInstance.SetActive(false);
                Destroy(playerInstance);
                playerInstance = null;
                playerProgression = null;
                playerRelics = null;
                playerEnhancers = null;
                PlayerLocator.Invalidate();
            }

            yield return null;

            SpawnPlayer(position, rotation);
            ApplyLoadoutToCurrentPlayer();
            DisableNonPlayerMainCameras();

            isRebuildInFlight = false;
        }

        private void ApplyLoadoutToCurrentPlayer()
        {
            if (playerProgression == null)
                return;

            if (playerProgression.stats != null)
            {
                playerProgression.stats.ResetToBase();
                for (int i = 0; i < selectedUpgrades.Count; i++)
                {
                    UpgradeLoadoutEntry upgrade = selectedUpgrades[i];
                    playerProgression.stats.Apply(upgrade.stat, upgrade.value);
                }
            }

            if (playerRelics != null)
            {
                foreach (KeyValuePair<string, RelicLoadoutEntry> pair in selectedRelics)
                {
                    RelicLoadoutEntry entry = pair.Value;
                    if (entry == null || entry.definition == null)
                        continue;

                    for (int i = 0; i < entry.count; i++)
                    {
                        bool applied = playerRelics.AddRelic(entry.definition);
                        if (!applied)
                            Debug.LogWarning($"[TestSceneSandbox] Failed to apply relic '{GetRelicDisplayName(entry.definition)}' ({entry.definition.id}).", this);
                    }
                }
            }

            if (playerEnhancers != null)
            {
                foreach (KeyValuePair<string, EnhancerLoadoutEntry> pair in selectedEnhancers)
                {
                    EnhancerLoadoutEntry entry = pair.Value;
                    if (entry == null || entry.definition == null)
                        continue;

                    for (int i = 0; i < entry.count; i++)
                        playerEnhancers.AddEnhancer(entry.definition);
                }
            }

            RefillPlayerResources();
            playerProgression.NotifyStatsChanged();
            BuildUpgradeCatalog();
        }

        private void RefillPlayerResources()
        {
            if (playerProgression == null)
                return;

            playerProgression.currentHealth = playerProgression.MaxHealth;
            playerProgression.currentStamina = playerProgression.MaxStamina;
            playerProgression.NotifyStatsChanged();
        }

        private void AddRelicToLoadout(RelicDefinition relic)
        {
            if (relic == null)
                return;

            string key = GetRelicKey(relic);
            if (string.IsNullOrWhiteSpace(key))
                return;

            int maxStacks = relic.stackable ? Mathf.Max(1, relic.maxStacks) : 1;
            if (selectedRelics.TryGetValue(key, out RelicLoadoutEntry existing))
                existing.count = Mathf.Clamp(existing.count + 1, 1, maxStacks);
            else
                selectedRelics[key] = new RelicLoadoutEntry { definition = relic, count = 1 };

            RebuildPlayerFromLoadout();
        }

        private void RemoveRelicFromLoadout(string key)
        {
            if (!selectedRelics.TryGetValue(key, out RelicLoadoutEntry existing))
                return;

            existing.count--;
            if (existing.count <= 0)
                selectedRelics.Remove(key);

            RebuildPlayerFromLoadout();
        }

        private void AddEnhancerToLoadout(EnhancerDefinition enhancer)
        {
            if (enhancer == null)
                return;

            string key = GetEnhancerKey(enhancer);
            if (string.IsNullOrWhiteSpace(key))
                return;

            int maxStacks = Mathf.Max(1, enhancer.maxStacks);
            if (selectedEnhancers.TryGetValue(key, out EnhancerLoadoutEntry existing))
                existing.count = Mathf.Clamp(existing.count + 1, 1, maxStacks);
            else
                selectedEnhancers[key] = new EnhancerLoadoutEntry { definition = enhancer, count = 1 };

            RebuildPlayerFromLoadout();
        }

        private void RemoveEnhancerFromLoadout(string key)
        {
            if (!selectedEnhancers.TryGetValue(key, out EnhancerLoadoutEntry existing))
                return;

            existing.count--;
            if (existing.count <= 0)
                selectedEnhancers.Remove(key);

            RebuildPlayerFromLoadout();
        }

        private void SpawnZombies(int count, ZombieMode mode)
        {
            if (zombiePrefab == null)
            {
                Debug.LogError("[TestSceneSandbox] Missing zombie prefab (expected Assets/Prefabs/Zombie.prefab).", this);
                return;
            }

            if (playerInstance == null)
                SpawnFreshSandboxPlayer();

            Vector3 center = playerInstance != null ? playerInstance.transform.position : Vector3.zero;
            int spawnAmount = Mathf.Max(1, count);

            for (int i = 0; i < spawnAmount; i++)
            {
                Vector3 spawnPosition = SampleZombieSpawnPosition(center);
                GameObject zombie = Instantiate(
                    zombiePrefab,
                    spawnPosition,
                    Quaternion.identity
                );

                if (playerInstance != null)
                {
                    Vector3 look = playerInstance.transform.position - zombie.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.001f)
                        zombie.transform.rotation = Quaternion.LookRotation(look.normalized);
                }

                InitializeSpawnedZombie(zombie);
                ApplyZombieMode(zombie, mode);
                spawnedZombies.Add(new SpawnedZombieEntry { gameObject = zombie });
            }
        }

        private void ReconfigureSpawnedZombies(ZombieMode mode)
        {
            for (int i = 0; i < spawnedZombies.Count; i++)
            {
                SpawnedZombieEntry entry = spawnedZombies[i];
                if (entry == null || entry.gameObject == null)
                    continue;

                ApplyZombieMode(entry.gameObject, mode);
            }
        }

        private void ClearSpawnedZombies()
        {
            for (int i = 0; i < spawnedZombies.Count; i++)
            {
                SpawnedZombieEntry entry = spawnedZombies[i];
                if (entry != null && entry.gameObject != null)
                    Destroy(entry.gameObject);
            }

            spawnedZombies.Clear();
        }

        private void CleanupZombieList()
        {
            for (int i = spawnedZombies.Count - 1; i >= 0; i--)
            {
                SpawnedZombieEntry entry = spawnedZombies[i];
                if (entry == null || entry.gameObject == null)
                    spawnedZombies.RemoveAt(i);
            }
        }

        private void InitializeSpawnedZombie(GameObject zombie)
        {
            if (zombie == null)
                return;

            EnemyCombatant enemyCombatant = zombie.GetComponent<EnemyCombatant>();
            Combatant combatant = zombie.GetComponent<Combatant>();

            if (enemyCombatant != null && enemyCombatant.stats != null && combatant != null)
                combatant.Initialize(enemyCombatant.stats.maxHealth);
        }

        private static void ApplyZombieMode(GameObject zombie, ZombieMode mode)
        {
            if (zombie == null)
                return;

            ZombieAI ai = zombie.GetComponent<ZombieAI>();
            Rigidbody rb = zombie.GetComponent<Rigidbody>();

            switch (mode)
            {
                case ZombieMode.Standing:
                    if (ai != null)
                        ai.enabled = false;

                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.constraints =
                            RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationZ |
                            RigidbodyConstraints.FreezePositionX |
                            RigidbodyConstraints.FreezePositionZ;
                    }
                    break;

                default:
                    if (rb != null)
                    {
                        rb.constraints =
                            RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationZ;
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    if (ai != null)
                        ai.enabled = true;
                    break;
            }
        }

        private Vector3 SampleZombieSpawnPosition(Vector3 center)
        {
            float minDistance = Mathf.Max(1f, zombieMinSpawnDistance);
            float maxDistance = Mathf.Max(minDistance + 0.5f, zombieSpawnRadius);
            LayerMask mask = groundMask.value != 0 ? groundMask : Physics.DefaultRaycastLayers;

            for (int i = 0; i < 24; i++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized;
                if (randomCircle.sqrMagnitude <= 0.0001f)
                    randomCircle = Vector2.right;

                float dist = UnityEngine.Random.Range(minDistance, maxDistance);
                Vector3 candidate = center + new Vector3(randomCircle.x, 0f, randomCircle.y) * dist;

                if (Physics.Raycast(
                        candidate + Vector3.up * 48f,
                        Vector3.down,
                        out RaycastHit hit,
                        120f,
                        mask,
                        QueryTriggerInteraction.Ignore
                    ))
                {
                    return hit.point;
                }
            }

            return center + Vector3.forward * minDistance;
        }

        private void DisableNonPlayerMainCameras()
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || cam.gameObject == null)
                    continue;

                if (playerInstance != null && cam.transform.IsChildOf(playerInstance.transform))
                    continue;

                if (cam.CompareTag("MainCamera") || string.Equals(cam.gameObject.name, "Main Camera", StringComparison.Ordinal))
                    cam.gameObject.SetActive(false);
            }
        }

        private void SetCursorLockMode(bool lockForGameplay)
        {
            lockCursorForGameplay = lockForGameplay;
            EnforceCursorMode();
        }

        private void EnforceCursorMode()
        {
            Cursor.lockState = lockCursorForGameplay
                ? CursorLockMode.Locked
                : CursorLockMode.None;
            Cursor.visible = !lockCursorForGameplay;
        }

        private void HandleCursorToggleHotkey()
        {
            if (!WasCursorToggleHotkeyPressed())
                return;

            SetCursorLockMode(!lockCursorForGameplay);
        }

        private bool WasCursorToggleHotkeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && IsInputSystemHotkeyPressed(keyboard, cursorToggleHotkey))
                return true;

            return false;
        }

        private static string GetSandboxWindowTitle()
        {
            string version = string.IsNullOrWhiteSpace(Application.version)
                ? "0.7.2"
                : Application.version;

            return $"{version} Test Sandbox";
        }

        private static bool IsInputSystemHotkeyPressed(Keyboard keyboard, KeyCode hotkey)
        {
            if (keyboard == null)
                return false;

            return hotkey switch
            {
                KeyCode.F1 => keyboard.f1Key.wasPressedThisFrame,
                KeyCode.F2 => keyboard.f2Key.wasPressedThisFrame,
                KeyCode.F3 => keyboard.f3Key.wasPressedThisFrame,
                KeyCode.F4 => keyboard.f4Key.wasPressedThisFrame,
                KeyCode.F5 => keyboard.f5Key.wasPressedThisFrame,
                KeyCode.F6 => keyboard.f6Key.wasPressedThisFrame,
                KeyCode.F7 => keyboard.f7Key.wasPressedThisFrame,
                KeyCode.F8 => keyboard.f8Key.wasPressedThisFrame,
                KeyCode.F9 => keyboard.f9Key.wasPressedThisFrame,
                KeyCode.F10 => keyboard.f10Key.wasPressedThisFrame,
                KeyCode.F11 => keyboard.f11Key.wasPressedThisFrame,
                KeyCode.F12 => keyboard.f12Key.wasPressedThisFrame,
                _ => keyboard.f1Key.wasPressedThisFrame
            };
        }

        private void SetSelectedRelicIndex(int index)
        {
            if (index < 0 || index >= relicCatalog.Count)
                return;

            bool changed = selectedRelicIndex != index;
            selectedRelicIndex = index;
            if (!changed)
                return;

            selectedRelicPreview = relicCatalog[index];
            selectedRelicPreviewVisibleUntil = Time.unscaledTime + Mathf.Max(0.5f, relicDescriptionDurationSeconds);
        }

        private static string GetRelicDescription(RelicDefinition relic)
        {
            if (relic == null)
                return "No relic selected.";

            if (!string.IsNullOrWhiteSpace(relic.description))
                return relic.description;

            if (relic.effect != null && !string.IsNullOrWhiteSpace(relic.effect.description))
                return relic.effect.description;

            return "No description available.";
        }

        private static string GetRelicKey(RelicDefinition relic)
        {
            if (relic == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(relic.id))
                return relic.id;

            return relic.name;
        }

        private static string GetEnhancerKey(EnhancerDefinition enhancer)
        {
            if (enhancer == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(enhancer.enhancerId))
                return enhancer.enhancerId;

            return enhancer.name;
        }

        private static string GetRelicDisplayName(RelicDefinition relic)
        {
            if (relic == null)
                return "<null>";

            return string.IsNullOrWhiteSpace(relic.displayName) ? relic.name : relic.displayName;
        }

        private static string GetEnhancerDisplayName(EnhancerDefinition enhancer)
        {
            if (enhancer == null)
                return "<null>";

            return string.IsNullOrWhiteSpace(enhancer.enhancerId)
                ? enhancer.name
                : enhancer.enhancerId;
        }

        private List<RelicDefinition> GetFilteredRelics(string filter)
        {
            List<RelicDefinition> result = new();
            string normalizedFilter = (filter ?? string.Empty).Trim();

            for (int i = 0; i < relicCatalog.Count; i++)
            {
                RelicDefinition relic = relicCatalog[i];
                if (relic == null)
                    continue;

                if (string.IsNullOrWhiteSpace(normalizedFilter))
                {
                    result.Add(relic);
                    continue;
                }

                string name = GetRelicDisplayName(relic);
                string id = relic.id ?? string.Empty;

                if (name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(relic);
                }
            }

            return result;
        }

        private List<EnhancerDefinition> GetFilteredEnhancers(string filter)
        {
            List<EnhancerDefinition> result = new();
            string normalizedFilter = (filter ?? string.Empty).Trim();

            for (int i = 0; i < enhancerCatalog.Count; i++)
            {
                EnhancerDefinition enhancer = enhancerCatalog[i];
                if (enhancer == null)
                    continue;

                if (string.IsNullOrWhiteSpace(normalizedFilter))
                {
                    result.Add(enhancer);
                    continue;
                }

                string name = enhancer.name ?? string.Empty;
                string id = enhancer.enhancerId ?? string.Empty;
                if (name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    || id.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(enhancer);
                }
            }

            return result;
        }

        private static Vector3 ResolveGroundedPosition(Vector3 target, float yFallback)
        {
            LayerMask mask = LayerMask.GetMask("Ground");
            if (mask.value == 0)
                mask = Physics.DefaultRaycastLayers;

            Vector3 rayOrigin = target + Vector3.up * 64f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 140f, mask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            return new Vector3(target.x, yFallback, target.z);
        }

        private void ClampSelectionIndices()
        {
            selectedRelicIndex = Mathf.Clamp(selectedRelicIndex, -1, relicCatalog.Count - 1);
            selectedEnhancerIndex = Mathf.Clamp(selectedEnhancerIndex, -1, enhancerCatalog.Count - 1);
            selectedUpgradeIndex = Mathf.Clamp(selectedUpgradeIndex, -1, upgradeCatalog.Count - 1);
        }

        private static bool IsSandboxSceneActive()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid() && string.Equals(activeScene.name, SandboxSceneName, StringComparison.Ordinal);
        }

        private enum ZombieMode
        {
            Walking = 0,
            Standing = 1
        }

        private sealed class RelicLoadoutEntry
        {
            public RelicDefinition definition;
            public int count;
        }

        private sealed class EnhancerLoadoutEntry
        {
            public EnhancerDefinition definition;
            public int count;
        }

        private sealed class UpgradeLoadoutEntry
        {
            public string label;
            public StatType stat;
            public float value;
        }

        private sealed class UpgradeCatalogEntry
        {
            public string label;
            public StatType stat;
            public float baseValue;
        }

        private sealed class SpawnedZombieEntry
        {
            public GameObject gameObject;
        }
    }
}
