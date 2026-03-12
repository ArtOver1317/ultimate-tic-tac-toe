#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;

namespace Runtime.Games.Battleship
{
    public interface IBattleshipGameplaySnapshotProvider
    {
        BattleshipPhase Phase { get; }

        int ActivePlayerSlot { get; }

        GameStatus CurrentStatus { get; }

        int? WinnerSlot { get; }

        bool IsPlacementConfirmed(int playerSlot);

        bool TryGetFleetLayout(int playerSlot, out FleetLayout layout);

        bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts);

        IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot);

        IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot);
    }

    public readonly struct BattleshipRecoveryState
    {
        public BattleshipPhase Phase { get; }
        public int ActivePlayerSlot { get; }
        public GameStatus FinishStatus { get; }
        public int? WinnerSlot { get; }
        public FleetLayout? Player0Layout { get; }
        public FleetLayout? Player1Layout { get; }
        public IReadOnlyList<BattleshipCellMark> Player0OpponentMarks { get; }
        public IReadOnlyList<BattleshipCellMark> Player1OpponentMarks { get; }
        public int Player0ConsecutiveTimeouts { get; }
        public int Player1ConsecutiveTimeouts { get; }
        public float PlacementTimerRemainingSeconds { get; }
        public float MoveTimerRemainingSeconds { get; }

        public BattleshipRecoveryState(
            BattleshipPhase phase,
            int activePlayerSlot,
            GameStatus finishStatus,
            int? winnerSlot,
            FleetLayout? player0Layout,
            FleetLayout? player1Layout,
            IReadOnlyList<BattleshipCellMark> player0OpponentMarks,
            IReadOnlyList<BattleshipCellMark> player1OpponentMarks,
            int player0ConsecutiveTimeouts,
            int player1ConsecutiveTimeouts,
            float placementTimerRemainingSeconds,
            float moveTimerRemainingSeconds)
        {
            Player0OpponentMarks = player0OpponentMarks ?? throw new ArgumentNullException(nameof(player0OpponentMarks));
            Player1OpponentMarks = player1OpponentMarks ?? throw new ArgumentNullException(nameof(player1OpponentMarks));

            Phase = phase;
            ActivePlayerSlot = activePlayerSlot;
            FinishStatus = finishStatus;
            WinnerSlot = winnerSlot;
            Player0Layout = player0Layout;
            Player1Layout = player1Layout;
            Player0ConsecutiveTimeouts = player0ConsecutiveTimeouts;
            Player1ConsecutiveTimeouts = player1ConsecutiveTimeouts;
            PlacementTimerRemainingSeconds = placementTimerRemainingSeconds;
            MoveTimerRemainingSeconds = moveTimerRemainingSeconds;
        }
    }

    public interface IBattleshipRecoveryStateApplier
    {
        bool TryApplyRecoveryState(in BattleshipRecoveryState state);
    }

    public interface IBattleshipGameplayEventStream
    {
        Observable<BattleshipPhaseChangedEvent> PhaseChanged { get; }

        Observable<BattleshipMarksChangedEvent> MarksChanged { get; }
    }

    public readonly struct BattleshipPhaseChangedEvent
    {
        public BattleshipPhase Phase { get; }

        public BattleshipPhaseChangedEvent(BattleshipPhase phase)
        {
            Phase = phase;
        }
    }

    public readonly struct BattleshipMarksChangedEvent
    {
        public int ViewerSlot { get; }

        public BattleshipMarksChangedEvent(int viewerSlot)
        {
            ViewerSlot = viewerSlot;
        }
    }
}
