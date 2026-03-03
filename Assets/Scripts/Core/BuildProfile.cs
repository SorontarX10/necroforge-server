using UnityEngine;

namespace GrassSim.Core
{
    public enum BuildProfileType
    {
        Unknown = 0,
        Dev = 1,
        InternalQA = 2,
        Demo = 3,
        Release = 4
    }

    public enum TelemetryMode
    {
        Off = 0,
        DevLocal = 1
    }

    public readonly struct BuildRuntimeFlags
    {
        public readonly bool isDevelopmentToolsEnabled;
        public readonly bool isLocalTelemetryEnabled;
        public readonly bool isOnlineLeaderboardEnabled;
        public readonly bool isAnticheatStrictMode;
        public readonly TelemetryMode telemetryMode;

        public BuildRuntimeFlags(
            bool isDevelopmentToolsEnabled,
            bool isLocalTelemetryEnabled,
            bool isOnlineLeaderboardEnabled,
            bool isAnticheatStrictMode,
            TelemetryMode telemetryMode
        )
        {
            this.isDevelopmentToolsEnabled = isDevelopmentToolsEnabled;
            this.isLocalTelemetryEnabled = isLocalTelemetryEnabled;
            this.isOnlineLeaderboardEnabled = isOnlineLeaderboardEnabled;
            this.isAnticheatStrictMode = isAnticheatStrictMode;
            this.telemetryMode = telemetryMode;
        }

        public override string ToString()
        {
            return
                $"devtools={isDevelopmentToolsEnabled}, "
                + $"localTelemetry={isLocalTelemetryEnabled}, "
                + $"leaderboard={isOnlineLeaderboardEnabled}, "
                + $"anticheatStrict={isAnticheatStrictMode}, "
                + $"telemetryMode={telemetryMode}";
        }
    }

    public readonly struct BuildSymbolSnapshot
    {
        public readonly bool hasDemo;
        public readonly bool hasDevtools;
        public readonly bool hasInternalQa;

        public BuildSymbolSnapshot(bool hasDemo, bool hasDevtools, bool hasInternalQa)
        {
            this.hasDemo = hasDemo;
            this.hasDevtools = hasDevtools;
            this.hasInternalQa = hasInternalQa;
        }

        public override string ToString()
        {
            return $"BUILD_DEMO={hasDemo}, BUILD_DEVTOOLS={hasDevtools}, BUILD_INTERNAL_QA={hasInternalQa}";
        }
    }

    public static class BuildProfileCatalog
    {
        public static BuildRuntimeFlags GetFlags(BuildProfileType profile)
        {
            return profile switch
            {
                BuildProfileType.Dev => new BuildRuntimeFlags(
                    isDevelopmentToolsEnabled: true,
                    isLocalTelemetryEnabled: true,
                    isOnlineLeaderboardEnabled: true,
                    isAnticheatStrictMode: false,
                    telemetryMode: TelemetryMode.DevLocal
                ),
                BuildProfileType.InternalQA => new BuildRuntimeFlags(
                    isDevelopmentToolsEnabled: false,
                    isLocalTelemetryEnabled: true,
                    isOnlineLeaderboardEnabled: true,
                    isAnticheatStrictMode: true,
                    telemetryMode: TelemetryMode.DevLocal
                ),
                BuildProfileType.Demo => new BuildRuntimeFlags(
                    isDevelopmentToolsEnabled: false,
                    isLocalTelemetryEnabled: false,
                    isOnlineLeaderboardEnabled: true,
                    isAnticheatStrictMode: true,
                    telemetryMode: TelemetryMode.Off
                ),
                BuildProfileType.Release => new BuildRuntimeFlags(
                    isDevelopmentToolsEnabled: false,
                    isLocalTelemetryEnabled: false,
                    isOnlineLeaderboardEnabled: true,
                    isAnticheatStrictMode: true,
                    telemetryMode: TelemetryMode.Off
                ),
                _ => new BuildRuntimeFlags(
                    isDevelopmentToolsEnabled: false,
                    isLocalTelemetryEnabled: false,
                    isOnlineLeaderboardEnabled: false,
                    isAnticheatStrictMode: true,
                    telemetryMode: TelemetryMode.Off
                )
            };
        }
    }

    public static class BuildProfileRules
    {
        public static BuildProfileType ResolveProfile(BuildSymbolSnapshot symbols, bool isEditorEnvironment)
        {
            int enabledProfileSymbols = 0;
            if (symbols.hasDemo)
                enabledProfileSymbols++;
            if (symbols.hasDevtools)
                enabledProfileSymbols++;
            if (symbols.hasInternalQa)
                enabledProfileSymbols++;

            if (enabledProfileSymbols > 1)
                return BuildProfileType.Unknown;

            if (symbols.hasDemo)
                return BuildProfileType.Demo;
            if (symbols.hasInternalQa)
                return BuildProfileType.InternalQA;
            if (symbols.hasDevtools)
                return BuildProfileType.Dev;

            return isEditorEnvironment ? BuildProfileType.Dev : BuildProfileType.Release;
        }

        public static bool Validate(
            BuildSymbolSnapshot symbols,
            BuildProfileType profile,
            BuildRuntimeFlags flags,
            out string error
        )
        {
            if (symbols.hasDemo && symbols.hasDevtools)
            {
                error = "BUILD_DEMO and BUILD_DEVTOOLS cannot be enabled together.";
                return false;
            }

            if (symbols.hasDemo && flags.telemetryMode != TelemetryMode.Off)
            {
                error = $"Demo profile must use TelemetryMode.Off, got {flags.telemetryMode}.";
                return false;
            }

            if (profile == BuildProfileType.Unknown)
            {
                error = "Conflicting build profile symbols detected.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    public static class BuildProfileResolver
    {
        private static bool reportedValidationWarning;
#if UNITY_EDITOR
        private static bool useEditorOverride;
        private static BuildProfileType editorOverrideProfile = BuildProfileType.Unknown;
        private static BuildRuntimeFlags editorOverrideFlags;
#endif

        public static BuildSymbolSnapshot ActiveSymbols => CreateCompileTimeSnapshot();

        public static BuildProfileType ActiveProfile
        {
            get
            {
#if UNITY_EDITOR
                if (useEditorOverride)
                    return editorOverrideProfile;
#endif
                return ResolveProfileAndWarnIfInvalid();
            }
        }

        public static BuildRuntimeFlags ActiveFlags
        {
            get
            {
#if UNITY_EDITOR
                if (useEditorOverride)
                    return editorOverrideFlags;
#endif
                return BuildProfileCatalog.GetFlags(ActiveProfile);
            }
        }

        public static TelemetryMode ActiveTelemetryMode => ActiveFlags.telemetryMode;

        public static bool IsDevelopmentToolsEnabled => ActiveFlags.isDevelopmentToolsEnabled;

        public static bool IsLocalTelemetryEnabled => ActiveFlags.isLocalTelemetryEnabled;

        public static bool IsOnlineLeaderboardEnabled => ActiveFlags.isOnlineLeaderboardEnabled;

        public static bool IsAnticheatStrictMode => ActiveFlags.isAnticheatStrictMode;

        public static string GetSummaryLine()
        {
            BuildProfileType profile = ActiveProfile;
            BuildRuntimeFlags flags = ActiveFlags;
            return $"profile={profile}, symbols=({ActiveSymbols}), flags=({flags})";
        }

#if UNITY_EDITOR
        public static bool IsEditorOverrideActive => useEditorOverride;

        public static void SetEditorOverride(BuildProfileType profile)
        {
            useEditorOverride = true;
            editorOverrideProfile = profile;
            editorOverrideFlags = BuildProfileCatalog.GetFlags(profile);
            reportedValidationWarning = false;
        }

        public static void ClearEditorOverride()
        {
            useEditorOverride = false;
            editorOverrideProfile = BuildProfileType.Unknown;
            editorOverrideFlags = default;
            reportedValidationWarning = false;
        }
#endif

        private static BuildProfileType ResolveProfileAndWarnIfInvalid()
        {
            BuildSymbolSnapshot symbols = ActiveSymbols;
            BuildProfileType profile = BuildProfileRules.ResolveProfile(symbols, Application.isEditor);
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(profile);

            if (!reportedValidationWarning && !BuildProfileRules.Validate(symbols, profile, flags, out string error))
            {
                reportedValidationWarning = true;
                Debug.LogWarning($"[BuildProfile] Invalid configuration: {error} symbols=({symbols}) profile={profile}");
            }

            return profile;
        }

        private static BuildSymbolSnapshot CreateCompileTimeSnapshot()
        {
#if BUILD_DEMO
            bool hasDemo = true;
#else
            bool hasDemo = false;
#endif

#if BUILD_DEVTOOLS
            bool hasDevtools = true;
#else
            bool hasDevtools = false;
#endif

#if BUILD_INTERNAL_QA
            bool hasInternalQa = true;
#else
            bool hasInternalQa = false;
#endif

            return new BuildSymbolSnapshot(hasDemo, hasDevtools, hasInternalQa);
        }
    }

    public static class BuildProfileStartupLogger
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LogRuntimeProfile()
        {
            Debug.Log($"[BuildProfile] {BuildProfileResolver.GetSummaryLine()}");
        }
    }
}
