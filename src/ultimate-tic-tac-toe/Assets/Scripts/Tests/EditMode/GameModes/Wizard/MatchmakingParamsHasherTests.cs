using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingParamsHasherTests
    {
        [Test]
        public void WhenComputeCalledWithSameRequestData_ThenReturnsSameHash()
        {
            var first = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3"), ("isUltimate", "false")), 30);
            var second = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3"), ("isUltimate", "false")), 30);

            var firstHash = MatchmakingParamsHasher.Compute(first);
            var secondHash = MatchmakingParamsHasher.Compute(second);

            firstHash.Should().Be(secondHash);
            firstHash.Should().HaveLength(8);
        }

        [Test]
        public void WhenComputeCalledAndGameIdChanged_ThenReturnsDifferentHash()
        {
            var first = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3")), 30);
            var second = new MatchmakingRequest("ultimate-tic-tac-toe", new TestConfig(("boardSize", "3")), 30);

            var firstHash = MatchmakingParamsHasher.Compute(first);
            var secondHash = MatchmakingParamsHasher.Compute(second);

            firstHash.Should().NotBe(secondHash);
        }

        [Test]
        public void WhenComputeCalledAndMoveTimeLimitChanged_ThenReturnsDifferentHash()
        {
            var first = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3")), 30);
            var second = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3")), 60);

            var firstHash = MatchmakingParamsHasher.Compute(first);
            var secondHash = MatchmakingParamsHasher.Compute(second);

            firstHash.Should().NotBe(secondHash);
        }

        [Test]
        public void WhenComputeCalledAndGameSpecificParamsChanged_ThenReturnsDifferentHash()
        {
            var first = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3"), ("isUltimate", "false")), 30);
            var second = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "5"), ("isUltimate", "false")), 30);

            var firstHash = MatchmakingParamsHasher.Compute(first);
            var secondHash = MatchmakingParamsHasher.Compute(second);

            firstHash.Should().NotBe(secondHash);
        }

        [Test]
        public void WhenComputeCalledWithParamsInDifferentOrder_ThenReturnsSameHash()
        {
            var first = new MatchmakingRequest("tic-tac-toe", new TestConfig(("boardSize", "3"), ("isUltimate", "false")), 30);
            var second = new MatchmakingRequest("tic-tac-toe", new TestConfig(("isUltimate", "false"), ("boardSize", "3")), 30);

            var firstHash = MatchmakingParamsHasher.Compute(first);
            var secondHash = MatchmakingParamsHasher.Compute(second);

            firstHash.Should().Be(secondHash);
        }

        [Test]
        public void WhenNormalizeGameIdCalledWithWhitespaceAndUpperCase_ThenReturnsTrimmedLowerCase()
        {
            var normalized = MatchmakingParamsHasher.NormalizeGameId("  Tic-Tac-Toe  ");

            normalized.Should().Be("tic-tac-toe");
        }

        private sealed class TestConfig : IGameConfig
        {
            private readonly IReadOnlyList<KeyValuePair<string, string>> _params;

            public TestConfig(params (string key, string value)[] entries)
            {
                var list = new List<KeyValuePair<string, string>>(entries.Length);
                for (var i = 0; i < entries.Length; i++)
                    list.Add(new KeyValuePair<string, string>(entries[i].key, entries[i].value));

                _params = list;
            }

            public IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams() => _params;
        }
    }
}
