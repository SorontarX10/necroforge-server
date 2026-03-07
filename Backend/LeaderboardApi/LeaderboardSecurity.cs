using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LeaderboardApi;

public static class LeaderboardSecurity
{
    public static string CreateRandomToken(int bytes = 32)
    {
        byte[] buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer);
    }

    public static string BuildCanonicalPayload(
        Guid runId,
        string playerId,
        int score,
        float runDurationSec,
        int kills,
        string buildVersion,
        bool isCheatSession
    )
    {
        string normalizedDuration = MathF.Max(0f, runDurationSec).ToString("0.###", CultureInfo.InvariantCulture);
        string cheat = isCheatSession ? "1" : "0";
        return $"{runId:D}|{playerId}|{score}|{normalizedDuration}|{kills}|{buildVersion}|{cheat}";
    }

    public static string ComputeSignatureBase64(string sessionKey, string canonicalPayload)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(sessionKey);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload);
        using HMACSHA256 hmac = new(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
    }

    public static bool FixedTimeEqualsBase64(string providedBase64, string expectedBase64)
    {
        try
        {
            byte[] providedBytes = Convert.FromBase64String(providedBase64);
            byte[] expectedBytes = Convert.FromBase64String(expectedBase64);
            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
        catch
        {
            return false;
        }
    }
}
