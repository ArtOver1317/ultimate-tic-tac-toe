#nullable enable

using System;
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
        private IRulesEngine _rules = null!;
        private MinimaxDecisionEngine _engine = null!;

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
            int seed = 42,
            BotSearchSettingsData? searchSettingsOverride = null)
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
                lastMove, legal, commandSequence: 1, new BotRandom(seed),
                searchSettingsOverride);
        }

        /// <summary>
        /// Search settings with no depth caps and frequent yields — guarantees
        /// long-running search for cancellation tests.
        /// </summary>
        private static readonly BotSearchSettingsData NoCapsSettings = new(
            yieldEveryNNodes: 256,
            safetyBudgetMultiplier: 10f,
            candidateFilterMinBoardSize: 100, // disable candidate filtering
            candidateNeighborRadius: 2,
            depthCap3OrLess: 99,
            depthCap4: 99,
            depthCap5: 99,
            depthCap6: 99,
            depthCap7: 99,
            depthCap8Plus: 99);

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

        // ══════════════════════════════════════════════
        //  Cancellation & Timeout (§3.1)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenCancellationTokenCancelled_ThenThrowsOperationCanceledException() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: 5x5 board with NoCapsSettings override so depth cap doesn't
                // short-circuit the search. Deep search + frequent yields guarantee CT check.
                var cells = new PlayerMark[25];
                cells[12] = PlayerMark.X;
                cells[6] = PlayerMark.O;

                var request = MakeRequest(cells, 5, activeSlot: 0, lastMove: new CellId(1, 1),
                    searchSettingsOverride: NoCapsSettings);
                var profile = MakeProfile(timeBudgetMs: 10000, minDepth: 1, maxDepth: 9);

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(100);

                // Act & Assert
                bool threw = false;
                try
                {
                    await _engine.ChooseMoveAsync(request, profile, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                threw.Should().BeTrue("engine should throw OperationCanceledException when token is cancelled");
            });

        [UnityTest]
        public IEnumerator WhenTimeBudgetExceeded_ThenReturnsLegalMove() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: 5x5, 23 empty cells, very low budget forces timeout
                var cells = new PlayerMark[25];
                cells[12] = PlayerMark.X;
                cells[7] = PlayerMark.O;

                var request = MakeRequest(cells, 5, activeSlot: 0, lastMove: new CellId(1, 2));
                var profile = MakeProfile(timeBudgetMs: 50, minDepth: 1, maxDepth: 9);

                var previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;

                try
                {
                    // Act
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);
                    sw.Stop();

                    // Assert: legal move returned, no hang
                    request.LegalMoves.Should().Contain(move);
                    sw.ElapsedMilliseconds.Should().BeLessThan(5000, "engine should not hang indefinitely");
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                }
            });

        // ══════════════════════════════════════════════
        //  Scaling (§3.2) — [Explicit] calibration only
        // ══════════════════════════════════════════════

        [UnityTest, Explicit("Calibration: manual run only")]
        public IEnumerator When8x8Board_ThenReturnsLegalMove() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: 8x8, ~50 empty cells
            var cells = new PlayerMark[64];
            // Place a few pieces to create a realistic mid-game
            cells[27] = PlayerMark.X; // (3,3)
            cells[28] = PlayerMark.O; // (3,4)
            cells[35] = PlayerMark.X; // (4,3)
            cells[36] = PlayerMark.O; // (4,4)
            cells[19] = PlayerMark.X; // (2,3)
            cells[20] = PlayerMark.O; // (2,4)
            cells[43] = PlayerMark.X; // (5,3)
            cells[44] = PlayerMark.O; // (5,4)
            cells[26] = PlayerMark.X; // (3,2)
            cells[29] = PlayerMark.O; // (3,5)
            cells[34] = PlayerMark.X; // (4,2)
            cells[37] = PlayerMark.O; // (4,5)
            cells[18] = PlayerMark.X; // (2,2)
            cells[21] = PlayerMark.O; // (2,5)

            var request = MakeRequest(cells, 8, activeSlot: 0, lastMove: new CellId(2, 5));
            var profile = MakeProfile(timeBudgetMs: 2000, minDepth: 1, maxDepth: 7);

            // Act
            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            // Assert
            request.LegalMoves.Should().Contain(move);
        });

        [UnityTest, Explicit("Calibration: manual run only")]
        public IEnumerator When10x10Board_ThenReturnsLegalMove() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: 10x10, ~70 empty cells
            var cells = new PlayerMark[100];
            // Cluster pieces in the center
            cells[44] = PlayerMark.X; // (4,4)
            cells[45] = PlayerMark.O; // (4,5)
            cells[54] = PlayerMark.X; // (5,4)
            cells[55] = PlayerMark.O; // (5,5)
            cells[34] = PlayerMark.X; // (3,4)
            cells[35] = PlayerMark.O; // (3,5)
            cells[64] = PlayerMark.X; // (6,4)
            cells[65] = PlayerMark.O; // (6,5)
            cells[43] = PlayerMark.X; // (4,3)
            cells[46] = PlayerMark.O; // (4,6)
            cells[53] = PlayerMark.X; // (5,3)
            cells[56] = PlayerMark.O; // (5,6)
            cells[33] = PlayerMark.X; // (3,3)
            cells[36] = PlayerMark.O; // (3,6)
            cells[63] = PlayerMark.X; // (6,3)
            cells[66] = PlayerMark.O; // (6,6)
            cells[24] = PlayerMark.X; // (2,4)
            cells[25] = PlayerMark.O; // (2,5)
            cells[74] = PlayerMark.X; // (7,4)
            cells[75] = PlayerMark.O; // (7,5)
            cells[42] = PlayerMark.X; // (4,2)
            cells[47] = PlayerMark.O; // (4,7)
            cells[52] = PlayerMark.X; // (5,2)
            cells[57] = PlayerMark.O; // (5,7)
            cells[32] = PlayerMark.X; // (3,2)
            cells[37] = PlayerMark.O; // (3,7)
            cells[62] = PlayerMark.X; // (6,2)
            cells[67] = PlayerMark.O; // (6,7)
            cells[23] = PlayerMark.X; // (2,3)
            cells[26] = PlayerMark.O; // (2,6)

            var request = MakeRequest(cells, 10, activeSlot: 0, lastMove: new CellId(2, 6));
            var profile = MakeProfile(timeBudgetMs: 3000, minDepth: 1, maxDepth: 7);

            // Act
            var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

            // Assert
            request.LegalMoves.Should().Contain(move);
        });

        // ══════════════════════════════════════════════
        //  Profile Parameters (§3.3)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenNoiseGreaterThanZero_ThenIntroducesRandomness() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: zero heuristic weights => many candidates get equal scores.
                // This makes Noise effect deterministic enough for variability assertion.
                var cells = new PlayerMark[9];
                cells[4] = PlayerMark.X;
                cells[0] = PlayerMark.O;

                var profile = new BotProfileData(
                    mustWinNowProbability: 0f,
                    mustBlockNowProbability: 0f,
                    timeBudgetMs: 500,
                    minSearchDepth: 1,
                    maxSearchDepth: 3,
                    topCandidateCount: 9,
                    noise: 0.7f,
                    riskBias: 0f,
                    weights: new EvaluationWeights(0f, 0f, 0f, 0f),
                    enableDiagnostics: false);

                var uniqueMoves = new HashSet<CellId>();

                // Act: 30 runs with different seeds
                for (int i = 0; i < 30; i++)
                {
                    var request = MakeRequest(cells, 3, activeSlot: 1, lastMove: new CellId(0, 0), seed: i * 7);
                    var move = await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);
                    request.LegalMoves.Should().Contain(move);
                    uniqueMoves.Add(move);
                }

                // Assert: at least 2 unique moves across 30 runs
                uniqueMoves.Count.Should().BeGreaterThanOrEqualTo(2,
                    "noise should introduce variability across different seeds");
            });

        [UnityTest]
        public IEnumerator WhenTopCandidateCountLimited_ThenReturnsLegalMove() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: 5x5 with 23 empty cells
                var cells = new PlayerMark[25];
                cells[12] = PlayerMark.X;
                cells[7] = PlayerMark.O;
                var cellsCopy = (PlayerMark[])cells.Clone();

                var profileTopN3 = MakeProfile(timeBudgetMs: 500, maxDepth: 3, topN: 3);
                var profileTopN23 = MakeProfile(timeBudgetMs: 500, maxDepth: 3, topN: 23);

                var request3 = MakeRequest(cells, 5, activeSlot: 0, lastMove: new CellId(1, 2));
                var request23 = MakeRequest(cellsCopy, 5, activeSlot: 0, lastMove: new CellId(1, 2));

                // Act
                var move3 = await _engine.ChooseMoveAsync(request3, profileTopN3, CancellationToken.None);
                var move23 = await _engine.ChooseMoveAsync(request23, profileTopN23, CancellationToken.None);

                // Assert: both return legal moves
                request3.LegalMoves.Should().Contain(move3);
                request23.LegalMoves.Should().Contain(move23);
            });

        // ══════════════════════════════════════════════
        //  Buffer Mutation (§3.3)
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenChooseMoveAsyncCompletes_ThenCellsBufferNotMutated() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: 3x3 board with some pieces
                var cells = new PlayerMark[9];
                cells[0] = PlayerMark.X;
                cells[4] = PlayerMark.O;
                cells[8] = PlayerMark.X;

                var snapshot = (PlayerMark[])cells.Clone();
                var request = MakeRequest(cells, 3, activeSlot: 1, lastMove: new CellId(2, 2));
                var profile = MakeProfile(timeBudgetMs: 500, maxDepth: 5);

                // Act
                await _engine.ChooseMoveAsync(request, profile, CancellationToken.None);

                // Assert: strict sequence equality (same length + same value at each index)
                cells.Should().Equal(snapshot, "engine must not mutate request.Cells");
            });

        [UnityTest]
        public IEnumerator WhenCancellationTokenCancelled_ThenCellsBufferNotMutated() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange: 5x5 board with NoCapsSettings to ensure deep search.
                var cells = new PlayerMark[25];
                cells[12] = PlayerMark.X;
                cells[6] = PlayerMark.O;

                var snapshot = (PlayerMark[])cells.Clone();
                var request = MakeRequest(cells, 5, activeSlot: 0, lastMove: new CellId(1, 1),
                    searchSettingsOverride: NoCapsSettings);
                var profile = MakeProfile(timeBudgetMs: 10000, minDepth: 1, maxDepth: 9);

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(100);

                // Act: expect cancellation
                bool threw = false;
                try
                {
                    await _engine.ChooseMoveAsync(request, profile, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                threw.Should().BeTrue("test contract requires real cancellation path");

                // Assert: buffer must be restored even after cancellation
                cells.Should().Equal(snapshot,
                    "engine must restore cells buffer via undo even when cancelled");
            });
    }
}
