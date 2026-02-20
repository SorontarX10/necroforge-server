using System;
using System.Collections.Generic;
using GrassSim.Enhancers;
using GrassSim.Telemetry;
using UnityEngine;

namespace GrassSim.UI
{
    /// <summary>
    /// Event-driven registry for map collectibles.
    /// Avoids periodic scene scans in WorldMapController.
    /// </summary>
    public static class MapCollectibleRegistry
    {
        private static readonly HashSet<ChestRelicTrigger> chests = new();
        private static readonly HashSet<EnhancerPickup> enhancers = new();

        public static event Action RegistryChanged;

        public static void RegisterChest(ChestRelicTrigger chest)
        {
            if (chest == null)
                return;

            if (chests.Add(chest))
                RegistryChanged?.Invoke();
        }

        public static void UnregisterChest(ChestRelicTrigger chest)
        {
            if (chest == null)
                return;

            if (chests.Remove(chest))
                RegistryChanged?.Invoke();
        }

        public static void RegisterEnhancer(EnhancerPickup enhancer)
        {
            if (enhancer == null)
                return;

            if (enhancers.Add(enhancer))
                RegistryChanged?.Invoke();
        }

        public static void UnregisterEnhancer(EnhancerPickup enhancer)
        {
            if (enhancer == null)
                return;

            if (enhancers.Remove(enhancer))
                RegistryChanged?.Invoke();
        }

        public static void GetActiveChests(List<ChestRelicTrigger> result)
        {
            CopyActive(chests, result);
        }

        public static void GetActiveEnhancers(List<EnhancerPickup> result)
        {
            CopyActive(enhancers, result);
        }

        private static void CopyActive<T>(HashSet<T> source, List<T> result) where T : Component
        {
            if (result == null)
                return;

            result.Clear();
            foreach (T item in source)
            {
                if (item == null)
                    continue;

                GameObject go = item.gameObject;
                if (!go.scene.IsValid() || !go.activeInHierarchy)
                    continue;

                result.Add(item);
            }
        }
    }

    /// <summary>
    /// Serializes modal choice UIs (upgrades/relics), so only one can be shown at once.
    /// </summary>
    public static class ChoiceUiQueue
    {
        private static readonly Queue<Action> queue = new();
        private static bool isShowing;

        public static bool IsShowing => isShowing;
        public static int PendingCount => queue.Count;

        public static void Enqueue(Action showAction, string source = null)
        {
            if (showAction == null)
                return;

            if (isShowing)
            {
                queue.Enqueue(showAction);
                ReportQueueChanged("enqueued", source);
                return;
            }

            isShowing = true;
            ReportQueueChanged("shown", source);
            InvokeSafely(showAction);
        }

        public static void CompleteCurrent(string source = null)
        {
            if (queue.Count == 0)
            {
                isShowing = false;
                ReportQueueChanged("idle", source);
                return;
            }

            ReportQueueChanged("advance", source);
            InvokeSafely(queue.Dequeue());
        }

        public static void Clear()
        {
            queue.Clear();
            isShowing = false;
            ReportQueueChanged("clear", "system");
        }

        private static void InvokeSafely(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                CompleteCurrent("exception");
            }
        }

        private static void ReportQueueChanged(string action, string source)
        {
            GameplayTelemetryHub.ReportChoiceQueueChanged(
                new GameplayTelemetryHub.ChoiceQueueSample(
                    GetRunTimeSeconds(),
                    string.IsNullOrWhiteSpace(source) ? "unknown" : source,
                    string.IsNullOrWhiteSpace(action) ? "unknown" : action,
                    queue.Count,
                    isShowing
                )
            );
        }

        private static float GetRunTimeSeconds()
        {
            if (GameTimerController.Instance != null)
                return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

            return 0f;
        }
    }
}
