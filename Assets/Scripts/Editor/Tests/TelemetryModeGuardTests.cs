using System;
using System.IO;
using GrassSim.Core;
using NUnit.Framework;

namespace GrassSim.Editor.Tests
{
    public sealed class TelemetryModeGuardTests
    {
        [SetUp]
        public void SetUp()
        {
            BuildProfileResolver.ClearEditorOverride();
        }

        [TearDown]
        public void TearDown()
        {
            BuildProfileResolver.ClearEditorOverride();
        }

        [TestCase(BuildProfileType.Dev, true, "DEV_LOCAL")]
        [TestCase(BuildProfileType.InternalQA, true, "DEV_LOCAL")]
        [TestCase(BuildProfileType.Demo, false, "OFF")]
        [TestCase(BuildProfileType.Release, false, "OFF")]
        public void TelemetryModeAndFlags_MatchProfile(
            BuildProfileType profile,
            bool expectedLocalTelemetryEnabled,
            string expectedModeLabel
        )
        {
            BuildProfileResolver.SetEditorOverride(profile);

            Assert.AreEqual(expectedLocalTelemetryEnabled, BuildProfileResolver.IsLocalTelemetryEnabled);
            Assert.AreEqual(expectedModeLabel, BuildProfileResolver.ActiveTelemetryModeLabel);
        }

        [Test]
        public void LocalTelemetryFileOutput_DoesNotWriteInDemoProfile()
        {
            BuildProfileResolver.SetEditorOverride(BuildProfileType.Demo);
            string path = BuildTempFilePath();

            try
            {
                bool created = LocalTelemetryFileOutput.TryEnsureDirectoryForFile(path, "TelemetryTests");
                bool appended = LocalTelemetryFileOutput.TryAppendText(path, "line-1\n", "TelemetryTests");

                Assert.IsFalse(created);
                Assert.IsFalse(appended);
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                CleanupTempFile(path);
            }
        }

        [Test]
        public void LocalTelemetryFileOutput_WritesInDevProfile()
        {
            BuildProfileResolver.SetEditorOverride(BuildProfileType.Dev);
            string path = BuildTempFilePath();

            try
            {
                bool created = LocalTelemetryFileOutput.TryEnsureDirectoryForFile(path, "TelemetryTests");
                bool appended = LocalTelemetryFileOutput.TryAppendText(path, "line-1\n", "TelemetryTests");

                Assert.IsTrue(created);
                Assert.IsTrue(appended);
                Assert.IsTrue(File.Exists(path));
                StringAssert.Contains("line-1", File.ReadAllText(path));
            }
            finally
            {
                CleanupTempFile(path);
            }
        }

        private static string BuildTempFilePath()
        {
            string directory = Path.Combine(Path.GetTempPath(), "necroforge_telemetry_tests", Guid.NewGuid().ToString("N"));
            return Path.Combine(directory, "telemetry_probe.jsonl");
        }

        private static void CleanupTempFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Cleanup best-effort for temp test artifacts.
            }
        }
    }
}
