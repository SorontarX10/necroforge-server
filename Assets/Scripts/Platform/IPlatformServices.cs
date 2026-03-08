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
}
