using GrassSim.Core;
using NUnit.Framework;

namespace GrassSim.Editor.Tests
{
    public sealed class BuildProfileCatalogTests
    {
        [Test]
        public void DemoProfile_UsesTelemetryOff_AndDisablesDevTools()
        {
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(BuildProfileType.Demo);
            Assert.IsFalse(flags.isDevelopmentToolsEnabled);
            Assert.IsFalse(flags.isLocalTelemetryEnabled);
            Assert.AreEqual(TelemetryMode.Off, flags.telemetryMode);
        }

        [Test]
        public void DevProfile_EnablesDevTools_AndLocalTelemetry()
        {
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(BuildProfileType.Dev);
            Assert.IsTrue(flags.isDevelopmentToolsEnabled);
            Assert.IsTrue(flags.isLocalTelemetryEnabled);
            Assert.AreEqual(TelemetryMode.DevLocal, flags.telemetryMode);
        }

        [Test]
        public void ResolveProfile_ReturnsUnknown_ForConflictingSymbols()
        {
            BuildSymbolSnapshot symbols = new(
                hasDemo: true,
                hasDevtools: true,
                hasInternalQa: false
            );
            BuildProfileType profile = BuildProfileRules.ResolveProfile(symbols, isEditorEnvironment: false);
            Assert.AreEqual(BuildProfileType.Unknown, profile);
        }

        [Test]
        public void Validate_Fails_WhenDemoAndDevtoolsAreEnabledTogether()
        {
            BuildSymbolSnapshot symbols = new(
                hasDemo: true,
                hasDevtools: true,
                hasInternalQa: false
            );
            BuildRuntimeFlags flags = BuildProfileCatalog.GetFlags(BuildProfileType.Demo);
            bool valid = BuildProfileRules.Validate(symbols, BuildProfileType.Demo, flags, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("BUILD_DEMO and BUILD_DEVTOOLS", error);
        }

        [Test]
        public void Validate_Fails_WhenDemoTelemetryModeIsNotOff()
        {
            BuildSymbolSnapshot symbols = new(
                hasDemo: true,
                hasDevtools: false,
                hasInternalQa: false
            );
            BuildRuntimeFlags invalidFlags = new(
                isDevelopmentToolsEnabled: false,
                isLocalTelemetryEnabled: true,
                isOnlineLeaderboardEnabled: true,
                isAnticheatStrictMode: true,
                telemetryMode: TelemetryMode.DevLocal
            );

            bool valid = BuildProfileRules.Validate(symbols, BuildProfileType.Demo, invalidFlags, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("TelemetryMode.Off", error);
        }

        [Test]
        public void ResolveProfile_ReturnsRelease_WhenNoProfileSymbolsOutsideEditor()
        {
            BuildSymbolSnapshot symbols = new(
                hasDemo: false,
                hasDevtools: false,
                hasInternalQa: false
            );
            BuildProfileType profile = BuildProfileRules.ResolveProfile(symbols, isEditorEnvironment: false);
            Assert.AreEqual(BuildProfileType.Release, profile);
        }
    }
}
