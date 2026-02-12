#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// Pure board simulation runner for bot calibration (ADR-5).
    /// Does NOT use ECS/UI — works directly with <see cref="PlayerMark"/>[] + <see cref="IRulesEngine"/>.
    /// </summary>
    public sealed class SelfPlayRunner : ISelfPlayRunner
    {
        private readonly IBotDecisionEngine _engine;
        private readonly IRulesEngine _rules;
        private readonly IClassicWinLengthProvider _winLengthProvider;

        /// <summary>Mutable accumulator — async methods cannot use ref parameters.</summary>
        private sealed class Stats
        {
            public int MissedWinP1, MissedWinP2, MissedBlockP1, MissedBlockP2;
            public double TotalMsP1, TotalMsP2;
            public int MovesP1, MovesP2;
        }

        public SelfPlayRunner(
            IBotDecisionEngine engine,
            IRulesEngine rules,
            IClassicWinLengthProvider winLengthProvider)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _winLengthProvider = winLengthProvider ?? throw new ArgumentNullException(nameof(winLengthProvider));
        }

        public async UniTask<SelfPlayReport> RunAsync(
            SelfPlayConfig config,
            CancellationToken ct,
            Action<SelfPlayProgress>? onProgress = null)
        {
            int winLength = config.WinLengthOverride ?? _winLengthProvider.GetWinLength(config.BoardSize);
            var profiles = new[] { config.Profile1, config.Profile2 };
            var searchOverrides = new[]
            {
                config.Player1SearchSettingsOverride,
                config.Player2SearchSettingsOverride,
            };

            int p1Wins = 0, p2Wins = 0, draws = 0;
            var stats = new Stats();

            var totalSw = Stopwatch.StartNew();

            for (int matchIdx = 0; matchIdx < config.MatchCount; matchIdx++)
            {
                ct.ThrowIfCancellationRequested();

                int startingSlot = matchIdx % 2;
                int matchSeed = unchecked(config.BaseSeed + matchIdx * 7919);

                var result = await PlayOneMatchAsync(
                    config.BoardSize, winLength, profiles, searchOverrides, startingSlot, matchSeed,
                    matchIdx, config.MatchCount, stats, onProgress, ct);

                switch (result)
                {
                    case 0: p1Wins++; break;
                    case 1: p2Wins++; break;
                    default: draws++; break;
                }

                onProgress?.Invoke(new SelfPlayProgress(matchIdx + 1, config.MatchCount, 0,
                    config.BoardSize * config.BoardSize));

                if (matchIdx % 5 == 0)
                    await UniTask.Yield(ct);
            }

            totalSw.Stop();

            return new SelfPlayReport(
                p1Wins, p2Wins, draws,
                stats.MovesP1 > 0 ? (float)(stats.TotalMsP1 / stats.MovesP1) : 0f,
                stats.MovesP2 > 0 ? (float)(stats.TotalMsP2 / stats.MovesP2) : 0f,
                stats.MissedWinP1, stats.MissedWinP2,
                stats.MissedBlockP1, stats.MissedBlockP2,
                stats.MovesP1 + stats.MovesP2,
                totalSw.Elapsed.TotalMilliseconds);
        }

        /// <returns>Winner slot (0 or 1), or -1 for draw.</returns>
        private async UniTask<int> PlayOneMatchAsync(
            int boardSize, int winLength,
            BotProfileData[] profiles,
            BotSearchSettingsData?[] searchOverrides,
            int startingSlot, int matchSeed,
            int matchIdx,
            int totalMatches,
            Stats stats,
            Action<SelfPlayProgress>? onProgress,
            CancellationToken ct)
        {
            int totalCells = boardSize * boardSize;
            var cells = new PlayerMark[totalCells];
            int activeSlot = startingSlot;
            CellId? lastMove = null;
            long commandSeq = 0;

            for (int turn = 0; turn < totalCells; turn++)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(new SelfPlayProgress(matchIdx, totalMatches, turn, totalCells));

                var legalMoves = CollectLegalMoves(cells, boardSize);
                if (legalMoves.Count == 0) break;

                var winOpportunity = FindWinningMove(cells, boardSize, activeSlot, legalMoves);
                int opponentSlot = 1 - activeSlot;
                var blockOpportunity = FindWinningMove(cells, boardSize, opponentSlot, legalMoves);

                int moveSeed = unchecked(matchSeed + turn * 31 + activeSlot * 997);
                var rng = new BotRandom(moveSeed);
                var request = new BotDecisionRequest(
                    boardSize, winLength, cells, activeSlot, lastMove,
                    legalMoves, commandSeq, rng, searchOverrides[activeSlot]);

                var profile = profiles[activeSlot];
                var sw = Stopwatch.StartNew();
                var chosenMove = await _engine.ChooseMoveAsync(request, profile, ct);
                sw.Stop();
                double moveMs = sw.Elapsed.TotalMilliseconds;
                bool timedOut = moveMs > profile.TimeBudgetMs;

                // Validate chosen move is legal (defensive — prevents corrupted simulation state)
                int chosenIdx = chosenMove.Major * boardSize + chosenMove.Minor;
                if (chosenIdx < 0 || chosenIdx >= totalCells || cells[chosenIdx] != PlayerMark.None)
                {
                    throw new InvalidOperationException(
                        $"[SelfPlay] Engine returned illegal move ({chosenMove.Major},{chosenMove.Minor}) " +
                        $"for slot {activeSlot} at turn {turn}. Cell state: {(chosenIdx >= 0 && chosenIdx < totalCells ? cells[chosenIdx].ToString() : "OOB")}");
                }

                if (activeSlot == 0) { stats.TotalMsP1 += moveMs; stats.MovesP1++; }
                else { stats.TotalMsP2 += moveMs; stats.MovesP2++; }

                if (!timedOut)
                {
                    if (winOpportunity != null && chosenMove != winOpportunity.Value)
                    {
                        if (profile.MustWinNowProbability >= 1f)
                        {
                            if (activeSlot == 0) stats.MissedWinP1++;
                            else stats.MissedWinP2++;
                        }
                    }

                    if (blockOpportunity != null && winOpportunity == null && chosenMove != blockOpportunity.Value)
                    {
                        if (profile.MustBlockNowProbability >= 1f)
                        {
                            if (activeSlot == 0) stats.MissedBlockP1++;
                            else stats.MissedBlockP2++;
                        }
                    }
                }

                var mark = activeSlot == 0 ? PlayerMark.X : PlayerMark.O;
                cells[chosenMove.Major * boardSize + chosenMove.Minor] = mark;
                lastMove = chosenMove;
                commandSeq++;

                var evalResult = _rules.Evaluate(cells, boardSize, chosenMove);
                if (evalResult.Status == Rules.GameStatus.Win)
                    return evalResult.Winner == PlayerMark.X ? 0 : 1;

                if (evalResult.Status == Rules.GameStatus.Draw)
                    return -1;

                activeSlot = 1 - activeSlot;
            }
            return -1;
        }

        // ── Helpers ──

        private static List<CellId> CollectLegalMoves(PlayerMark[] cells, int boardSize)
        {
            var moves = new List<CellId>();
            for (int r = 0; r < boardSize; r++)
            {
                for (int c = 0; c < boardSize; c++)
                {
                    if (cells[r * boardSize + c] == PlayerMark.None)
                        moves.Add(new CellId(r, c));
                }
            }
            return moves;
        }

        private CellId? FindWinningMove(PlayerMark[] cells, int boardSize, int playerSlot,
            IReadOnlyList<CellId> legalMoves)
        {
            var mark = playerSlot == 0 ? PlayerMark.X : PlayerMark.O;
            for (int i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];
                int idx = move.Major * boardSize + move.Minor;
                var prev = cells[idx];
                cells[idx] = mark;

                var result = _rules.Evaluate(cells, boardSize, move);
                cells[idx] = prev;

                if (result.Status == Rules.GameStatus.Win && result.Winner == mark)
                    return move;
            }
            return null;
        }
    }
}
