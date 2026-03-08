using System;
using System.Collections;
using System.IO;
using GrassSim.Core;
using GrassSim.Telemetry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GrassSim.Tests.PlayMode
{
    public sealed class SceneLifecyclePlayModeTests
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string LoadingScenePath = "Assets/Scenes/Loading.unity";
        private const string GameSceneName = "Game";
        private const string LoadingSceneName = "Loading";
        private const float LoadTimeoutSeconds = 45f;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        [UnityTest]
        public IEnumerator T083_MainMenuToLoadingToGameToMainMenuToLoadingToGame()
        {
            EnsureRequiredSceneFilesExist();

            yield return LoadSceneSingle(MainMenuScenePath);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name, "Expected MainMenu to be active.");

            yield return LoadSceneSingle(LoadingScenePath);
            yield return WaitForGameReady("first_cycle");

            yield return LoadSceneSingle(MainMenuScenePath);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name, "Expected MainMenu to be active after return.");

            yield return LoadSceneSingle(LoadingScenePath);
            yield return WaitForGameReady("second_cycle");
        }

        [UnityTest]
        public IEnumerator T084_TenLoadingCycles_NoSingletonDuplication_NoInputAudioRegression()
        {
            EnsureRequiredSceneFilesExist();

            for (int cycle = 1; cycle <= 10; cycle++)
            {
                yield return LoadSceneSingle(LoadingScenePath);
                yield return WaitForGameReady($"cycle_{cycle}");

                AssertSingletonCountAtMostOne<RuntimePerformanceSummary>("RuntimePerformanceSummary");
                AssertSingletonCountAtMostOne<RuntimeHitchDiagnostics>("RuntimeHitchDiagnostics");
                AssertSingletonCountAtMostOne<RuntimeVisualReadabilityStabilizer>("RuntimeVisualReadabilityStabilizer");
                AssertSingletonCountAtMostOne<GameplayTelemetryRecorder>("GameplayTelemetryRecorder");
                AssertSingletonCountAtMostOne<HordeAISystem>("HordeAISystem");
                AssertSingletonCountAtMostOne<RelicBatchedTickSystem>("RelicBatchedTickSystem");
                AssertSingletonCountAtMostOne<RelicVfxTickSystem>("RelicVfxTickSystem");

                AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );
                Assert.Greater(listeners.Length, 0, $"cycle={cycle}: expected at least one AudioListener.");
                Assert.LessOrEqual(listeners.Length, 1, $"cycle={cycle}: expected max one AudioListener.");
                Assert.IsFalse(AudioListener.pause, $"cycle={cycle}: AudioListener.pause should be false.");

                Assert.NotNull(InputSystem.settings, $"cycle={cycle}: InputSystem settings should be available.");
                Assert.NotNull(PlayerLocator.GetTransform(), $"cycle={cycle}: player transform should be available.");
            }
        }

        [UnityTest]
        public IEnumerator T085_RuntimeSummarySceneContext_ReportsGameDuringGameplay()
        {
            EnsureRequiredSceneFilesExist();

            yield return LoadSceneSingle(LoadingScenePath);
            yield return WaitForGameReady("runtime_summary_scene_context");

            string resolvedScene = RuntimePerformanceSummary.ResolveSceneNameForDiagnostics();
            Assert.AreEqual(GameSceneName, resolvedScene, "RuntimePerformanceSummary should resolve scene as Game during gameplay.");
        }

        private static void EnsureRequiredSceneFilesExist()
        {
            if (!File.Exists(MainMenuScenePath))
                Assert.Ignore($"Missing scene file: {MainMenuScenePath}");

            if (!File.Exists(LoadingScenePath))
                Assert.Ignore($"Missing scene file: {LoadingScenePath}");
        }

        private static IEnumerator LoadSceneSingle(string scenePath)
        {
            Time.timeScale = 1f;
            AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            Assert.NotNull(load, $"Failed to start scene load for {scenePath}.");

            yield return WaitForCondition(
                () => load.isDone,
                LoadTimeoutSeconds,
                $"Timeout loading scene: {scenePath}"
            );
            yield return null;
        }

        private static IEnumerator WaitForGameReady(string context)
        {
            yield return WaitForCondition(
                () =>
                {
                    Scene gameScene = SceneManager.GetSceneByName(GameSceneName);
                    return gameScene.IsValid()
                           && gameScene.isLoaded
                           && string.Equals(SceneManager.GetActiveScene().name, GameSceneName, StringComparison.Ordinal)
                           && ChunkedProceduralLevelGenerator.WorldReady;
                },
                LoadTimeoutSeconds,
                $"Timeout waiting for Game readiness ({context})."
            );

            bool loadingStillLoaded = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (string.Equals(scene.name, LoadingSceneName, StringComparison.Ordinal))
                {
                    loadingStillLoaded = true;
                    break;
                }
            }

            Assert.IsFalse(loadingStillLoaded, $"Loading scene should be unloaded after Game is ready ({context}).");
        }

        private static IEnumerator WaitForCondition(Func<bool> condition, float timeoutSeconds, string timeoutMessage)
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                    yield break;

                yield return null;
            }

            Assert.Fail(timeoutMessage);
        }

        private static void AssertSingletonCountAtMostOne<T>(string label) where T : UnityEngine.Object
        {
            T[] instances = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            Assert.LessOrEqual(instances.Length, 1, $"{label} duplicated: count={instances.Length}");
        }
    }
}
