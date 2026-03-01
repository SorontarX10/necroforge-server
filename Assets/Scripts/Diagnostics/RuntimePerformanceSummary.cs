using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(10020)]
public sealed class RuntimePerformanceSummary : MonoBehaviour
{
    [Serializable]
    private sealed class PerformanceSummaryRecord
    {
        public string utc_timestamp;
        public string version;
        public string scene;
        public string trigger_reason;
        public float warmup_seconds;
        public float capture_seconds;
        public int frame_count;
        public float avg_fps;
        public float one_percent_low_fps;
        public float avg_frame_ms;
        public float p95_frame_ms;
        public float p99_frame_ms;
        public float max_frame_ms;
        public int hitches_33ms;
        public int hitches_50ms;
        public float cpu_main_thread_ms_avg;
        public float cpu_render_thread_ms_avg;
        public float gc_alloc_kb_avg;
        public float draw_calls_avg;
        public float setpass_calls_avg;
        public bool kpi_60fps_stable_pass;
        public bool kpi_one_percent_low_45_pass;
    }

    [Header("Capture")]
    [SerializeField] private bool autoCaptureOnStartup = true;
    [SerializeField] private bool requireFocus = true;
    [SerializeField, Min(0f)] private float warmupSeconds = 5f;
    [SerializeField, Min(10f)] private float captureDurationSeconds = 180f;
    [SerializeField] private Key forceFinalizeKey = Key.F10;

    [Header("Output")]
    [SerializeField] private bool writeJsonLog = true;
    [SerializeField] private bool mirrorToProjectResultsInEditor = true;
    [SerializeField] private bool emitSummaryLog = true;

    private static RuntimePerformanceSummary instance;
    private static bool shuttingDown;

    private readonly List<float> frameTimesMs = new(16384);
    private float captureStartAt = -1f;
    private float warmupEndsAt;
    private bool captureCompleted;

    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder renderThreadRecorder;
    private ProfilerRecorder gcAllocRecorder;
    private ProfilerRecorder drawCallsRecorder;
    private ProfilerRecorder setPassRecorder;

    private double mainThreadMsAccum;
    private int mainThreadSamples;
    private double renderThreadMsAccum;
    private int renderThreadSamples;
    private double gcAllocKbAccum;
    private int gcAllocSamples;
    private double drawCallsAccum;
    private int drawCallSamples;
    private double setPassAccum;
    private int setPassSamples;

    private string persistentLogPath;
    private string projectLogPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static RuntimePerformanceSummary EnsureInstance()
    {
        if (shuttingDown)
            return null;

        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<RuntimePerformanceSummary>();
        if (instance != null)
            return instance;

        GameObject go = new("RuntimePerformanceSummary");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RuntimePerformanceSummary>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        InitializePaths();
        StartRecorders();
        ResetCaptureWindow();
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

    private void Update()
    {
        if (captureCompleted)
            return;

        bool forceFinalize = IsForceFinalizeRequested();

        if (captureStartAt < 0f)
        {
            if (!autoCaptureOnStartup && !forceFinalize)
                return;

            if (!forceFinalize)
            {
                if (requireFocus && !Application.isFocused)
                    return;

                if (Time.unscaledTime < warmupEndsAt)
                    return;
            }

            StartCaptureNow();
        }

        CollectFrameSample();

        if (forceFinalize)
        {
            FinalizeCapture("manual");
            return;
        }

        if (Time.unscaledTime - captureStartAt >= Mathf.Max(10f, captureDurationSeconds))
            FinalizeCapture("duration");
    }

    private void ResetCaptureWindow()
    {
        warmupEndsAt = Time.unscaledTime + Mathf.Max(0f, warmupSeconds);
        captureStartAt = -1f;
        captureCompleted = false;
        frameTimesMs.Clear();
        mainThreadMsAccum = 0d;
        mainThreadSamples = 0;
        renderThreadMsAccum = 0d;
        renderThreadSamples = 0;
        gcAllocKbAccum = 0d;
        gcAllocSamples = 0;
        drawCallsAccum = 0d;
        drawCallSamples = 0;
        setPassAccum = 0d;
        setPassSamples = 0;
    }

    private void StartCaptureNow()
    {
        captureStartAt = Time.unscaledTime;
        frameTimesMs.Clear();
        mainThreadMsAccum = 0d;
        mainThreadSamples = 0;
        renderThreadMsAccum = 0d;
        renderThreadSamples = 0;
        gcAllocKbAccum = 0d;
        gcAllocSamples = 0;
        drawCallsAccum = 0d;
        drawCallSamples = 0;
        setPassAccum = 0d;
        setPassSamples = 0;
    }

    private void CollectFrameSample()
    {
        float frameMs = Mathf.Max(0.01f, Time.unscaledDeltaTime * 1000f);
        frameTimesMs.Add(frameMs);

        if (HasSample(mainThreadRecorder))
        {
            mainThreadMsAccum += ReadTimeMilliseconds(mainThreadRecorder, 0d);
            mainThreadSamples++;
        }

        if (HasSample(renderThreadRecorder))
        {
            renderThreadMsAccum += ReadTimeMilliseconds(renderThreadRecorder, 0d);
            renderThreadSamples++;
        }

        if (HasSample(gcAllocRecorder))
        {
            gcAllocKbAccum += ReadBytesAsKilobytes(gcAllocRecorder, 0d);
            gcAllocSamples++;
        }

        if (HasSample(drawCallsRecorder))
        {
            drawCallsAccum += ReadCount(drawCallsRecorder, 0d);
            drawCallSamples++;
        }

        if (HasSample(setPassRecorder))
        {
            setPassAccum += ReadCount(setPassRecorder, 0d);
            setPassSamples++;
        }
    }

    private void FinalizeCapture(string reason)
    {
        if (captureCompleted)
            return;

        int count = frameTimesMs.Count;
        if (count <= 0)
            return;

        float[] sorted = frameTimesMs.ToArray();
        Array.Sort(sorted);

        double sum = 0d;
        float maxFrameMs = 0f;
        int hitches33 = 0;
        int hitches50 = 0;
        for (int i = 0; i < frameTimesMs.Count; i++)
        {
            float v = frameTimesMs[i];
            sum += v;
            if (v > maxFrameMs)
                maxFrameMs = v;
            if (v >= 33.333f)
                hitches33++;
            if (v >= 50f)
                hitches50++;
        }

        float avgFrameMs = (float)(sum / Math.Max(1, count));
        float avgFps = 1000f / Mathf.Max(0.01f, avgFrameMs);

        int p95Index = Mathf.Clamp(Mathf.CeilToInt(count * 0.95f) - 1, 0, count - 1);
        int p99Index = Mathf.Clamp(Mathf.CeilToInt(count * 0.99f) - 1, 0, count - 1);
        float p95FrameMs = sorted[p95Index];
        float p99FrameMs = sorted[p99Index];
        float onePercentLowFps = 1000f / Mathf.Max(0.01f, p99FrameMs);

        var record = new PerformanceSummaryRecord
        {
            utc_timestamp = DateTime.UtcNow.ToString("O"),
            version = Application.version,
            scene = SceneManager.GetActiveScene().name,
            trigger_reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason,
            warmup_seconds = Mathf.Max(0f, warmupSeconds),
            capture_seconds = Time.unscaledTime - captureStartAt,
            frame_count = count,
            avg_fps = avgFps,
            one_percent_low_fps = onePercentLowFps,
            avg_frame_ms = avgFrameMs,
            p95_frame_ms = p95FrameMs,
            p99_frame_ms = p99FrameMs,
            max_frame_ms = maxFrameMs,
            hitches_33ms = hitches33,
            hitches_50ms = hitches50,
            cpu_main_thread_ms_avg = AverageOrFallback(mainThreadMsAccum, mainThreadSamples),
            cpu_render_thread_ms_avg = AverageOrFallback(renderThreadMsAccum, renderThreadSamples),
            gc_alloc_kb_avg = AverageOrFallback(gcAllocKbAccum, gcAllocSamples),
            draw_calls_avg = AverageOrFallback(drawCallsAccum, drawCallSamples),
            setpass_calls_avg = AverageOrFallback(setPassAccum, setPassSamples),
            kpi_60fps_stable_pass = avgFps >= 60f,
            kpi_one_percent_low_45_pass = onePercentLowFps >= 45f
        };

        PersistRecord(record);
        captureCompleted = true;

        if (emitSummaryLog)
        {
            Debug.Log(
                $"[PerfSummary] {record.scene} {record.capture_seconds:F1}s | " +
                $"avg {record.avg_fps:F1} FPS, 1% low {record.one_percent_low_fps:F1}, " +
                $"avg {record.avg_frame_ms:F2} ms, p99 {record.p99_frame_ms:F2} ms | " +
                $"KPI 60/45: {(record.kpi_60fps_stable_pass ? "PASS" : "FAIL")}/{(record.kpi_one_percent_low_45_pass ? "PASS" : "FAIL")} | " +
                $"log: {persistentLogPath}"
            );
        }
    }

    private void PersistRecord(PerformanceSummaryRecord record)
    {
        if (!writeJsonLog || record == null)
            return;

        string json = JsonUtility.ToJson(record);
        AppendLineSafe(persistentLogPath, json);

        if (!string.IsNullOrWhiteSpace(projectLogPath))
            AppendLineSafe(projectLogPath, json);
    }

    private static float AverageOrFallback(double sum, int count)
    {
        if (count <= 0)
            return -1f;

        return (float)(sum / count);
    }

    private bool IsForceFinalizeRequested()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        return forceFinalizeKey switch
        {
            Key.F1 => keyboard.f1Key.wasPressedThisFrame,
            Key.F2 => keyboard.f2Key.wasPressedThisFrame,
            Key.F3 => keyboard.f3Key.wasPressedThisFrame,
            Key.F4 => keyboard.f4Key.wasPressedThisFrame,
            Key.F5 => keyboard.f5Key.wasPressedThisFrame,
            Key.F6 => keyboard.f6Key.wasPressedThisFrame,
            Key.F7 => keyboard.f7Key.wasPressedThisFrame,
            Key.F8 => keyboard.f8Key.wasPressedThisFrame,
            Key.F9 => keyboard.f9Key.wasPressedThisFrame,
            Key.F10 => keyboard.f10Key.wasPressedThisFrame,
            Key.F11 => keyboard.f11Key.wasPressedThisFrame,
            Key.F12 => keyboard.f12Key.wasPressedThisFrame,
            _ => keyboard.f10Key.wasPressedThisFrame
        };
    }

    private void InitializePaths()
    {
        persistentLogPath = Path.Combine(Application.persistentDataPath, "perf_summary.jsonl");
        EnsureDirectoryForPath(persistentLogPath);

        if (Application.isEditor && mirrorToProjectResultsInEditor)
        {
            projectLogPath = Path.GetFullPath(Path.Combine("results", "perf", "runtime_perf_summary.jsonl"));
            EnsureDirectoryForPath(projectLogPath);
        }
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
            catch
            {
                // Ignore missing profiler counters and keep probing fallback names.
            }
        }

        return default;
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
        if (!recorder.Valid || recorder.Count <= 0)
            return -1L;

        return recorder.LastValue;
    }

    private static void DisposeRecorder(ref ProfilerRecorder recorder)
    {
        if (recorder.Valid)
            recorder.Dispose();

        recorder = default;
    }

    private static void AppendLineSafe(string path, string line)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(line))
            return;

        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PerfSummary] Failed to append log '{path}': {ex.Message}");
        }
    }

    private static void EnsureDirectoryForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
    }
}
