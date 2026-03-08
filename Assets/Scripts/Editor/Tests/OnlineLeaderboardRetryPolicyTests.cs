using NUnit.Framework;

namespace GrassSim.Editor.Tests
{
    public sealed class OnlineLeaderboardRetryPolicyTests
    {
        [Test]
        public void ShouldRetry_ReturnsTrue_ForTransientServerFailureWithinBudget()
        {
            bool shouldRetry = OnlineLeaderboardRetryPolicy.ShouldRetry(
                attemptNumber: 1,
                maxAttempts: 2,
                elapsedSeconds: 1f,
                retryBudgetSeconds: 6f,
                responseCode: 503,
                error: "http_error:503:Service Unavailable"
            );

            Assert.IsTrue(shouldRetry);
        }

        [Test]
        public void ShouldRetry_ReturnsFalse_ForClientValidationFailure()
        {
            bool shouldRetry = OnlineLeaderboardRetryPolicy.ShouldRetry(
                attemptNumber: 1,
                maxAttempts: 2,
                elapsedSeconds: 1f,
                retryBudgetSeconds: 6f,
                responseCode: 400,
                error: "http_error:400:Bad Request"
            );

            Assert.IsFalse(shouldRetry);
        }

        [Test]
        public void ShouldRetry_ReturnsFalse_WhenRetryBudgetIsExhausted()
        {
            bool shouldRetry = OnlineLeaderboardRetryPolicy.ShouldRetry(
                attemptNumber: 1,
                maxAttempts: 3,
                elapsedSeconds: 6f,
                retryBudgetSeconds: 6f,
                responseCode: 0,
                error: "http_error:0:Connection timed out"
            );

            Assert.IsFalse(shouldRetry);
        }

        [Test]
        public void ShouldRetry_ReturnsTrue_ForConnectionFailureWithoutStatusCode()
        {
            bool shouldRetry = OnlineLeaderboardRetryPolicy.ShouldRetry(
                attemptNumber: 1,
                maxAttempts: 2,
                elapsedSeconds: 0.5f,
                retryBudgetSeconds: 6f,
                responseCode: 0,
                error: "http_error:0:Cannot resolve destination host"
            );

            Assert.IsTrue(shouldRetry);
        }

        [Test]
        public void GetRetryDelaySeconds_ClampsDelayToRemainingBudget()
        {
            float delay = OnlineLeaderboardRetryPolicy.GetRetryDelaySeconds(
                retryNumber: 2,
                baseDelaySeconds: 0.35f,
                elapsedSeconds: 5.6f,
                retryBudgetSeconds: 6f
            );

            Assert.AreEqual(0.4f, delay, 0.001f);
        }
    }
}
