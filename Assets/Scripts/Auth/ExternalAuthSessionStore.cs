using System;
using UnityEngine;

namespace GrassSim.Auth
{
    public static class ExternalAuthSessionStore
    {
        private const string SessionKey = "external_auth_session_v1";
        private static ExternalAuthSession cachedSession;
        private static bool loaded;

        public static bool TryGetActiveSession(out ExternalAuthSession session)
        {
            session = Load();
            if (session == null || !session.HasIdentity)
                return false;

            if (string.IsNullOrWhiteSpace(session.account_id))
                session.account_id = BuildAccountId(session.provider, session.provider_user_id);

            return !string.IsNullOrWhiteSpace(session.account_id);
        }

        public static void Save(ExternalAuthSession session)
        {
            if (session == null || !session.HasIdentity)
                return;

            if (string.IsNullOrWhiteSpace(session.account_id))
                session.account_id = BuildAccountId(session.provider, session.provider_user_id);

            cachedSession = session;
            loaded = true;
            PlayerPrefs.SetString(SessionKey, JsonUtility.ToJson(session));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            cachedSession = null;
            loaded = true;
            PlayerPrefs.DeleteKey(SessionKey);
            PlayerPrefs.Save();
        }

        private static ExternalAuthSession Load()
        {
            if (loaded)
                return cachedSession;

            loaded = true;
            string raw = PlayerPrefs.GetString(SessionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                cachedSession = JsonUtility.FromJson<ExternalAuthSession>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ExternalAuth] Failed to parse cached session: {ex.Message}");
                cachedSession = null;
            }

            return cachedSession;
        }

        private static string BuildAccountId(string provider, string providerUserId)
        {
            string p = string.IsNullOrWhiteSpace(provider) ? "oidc" : provider.Trim().ToLowerInvariant();
            string uid = string.IsNullOrWhiteSpace(providerUserId) ? string.Empty : providerUserId.Trim();
            if (string.IsNullOrWhiteSpace(uid))
                return string.Empty;

            string accountId = $"{p}:{uid}";
            return accountId.Length <= 128 ? accountId : accountId.Substring(0, 128);
        }
    }
}
