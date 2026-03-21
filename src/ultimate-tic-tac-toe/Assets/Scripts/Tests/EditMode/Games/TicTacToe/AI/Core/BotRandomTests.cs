#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Core;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI.Core
{
    [TestFixture]
    [Category("Unit")]
    public class BotRandomTests
    {
        [Test]
        public void WhenSameSeed_ThenProducesSameSequence()
        {
            const int seed = 42;
            var rng1 = new BotRandom(seed);
            var rng2 = new BotRandom(seed);

            for (var i = 0; i < 100; i++)
            {
                rng1.NextFloat01().Should().Be(rng2.NextFloat01());
            }
        }

        [Test]
        public void WhenSameSeedNextInt_ThenProducesSameSequence()
        {
            const int seed = 123;
            var rng1 = new BotRandom(seed);
            var rng2 = new BotRandom(seed);

            for (var i = 0; i < 100; i++)
            {
                rng1.NextInt(0, 10).Should().Be(rng2.NextInt(0, 10));
            }
        }

        [Test]
        public void WhenDifferentSeeds_ThenDifferentResults()
        {
            var rng1 = new BotRandom(1);
            var rng2 = new BotRandom(2);

            var anyDifference = false;
           
            for (var i = 0; i < 20; i++)
            {
                if (!Mathf.Approximately(rng1.NextFloat01(), rng2.NextFloat01()))
                {
                    anyDifference = true;
                    break;
                }
            }

            anyDifference.Should().BeTrue();
        }

        [Test]
        public void WhenNextFloat01Called_ThenResultInRange()
        {
            var rng = new BotRandom(99);

            for (var i = 0; i < 1000; i++)
            {
                var val = rng.NextFloat01();
                val.Should().BeGreaterThanOrEqualTo(0f);
                val.Should().BeLessThan(1f);
            }
        }

        [Test]
        public void WhenNextIntCalled_ThenResultInRange()
        {
            var rng = new BotRandom(77);

            for (var i = 0; i < 1000; i++)
            {
                var val = rng.NextInt(5, 15);
                val.Should().BeGreaterThanOrEqualTo(5);
                val.Should().BeLessThan(15);
            }
        }

        [Test]
        public void WhenMinEqualsMax_ThenThrows()
        {
            var rng = new BotRandom(1);
            System.Action act = () => rng.NextInt(5, 5);
            act.Should().Throw<System.ArgumentOutOfRangeException>();
        }
    }
}
