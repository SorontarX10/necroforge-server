using System;
using System.IO;
using UnityEngine;

namespace GrassSim.Core
{
    public static class LocalTelemetryFileOutput
    {
        public static bool CanWriteFiles => BuildProfileResolver.IsLocalTelemetryEnabled;

        public static bool TryEnsureDirectoryForFile(string path, string ownerTag)
        {
            if (!CanWriteFiles || string.IsNullOrWhiteSpace(path))
                return false;

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            try
            {
                Directory.CreateDirectory(directory);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{ownerTag}] Failed to create directory for '{path}': {ex.Message}");
                return false;
            }
        }

        public static bool TryAppendText(string path, string payload, string ownerTag)
        {
            if (!CanWriteFiles || string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(payload))
                return false;

            try
            {
                File.AppendAllText(path, payload);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{ownerTag}] Failed to write '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
