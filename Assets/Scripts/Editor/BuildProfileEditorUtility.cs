using System;
using System.Collections.Generic;
using System.Linq;
using GrassSim.Core;
using UnityEditor;

namespace GrassSim.Editor
{
    public static class BuildProfileEditorUtility
    {
        public const string DemoSymbol = "BUILD_DEMO";
        public const string DevtoolsSymbol = "BUILD_DEVTOOLS";
        public const string InternalQaSymbol = "BUILD_INTERNAL_QA";

        private static readonly string[] ProfileSymbols = { DemoSymbol, DevtoolsSymbol, InternalQaSymbol };

        public static HashSet<string> GetStandaloneDefines()
        {
            string raw = GetStandaloneDefinesRaw();
            HashSet<string> defines = new(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(raw))
                return defines;

            string[] split = raw.Split(';');
            for (int i = 0; i < split.Length; i++)
            {
                string symbol = split[i]?.Trim();
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                defines.Add(symbol);
            }

            return defines;
        }

        public static void ConfigureStandaloneSymbols(BuildProfileType profile)
        {
            HashSet<string> defines = GetStandaloneDefines();
            for (int i = 0; i < ProfileSymbols.Length; i++)
                defines.Remove(ProfileSymbols[i]);

            switch (profile)
            {
                case BuildProfileType.Demo:
                    defines.Add(DemoSymbol);
                    break;
                case BuildProfileType.InternalQA:
                    defines.Add(InternalQaSymbol);
                    break;
                case BuildProfileType.Dev:
                    defines.Add(DevtoolsSymbol);
                    break;
                case BuildProfileType.Release:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported build profile.");
            }

            SetStandaloneDefines(defines);
        }

        public static BuildSymbolSnapshot GetStandaloneSymbolSnapshotFromPlayerSettings()
        {
            HashSet<string> defines = GetStandaloneDefines();
            return new BuildSymbolSnapshot(
                hasDemo: defines.Contains(DemoSymbol),
                hasDevtools: defines.Contains(DevtoolsSymbol),
                hasInternalQa: defines.Contains(InternalQaSymbol)
            );
        }

        private static void SetStandaloneDefines(HashSet<string> defines)
        {
            List<string> ordered = defines
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            string raw = string.Join(";", ordered);

#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone, raw);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, raw);
#endif
            AssetDatabase.SaveAssets();
        }

        private static string GetStandaloneDefinesRaw()
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone);
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
#endif
        }
    }
}
