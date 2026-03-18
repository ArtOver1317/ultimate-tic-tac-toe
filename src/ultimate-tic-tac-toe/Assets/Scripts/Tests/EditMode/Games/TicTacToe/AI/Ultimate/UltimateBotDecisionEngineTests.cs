#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Gameplay;
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
                    0),
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

            result.DegradationReason.Should().Be(BotFailureReason.TimeoutFallbackLegal);
            result.CutoffReason.Should().Be(SearchCutoffReason.TimeBudgetExceeded);
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

        [Test]
        public void WhenChooseMoveCalledWithEmptyLegalMoves_ThenThrowsInvalidOperationException()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var request = BuildRequest(System.Array.Empty<CellId>(), BuildProfile());

            System.Action act = () => engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            act.Should().Throw<System.InvalidOperationException>();
        }

        [Test]
        public void WhenChooseMoveCancelledBeforeSearch_ThenThrowsOperationCanceledException()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var request = BuildRequest(new[] { new CellId(0, 0), new CellId(0, 1) }, BuildProfile());
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            System.Action act = () => engine.ChooseMoveAsync(request, cts.Token).GetAwaiter().GetResult();

            act.Should().Throw<System.OperationCanceledException>();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenChooseMoveProbabilityIsZeroForHardRule_ThenDoesNotApplyThatHardRule()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(4 * 9) + 0] = PlayerMark.X;
            cells[(4 * 9) + 1] = PlayerMark.X;

            var profile = BuildProfile(
                timeBudgetMs: 100,
                mustWinGlobal: 0f,
                mustBlockGlobal: 0f,
                mustWinLocal: 0f,
                mustBlockLocal: 0f,
                maxEvaluatedNodes: 2000);

            var request = BuildRequest(
                new[] { new CellId(4, 2), new CellId(4, 3), new CellId(4, 4) },
                profile,
                cells,
                BuildMiniBoards(),
                new AllowedMajors(1 << 4),
                new XorShift32BotRngSession(777));

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.HardRuleApplied.Should().BeFalse();
            result.AppliedHardRule.Should().BeNull();
        }

        [Test]
        public async System.Threading.Tasks.Task WhenChooseMoveProbabilityIsBetweenZeroAndOne_ThenDecisionIsDeterministicForFixedSeed()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(4 * 9) + 0] = PlayerMark.X;
            cells[(4 * 9) + 1] = PlayerMark.X;
            var legal = new[] { new CellId(4, 2), new CellId(4, 3) };
            var profile = BuildProfile(
                timeBudgetMs: 100,
                mustWinGlobal: 0f,
                mustBlockGlobal: 0f,
                mustWinLocal: 0.5f,
                mustBlockLocal: 0f,
                maxEvaluatedNodes: 2000);

            var request1 = BuildRequest(legal, profile, cells, BuildMiniBoards(), new AllowedMajors(1 << 4), new XorShift32BotRngSession(12345));
            var request2 = BuildRequest(legal, profile, cells, BuildMiniBoards(), new AllowedMajors(1 << 4), new XorShift32BotRngSession(12345));

            var result1 = await engine.ChooseMoveAsync(request1, System.Threading.CancellationToken.None);
            var result2 = await engine.ChooseMoveAsync(request2, System.Threading.CancellationToken.None);

            result1.Move.Should().Be(result2.Move);
            result1.HardRuleApplied.Should().Be(result2.HardRuleApplied);
            result1.AppliedHardRule.Should().Be(result2.AppliedHardRule);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenGlobalBlockNowAvailableAndHardProfile_ThenAlwaysAppliesGlobalBlockHardRule()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(2 * 9) + 0] = PlayerMark.O;
            cells[(2 * 9) + 1] = PlayerMark.O;

            var miniBoards = BuildMiniBoards();
            miniBoards[0] = MiniBoardStatus.WonByO;
            miniBoards[1] = MiniBoardStatus.WonByO;

            var request = BuildRequest(
                new[] { new CellId(2, 2), new CellId(2, 3) },
                BuildProfile(
                    mustWinGlobal: 1f,
                    mustBlockGlobal: 1f,
                    mustWinLocal: 1f,
                    mustBlockLocal: 1f),
                cells,
                miniBoards,
                new AllowedMajors(1 << 2),
                new XorShift32BotRngSession(10));

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(2, 2));
            result.HardRuleApplied.Should().BeTrue();
            result.AppliedHardRule.Should().Be(HardRuleType.GlobalBlockNow);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenLocalWinNowAvailableAndHardProfile_ThenAlwaysAppliesLocalWinHardRule()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(4 * 9) + 0] = PlayerMark.X;
            cells[(4 * 9) + 1] = PlayerMark.X;

            var request = BuildRequest(
                new[] { new CellId(4, 2), new CellId(4, 3) },
                BuildProfile(),
                cells,
                BuildMiniBoards(),
                new AllowedMajors(1 << 4),
                new XorShift32BotRngSession(99));

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(4, 2));
            result.HardRuleApplied.Should().BeTrue();
            result.AppliedHardRule.Should().Be(HardRuleType.LocalWinNow);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenLocalBlockNowAvailableAndHardProfile_ThenAlwaysAppliesLocalBlockHardRule()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var cells = new PlayerMark[81];
            cells[(4 * 9) + 0] = PlayerMark.O;
            cells[(4 * 9) + 1] = PlayerMark.O;

            var request = BuildRequest(
                new[] { new CellId(4, 2), new CellId(4, 3) },
                BuildProfile(),
                cells,
                BuildMiniBoards(),
                new AllowedMajors(1 << 4),
                new XorShift32BotRngSession(7));

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.Move.Should().Be(new CellId(4, 2));
            result.HardRuleApplied.Should().BeTrue();
            result.AppliedHardRule.Should().Be(HardRuleType.LocalBlockNow);
        }

        [Test]
        public async System.Threading.Tasks.Task WhenNodeCapExceeded_ThenReturnsValidMoveWithNodeCapCutoffReason()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var legal = new[] { new CellId(0, 0), new CellId(0, 1), new CellId(0, 2), new CellId(0, 3) };
            var profile = BuildProfile(
                timeBudgetMs: 1000,
                mustWinGlobal: 0f,
                mustBlockGlobal: 0f,
                mustWinLocal: 0f,
                mustBlockLocal: 0f,
                maxEvaluatedNodes: 1);
            var request = BuildRequest(legal, profile);

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            legal.Should().Contain(result.Move);
            result.CutoffReason.Should().Be(SearchCutoffReason.NodeCapExceeded);
            result.CutoffDetails.Should().Be("node_cap");
        }

        [Test]
        public async System.Threading.Tasks.Task WhenTimeBudgetExceededWithPartialSearch_ThenReturnsTimeoutBestWithBestCompletedResult()
        {
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var legal = new[]
            {
                new CellId(0, 0), new CellId(0, 1), new CellId(0, 2), new CellId(0, 3),
                new CellId(1, 0), new CellId(1, 1), new CellId(1, 2), new CellId(1, 3),
            };
            var profile = BuildProfile(
                timeBudgetMs: 2,
                mustWinGlobal: 0f,
                mustBlockGlobal: 0f,
                mustWinLocal: 0f,
                mustBlockLocal: 0f,
                maxEvaluatedNodes: 200_000,
                minSearchDepth: 1,
                maxSearchDepth: 6);
            var request = BuildRequest(legal, profile);

            var result = await engine.ChooseMoveAsync(request, System.Threading.CancellationToken.None);

            result.CutoffReason.Should().Be(SearchCutoffReason.TimeBudgetExceeded);
            result.IterationsCompleted.Should().BeGreaterThan(0);
            result.DegradationReason.Should().Be(BotFailureReason.TimeoutBest);
            legal.Should().Contain(result.Move);
        }

        private static UltimateBotDecisionRequest BuildRequest(
            System.Collections.Generic.IReadOnlyList<CellId> legal,
            UltimateBotDifficultyProfileData profile)
        {
            return BuildRequest(legal, profile, new PlayerMark[81], BuildMiniBoards(), AllowedMajors.All, new XorShift32BotRngSession(42));
        }

        private static UltimateBotDecisionRequest BuildRequest(
            System.Collections.Generic.IReadOnlyList<CellId> legal,
            UltimateBotDifficultyProfileData profile,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            IBotRngSession rng)
        {
            return new UltimateBotDecisionRequest(
                BotTurnId.Build(10, 0),
                new UltimateBoardSnapshot(
                    cells,
                    miniBoards,
                    allowedMajors,
                    activePlayerSlot: 0),
                legal,
                profile,
                rng);
        }

        private static MiniBoardStatus[] BuildMiniBoards()
        {
            var miniBoards = new MiniBoardStatus[9];
            for (var i = 0; i < miniBoards.Length; i++)
            {
                miniBoards[i] = MiniBoardStatus.InProgress;
            }

            return miniBoards;
        }

        private static UltimateBotDifficultyProfileData BuildProfile(
            int timeBudgetMs = 100,
            int minSearchDepth = 1,
            int maxSearchDepth = 2,
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
                minSearchDepth,
                maxSearchDepth,
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
