using GrassSim.Auth;
using NUnit.Framework;
using UnityEngine;

namespace GrassSim.Editor.Tests
{
    public sealed class PlatformServicesTests
    {
        private sealed class FakePlatformServices : IPlatformServices
        {
            private readonly string playerId;
            private readonly string playerName;
            private readonly bool overlayOpenResult;

            public FakePlatformServices(string playerId, string playerName, bool overlayOpenResult)
            {
                this.playerId = playerId;
                this.playerName = playerName;
                this.overlayOpenResult = overlayOpenResult;
            }

            public string ProviderKey => "fake";
            public bool IsAvailable => true;
            public bool IsInitialized { get; private set; }
            public string LastOverlayUrl { get; private set; }

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
                return playerId;
            }

            public string GetPlayerName()
            {
                return playerName;
            }

            public bool OpenOverlayToLeaderboard(string leaderboardUrl)
            {
                LastOverlayUrl = leaderboardUrl;
                return overlayOpenResult;
            }
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey("leaderboard_player_id");
            PlayerPrefs.DeleteKey("leaderboard_display_name");
            ExternalAuthSessionStore.Clear();
            PlayerPrefs.Save();
            PlatformServices.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlatformServices.ResetForTests();
            PlayerPrefs.DeleteKey("leaderboard_player_id");
            PlayerPrefs.DeleteKey("leaderboard_display_name");
            ExternalAuthSessionStore.Clear();
            PlayerPrefs.Save();
        }

        [Test]
        public void SteamProvider_Initialize_DoesNotThrow_WhenSteamSdkMissing()
        {
            var provider = new SteamPlatformServices();

            Assert.DoesNotThrow(() => provider.Initialize());
            provider.Shutdown();
        }

        [Test]
        public void PlayerIdentity_UsesPlatformIdentity_WhenAvailable()
        {
            var fake = new FakePlatformServices(
                playerId: "steam:123456789",
                playerName: "SteamTester",
                overlayOpenResult: true
            );
            PlatformServices.SetProviderForTests(fake);

            string playerId = PlayerIdentityService.GetPlayerId();
            string playerName = PlayerIdentityService.GetDisplayName();

            Assert.AreEqual("steam:123456789", playerId);
            Assert.AreEqual("SteamTester", playerName);
        }

        [Test]
        public void PlayerIdentity_FallsBackToLocal_WhenPlatformReturnsEmpty()
        {
            var fake = new FakePlatformServices(
                playerId: string.Empty,
                playerName: string.Empty,
                overlayOpenResult: false
            );
            PlatformServices.SetProviderForTests(fake);

            string playerId = PlayerIdentityService.GetPlayerId();
            string playerName = PlayerIdentityService.GetDisplayName();

            Assert.IsFalse(string.IsNullOrWhiteSpace(playerId));
            Assert.IsTrue(playerName.StartsWith("Player-", System.StringComparison.Ordinal));
        }

        [Test]
        public void OpenLeaderboardOverlay_DelegatesToProvider()
        {
            var fake = new FakePlatformServices(
                playerId: "steam:999",
                playerName: "OverlayUser",
                overlayOpenResult: true
            );
            PlatformServices.SetProviderForTests(fake);

            bool opened = PlatformServices.OpenLeaderboardOverlay("https://example.com/leaderboard");

            Assert.IsTrue(opened);
            Assert.AreEqual("https://example.com/leaderboard", fake.LastOverlayUrl);
        }
    }
}
