using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GrassSim.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GrassSim.Editor
{
    public static class BuildProfileBuildAutomation
    {
        private const string BuildOutputArg = "-buildOutput";
        private const string BuildWithSmokeArg = "-buildWithSmoke";
        private const string SmokeTimeoutArg = "-smokeTimeoutSeconds";
        private const int DefaultSmokeTimeoutSeconds = 90;
        private const string DefaultBuildRoot = "Builds";

        [MenuItem("Tools/Build Profiles/Build Demo Win64")]
        public static void BuildDemoWindows64Menu()
        {
            BuildForProfile(
                profile: BuildProfileType.Demo,
                target: BuildTarget.StandaloneWindows64,
                explicitOutputPath: null,
                withSmoke: true,
                smokeTimeoutSeconds: DefaultSmokeTimeoutSeconds
            );
        }

        [MenuItem("Tools/Build Profiles/Build Dev Win64")]
        public static void BuildDevWindows64Menu()
        {
            BuildForProfile(
                profile: BuildProfileType.Dev,
                target: BuildTarget.StandaloneWindows64,
                explicitOutputPath: null,
                withSmoke: true,
                smokeTimeoutSeconds: DefaultSmokeTimeoutSeconds
            );
        }

        public static void BuildDemoWindows64()
        {
            BuildForProfileFromCommandLine(BuildProfileType.Demo);
        }

        public static void BuildDevWindows64()
        {
            BuildForProfileFromCommandLine(BuildProfileType.Dev);
        }

        private static void BuildForProfileFromCommandLine(BuildProfileType profile)
        {
            bool withSmoke = ParseBoolArg(BuildWithSmokeArg, fallback: true);
            int smokeTimeoutSeconds = ParseIntArg(SmokeTimeoutArg, fallback: DefaultSmokeTimeoutSeconds);
            string explicitOutputPath = ParseStringArg(BuildOutputArg, string.Empty);
            BuildForProfile(
                profile: profile,
                target: BuildTarget.StandaloneWindows64,
                explicitOutputPath: string.IsNullOrWhiteSpace(explicitOutputPath) ? null : explicitOutputPath,
                withSmoke: withSmoke,
                smokeTimeoutSeconds: Mathf.Max(15, smokeTimeoutSeconds)
            );
        }

        private static void BuildForProfile(
            BuildProfileType profile,
            BuildTarget target,
            string explicitOutputPath,
            bool withSmoke,
            int smokeTimeoutSeconds
        )
        {
            BuildProfileEditorUtility.ConfigureStandaloneSymbols(profile);
            BuildSymbolSnapshot symbols = BuildProfileEditorUtility.GetStandaloneSymbolSnapshotFromPlayerSettings();
            BuildProfileType resolvedProfile = BuildProfileRules.ResolveProfile(symbols, isEditorEnvironment: false);
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(resolvedProfile);

            if (!BuildProfileRules.Validate(symbols, resolvedProfile, flags, out string error))
                throw new BuildFailedException($"[BuildProfile] Invalid symbol/profile setup before build: {error}");

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s != null && s.enabled && !string.IsNullOrWhiteSpace(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new BuildFailedException("[BuildProfile] No enabled build scenes found.");

            string outputPath = ResolveOutputPath(profile, target, explicitOutputPath);
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new BuildFailedException($"[BuildProfile] Invalid output path: {outputPath}");

            Directory.CreateDirectory(outputDirectory);

            BuildOptions buildOptions = BuildOptions.StrictMode;
            if (profile == BuildProfileType.Dev || profile == BuildProfileType.InternalQA)
                buildOptions |= BuildOptions.Development;

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                options = buildOptions,
                locationPathName = outputPath
            };

            Debug.Log($"[BuildProfile] Starting build. profile={resolvedProfile}, output={outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"[BuildProfile] Build failed. profile={resolvedProfile}, result={report.summary.result}"
                );

            WriteProfileArtifact(outputDirectory, resolvedProfile, symbols, flags, outputPath, report.summary.totalSize);

            if (!withSmoke)
            {
                Debug.Log("[BuildProfile] Build completed (smoke skipped).");
                return;
            }

            RunSmokeTest(outputPath, outputDirectory, smokeTimeoutSeconds);
            Debug.Log("[BuildProfile] Build completed with smoke test.");
        }

        private static string ResolveOutputPath(BuildProfileType profile, BuildTarget target, string explicitOutputPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitOutputPath))
                return Path.GetFullPath(explicitOutputPath);

            string extension = target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64
                ? ".exe"
                : string.Empty;
            string folder = profile switch
            {
                BuildProfileType.Demo => "Demo",
                BuildProfileType.Dev => "Dev",
                BuildProfileType.InternalQA => "InternalQA",
                BuildProfileType.Release => "Release",
                _ => "Unknown"
            };
            string fileName = $"Necroforge_{folder}{extension}";
            return Path.GetFullPath(Path.Combine(DefaultBuildRoot, folder, fileName));
        }

        private static void WriteProfileArtifact(
            string outputDirectory,
            BuildProfileType profile,
            BuildSymbolSnapshot symbols,
            BuildRuntimeFlags flags,
            string outputPath,
            ulong totalSizeBytes
        )
        {
            StringBuilder sb = new();
            sb.AppendLine($"timestamp_utc={DateTime.UtcNow:O}");
            sb.AppendLine($"profile={profile}");
            sb.AppendLine($"symbols={symbols}");
            sb.AppendLine($"flags={flags}");
            sb.AppendLine($"output_path={outputPath}");
            sb.AppendLine($"build_size_bytes={totalSizeBytes}");
            string artifactPath = Path.Combine(outputDirectory, "build_profile_info.txt");
            File.WriteAllText(artifactPath, sb.ToString());
            Debug.Log($"[BuildProfile] Wrote profile artifact: {artifactPath}");
        }

        private static void RunSmokeTest(string executablePath, string outputDirectory, int smokeTimeoutSeconds)
        {
            if (!File.Exists(executablePath))
                throw new BuildFailedException($"[BuildProfile] Smoke test failed: missing executable '{executablePath}'.");

            string smokeLogPath = Path.Combine(outputDirectory, "smoke_runtime.log");
            string smokeArtifactPath = Path.Combine(outputDirectory, "build_profile_smoke.txt");

            if (File.Exists(smokeLogPath))
                File.Delete(smokeLogPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"-batchmode -nographics -quit -logFile \"{smokeLogPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
            };

            using Process process = Process.Start(startInfo);
            if (process == null)
                throw new BuildFailedException("[BuildProfile] Smoke test failed: process could not start.");

            DateTime deadline = DateTime.UtcNow.AddSeconds(Mathf.Max(15, smokeTimeoutSeconds));
            bool reachedTimeout = false;
            string marker = null;

            while (DateTime.UtcNow < deadline)
            {
                marker = ReadSmokeMarker(smokeLogPath) ?? marker;
                if (process.HasExited)
                    break;

                // The startup marker is enough for smoke pass.
                // Some environments keep the process alive despite -quit.
                if (!string.IsNullOrWhiteSpace(marker))
                {
                    TryKillProcess(process);
                    break;
                }

                Thread.Sleep(500);
            }

            if (!process.HasExited)
            {
                reachedTimeout = true;
                marker = ReadSmokeMarker(smokeLogPath) ?? marker;
                if (string.IsNullOrWhiteSpace(marker))
                {
                    TryKillProcess(process);
                    throw new BuildFailedException($"[BuildProfile] Smoke test timeout after {smokeTimeoutSeconds}s.");
                }

                TryKillProcess(process);
            }

            if (process.HasExited && process.ExitCode != 0 && string.IsNullOrWhiteSpace(marker))
                throw new BuildFailedException($"[BuildProfile] Smoke test exited with code {process.ExitCode}.");

            if (!File.Exists(smokeLogPath))
                throw new BuildFailedException("[BuildProfile] Smoke test did not create runtime log.");

            marker ??= ReadSmokeMarker(smokeLogPath);
            if (string.IsNullOrWhiteSpace(marker))
                throw new BuildFailedException("[BuildProfile] Smoke test log missing '[BuildProfile]' marker.");

            StringBuilder sb = new();
            sb.AppendLine($"timestamp_utc={DateTime.UtcNow:O}");
            sb.AppendLine($"smoke_timeout_seconds={smokeTimeoutSeconds}");
            sb.AppendLine($"log_path={smokeLogPath}");
            sb.AppendLine($"process_exit_code={(process.HasExited ? process.ExitCode : -999)}");
            sb.AppendLine($"process_timeout={reachedTimeout}");
            sb.AppendLine($"marker={marker}");
            File.WriteAllText(smokeArtifactPath, sb.ToString());
            Debug.Log($"[BuildProfile] Smoke artifact: {smokeArtifactPath}");
        }

        private static string ReadSmokeMarker(string smokeLogPath)
        {
            if (!File.Exists(smokeLogPath))
                return null;

            try
            {
                using FileStream stream = new(smokeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("[BuildProfile]"))
                        return line;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // Ignore process kill errors in smoke automation cleanup.
            }
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

        private static int ParseIntArg(string key, int fallback)
        {
            string raw = ParseStringArg(key, string.Empty);
            if (int.TryParse(raw, out int parsed))
                return parsed;

            return fallback;
        }

        private static bool ParseBoolArg(string key, bool fallback)
        {
            string raw = ParseStringArg(key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            if (bool.TryParse(raw, out bool parsed))
                return parsed;

            if (int.TryParse(raw, out int intValue))
                return intValue != 0;

            return fallback;
        }
    }
}
