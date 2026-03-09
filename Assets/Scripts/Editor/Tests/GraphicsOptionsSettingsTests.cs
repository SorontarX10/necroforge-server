using NUnit.Framework;
using UnityEngine;

namespace GrassSim.Editor.Tests
{
    public sealed class GraphicsOptionsSettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey("opt_window_mode");
            PlayerPrefs.DeleteKey("opt_fullscreen");
            PlayerPrefs.DeleteKey("opt_window_mode_default_v2");
            PlayerPrefs.DeleteKey("opt_resolution_width");
            PlayerPrefs.DeleteKey("opt_resolution_height");
            PlayerPrefs.DeleteKey("opt_resolution_refresh_hz");
            PlayerPrefs.DeleteKey("opt_vsync");
            PlayerPrefs.DeleteKey("opt_fps_cap");
            PlayerPrefs.DeleteKey("opt_quality_preset");
            PlayerPrefs.Save();
            GameSettings.Load();
        }

        [Test]
        public void SetWindowMode_Windowed_DisablesFullscreenFlag()
        {
            GameSettings.SetWindowMode(GameSettings.DisplayWindowMode.Windowed);

            Assert.AreEqual(GameSettings.DisplayWindowMode.Windowed, GameSettings.WindowMode);
            Assert.IsFalse(GameSettings.Fullscreen);
        }

        [Test]
        public void SetFpsCap_InvalidValue_IsNormalized()
        {
            GameSettings.SetFpsCap(999);

            Assert.AreEqual(120, GameSettings.FpsCap);
        }

        [Test]
        public void SetResolution_ClampsToSafeBounds()
        {
            GameSettings.SetResolution(10, 10, 1);

            Assert.GreaterOrEqual(GameSettings.ResolutionWidth, 640);
            Assert.GreaterOrEqual(GameSettings.ResolutionHeight, 360);
            Assert.GreaterOrEqual(GameSettings.ResolutionRefreshHz, 30);
        }

        [Test]
        public void Load_UsesLegacyFullscreenPref_WhenWindowModeNotSaved()
        {
            PlayerPrefs.SetInt("opt_window_mode_default_v2", 1);
            PlayerPrefs.DeleteKey("opt_window_mode");
            PlayerPrefs.SetInt("opt_fullscreen", 0);
            PlayerPrefs.Save();

            GameSettings.Load();

            Assert.AreEqual(GameSettings.DisplayWindowMode.Windowed, GameSettings.WindowMode);
            Assert.IsFalse(GameSettings.Fullscreen);
        }

        [Test]
        public void Load_FirstRun_AppliesBorderlessFullscreenDefault()
        {
            PlayerPrefs.DeleteKey("opt_window_mode_default_v2");
            PlayerPrefs.DeleteKey("opt_window_mode");
            PlayerPrefs.SetInt("opt_fullscreen", 0);
            PlayerPrefs.Save();

            GameSettings.Load();

            Assert.AreEqual(GameSettings.DisplayWindowMode.BorderlessFullscreen, GameSettings.WindowMode);
            Assert.IsTrue(GameSettings.Fullscreen);
        }
    }
}
