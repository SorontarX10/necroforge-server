using System;

namespace GrassSim.Auth
{
    [Serializable]
    public sealed class ExternalAuthSession
    {
        public string account_id;
        public string provider;
        public string provider_user_id;
        public string display_name;
        public string access_token;
        public string refresh_token;
        public long expires_at_unix;

        public bool HasIdentity
        {
            get
            {
                return !string.IsNullOrWhiteSpace(provider)
                    && !string.IsNullOrWhiteSpace(provider_user_id);
            }
        }

        public bool IsExpiredUtc
        {
            get
            {
                if (expires_at_unix <= 0)
                    return false;

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return now >= expires_at_unix;
            }
        }
    }
}
