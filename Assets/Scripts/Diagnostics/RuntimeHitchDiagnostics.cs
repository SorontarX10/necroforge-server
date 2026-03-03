using System;
using System.IO;
using System.Text;
using GrassSim.AI;
using GrassSim.Core;
using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public sealed class RuntimeHitchDiagnostics : MonoBehaviour
{
    [Serializable]
    private sealed class HitchEventRecord
    {
        public string utc_timestamp;
        public int frame;
        public float realtime_since_startup_s;
        public float frame_ms;
        public float moving_avg_frame_ms;
        public float cpu_main_thread_ms;
        public float cpu_render_thread_ms;
        public float gc_alloc_kb;
        public float draw_calls;
        public float setpass_calls;
        public int active_enemy_count;
        public int sim_enemy_count;
        public int horde_agent_count;
        public int horde_perception_count;
        public int activation_scanned;
        public int activation_spawned;
        public int activation_despawned;
        public int activation_cleanup_removed;
        public int sim_recycle_moved;
        public int sim_recycle_processed;
        public float sim_recycle_duration_ms;
        public int query_global_used;
        public int query_global_budget;
        public int query_owner_states;
        public int query_cache_count;
        public int query_cleanup_removed;
        public int pool_get_calls;
        public int pool_hits;
        public int pool_instantiates;
        public int pool_returns;
        public int pool_destroy_fallbacks;
        public int pool_inactive_count;
        public int relic_update_count;
        public int relic_update_processed;
        public int relic_update_budget;
        public float relic_update_duration_ms;
        public bool severe;
        public string probable_cause;
        public bool app_focused;
        public bool app_run_in_background;
        public int gc_gen0_collections_delta;
        public int gc_gen1_collections_delta;
        public int gc_gen2_collections_delta;
        public bool main_thread_counter_valid;
        public bool render_thread_counter_valid;
        public bool gc_counter_valid;
        public bool draw_calls_counter_valid;
        public bool setpass_counter_valid;
    }

    [Header("Detection")]
    [SerializeField, Min(50f)] private float hitchThresholdMs = 250f;
    [SerializeField, Min(100f)] private float severeHitchThresholdMs = 1000f;
    [SerializeField, Min(0f)] private float minSecondsBetweenLogs = 0.5f;
    [SerializeField, Range(0.01f, 0.5f)] private float movingAverageBlend = 0.06f;
    [SerializeField] private bool ignoreUnfocusedHitches = true;
    [SerializeField, Min(0f)] private float focusSettleSeconds = 0.3f;
    [SerializeField] private bool forceRunInBackground = true;

    [Header("Output")]
    [SerializeField] private bool writeJsonLog = true;
    [SerializeField] private bool mirrorToProjectResultsInEditor = true;
    [SerializeField] private bool emitWarningLog = true;

    private static RuntimeHitchDiagnostics instance;
    private static bool shuttingDown;
    private static bool telemetryModeReported;

    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder renderThreadRecorder;
    private ProfilerRecorder gcAllocRecorder;
    private ProfilerRecorder drawCallsRecorder;
    private ProfilerRecorder setPassRecorder;

    private float movingAvgFrameMs;
    private float lastLoggedAt = -999f;
    private float focusRegainedAt = -999f;
    private bool wasFocused = true;
    private string persistentLogPath;
    private string projectLogPath;
    private int lastGcGen0CollectionCount;
    private int lastGcGen1CollectionCount;
    private int lastGcGen2CollectionCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shuttingDown = false;
        telemetryModeReported = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!BuildProfileResolver.IsLocalTelemetryEnabled)
        {
            ReportTelemetryModeOnce("Bootstrap skipped", enabled: false);
            return;
        }

        ReportTelemetryModeOnce("Bootstrap enabled", enabled: true);
        EnsureInstance();
    }

    private static RuntimeHitchDiagnostics EnsureInstance()
    {
        if (shuttingDown || !BuildProfileResolver.IsLocalTelemetryEnabled)
            return null;

        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<RuntimeHitchDiagnostics>();
        if (instance != null)
            return instance;

        GameObject go = new("RuntimeHitchDiagnostics");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RuntimeHitchDiagnostics>();
        return instance;
    }

    private void Awake()
    {
        if (!BuildProfileResolver.IsLocalTelemetryEnabled)
        {
            ReportTelemetryModeOnce("Instance disabled", enabled: false);
            Destroy(gameObject);
            return;
        }

        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        if (forceRunInBackground)
            Application.runInBackground = true;

        wasFocused = Application.isFocused;
        focusRegainedAt = Time.unscaledTime;
        lastGcGen0CollectionCount = GC.CollectionCount(0);
        lastGcGen1CollectionCount = GC.CollectionCount(1);
        lastGcGen2CollectionCount = GC.CollectionCount(2);
        InitializePaths();
        StartRecorders();
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }

    private void OnDestroy()
    {
        DisposeRecorder(ref mainThreadRecorder);
        DisposeRecorder(ref renderThreadRecorder);
        DisposeRecorder(ref gcAllocRecorder);
        DisposeRecorder(ref drawCallsRecorder);
        DisposeRecorder(ref setPassRecorder);

        if (instance == this)
            instance = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && !wasFocused)
            focusRegainedAt = Time.unscaledTime;

        wasFocused = hasFocus;
    }

    private void Update()
    {
        int gcGen0Count = GC.CollectionCount(0);
        int gcGen1Count = GC.CollectionCount(1);
        int gcGen2Count = GC.CollectionCount(2);

        int gcGen0Delta = Mathf.Max(0, gcGen0Count - lastGcGen0CollectionCount);
        int gcGen1Delta = Mathf.Max(0, gcGen1Count - lastGcGen1CollectionCount);
        int gcGen2Delta = Mathf.Max(0, gcGen2Count - lastGcGen2CollectionCount);

        lastGcGen0CollectionCount = gcGen0Count;
        lastGcGen1CollectionCount = gcGen1Count;
        lastGcGen2CollectionCount = gcGen2Count;

        if (ignoreUnfocusedHitches)
        {
            if (!Application.isFocused)
            {
                wasFocused = false;
                return;
            }

            if (!wasFocused)
            {
                wasFocused = true;
                focusRegainedAt = Time.unscaledTime;
                movingAvgFrameMs = 0f;
                return;
            }

            if (Time.unscaledTime - focusRegainedAt < Mathf.Max(0f, focusSettleSeconds))
                return;
        }

        float frameMs = Mathf.Max(0.01f, Time.unscaledDeltaTime * 1000f);
        if (movingAvgFrameMs <= 0f)
            movingAvgFrameMs = frameMs;
        else
            movingAvgFrameMs = Mathf.Lerp(movingAvgFrameMs, frameMs, Mathf.Clamp01(movingAverageBlend));

        bool severe = frameMs >= Mathf.Max(hitchThresholdMs, severeHitchThresholdMs);
        bool hitch = frameMs >= hitchThresholdMs && frameMs >= movingAvgFrameMs * 1.8f;
        if (!severe && !hitch)
            return;

        float now = Time.unscaledTime;
        if (now - lastLoggedAt < minSecondsBetweenLogs)
            return;

        lastLoggedAt = now;
        RecordHitch(frameMs, severe, gcGen0Delta, gcGen1Delta, gcGen2Delta);
    }

    private void InitializePaths()
    {
        if (!LocalTelemetryFileOutput.CanWriteFiles)
        {
            persistentLogPath = null;
            projectLogPath = null;
            return;
        }

        persistentLogPath = Path.Combine(Application.persistentDataPath, "hitch_events.jsonl");
        LocalTelemetryFileOutput.TryEnsureDirectoryForFile(persistentLogPath, "HitchDiag");

        projectLogPath = null;
        if (Application.isEditor && mirrorToProjectResultsInEditor)
        {
            projectLogPath = Path.GetFullPath(Path.Combine("results", "perf", "hitch_events.jsonl"));
            LocalTelemetryFileOutput.TryEnsureDirectoryForFile(projectLogPath, "HitchDiag");
        }

        if (emitWarningLog)
            Debug.LogWarning($"[HitchDiag] Enabled. Persistent log: {persistentLogPath}");
    }

    private void RecordHitch(float frameMs, bool severe, int gcGen0Delta, int gcGen1Delta, int gcGen2Delta)
    {
        EnemyActivationController.ActivationRuntimeSnapshot activation = default;
        EnemyActivationController activationController = EnemyActivationController.Instance;
        if (activationController != null)
            activation = activationController.GetRuntimeSnapshot();

        EnemySimulationManager.RuntimeSnapshot simulation = default;
        EnemySimulationManager simulationManager = EnemySimulationManager.Instance;
        if (simulationManager != null)
            simulation = simulationManager.GetRuntimeSnapshot();

        EnemyQueryService.RuntimeSnapshot query = EnemyQueryService.GetRuntimeSnapshot();
        SimplePool.RuntimeSnapshot pool = SimplePool.GetRuntimeSnapshot();

        HordeAISystem.RuntimeSnapshot horde = default;
        HordeAISystem.TryGetRuntimeSnapshot(out horde);

        RelicBatchedTickSystem.RuntimeSnapshot relic = default;
        RelicBatchedTickSystem.TryGetRuntimeSnapshot(out relic);

        bool mainCounterValid = HasSample(mainThreadRecorder);
        bool renderCounterValid = HasSample(renderThreadRecorder);
        bool gcCounterValid = HasSample(gcAllocRecorder);
        bool drawCounterValid = HasSample(drawCallsRecorder);
        bool setPassCounterValid = HasSample(setPassRecorder);

        float mainThreadMs = (float)ReadTimeMilliseconds(mainThreadRecorder, -1d);
        float renderThreadMs = (float)ReadTimeMilliseconds(renderThreadRecorder, -1d);
        float gcAllocKb = (float)ReadBytesAsKilobytes(gcAllocRecorder, 0d);
        float drawCalls = (float)ReadCount(drawCallsRecorder, 0d);
        float setPassCalls = (float)ReadCount(setPassRecorder, 0d);

        string probableCause = BuildProbableCause(
            frameMs,
            mainThreadMs,
            gcAllocKb,
            gcGen0Delta,
            gcGen1Delta,
            gcGen2Delta,
            activation,
            simulation,
            query,
            pool,
            horde,
            relic
        );

        var record = new HitchEventRecord
        {
            utc_timestamp = DateTime.UtcNow.ToString("O"),
            frame = Time.frameCount,
            realtime_since_startup_s = Time.realtimeSinceStartup,
            frame_ms = frameMs,
            moving_avg_frame_ms = movingAvgFrameMs,
            cpu_main_thread_ms = mainThreadMs,
            cpu_render_thread_ms = renderThreadMs,
            gc_alloc_kb = gcAllocKb,
            draw_calls = drawCalls,
            setpass_calls = setPassCalls,
            active_enemy_count = activationController != null ? activationController.ActiveCount : 0,
            sim_enemy_count = simulationManager != null ? simulationManager.SimulatedCount : 0,
            horde_agent_count = horde.agentCount,
            horde_perception_count = horde.perceptionCount,
            activation_scanned = activation.scannedCandidates,
            activation_spawned = activation.spawnedThisTick,
            activation_despawned = activation.despawnedThisTick,
            activation_cleanup_removed = activation.cleanupRemoved,
            sim_recycle_moved = simulation.recycleMoved,
            sim_recycle_processed = simulation.recycleProcessed,
            sim_recycle_duration_ms = simulation.recycleDurationMs,
            query_global_used = query.globalQueriesThisFrame,
            query_global_budget = query.globalQueryBudget,
            query_owner_states = query.ownerStateCount,
            query_cache_count = query.combatantCacheCount,
            query_cleanup_removed = query.lastCleanupRemoved,
            pool_get_calls = pool.getCallsThisFrame,
            pool_hits = pool.pooledHitsThisFrame,
            pool_instantiates = pool.instantiatesThisFrame,
            pool_returns = pool.returnCallsThisFrame,
            pool_destroy_fallbacks = pool.destroyFallbacksThisFrame,
            pool_inactive_count = pool.pooledInactiveCount,
            relic_update_count = relic.updateEntryCount,
            relic_update_processed = relic.updateProcessedThisFrame,
            relic_update_budget = relic.updateBudgetThisFrame,
            relic_update_duration_ms = relic.updateDurationMs,
            severe = severe,
            probable_cause = probableCause,
            app_focused = Application.isFocused,
            app_run_in_background = Application.runInBackground,
            gc_gen0_collections_delta = gcGen0Delta,
            gc_gen1_collections_delta = gcGen1Delta,
            gc_gen2_collections_delta = gcGen2Delta,
            main_thread_counter_valid = mainCounterValid,
            render_thread_counter_valid = renderCounterValid,
            gc_counter_valid = gcCounterValid,
            draw_calls_counter_valid = drawCounterValid,
            setpass_counter_valid = setPassCounterValid
        };

        PersistRecord(record);

        if (emitWarningLog)
        {
            string mainMsText = mainCounterValid ? $"{mainThreadMs:F1}" : "n/a";
            string renderMsText = renderCounterValid ? $"{renderThreadMs:F1}" : "n/a";
            Debug.LogWarning(
                $"[HITCH] {frameMs:F1} ms (avg {movingAvgFrameMs:F1} ms). Cause: {probableCause}. " +
                $"Main={mainMsText}ms Render={renderMsText}ms GC={gcAllocKb:F0}KB, " +
                $"GCCollect={gcGen0Delta}/{gcGen1Delta}/{gcGen2Delta}, Spawn={activation.spawnedThisTick}/{activation.despawnedThisTick}, " +
                $"Queries={query.globalQueriesThisFrame}/{query.globalQueryBudget}, PoolMiss={pool.instantiatesThisFrame}. " +
                $"Log={persistentLogPath}"
            );
        }
    }

    private string BuildProbableCause(
        float frameMs,
        float mainThreadMs,
        float gcAllocKb,
        int gcGen0Delta,
        int gcGen1Delta,
        int gcGen2Delta,
        EnemyActivationController.ActivationRuntimeSnapshot activation,
        EnemySimulationManager.RuntimeSnapshot simulation,
        EnemyQueryService.RuntimeSnapshot query,
        SimplePool.RuntimeSnapshot pool,
        HordeAISystem.RuntimeSnapshot horde,
        RelicBatchedTickSystem.RuntimeSnapshot relic
    )
    {
        StringBuilder sb = new(160);

        AppendCauseIf(sb, gcAllocKb >= 768f, $"GC spike ({gcAllocKb:F0} KB)");
        AppendCauseIf(sb, gcGen2Delta > 0, $"GC gen2 collect x{gcGen2Delta}");
        AppendCauseIf(sb, gcGen1Delta > 0 && gcAllocKb >= 256f, $"GC gen1 collect x{gcGen1Delta}");
        AppendCauseIf(sb, gcGen0Delta > 0 && gcAllocKb >= 512f, $"GC gen0 collect x{gcGen0Delta}");
        AppendCauseIf(sb, pool.instantiatesThisFrame > 0, $"pool miss/instantiate x{pool.instantiatesThisFrame}");

        int spawnChurn = activation.spawnedThisTick + activation.despawnedThisTick;
        AppendCauseIf(sb, spawnChurn >= 6, $"spawn churn {activation.spawnedThisTick}/{activation.despawnedThisTick}");
        AppendCauseIf(sb, activation.scannedCandidates >= 280, $"activation sweep {activation.scannedCandidates} sim entities");

        float queryUsage = query.globalQueryBudget > 0
            ? query.globalQueriesThisFrame / (float)query.globalQueryBudget
            : 0f;
        AppendCauseIf(sb, queryUsage >= 0.9f, $"physics query budget saturation {query.globalQueriesThisFrame}/{query.globalQueryBudget}");
        AppendCauseIf(sb, query.lastCleanupFrame == Time.frameCount && query.lastCleanupRemoved >= 128, $"query cache cleanup removed {query.lastCleanupRemoved}");

        AppendCauseIf(
            sb,
            simulation.recycleDurationMs >= 5f || simulation.recycleMoved >= 40,
            $"sim recycle heavy (moved {simulation.recycleMoved}, {simulation.recycleDurationMs:F2} ms)"
        );

        AppendCauseIf(
            sb,
            horde.fixedUpdateDurationMs >= 6f && horde.agentCount >= 120,
            $"horde fixed tick load ({horde.agentCount} agents, {horde.fixedUpdateDurationMs:F2} ms)"
        );

        AppendCauseIf(
            sb,
            relic.updateDurationMs >= 4f && relic.updateProcessedThisFrame >= Mathf.Max(1, relic.updateBudgetThisFrame),
            $"relic batched tick saturated ({relic.updateProcessedThisFrame}/{relic.updateBudgetThisFrame})"
        );

        AppendCauseIf(
            sb,
            frameMs >= severeHitchThresholdMs && mainThreadMs >= 0f && mainThreadMs <= Mathf.Max(12f, frameMs * 0.02f),
            $"frame stall outside game-thread workload (main {mainThreadMs:F2} ms)"
        );

        if (sb.Length == 0)
        {
            if (frameMs >= severeHitchThresholdMs)
                return "severe main thread stall (engine/driver/OS scheduling or blocking IO)";

            return "main thread spike (reason unclear from runtime counters)";
        }

        return sb.ToString();
    }

    private static void AppendCauseIf(StringBuilder sb, bool condition, string cause)
    {
        if (!condition || string.IsNullOrWhiteSpace(cause))
            return;

        if (sb.Length > 0)
            sb.Append(" | ");

        sb.Append(cause);
    }

    private void PersistRecord(HitchEventRecord record)
    {
        if (!writeJsonLog || record == null || !LocalTelemetryFileOutput.CanWriteFiles)
            return;

        string json = JsonUtility.ToJson(record);
        AppendLineSafe(persistentLogPath, json);

        if (!string.IsNullOrWhiteSpace(projectLogPath))
            AppendLineSafe(projectLogPath, json);
    }

    private static void AppendLineSafe(string path, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        LocalTelemetryFileOutput.TryAppendText(path, line + Environment.NewLine, "HitchDiag");
    }

    private static void ReportTelemetryModeOnce(string source, bool enabled)
    {
        if (telemetryModeReported)
            return;

        telemetryModeReported = true;
        string modeLabel = BuildProfileResolver.ActiveTelemetryModeLabel;
        string state = enabled ? "enabled" : "disabled";
        Debug.Log($"[HitchDiag] TelemetryMode={modeLabel}. {source}. Local diagnostics {state}.");
    }

    private void StartRecorders()
    {
        mainThreadRecorder = TryStartRecorder(
            ProfilerCategory.Internal,
            "Main Thread",
            "Main Thread CPU Time",
            "Main Thread Time"
        );
        renderThreadRecorder = TryStartRecorder(
            ProfilerCategory.Internal,
            "Render Thread",
            "Render Thread CPU Time",
            "Render Thread Time"
        );
        gcAllocRecorder = TryStartRecorder(
            ProfilerCategory.Memory,
            "GC Allocated In Frame",
            "GC Allocated"
        );
        drawCallsRecorder = TryStartRecorder(
            ProfilerCategory.Render,
            "Draw Calls Count",
            "Draw Calls"
        );
        setPassRecorder = TryStartRecorder(
            ProfilerCategory.Render,
            "SetPass Calls Count",
            "SetPass Calls"
        );
    }

    private static ProfilerRecorder TryStartRecorder(ProfilerCategory category, params string[] statNames)
    {
        for (int i = 0; i < statNames.Length; i++)
        {
            string statName = statNames[i];
            if (string.IsNullOrWhiteSpace(statName))
                continue;

            try
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, statName, 1);
                if (recorder.Valid)
                    return recorder;

                recorder.Dispose();
            }
            catch (Exception)
            {
                // Ignore missing profiler counters and keep probing fallback names.
            }
        }

        return default;
    }

    private static void DisposeRecorder(ref ProfilerRecorder recorder)
    {
        if (recorder.Valid)
            recorder.Dispose();

        recorder = default;
    }

    private static bool HasSample(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0;
    }

    private static double ReadTimeMilliseconds(ProfilerRecorder recorder, double fallbackMs)
    {
        long raw = ReadRaw(recorder);
        if (raw <= 0L)
            return fallbackMs;

        return raw / 1_000_000d;
    }

    private static double ReadBytesAsKilobytes(ProfilerRecorder recorder, double fallbackKb)
    {
        long raw = ReadRaw(recorder);
        if (raw < 0L)
            return fallbackKb;

        return raw / 1024d;
    }

    private static double ReadCount(ProfilerRecorder recorder, double fallback)
    {
        long raw = ReadRaw(recorder);
        if (raw < 0L)
            return fallback;

        return raw;
    }

    private static long ReadRaw(ProfilerRecorder recorder)
    {
        if (!recorder.Valid)
            return -1L;

        if (recorder.Count <= 0)
            return -1L;

        return recorder.LastValue;
    }
}
