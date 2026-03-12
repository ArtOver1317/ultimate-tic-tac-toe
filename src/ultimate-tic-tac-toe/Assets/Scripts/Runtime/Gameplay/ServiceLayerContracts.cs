#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.Shared;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Runtime.Gameplay
{
    /// <summary>
    /// Accepts gameplay commands from UI/bot/network.
    /// Exists only when a match is active (ADR-2).
    /// </summary>
    public interface IGameplayCommandSink
    {
        void SubmitCommand(IGameplayCommand command);
    }

    /// <summary>
    /// Hot-path reactive event streams published after each ECS tick (ADR-4, ADR-5).
    /// Deterministic order: CellChanged → LastMoveChanged → CurrentPlayerChanged → RoundFinished.
    /// </summary>
    public interface IGameplayEventStream
    {
        Observable<CellChangedEvent> CellChanged { get; }
        Observable<LastMoveChangedEvent> LastMoveChanged { get; }
        Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged { get; }
        Observable<CommandRejectedEvent> CommandRejected { get; }
        Observable<RoundFinishedEvent> RoundFinished { get; }
    }

    /// <summary>
    /// Publishes an immediate current-player change when ECS state is externally restored.
    /// </summary>
    public interface ICurrentPlayerChangedPublisher
    {
        void PublishCurrentPlayerChangedImmediate(int activePlayerSlot);
    }

    /// <summary>
    /// Cold-path snapshot reads from ECS state.
    /// </summary>
    public interface IGameplaySnapshotProvider
    {
        /// <summary>
        /// Returns the player slot occupying the cell, or -1 if empty/invalid.
        /// </summary>
        int GetCellSlot(CellId cellId);

        IReadOnlyList<CellSnapshot> GetAllCells();
        long CommandSequence { get; }

        /// <summary>
        /// Returns the slot index of the player whose turn it is (e.g., 0 = X, 1 = O).
        /// Used by binder cold-path to show the correct starting-player label.
        /// </summary>
        int ActivePlayerSlot { get; }

        /// <summary>
        /// Returns the last move submitted, or null if no moves were made yet.
        /// Used for UI highlights, reconnect recovery, and move history display.
        /// </summary>
        CellId? LastMove { get; }
    }

    /// <summary>
    /// Composite ISP aggregator for DI convenience. Consumers should depend on narrow contracts
    /// (<see cref="IGameplayCommandSink"/>, <see cref="IGameplayEventStream"/>,
    /// <see cref="IGameplaySnapshotProvider"/>) when possible (ADR-4).
    /// </summary>
    public interface IMatchStateProvider
        : IGameplayCommandSink, IGameplayEventStream, IGameplaySnapshotProvider, IDisposable
    {
        bool IsMatchActive { get; }
    }
}
