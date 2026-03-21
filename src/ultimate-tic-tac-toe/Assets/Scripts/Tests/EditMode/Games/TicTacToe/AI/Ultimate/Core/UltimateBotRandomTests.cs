#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate.Core
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotRandomTests
    {
        [Test]
        public void WhenSameSeed_ThenXorShiftSequenceIsDeterministic()
        {
            var a = new XorShift32BotRngSession(12345);
            var b = new XorShift32BotRngSession(12345);

            for (var i = 0; i < 20; i++)
            {
                a.NextUInt().Should().Be(b.NextUInt());
            }
        }

        [Test]
        public void WhenFactoryUsesDifferentBotSlot_ThenProducesDifferentSequence()
        {
            var factory = new BotRngSessionFactory();
            var profile = BuildProfile(useSeed: true, seed: 10);

            var left = factory.Create("match-1", 0, profile);
            var right = factory.Create("match-1", 1, profile);

            left.NextUInt().Should().NotBe(right.NextUInt());
        }

        [Test]
        public void WhenFactoryInputsEqual_ThenProducesSameSequence()
        {
            var factory = new BotRngSessionFactory();
            var profile = BuildProfile(useSeed: true, seed: 77);

            var a = factory.Create("match-7", 0, profile);
            var b = factory.Create("match-7", 0, profile);

            for (var i = 0; i < 8; i++)
            {
                a.NextUInt().Should().Be(b.NextUInt());
            }
        }

        private static UltimateBotDifficultyProfileData BuildProfile(bool useSeed, int seed) =>
            new(
                "easy",
                "1.0.0",
                new string('f', 64),
                100,
                1,
                2,
                1000,
                3,
                0f,
                1f,
                1f,
                1f,
                1f,
                useSeed,
                seed,
                0,
                false,
                EvaluationWeights.Default);
    }
}
