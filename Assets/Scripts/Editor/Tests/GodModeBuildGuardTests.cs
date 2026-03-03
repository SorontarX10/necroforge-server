using GrassSim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GrassSim.Editor.Tests
{
    public sealed class GodModeBuildGuardTests
    {
        [SetUp]
        public void SetUp()
        {
            BuildProfileResolver.ClearEditorOverride();
            PlayerPrefs.DeleteKey("opt_godmode");
            PlayerPrefs.Save();
            GameSettings.Load();
        }

        [TearDown]
        public void TearDown()
        {
            BuildProfileResolver.ClearEditorOverride();
            PlayerPrefs.DeleteKey("opt_godmode");
            PlayerPrefs.Save();
            GameSettings.Load();
        }

        [Test]
        public void SetGodMode_DoesNotEnableInDemoProfile()
        {
            BuildProfileResolver.SetEditorOverride(BuildProfileType.Demo);

            GameSettings.SetGodMode(true);

            Assert.IsFalse(GameSettings.GodMode);
        }

        [Test]
        public void Load_IgnoresPersistedGodModeInDemoProfile()
        {
            PlayerPrefs.SetInt("opt_godmode", 1);
            PlayerPrefs.Save();
            BuildProfileResolver.SetEditorOverride(BuildProfileType.Demo);

            GameSettings.Load();

            Assert.IsFalse(GameSettings.GodMode);
        }

        [Test]
        public void OptionsMenu_DoesNotCreateGodModeToggleInDemoProfile()
        {
            BuildProfileResolver.SetEditorOverride(BuildProfileType.Demo);

            GameObject root = new("OptionsMenuRoot");
            root.SetActive(false);

            try
            {
                OptionsMenuController controller = root.AddComponent<OptionsMenuController>();
                controller.masterVolume = CreateSlider("MasterVolume", root.transform);
                controller.musicVolume = CreateSlider("MusicVolume", root.transform);
                controller.sfxVolume = CreateSlider("SfxVolume", root.transform);
                controller.mouseSensitivity = CreateSlider("MouseSensitivity", root.transform);

                GameObject togglesRoot = new("TogglesRoot");
                togglesRoot.transform.SetParent(root.transform, false);
                controller.fullscreenToggle = CreateToggle("Fullscreen", togglesRoot.transform);
                controller.godModeToggle = null;

                root.SetActive(true);

                Assert.IsNull(togglesRoot.transform.Find("GodMode"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Slider CreateSlider(string name, Transform parent)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<Slider>();
        }

        private static Toggle CreateToggle(string name, Transform parent)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<Toggle>();
        }
    }
}
