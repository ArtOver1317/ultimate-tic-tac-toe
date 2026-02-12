#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using UnityEngine.TestTools;

namespace Tests.EditMode.Games.TicTacToe.AI
{
    [TestFixture]
    [Category("Unit")]
    public class MinimaxDecisionEngineTests
    {
        private IRulesEngine _rules;
        private MinimaxDecisionEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _rules = new ClassicRulesEngine();
            _engine = new MinimaxDecisionEngine(_rules);
        }

        private static BotProfileData MakeProfile(
            float mustWinNow = 1f,
            float mustBlockNow = 1f,
            int timeBudgetMs = 5000,
            int minDepth = 1,
            int maxDepth = 9,
            int topN = 1,
            float noise = 0f)
        {
            return new BotProfileData(
                mustWinNow, mustBlockNow, timeBudgetMs,
                minDepth, maxDepth, topN, noise,
                riskBias: 0f, EvaluationWeights.Default, enableDiagnostics: false);
        }

        private static BotDecisionRequest MakeRequest(
            PlayerMark[] cells,
            int boardSize,
            int activeSlot,
            CellId? lastMove,
            int seed = 42)
        {
            int winLength = ClassicRulesEngine.GetWinLength(boardSize);
            var legal = new List<CellId>();
            for (int r = 0; r < boardSize; r++)
            {
                for (int c = 0; c < boardSize; c++)
                {
                    if (cells[r * boardSize + c] == PlayerMark.None)
                        legal.Add(new CellId(r, c));
                }
            }

            return new BotDecisionRequest(
                boardSize, winLength, cells, activeSlot,
                lastMove, legal, commandSequence: 1, new BotRandom(seed));
        }

        // ── WinNow ──

        [UnityTest]
        public IEnumerator WhenBotCanWinImmediately_ThenChoosesWinningMove() => UniTask.ToCoroutine(async () =>
        {
            // X at (0,0), (0,1) — X can win at (0,2)
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X; // (0,0)
            cells[1] = PlayerMark.X; // (0,1)
            cells[3] = PlayerMark.O; // (1,0)
            cells[4] = PlayerMark.O; // (1,1)

            var request = MakeRequest(cells, 3, activeSlot: 0, lastMove: new CellId(0, 1));
            var profile = MakeProfile(mustWinNow: 1f);

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            move.Should().Be(new CellId(0, 2));
        });

        // ── BlockNow ──

        [UnityTest]
        public IEnumerator WhenOpponentCanWinImmediately_ThenBlocksIt() => UniTask.ToCoroutine(async () =>
        {
            // O at (0,0), (0,1) — O can win at (0,2). X must block.
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.O; // (0,0)
            cells[1] = PlayerMark.O; // (0,1)
            cells[4] = PlayerMark.X; // (1,1)

            var request = MakeRequest(cells, 3, activeSlot: 0, lastMove: new CellId(0, 1));
            var profile = MakeProfile(mustBlockNow: 1f);

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            move.Should().Be(new CellId(0, 2));
        });

        // ── Determinism ──

        [UnityTest]
        public IEnumerator WhenSameSeedAndPosition_ThenSameMove() => UniTask.ToCoroutine(async () =>
        {
            var cells = new PlayerMark[9];
            cells[4] = PlayerMark.X; // center

            var profile = MakeProfile(maxDepth: 5, topN: 3, noise: 0.3f);

            var request1 = MakeRequest(cells, 3, activeSlot: 1, lastMove: new CellId(1, 1), seed: 99);
            var move1 = await _engine.ChooseMoveAsync(request1, profile, CancellationToken.None);

            var request2 = MakeRequest(cells, 3, activeSlot: 1, lastMove: new CellId(1, 1), seed: 99);
            var move2 = await _engine.ChooseMoveAsync(request2, profile, CancellationToken.None);

            move1.Should().Be(move2);
        });

        // ── Only legal move ──

        [UnityTest]
        public IEnumerator WhenOnlyOneMove_ThenReturnsThatMove() => UniTask.ToCoroutine(async () =>
        {
            var cells = new PlayerMark[9];
            for (int i = 0; i < 9; i++) cells[i] = PlayerMark.X;
            cells[8] = PlayerMark.None; // only (2,2) empty

            var request = MakeRequest(cells, 3, activeSlot: 0, lastMove: new CellId(2, 1));
            var profile = MakeProfile();

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);
            move.Should().Be(new CellId(2, 2));
        });

        // ── Low budget → still legal move ──

        [UnityTest]
        public IEnumerator WhenTimeBudgetVeryLow_ThenStillReturnsLegalMove() => UniTask.ToCoroutine(async () =>
        {
            var cells = new PlayerMark[25]; // 5x5
            cells[12] = PlayerMark.X;

            var request = MakeRequest(cells, 5, activeSlot: 1, lastMove: new CellId(2, 2));
            var profile = MakeProfile(timeBudgetMs: 1, minDepth: 1, maxDepth: 1);

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            // Move should be one of the legal cells
            var legal = request.LegalMoves;
            legal.Should().Contain(move);
        });

        // ── Easy missable block (probability < 1) ──

        [UnityTest]
        public IEnumerator WhenMustBlockProbabilityZero_ThenMayNotBlock() => UniTask.ToCoroutine(async () =>
        {
            // O threatens win at (0,2). Easy bot with 0% block probability.
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.O;
            cells[1] = PlayerMark.O;
            cells[4] = PlayerMark.X;

            var request = MakeRequest(cells, 3, activeSlot: 0, lastMove: new CellId(0, 1));
            var profile = MakeProfile(mustBlockNow: 0f, timeBudgetMs: 500, maxDepth: 3);

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            // Move should be legal regardless
            request.LegalMoves.Should().Contain(move);
        });

        // ── 5×5 board ──

        [UnityTest]
        public IEnumerator When5x5Board_ThenReturnsLegalMove() => UniTask.ToCoroutine(async () =>
        {
            var cells = new PlayerMark[25];
            cells[12] = PlayerMark.X; // center
            cells[7] = PlayerMark.O;

            var request = MakeRequest(cells, 5, activeSlot: 0, lastMove: new CellId(1, 2));
            var profile = MakeProfile(timeBudgetMs: 2000, maxDepth: 5);

            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);
            request.LegalMoves.Should().Contain(move);
        });
    }
}
