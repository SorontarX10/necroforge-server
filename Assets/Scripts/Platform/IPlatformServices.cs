public interface IPlatformServices
{
    string ProviderKey { get; }
    bool IsAvailable { get; }
    bool IsInitialized { get; }

    bool Initialize();
    void Tick();
    void Shutdown();

    string GetPlayerId();
    string GetPlayerName();
    bool OpenOverlayToLeaderboard(string leaderboardUrl);
    bool TryGetExternalAuthTicket(out string provider, out string providerUserId, out string sessionTicket);
}
