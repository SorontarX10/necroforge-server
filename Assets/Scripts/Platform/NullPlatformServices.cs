public sealed class NullPlatformServices : IPlatformServices
{
    public string ProviderKey => "null";
    public bool IsAvailable => false;
    public bool IsInitialized { get; private set; }

    public bool Initialize()
    {
        IsInitialized = true;
        return true;
    }

    public void Tick()
    {
    }

    public void Shutdown()
    {
        IsInitialized = false;
    }

    public string GetPlayerId()
    {
        return string.Empty;
    }

    public string GetPlayerName()
    {
        return string.Empty;
    }

    public bool OpenOverlayToLeaderboard(string leaderboardUrl)
    {
        return false;
    }

    public bool TryGetExternalAuthTicket(out string provider, out string providerUserId, out string sessionTicket)
    {
        provider = string.Empty;
        providerUserId = string.Empty;
        sessionTicket = string.Empty;
        return false;
    }
}
