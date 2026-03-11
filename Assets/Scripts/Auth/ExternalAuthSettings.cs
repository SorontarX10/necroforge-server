using System;
using System.IO;
using UnityEngine;

namespace GrassSim.Auth
{
    public static class ExternalAuthSettings
    {
        private const string ConfigFileName = "auth_config.json";

        [Serializable]
        private sealed class AuthConfigFile
        {
            public bool enabled = true;
            public string broker_base_url = string.Empty;
            public bool google_login_enabled = true;
            public bool microsoft_login_enabled = false;
            public bool facebook_login_enabled = false;
            public string provider_start_path_template = "/auth/external/{provider}/start";
            public string flow_start_path_template = "/auth/external/{provider}/flow/start";
            public string flow_session_path_template = "/auth/external/flow/{flow_id}/session";
            public string steam_session_exchange_path = "/auth/external/steam/session";
            public string exchange_path = "/auth/external/exchange";
            public string refresh_path = "/auth/external/refresh";
            public string logout_path = "/auth/external/logout";
            public string callback_code_query_key = "code";
            public string callback_provider_query_key = "provider";
            public bool steam_auto_sign_in_enabled = false;
            public float flow_poll_interval_seconds = 0.5f;
            public float timeout_seconds = 8f;
            public float refresh_lead_seconds = 90f;
            public float clipboard_poll_interval_seconds = 0.5f;
        }

        private static bool loaded;
        private static AuthConfigFile config;

        public static bool IsEnabled => GetConfig().enabled;

        public static string BrokerBaseUrl
        {
            get
            {
                string raw = GetConfig().broker_base_url;
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                return raw.Trim().TrimEnd('/');
            }
        }

        public static string CallbackCodeQueryKey
        {
            get
            {
                string value = GetConfig().callback_code_query_key;
                return string.IsNullOrWhiteSpace(value) ? "code" : value.Trim();
            }
        }

        public static string CallbackProviderQueryKey
        {
            get
            {
                string value = GetConfig().callback_provider_query_key;
                return string.IsNullOrWhiteSpace(value) ? "provider" : value.Trim();
            }
        }

        public static float TimeoutSeconds
        {
            get
            {
                float value = GetConfig().timeout_seconds;
                if (value <= 0f)
                    value = 8f;
                return Mathf.Clamp(value, 3f, 30f);
            }
        }

        public static float RefreshLeadSeconds
        {
            get
            {
                float value = GetConfig().refresh_lead_seconds;
                if (value <= 0f)
                    value = 90f;
                return Mathf.Clamp(value, 10f, 900f);
            }
        }

        public static float FlowPollIntervalSeconds
        {
            get
            {
                float value = GetConfig().flow_poll_interval_seconds;
                if (value <= 0f)
                    value = 0.5f;
                return Mathf.Clamp(value, 0.2f, 5f);
            }
        }

        public static float ClipboardPollIntervalSeconds
        {
            get
            {
                float value = GetConfig().clipboard_poll_interval_seconds;
                if (value <= 0f)
                    value = 0.5f;
                return Mathf.Clamp(value, 0.1f, 5f);
            }
        }

        public static string BuildProviderStartUrl(string provider)
        {
            string baseUrl = BrokerBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            string normalizedProvider = NormalizeProvider(provider);
            if (string.IsNullOrWhiteSpace(normalizedProvider))
                return string.Empty;
            if (!IsProviderLoginEnabled(normalizedProvider))
                return string.Empty;

            string template = GetConfig().provider_start_path_template;
            if (string.IsNullOrWhiteSpace(template))
                template = "/auth/external/{provider}/start";

            string path = template.Replace("{provider}", Uri.EscapeDataString(normalizedProvider));
            return JoinUrl(baseUrl, path);
        }

        public static string BuildProviderFlowStartUrl(string provider)
        {
            string baseUrl = BrokerBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            string normalizedProvider = NormalizeProvider(provider);
            if (string.IsNullOrWhiteSpace(normalizedProvider))
                return string.Empty;
            if (!IsProviderLoginEnabled(normalizedProvider))
                return string.Empty;

            string template = GetConfig().flow_start_path_template;
            if (string.IsNullOrWhiteSpace(template))
                template = "/auth/external/{provider}/flow/start";

            string path = template.Replace("{provider}", Uri.EscapeDataString(normalizedProvider));
            return JoinUrl(baseUrl, path);
        }

        public static string BuildFlowSessionUrl(string flowId)
        {
            string baseUrl = BrokerBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;
            if (string.IsNullOrWhiteSpace(flowId))
                return string.Empty;

            string template = GetConfig().flow_session_path_template;
            if (string.IsNullOrWhiteSpace(template))
                template = "/auth/external/flow/{flow_id}/session";

            string path = template.Replace("{flow_id}", Uri.EscapeDataString(flowId.Trim()));
            return JoinUrl(baseUrl, path);
        }

        public static string BuildSteamSessionExchangeUrl()
        {
            return BuildAbsolutePath(GetConfig().steam_session_exchange_path);
        }

        public static bool SteamAutoSignInEnabled => GetConfig().steam_auto_sign_in_enabled;

        public static bool IsProviderLoginEnabled(string provider)
        {
            string normalized = NormalizeProvider(provider);
            return normalized switch
            {
                "google" => GetConfig().google_login_enabled,
                "microsoft" => GetConfig().microsoft_login_enabled,
                "facebook" => GetConfig().facebook_login_enabled,
                _ => false
            };
        }

        public static string BuildExchangeUrl()
        {
            return BuildAbsolutePath(GetConfig().exchange_path);
        }

        public static string BuildRefreshUrl()
        {
            return BuildAbsolutePath(GetConfig().refresh_path);
        }

        public static string BuildLogoutUrl()
        {
            return BuildAbsolutePath(GetConfig().logout_path);
        }

        private static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return string.Empty;

            string trimmed = provider.Trim().ToLowerInvariant();
            return trimmed.Length <= 32 ? trimmed : trimmed.Substring(0, 32);
        }

        private static string BuildAbsolutePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
                return string.Empty;

            string path = relativeOrAbsolutePath.Trim();
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            string baseUrl = BrokerBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            return JoinUrl(baseUrl, path);
        }

        private static string JoinUrl(string baseUrl, string path)
        {
            string left = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim().TrimEnd('/');
            string right = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
            if (string.IsNullOrWhiteSpace(left))
                return right;
            if (string.IsNullOrWhiteSpace(right))
                return left;
            if (right.StartsWith("/"))
                return left + right;
            return left + "/" + right;
        }

        private static AuthConfigFile GetConfig()
        {
            if (loaded)
                return config ?? new AuthConfigFile();

            loaded = true;
            config = new AuthConfigFile();

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
                if (!File.Exists(path))
                    return config;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return config;

                AuthConfigFile parsed = JsonUtility.FromJson<AuthConfigFile>(json);
                if (parsed != null)
                    config = parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ExternalAuth] Failed to load auth config: {ex.Message}");
            }

            return config;
        }
    }
}
