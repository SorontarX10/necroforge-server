using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GrassSim.Auth
{
    public enum ExternalAuthState
    {
        Disabled = 0,
        SignedOut = 1,
        AwaitingCallback = 2,
        SigningIn = 3,
        SignedIn = 4,
        Refreshing = 5,
        Expired = 6,
        Error = 7
    }

    public static class ExternalAuthService
    {
        [Serializable]
        private sealed class ExchangeCodeRequestDto
        {
            public string provider;
            public string code;
        }

        [Serializable]
        private sealed class RefreshSessionRequestDto
        {
            public string account_id;
            public string provider;
            public string provider_user_id;
            public string refresh_token;
        }

        [Serializable]
        private sealed class LogoutRequestDto
        {
            public string account_id;
            public string provider;
            public string provider_user_id;
            public string access_token;
        }

        [Serializable]
        private sealed class SessionEnvelopeDto
        {
            public ExternalAuthSession session;
            public string error;
            public string message;
        }

        private static bool initialized;
        private static ExternalAuthUpdater updater;
        private static ExternalAuthSession currentSession;
        private static ExternalAuthState state = ExternalAuthState.Disabled;
        private static string statusMessage = "External auth disabled.";
        private static string pendingProvider = string.Empty;
        private static string lastClipboardValue = string.Empty;
        private static float nextClipboardPollAt;
        private static float nextRefreshCheckAt;
        private static bool requestInFlight;

        public static event Action StateChanged;

        public static ExternalAuthState State => state;
        public static string StatusMessage => statusMessage;
        public static ExternalAuthSession CurrentSession => currentSession;
        public static bool IsSignedIn => currentSession != null && currentSession.HasIdentity;
        public static string CurrentProvider => currentSession?.provider ?? string.Empty;
        public static string CurrentDisplayName => currentSession?.display_name ?? string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            EnsureUpdater();

            if (!ExternalAuthSettings.IsEnabled)
            {
                SetState(ExternalAuthState.Disabled, "External auth disabled in config.");
                return;
            }

            RestoreSessionFromStore();
        }

        public static void SignInWithProvider(string provider)
        {
            Initialize();
            if (!ExternalAuthSettings.IsEnabled)
            {
                SetState(ExternalAuthState.Disabled, "External auth disabled in config.");
                return;
            }

            if (requestInFlight)
                return;

            string normalizedProvider = NormalizeProvider(provider);
            if (string.IsNullOrWhiteSpace(normalizedProvider))
            {
                SetState(ExternalAuthState.Error, "Invalid auth provider.");
                return;
            }

            string startUrl = ExternalAuthSettings.BuildProviderStartUrl(normalizedProvider);
            if (string.IsNullOrWhiteSpace(startUrl))
            {
                SetState(ExternalAuthState.Error, "Auth broker start URL is not configured.");
                return;
            }

            pendingProvider = normalizedProvider;
            lastClipboardValue = GUIUtility.systemCopyBuffer ?? string.Empty;
            nextClipboardPollAt = Time.unscaledTime + 0.2f;

            SetState(
                ExternalAuthState.AwaitingCallback,
                $"Login in browser ({normalizedProvider}), then copy callback URL to clipboard."
            );
            Application.OpenURL(startUrl);
        }

        public static void SignOut()
        {
            Initialize();
            pendingProvider = string.Empty;
            if (requestInFlight)
                return;

            if (currentSession == null)
            {
                ExternalAuthSessionStore.Clear();
                SetState(ExternalAuthState.SignedOut, "Not signed in.");
                return;
            }

            string logoutUrl = ExternalAuthSettings.BuildLogoutUrl();
            if (string.IsNullOrWhiteSpace(logoutUrl))
            {
                ClearSession("Signed out.");
                return;
            }

            updater.StartCoroutine(LogoutRoutine(logoutUrl, currentSession));
        }

        public static void TryRefreshSession(bool force)
        {
            Initialize();
            if (requestInFlight || currentSession == null)
                return;

            if (!CanRefresh(currentSession))
            {
                if (force)
                    HandleExpiredSession("Session expired. Sign in again.");
                return;
            }

            updater.StartCoroutine(RefreshRoutine(force));
        }

        public static bool TryExtractAuthorizationCode(
            string source,
            string expectedProvider,
            out string resolvedProvider,
            out string code
        )
        {
            resolvedProvider = NormalizeProvider(expectedProvider);
            code = string.Empty;

            if (string.IsNullOrWhiteSpace(source))
                return false;

            Dictionary<string, string> query = ParseQueryValues(source);
            if (query.Count == 0)
                return false;

            string codeKey = ExternalAuthSettings.CallbackCodeQueryKey.ToLowerInvariant();
            if (!query.TryGetValue(codeKey, out string parsedCode) || string.IsNullOrWhiteSpace(parsedCode))
                return false;

            string providerKey = ExternalAuthSettings.CallbackProviderQueryKey.ToLowerInvariant();
            if (query.TryGetValue(providerKey, out string parsedProvider) && !string.IsNullOrWhiteSpace(parsedProvider))
                resolvedProvider = NormalizeProvider(parsedProvider);

            if (string.IsNullOrWhiteSpace(resolvedProvider))
                return false;

            string normalizedExpected = NormalizeProvider(expectedProvider);
            if (!string.IsNullOrWhiteSpace(normalizedExpected)
                && !string.Equals(resolvedProvider, normalizedExpected, StringComparison.Ordinal))
            {
                return false;
            }

            code = parsedCode.Trim();
            return !string.IsNullOrWhiteSpace(code);
        }

        internal static void Tick()
        {
            if (!initialized || !ExternalAuthSettings.IsEnabled)
                return;

            float now = Time.unscaledTime;

            if (state == ExternalAuthState.AwaitingCallback && !requestInFlight && now >= nextClipboardPollAt)
            {
                nextClipboardPollAt = now + ExternalAuthSettings.ClipboardPollIntervalSeconds;
                string clipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
                if (!string.Equals(clipboard, lastClipboardValue, StringComparison.Ordinal))
                {
                    lastClipboardValue = clipboard;
                    if (TryExtractAuthorizationCode(
                        clipboard,
                        pendingProvider,
                        out string resolvedProvider,
                        out string code))
                    {
                        pendingProvider = resolvedProvider;
                        updater.StartCoroutine(ExchangeCodeRoutine(resolvedProvider, code));
                    }
                }
            }

            if (state == ExternalAuthState.SignedIn
                && !requestInFlight
                && currentSession != null
                && now >= nextRefreshCheckAt)
            {
                nextRefreshCheckAt = now + 5f;
                if (ShouldRefreshSoon(currentSession))
                    TryRefreshSession(force: false);
            }
        }

        private static void RestoreSessionFromStore()
        {
            if (!ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession restored))
            {
                currentSession = null;
                SetState(ExternalAuthState.SignedOut, "Not signed in.");
                return;
            }

            currentSession = NormalizeSession(restored);
            if (currentSession == null)
            {
                ExternalAuthSessionStore.Clear();
                SetState(ExternalAuthState.SignedOut, "Not signed in.");
                return;
            }

            if (currentSession.IsExpiredUtc)
            {
                if (CanRefresh(currentSession))
                    TryRefreshSession(force: true);
                else
                    HandleExpiredSession("Session expired. Sign in again.");
                return;
            }

            SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(currentSession));
            nextRefreshCheckAt = Time.unscaledTime + 2f;
        }

        private static bool ShouldRefreshSoon(ExternalAuthSession session)
        {
            if (session == null || session.expires_at_unix <= 0)
                return false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long secondsToExpiry = session.expires_at_unix - now;
            return secondsToExpiry <= (long)ExternalAuthSettings.RefreshLeadSeconds;
        }

        private static bool CanRefresh(ExternalAuthSession session)
        {
            if (session == null)
                return false;
            if (string.IsNullOrWhiteSpace(session.refresh_token))
                return false;

            string refreshUrl = ExternalAuthSettings.BuildRefreshUrl();
            return !string.IsNullOrWhiteSpace(refreshUrl);
        }

        private static IEnumerator ExchangeCodeRoutine(string provider, string code)
        {
            requestInFlight = true;
            SetState(ExternalAuthState.SigningIn, $"Signing in with {provider}...");

            string url = ExternalAuthSettings.BuildExchangeUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                requestInFlight = false;
                SetState(ExternalAuthState.Error, "Auth exchange URL is not configured.");
                yield break;
            }

            ExchangeCodeRequestDto payload = new()
            {
                provider = provider,
                code = code
            };

            string responseBody = string.Empty;
            string requestError = string.Empty;
            yield return SendPostJson(
                url,
                JsonUtility.ToJson(payload),
                onSuccess: body => responseBody = body,
                onError: error => requestError = error
            );

            requestInFlight = false;
            if (!string.IsNullOrWhiteSpace(requestError))
            {
                SetState(ExternalAuthState.Error, $"Auth exchange failed: {requestError}");
                yield break;
            }

            if (!TryParseSessionResponse(responseBody, out ExternalAuthSession session, out string parseError))
            {
                SetState(ExternalAuthState.Error, $"Auth exchange invalid response: {parseError}");
                yield break;
            }

            currentSession = session;
            ExternalAuthSessionStore.Save(session);
            pendingProvider = string.Empty;
            SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(session));
            nextRefreshCheckAt = Time.unscaledTime + 5f;
        }

        private static IEnumerator RefreshRoutine(bool force)
        {
            if (currentSession == null)
                yield break;

            requestInFlight = true;
            SetState(ExternalAuthState.Refreshing, "Refreshing login session...");

            string url = ExternalAuthSettings.BuildRefreshUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                requestInFlight = false;
                if (force)
                    HandleExpiredSession("Session expired. Sign in again.");
                else
                    SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(currentSession));
                yield break;
            }

            RefreshSessionRequestDto payload = new()
            {
                account_id = currentSession.account_id,
                provider = currentSession.provider,
                provider_user_id = currentSession.provider_user_id,
                refresh_token = currentSession.refresh_token
            };

            string responseBody = string.Empty;
            string requestError = string.Empty;
            yield return SendPostJson(
                url,
                JsonUtility.ToJson(payload),
                onSuccess: body => responseBody = body,
                onError: error => requestError = error
            );

            requestInFlight = false;
            if (!string.IsNullOrWhiteSpace(requestError))
            {
                if (force || (currentSession != null && currentSession.IsExpiredUtc))
                    HandleExpiredSession("Session expired. Sign in again.");
                else
                    SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(currentSession));
                yield break;
            }

            if (!TryParseSessionResponse(responseBody, out ExternalAuthSession refreshed, out _))
            {
                if (force || (currentSession != null && currentSession.IsExpiredUtc))
                    HandleExpiredSession("Session expired. Sign in again.");
                else
                    SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(currentSession));
                yield break;
            }

            currentSession = refreshed;
            ExternalAuthSessionStore.Save(refreshed);
            SetState(ExternalAuthState.SignedIn, BuildSignedInMessage(refreshed));
            nextRefreshCheckAt = Time.unscaledTime + 5f;
        }

        private static IEnumerator LogoutRoutine(string url, ExternalAuthSession session)
        {
            requestInFlight = true;
            LogoutRequestDto payload = new()
            {
                account_id = session.account_id,
                provider = session.provider,
                provider_user_id = session.provider_user_id,
                access_token = session.access_token
            };

            bool requestFailed = false;
            yield return SendPostJson(
                url,
                JsonUtility.ToJson(payload),
                onSuccess: _ => { },
                onError: _ => requestFailed = true
            );

            requestInFlight = false;
            string message = requestFailed
                ? "Signed out locally (broker logout not confirmed)."
                : "Signed out.";
            ClearSession(message);
        }

        private static void ClearSession(string message)
        {
            currentSession = null;
            pendingProvider = string.Empty;
            ExternalAuthSessionStore.Clear();
            SetState(ExternalAuthState.SignedOut, message);
        }

        private static void HandleExpiredSession(string message)
        {
            currentSession = null;
            pendingProvider = string.Empty;
            ExternalAuthSessionStore.Clear();
            SetState(ExternalAuthState.Expired, message);
        }

        private static string BuildSignedInMessage(ExternalAuthSession session)
        {
            string name = string.IsNullOrWhiteSpace(session?.display_name) ? "Player" : session.display_name.Trim();
            string provider = string.IsNullOrWhiteSpace(session?.provider) ? "external" : session.provider.Trim();
            return $"Signed in: {name} ({provider})";
        }

        private static ExternalAuthSession NormalizeSession(ExternalAuthSession session)
        {
            if (session == null)
                return null;

            string provider = NormalizeProvider(session.provider);
            string providerUserId = NormalizeProviderUserId(session.provider_user_id);
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerUserId))
                return null;

            session.provider = provider;
            session.provider_user_id = providerUserId;
            session.account_id = NormalizeAccountId(session.account_id, provider, providerUserId);
            if (string.IsNullOrWhiteSpace(session.account_id))
                return null;

            session.display_name = NormalizeDisplayName(session.display_name, session.account_id);
            return session;
        }

        private static bool TryParseSessionResponse(string json, out ExternalAuthSession session, out string error)
        {
            session = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty_response";
                return false;
            }

            try
            {
                ExternalAuthSession direct = JsonUtility.FromJson<ExternalAuthSession>(json);
                direct = NormalizeSession(direct);
                if (direct != null)
                {
                    session = direct;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try
            {
                SessionEnvelopeDto envelope = JsonUtility.FromJson<SessionEnvelopeDto>(json);
                ExternalAuthSession wrapped = NormalizeSession(envelope?.session);
                if (wrapped != null)
                {
                    session = wrapped;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(envelope?.error))
                    error = envelope.error;
                else if (!string.IsNullOrWhiteSpace(envelope?.message))
                    error = envelope.message;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = ex.Message;
            }

            if (string.IsNullOrWhiteSpace(error))
                error = "invalid_session_payload";
            return false;
        }

        private static IEnumerator SendPostJson(
            string url,
            string json,
            Action<string> onSuccess,
            Action<string> onError
        )
        {
            byte[] payload = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.CeilToInt(ExternalAuthSettings.TimeoutSeconds)
            };
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success
                && request.responseCode >= 200
                && request.responseCode < 300;
            if (success)
            {
                onSuccess?.Invoke(request.downloadHandler?.text ?? string.Empty);
            }
            else
            {
                string detail = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(detail))
                    detail = request.error;
                onError?.Invoke($"{request.responseCode}:{SanitizeError(detail)}");
            }

            request.Dispose();
        }

        private static Dictionary<string, string> ParseQueryValues(string source)
        {
            Dictionary<string, string> output = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(source))
                return output;

            string trimmed = source.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
            {
                AddQueryValues(output, uri.Query);
                AddQueryValues(output, uri.Fragment);
                return output;
            }

            AddQueryValues(output, trimmed);
            return output;
        }

        private static void AddQueryValues(Dictionary<string, string> output, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string query = raw.Trim();
            int questionMark = query.IndexOf('?');
            if (questionMark >= 0 && questionMark < query.Length - 1)
                query = query.Substring(questionMark + 1);

            if (query.StartsWith("#", StringComparison.Ordinal))
                query = query.Substring(1);
            if (query.StartsWith("?", StringComparison.Ordinal))
                query = query.Substring(1);

            string[] pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                int separator = pair.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = pair.Substring(0, separator).Trim().ToLowerInvariant();
                string value = pair.Substring(separator + 1).Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                output[key] = UnityWebRequest.UnEscapeURL(value ?? string.Empty);
            }
        }

        private static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return string.Empty;
            string trimmed = provider.Trim().ToLowerInvariant();
            return trimmed.Length <= 32 ? trimmed : trimmed.Substring(0, 32);
        }

        private static string NormalizeProviderUserId(string providerUserId)
        {
            if (string.IsNullOrWhiteSpace(providerUserId))
                return string.Empty;
            string trimmed = providerUserId.Trim();
            return trimmed.Length <= 128 ? trimmed : trimmed.Substring(0, 128);
        }

        private static string NormalizeAccountId(string rawAccountId, string provider, string providerUserId)
        {
            if (!string.IsNullOrWhiteSpace(rawAccountId))
            {
                string trimmed = rawAccountId.Trim();
                return trimmed.Length <= 128 ? trimmed : trimmed.Substring(0, 128);
            }

            string generated = $"{provider}:{providerUserId}";
            return generated.Length <= 128 ? generated : generated.Substring(0, 128);
        }

        private static string NormalizeDisplayName(string rawDisplayName, string accountId)
        {
            if (!string.IsNullOrWhiteSpace(rawDisplayName))
            {
                string trimmed = rawDisplayName.Trim();
                if (trimmed.Length > 48)
                    trimmed = trimmed.Substring(0, 48);
                if (!string.IsNullOrWhiteSpace(trimmed))
                    return trimmed;
            }

            string suffix = accountId.Length >= 6
                ? accountId.Substring(accountId.Length - 6)
                : accountId;
            return $"Player-{suffix}";
        }

        private static string SanitizeError(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "request_failed";
            string trimmed = value.Trim().Replace('\n', ' ').Replace('\r', ' ');
            return trimmed.Length <= 160 ? trimmed : trimmed.Substring(0, 160);
        }

        private static void EnsureUpdater()
        {
            if (updater != null)
                return;

            updater = UnityEngine.Object.FindFirstObjectByType<ExternalAuthUpdater>();
            if (updater != null)
                return;

            GameObject go = new("ExternalAuthUpdater");
            UnityEngine.Object.DontDestroyOnLoad(go);
            updater = go.AddComponent<ExternalAuthUpdater>();
        }

        private static void SetState(ExternalAuthState nextState, string message)
        {
            bool changed = state != nextState || !string.Equals(statusMessage, message, StringComparison.Ordinal);
            state = nextState;
            statusMessage = string.IsNullOrWhiteSpace(message) ? nextState.ToString() : message;
            if (changed)
                StateChanged?.Invoke();
        }
    }

    [DefaultExecutionOrder(-990)]
    public sealed class ExternalAuthUpdater : MonoBehaviour
    {
        private void Update()
        {
            ExternalAuthService.Tick();
        }
    }
}
