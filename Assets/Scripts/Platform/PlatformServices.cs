using System;
using UnityEngine;

public static class PlatformServices
{
    private const string ProviderOverrideEnvVar = "NECROFORGE_PLATFORM_PROVIDER";

    private static IPlatformServices current = new NullPlatformServices();
    private static bool initialized;
    private static bool providerOverriddenForTests;
    private static PlatformServicesUpdater updater;

    public static IPlatformServices Current => current;
    public static bool IsInitialized => initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Initialize();
        EnsureUpdater();
    }

    public static void Initialize()
    {
        if (initialized)
            return;

        if (providerOverriddenForTests)
        {
            initialized = current.Initialize();
            EnsureUpdater();
            return;
        }

        current = CreateBestProvider();
        initialized = current.Initialize();
        EnsureUpdater();
        Debug.Log($"[Platform] provider={current.ProviderKey} available={current.IsAvailable} initialized={initialized}");
    }

    public static void Tick()
    {
        if (!initialized)
            return;

        current.Tick();
    }

    public static void Shutdown()
    {
        if (!initialized)
            return;

        try
        {
            current.Shutdown();
        }
        finally
        {
            initialized = false;
            current = new NullPlatformServices();
        }
    }

    public static string GetPlayerId()
    {
        Initialize();
        return current.GetPlayerId();
    }

    public static string GetPlayerName()
    {
        Initialize();
        return current.GetPlayerName();
    }

    public static bool OpenLeaderboardOverlay(string leaderboardUrl)
    {
        Initialize();
        return current.OpenOverlayToLeaderboard(leaderboardUrl);
    }

    public static void SetProviderForTests(IPlatformServices provider)
    {
        Shutdown();
        current = provider ?? new NullPlatformServices();
        initialized = current.Initialize();
        providerOverriddenForTests = true;
    }

    public static void ResetForTests()
    {
        Shutdown();
        current = new NullPlatformServices();
        initialized = false;
        providerOverriddenForTests = false;
    }

    private static IPlatformServices CreateBestProvider()
    {
        string forcedProvider = Environment.GetEnvironmentVariable(ProviderOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(forcedProvider))
            return CreateProviderFromOverride(forcedProvider);

        IPlatformServices steam = new SteamPlatformServices();
        if (steam.IsAvailable && steam.Initialize())
            return steam;

        steam.Shutdown();
        return new NullPlatformServices();
    }

    private static IPlatformServices CreateProviderFromOverride(string forcedProviderRaw)
    {
        string forced = forcedProviderRaw.Trim().ToLowerInvariant();
        if (forced == "null")
            return new NullPlatformServices();

        if (forced == "steam")
        {
            IPlatformServices steam = new SteamPlatformServices();
            if (steam.IsAvailable && steam.Initialize())
                return steam;

            steam.Shutdown();
            return new NullPlatformServices();
        }

        return new NullPlatformServices();
    }

    private static void EnsureUpdater()
    {
        if (updater != null)
            return;

        updater = UnityEngine.Object.FindFirstObjectByType<PlatformServicesUpdater>();
        if (updater != null)
            return;

        GameObject go = new("PlatformServicesUpdater");
        UnityEngine.Object.DontDestroyOnLoad(go);
        updater = go.AddComponent<PlatformServicesUpdater>();
    }
}

[DefaultExecutionOrder(-1000)]
public sealed class PlatformServicesUpdater : MonoBehaviour
{
    private void Update()
    {
        PlatformServices.Tick();
    }

    private void OnApplicationQuit()
    {
        PlatformServices.Shutdown();
    }
}
