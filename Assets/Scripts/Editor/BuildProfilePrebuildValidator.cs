using GrassSim.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GrassSim.Editor
{
    public sealed class BuildProfilePrebuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -2000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!IsStandaloneBuild(report.summary.platform))
                return;

            BuildSymbolSnapshot symbols = BuildProfileEditorUtility.GetStandaloneSymbolSnapshotFromPlayerSettings();
            BuildProfileType profile = BuildProfileRules.ResolveProfile(symbols, isEditorEnvironment: false);
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(profile);

            if (!BuildProfileRules.Validate(symbols, profile, flags, out string error))
                throw new BuildFailedException($"[BuildProfile] Prebuild validation failed: {error} symbols=({symbols})");

            Debug.Log($"[BuildProfile] Prebuild validation passed. profile={profile}, symbols=({symbols}), flags=({flags})");
        }

        private static bool IsStandaloneBuild(BuildTarget target)
        {
            return target == BuildTarget.StandaloneWindows
                || target == BuildTarget.StandaloneWindows64
                || target == BuildTarget.StandaloneLinux64
                || target == BuildTarget.StandaloneOSX;
        }
    }
}
