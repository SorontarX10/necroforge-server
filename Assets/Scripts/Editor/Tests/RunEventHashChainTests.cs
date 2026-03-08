using NUnit.Framework;

namespace GrassSim.Editor.Tests
{
    public sealed class RunEventHashChainTests
    {
        [SetUp]
        public void SetUp()
        {
            RunEventHashChain.ResetRun();
        }

        [Test]
        public void BuildPayload_IsDeterministic_ForTheSameInputSequence()
        {
            RecordSequence();
            GameRunStats stats = new() { kills = 3, timeSurvived = 12.7f, finalScore = 312 };
            RunEventHashChain.Payload first = RunEventHashChain.BuildPayload("run-1", "nonce-1", stats);

            RunEventHashChain.ResetRun();
            RecordSequence();
            RunEventHashChain.Payload second = RunEventHashChain.BuildPayload("run-1", "nonce-1", stats);

            Assert.AreEqual(first.eventChain, second.eventChain);
            Assert.AreEqual(first.eventChainHash, second.eventChainHash);
            Assert.AreEqual(first.eventCount, second.eventCount);
            Assert.GreaterOrEqual(first.eventCount, 2);
        }

        [Test]
        public void RecordCheckpoint_DoesNotAllowDecreasingValues()
        {
            RunEventHashChain.RecordCheckpoint(5.2f, 3, 305);
            RunEventHashChain.RecordCheckpoint(4.0f, 1, 200);

            GameRunStats stats = new() { kills = 3, timeSurvived = 6.0f, finalScore = 306 };
            RunEventHashChain.Payload payload = RunEventHashChain.BuildPayload("run-2", "nonce-2", stats);

            StringAssert.Contains("5,3,305", payload.eventChain);
            StringAssert.Contains("6,3,306", payload.eventChain);
        }

        private static void RecordSequence()
        {
            RunEventHashChain.RecordCheckpoint(1.0f, 0, 1);
            RunEventHashChain.RecordCheckpoint(4.0f, 1, 104);
            RunEventHashChain.RecordCheckpoint(8.4f, 2, 208);
        }
    }
}
