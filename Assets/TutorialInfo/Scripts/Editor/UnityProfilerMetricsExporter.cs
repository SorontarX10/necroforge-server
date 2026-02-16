using System;
using System.IO;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassSim.Editor
{
    public static class UnityProfilerMetricsExporter
    {
        private const int DefaultWarmupFrames = 120;
        private const int DefaultSampleFrames = 360;
        private const int DefaultTimeoutSeconds = 180;
        private const string DefaultOutputPath = "results/perf/latest_metrics.json";
        private const string PreferredSceneSuffix = "/Game.unity";

        private static readonly FrameTiming[] FrameTimingBuffer = new FrameTiming[1];

        private static bool running;
        private static bool captureStarted;
        private static bool exitOnComplete;

        private static string outputPath;
        private static int warmupFrames;
        private static int sampleFrames;
        private static int timeoutSeconds;
        private static double startedAt;
        private static int frameIndex;
        private static int sampledFrames;

        private static double mainThreadMsSum;
        private static double renderThreadMsSum;
        private static double gcKbSum;
        private static double drawCallsSum;
        private static double setPassCallsSum;

        private static ProfilerRecorder mainThreadRecorder;
        private static ProfilerRecorder renderThreadRecorder;
        private static ProfilerRecorder gcAllocRecorder;
        private static ProfilerRecorder drawCallsRecorder;
        private static ProfilerRecorder setPassRecorder;

        [Serializable]
        private class MetricsPayload
        {
            public double cpu_main_thread_ms;
            public double cpu_render_thread_ms;
            public double gc_alloc_kb_per_frame;
            public double draw_calls;
            public double setpass_calls;
            public int sampled_frames;
        }

        [MenuItem("Tools/CI/Export Profiler Metrics")]
        public static void ExportLatestMetricsMenu()
        {
            BeginExport(exitAfterCompletion: false);
        }

        public static void ExportLatestMetricsForCI()
        {
            BeginExport(exitAfterCompletion: Application.isBatchMode);
        }

        private static void BeginExport(bool exitAfterCompletion)
        {
            if (running)
                return;

            running = true;
            exitOnComplete = exitAfterCompletion;
            captureStarted = false;
            frameIndex = 0;
            sampledFrames = 0;
            mainThreadMsSum = 0d;
            renderThreadMsSum = 0d;
            gcKbSum = 0d;
            drawCallsSum = 0d;
            setPassCallsSum = 0d;

            outputPath = ResolveOutputPath();
            warmupFrames = Mathf.Max(0, ParseIntArg("-perfWarmupFrames", DefaultWarmupFrames));
            sampleFrames = Mathf.Max(1, ParseIntArg("-perfSampleFrames", DefaultSampleFrames));
            timeoutSeconds = Mathf.Max(20, ParseIntArg("-perfTimeoutSeconds", DefaultTimeoutSeconds));
            startedAt = EditorApplication.timeSinceStartup;

            string scenePath = ResolveScenePath();
            if (!TryOpenScene(scenePath))
            {
                FinishWithFailure(2, $"[ProfilerExport] Could not open scene: {scenePath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!running)
                return;

            if (state == PlayModeStateChange.EnteredPlayMode)
                StartCapture();
        }

        private static void OnEditorUpdate()
        {
            if (!running)
                return;

            if (EditorApplication.timeSinceStartup - startedAt > timeoutSeconds)
            {
                FinishWithFailure(3, "[ProfilerExport] Timed out while waiting for profiling samples.");
                return;
            }

            if (!EditorApplication.isPlaying || !captureStarted)
                return;

            frameIndex++;
            FrameTimingManager.CaptureFrameTimings();

            if (frameIndex <= warmupFrames)
                return;

            SampleFrame();
            if (sampledFrames >= sampleFrames)
                FinishWithSuccess();
        }

        private static void StartCapture()
        {
            if (captureStarted)
                return;

            captureStarted = true;
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

        private static void SampleFrame()
        {
            sampledFrames++;

            double fallbackMainMs = Mathf.Max(0.01f, Time.unscaledDeltaTime * 1000f);
            double fallbackRenderMs = fallbackMainMs;
            if (FrameTimingManager.GetLatestTimings(1, FrameTimingBuffer) > 0)
            {
                FrameTiming timing = FrameTimingBuffer[0];
                if (timing.cpuMainThreadFrameTime > 0d)
                    fallbackMainMs = timing.cpuMainThreadFrameTime;
                if (timing.cpuRenderThreadFrameTime > 0d)
                    fallbackRenderMs = timing.cpuRenderThreadFrameTime;
            }

            mainThreadMsSum += ReadTimeMilliseconds(mainThreadRecorder, fallbackMainMs);
            renderThreadMsSum += ReadTimeMilliseconds(renderThreadRecorder, fallbackRenderMs);
            gcKbSum += ReadBytesAsKilobytes(gcAllocRecorder, 0d);
            drawCallsSum += ReadCount(drawCallsRecorder, 0d);
            setPassCallsSum += ReadCount(setPassRecorder, 0d);
        }

        private static void FinishWithSuccess()
        {
            int safeSamples = Mathf.Max(1, sampledFrames);
            var payload = new MetricsPayload
            {
                cpu_main_thread_ms = Round3(mainThreadMsSum / safeSamples),
                cpu_render_thread_ms = Round3(renderThreadMsSum / safeSamples),
                gc_alloc_kb_per_frame = Round3(gcKbSum / safeSamples),
                draw_calls = Round3(drawCallsSum / safeSamples),
                setpass_calls = Round3(setPassCallsSum / safeSamples),
                sampled_frames = sampledFrames
            };

            string json = JsonUtility.ToJson(payload, prettyPrint: true);
            File.WriteAllText(outputPath, json);
            Debug.Log($"[ProfilerExport] Wrote metrics to {outputPath}");
            Complete(0);
        }

        private static void FinishWithFailure(int exitCode, string message)
        {
            Debug.LogError(message);
            Complete(exitCode);
        }

        private static void Complete(int exitCode)
        {
            running = false;
            captureStarted = false;

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            DisposeRecorder(ref mainThreadRecorder);
            DisposeRecorder(ref renderThreadRecorder);
            DisposeRecorder(ref gcAllocRecorder);
            DisposeRecorder(ref drawCallsRecorder);
            DisposeRecorder(ref setPassRecorder);

            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            if (!exitOnComplete)
                return;

            EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
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
                    // Ignore missing profiler counters and continue with fallback probes.
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

        private static double ReadTimeMilliseconds(ProfilerRecorder recorder, double fallbackMs)
        {
            long raw = ReadRaw(recorder);
            if (raw <= 0L)
                return fallbackMs;

            double nsToMs = raw / 1_000_000d;
            if (nsToMs > 0d && nsToMs < 1000d)
                return nsToMs;

            double usToMs = raw / 1_000d;
            if (usToMs > 0d && usToMs < 1000d)
                return usToMs;

            return fallbackMs;
        }

        private static double ReadBytesAsKilobytes(ProfilerRecorder recorder, double fallbackKb)
        {
            long raw = ReadRaw(recorder);
            if (raw <= 0L)
                return fallbackKb;

            return raw / 1024d;
        }

        private static double ReadCount(ProfilerRecorder recorder, double fallbackCount)
        {
            long raw = ReadRaw(recorder);
            if (raw < 0L)
                return fallbackCount;

            return raw;
        }

        private static long ReadRaw(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count <= 0)
                return 0L;

            return recorder.LastValue;
        }

        private static string ResolveOutputPath()
        {
            string configured = ParseStringArg("-perfOutput", DefaultOutputPath);
            return Path.GetFullPath(configured);
        }

        private static string ResolveScenePath()
        {
            string fromArgs = ParseStringArg("-perfScene", string.Empty);
            if (!string.IsNullOrWhiteSpace(fromArgs))
                return fromArgs;

            string firstEnabled = string.Empty;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (scene == null || !scene.enabled || string.IsNullOrWhiteSpace(scene.path))
                    continue;

                if (string.IsNullOrEmpty(firstEnabled))
                    firstEnabled = scene.path;

                if (scene.path.EndsWith(PreferredSceneSuffix, StringComparison.OrdinalIgnoreCase))
                    return scene.path;
            }

            return firstEnabled;
        }

        private static bool TryOpenScene(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
                return false;

            try
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProfilerExport] Failed to open scene '{scenePath}': {ex.Message}");
                return false;
            }
        }

        private static int ParseIntArg(string key, int fallback)
        {
            string raw = ParseStringArg(key, string.Empty);
            if (int.TryParse(raw, out int parsed))
                return parsed;

            return fallback;
        }

        private static string ParseStringArg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    continue;

                return args[i + 1];
            }

            return fallback;
        }

        private static double Round3(double value)
        {
            return Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }
    }
}
