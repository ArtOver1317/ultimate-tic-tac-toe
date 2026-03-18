#nullable enable

using System;
using R3;
using Runtime.Gameplay;

namespace Runtime.Games.TicTacToe.Series
{
    public readonly struct SeriesScore : IEquatable<SeriesScore>
    {
        public int Player1Wins { get; }
        public int Player2Wins { get; }
        public int Draws { get; }
        public int RoundIndex { get; }

        public SeriesScore(int player1Wins, int player2Wins, int draws, int roundIndex)
        {
            Player1Wins = player1Wins;
            Player2Wins = player2Wins;
            Draws = draws;
            RoundIndex = roundIndex;
        }

        public bool Equals(SeriesScore other)
            => Player1Wins == other.Player1Wins && Player2Wins == other.Player2Wins
                                                && Draws == other.Draws && RoundIndex == other.RoundIndex;

        public override bool Equals(object? obj) => obj is SeriesScore other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Player1Wins, Player2Wins, Draws, RoundIndex);
        public static bool operator ==(SeriesScore left, SeriesScore right) => left.Equals(right);
        public static bool operator !=(SeriesScore left, SeriesScore right) => !left.Equals(right);

        public override string ToString()
            => $"Score(P1={Player1Wins}, P2={Player2Wins}, D={Draws}, Round={RoundIndex})";
    }

    public interface ISeriesService : IDisposable
    {
        ReadOnlyReactiveProperty<SeriesScore> Score { get; }
        void StartSeries();
        void RecordResult(GameResult result);

        /// <summary>
        /// Advances to the next round. Returns the starting player for that round.
        /// Must be called exactly once per round.
        /// Alternation: round 0 → X, round 1 → O, round 2 → X, ...
        /// </summary>
        PlayerMark NextRound();
    }

    public sealed class SeriesService : ISeriesService
    {
        private readonly ReactiveProperty<SeriesScore> _score = new(default);
        private bool _disposed;

        public ReadOnlyReactiveProperty<SeriesScore> Score => _score;

        public void StartSeries()
        {
            ThrowIfDisposed();
            _score.Value = default; // reset to zeros
        }

        public void RecordResult(GameResult result)
        {
            ThrowIfDisposed();
            var s = _score.Value;
            
            _score.Value = result.Status switch
            {
                GameStatus.Win or GameStatus.Timeout when result.Winner == PlayerMark.X
                    => new SeriesScore(s.Player1Wins + 1, s.Player2Wins, s.Draws, s.RoundIndex),
                GameStatus.Win or GameStatus.Timeout when result.Winner == PlayerMark.O
                    => new SeriesScore(s.Player1Wins, s.Player2Wins + 1, s.Draws, s.RoundIndex),
                GameStatus.Draw
                    => new SeriesScore(s.Player1Wins, s.Player2Wins, s.Draws + 1, s.RoundIndex),
                _ => s, // InProgress — ignore
            };
        }

        public PlayerMark NextRound()
        {
            ThrowIfDisposed();

            var s = _score.Value;
            var nextRound = s.RoundIndex + 1;
            _score.Value = new SeriesScore(s.Player1Wins, s.Player2Wins, s.Draws, nextRound);

            return GetFirstPlayerForRound(nextRound);
        }

        private static PlayerMark GetFirstPlayerForRound(int roundIndex)
            => roundIndex % 2 == 0 ? PlayerMark.X : PlayerMark.O;

        public void Dispose()
        {
            if (_disposed) 
                return;
            
            _disposed = true;
            _score.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SeriesService));
        }
    }
}