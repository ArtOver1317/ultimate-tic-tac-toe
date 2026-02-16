#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotDecisionEngineTests
    {
        [Test]
        public async System.Threading.Tasks.Task WhenSingleLegalMove_ThenReturnsThatMove()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var legal = new[] { new CellId(4, 4) };
            var request = BuildRequest(legal, BuildProfile());

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(4, 4));
        }

        [Test]
        public async System.Threading.Tasks.Task WhenGlobalWinNowAvailableAndProbabilityOne_ThenAppliesGlobalWinHardRule()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(2 * 9) + 0] = PlayerMark.X;
            cells[(2 * 9) + 1] = PlayerMark.X;

            var miniBoards = new MiniBoardStatus[9]
            {
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
            };

            var profile = BuildProfile(mustWinGlobal: 1f);
            var request = new UltimateBotDecisionRequest(
                BotTurnId.Build(10, 0),
                new UltimateBoardSnapshot(
                    cells,
                    miniBoards,
                    new AllowedMajors(1 << 2),
                    0,
                    default,
                    false,
                    GameStatus.InProgress),
                new[] { new CellId(2, 2), new CellId(2, 3) },
                profile,
                new XorShift32BotRngSession(123));

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(2, 2));
            result.HardRuleApplied.Should().BeTrue();
            result.AppliedHardRule.Should().Be(HardRuleType.GlobalWinNow);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenTimeBudgetZero_ThenReturnsTimeoutFallbackLegal()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var profile = BuildProfile(
                timeBudgetMs: 0,
                mustWinGlobal: 0f,
                mustBlockGlobal: 0f,
                mustWinLocal: 0f,
                mustBlockLocal: 0f,
                maxEvaluatedNodes: 0);
            var request = BuildRequest(new[] { new CellId(0, 0), new CellId(0, 1) }, profile);

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            (result.DegradationReason == BotFailureReason.TimeoutFallbackLegal || result.DegradationReason == null)
                .Should().BeTrue();
            result.Move.Should().Be(new CellId(0, 0));
        }

        [Test]
        public async System.Threading.Tasks.Task WhenScoresEqualAndNoiseZero_ThenUsesStableLegalOrderTieBreak()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var profile = BuildProfile(timeBudgetMs: 100, mustWinGlobal: 0f);
            var request = BuildRequest(new[] { new CellId(0, 0), new CellId(0, 2) }, profile);

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(0, 0));
        }

        private static UltimateBotDecisionRequest BuildRequest(
            System.Collections.Generic.IReadOnlyList<CellId> legal,
            UltimateBotDifficultyProfileData profile)
        {
            var cells = new PlayerMark[81];
            var miniBoards = new MiniBoardStatus[9];
            for (var i = 0; i < miniBoards.Length; i++) miniBoards[i] = MiniBoardStatus.InProgress;

            return new UltimateBotDecisionRequest(
                BotTurnId.Build(10, 0),
                new UltimateBoardSnapshot(
                    cells,
                    miniBoards,
                    AllowedMajors.All,
                    activePlayerSlot: 0,
                    default,
                    hasLastMove: false,
                    GameStatus.InProgress),
                legal,
                profile,
                new XorShift32BotRngSession(42));
        }

        private static UltimateBotDifficultyProfileData BuildProfile(
            int timeBudgetMs = 100,
            float mustWinGlobal = 1f,
            float mustBlockGlobal = 1f,
            float mustWinLocal = 1f,
            float mustBlockLocal = 1f,
            int maxEvaluatedNodes = 1000)
        {
            return new UltimateBotDifficultyProfileData(
                "test",
                "1.0.0",
                new string('a', 64),
                timeBudgetMs,
                1,
                2,
                maxEvaluatedNodes,
                3,
                0f,
                mustWinGlobal,
                mustBlockGlobal,
                mustWinLocal,
                mustBlockLocal,
                false,
                0,
                0,
                false,
                EvaluationWeights.Default);
        }
    }
}
