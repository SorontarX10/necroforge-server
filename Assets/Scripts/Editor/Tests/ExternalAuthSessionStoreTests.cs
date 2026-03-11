using System;
using GrassSim.Auth;
using NUnit.Framework;
using UnityEngine;

namespace GrassSim.Editor.Tests
{
    public sealed class ExternalAuthSessionStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            ExternalAuthSessionStore.Clear();
            PlayerPrefs.DeleteKey("leaderboard_player_id");
            PlayerPrefs.DeleteKey("leaderboard_display_name");
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            ExternalAuthSessionStore.Clear();
            PlayerPrefs.DeleteKey("leaderboard_player_id");
            PlayerPrefs.DeleteKey("leaderboard_display_name");
            PlayerPrefs.Save();
        }

        [Test]
        public void SaveAndLoad_BuildsStableAccountId()
        {
            ExternalAuthSession session = new()
            {
                provider = "Google",
                provider_user_id = "google-user-123",
                display_name = "GoogleUser"
            };

            ExternalAuthSessionStore.Save(session);

            bool found = ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession loaded);

            Assert.IsTrue(found);
            Assert.AreEqual("google:google-user-123", loaded.account_id);
            Assert.AreEqual("GoogleUser", loaded.display_name);
        }

        [Test]
        public void PlayerIdentityService_PrefersExternalAuthSession()
        {
            string providerUserId = $"ms-uid-{Guid.NewGuid():N}";
            ExternalAuthSession session = new()
            {
                provider = "microsoft",
                provider_user_id = providerUserId,
                display_name = "MsTester"
            };
            ExternalAuthSessionStore.Save(session);

            string playerId = PlayerIdentityService.GetPlayerId();
            string displayName = PlayerIdentityService.GetDisplayName();

            Assert.AreEqual($"microsoft:{providerUserId}", playerId);
            Assert.AreEqual("MsTester", displayName);
        }

        [Test]
        public void PlayerIdentityService_UsesCustomNicknameForExternalAuthSession()
        {
            string providerUserId = $"google-uid-{Guid.NewGuid():N}";
            ExternalAuthSession session = new()
            {
                provider = "google",
                provider_user_id = providerUserId,
                display_name = "Google Real Name"
            };
            ExternalAuthSessionStore.Save(session);

            PlayerIdentityService.SetDisplayName("NecroKnight");

            string displayName = PlayerIdentityService.GetDisplayName();
            Assert.AreEqual("NecroKnight", displayName);
        }
    }
}
