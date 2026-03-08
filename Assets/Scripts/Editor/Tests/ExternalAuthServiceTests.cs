using GrassSim.Auth;
using NUnit.Framework;

namespace GrassSim.Editor.Tests
{
    public sealed class ExternalAuthServiceTests
    {
        [Test]
        public void TryExtractAuthorizationCode_ParsesUrlQuery()
        {
            bool ok = ExternalAuthService.TryExtractAuthorizationCode(
                "https://example.local/callback?provider=google&code=abc123",
                "google",
                out string provider,
                out string code
            );

            Assert.IsTrue(ok);
            Assert.AreEqual("google", provider);
            Assert.AreEqual("abc123", code);
        }

        [Test]
        public void TryExtractAuthorizationCode_RejectsProviderMismatch()
        {
            bool ok = ExternalAuthService.TryExtractAuthorizationCode(
                "https://example.local/callback?provider=facebook&code=abc123",
                "google",
                out _,
                out _
            );

            Assert.IsFalse(ok);
        }

        [Test]
        public void TryExtractAuthorizationCode_AcceptsRawQuery()
        {
            bool ok = ExternalAuthService.TryExtractAuthorizationCode(
                "provider=microsoft&code=xyz789",
                "microsoft",
                out string provider,
                out string code
            );

            Assert.IsTrue(ok);
            Assert.AreEqual("microsoft", provider);
            Assert.AreEqual("xyz789", code);
        }
    }
}
