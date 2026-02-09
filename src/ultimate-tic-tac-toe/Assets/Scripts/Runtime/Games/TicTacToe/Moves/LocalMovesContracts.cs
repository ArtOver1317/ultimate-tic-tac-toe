using System;
using System.Collections.Generic;
using R3;

using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe.Moves
{
    public enum PlayerMark
    {
        None = 0,
        X = 1,
        O = 2,
    }

    public enum ApplyClickResult
    {
        Applied = 0,
        CellOccupied = 1,
        InvalidCellId = 2,
        NotStarted = 3,
    }

    public readonly struct CellChangedEvent
    {
        public CellId CellId { get; }
        public PlayerMark NewValue { get; }

        public CellChangedEvent(CellId cellId, PlayerMark newValue)
        {
            CellId = cellId;
            NewValue = newValue;
        }
    }

    public readonly struct LastMoveChangedEvent
    {
        public CellId? Previous { get; }
        public CellId? Current { get; }

        public LastMoveChangedEvent(CellId? previous, CellId? current)
        {
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct LocalMovesConfig
    {
        public FieldRenderSpec Field { get; }
        public PlayerMark StartingPlayer { get; }

        public LocalMovesConfig(FieldRenderSpec field, PlayerMark startingPlayer)
        {
            Field = field;
            StartingPlayer = startingPlayer;
        }
    }

    public readonly struct MovesVfxSettings
    {
        public bool EnableMarkAppearAnimation { get; }
        public float MarkAppearDurationSeconds { get; }

        public static MovesVfxSettings Default => new(enableMarkAppearAnimation: true, markAppearDurationSeconds: 0.16f);

        public MovesVfxSettings(bool enableMarkAppearAnimation, float markAppearDurationSeconds)
        {
            EnableMarkAppearAnimation = enableMarkAppearAnimation;
            MarkAppearDurationSeconds = markAppearDurationSeconds;
        }
    }

    public readonly struct ClickRejectedEvent
    {
        public CellId CellId { get; }
        public ApplyClickResult Reason { get; }

        public ClickRejectedEvent(CellId cellId, ApplyClickResult reason)
        {
            CellId = cellId;
            Reason = reason;
        }
    }

    public readonly struct CellValue
    {
        public CellId CellId { get; }
        public PlayerMark Value { get; }

        public CellValue(CellId cellId, PlayerMark value)
        {
            CellId = cellId;
            Value = value;
        }
    }

    public interface ILocalMovesService : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsStarted { get; }
        ReadOnlyReactiveProperty<PlayerMark> CurrentPlayer { get; }

        Observable<CellChangedEvent> CellChanged { get; }
        Observable<LastMoveChangedEvent> LastMoveChanged { get; }

        Observable<ClickRejectedEvent> ClickRejected { get; }

        void Start(LocalMovesConfig config);
        void Stop();

        ApplyClickResult TryApplyLocalClick(CellId cellId);
        PlayerMark GetCellValue(CellId cellId);

        /// <summary>
        /// Cold path snapshot for initial render (Bind/Start/Reset). Do not use in hot path.
        /// </summary>
        IReadOnlyList<CellValue> GetAllCells();

        new void Dispose();
    }
}
