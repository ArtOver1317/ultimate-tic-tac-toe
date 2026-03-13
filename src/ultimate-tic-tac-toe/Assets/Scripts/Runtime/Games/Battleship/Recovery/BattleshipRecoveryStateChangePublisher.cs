#nullable enable

using System;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.State;

namespace Runtime.Games.Battleship.Recovery
{
    internal sealed class BattleshipRecoveryStateChangePublisher
    {
        private readonly ICurrentPlayerChangedPublisher _currentPlayerChangedPublisher;
        private readonly BattleshipGameplayEventStream? _battleshipEventStream;

        public BattleshipRecoveryStateChangePublisher(
            ICurrentPlayerChangedPublisher currentPlayerChangedPublisher,
            BattleshipGameplayEventStream? battleshipEventStream)
        {
            _currentPlayerChangedPublisher = currentPlayerChangedPublisher ?? throw new ArgumentNullException(nameof(currentPlayerChangedPublisher));
            _battleshipEventStream = battleshipEventStream;
        }

        public void Publish(in BattleshipRecoverySnapshot previous, in BattleshipRecoverySnapshot current)
        {
            if (previous.Phase != current.Phase)
                _battleshipEventStream?.PublishPhaseChangedImmediate(new BattleshipPhaseChangedEvent(current.Phase));

            var player0Changed = DidPlayerSnapshotChange(previous.Player0, current.Player0);
            var player1Changed = DidPlayerSnapshotChange(previous.Player1, current.Player1);

            PublishMarksChanges(player0Changed, player1Changed);
            PublishCurrentPlayerChange(previous.ActivePlayerSlot, current.ActivePlayerSlot);
        }

        private void PublishMarksChanges(bool player0Changed, bool player1Changed)
        {
            if (player0Changed && player1Changed)
            {
                _battleshipEventStream?.PublishMarksChangedImmediate(
                    PlayerSlotMapping.SlotX,
                    PlayerSlotMapping.SlotO,
                    hasSecondaryViewer: true);
                
                return;
            }

            if (player0Changed)
            {
                _battleshipEventStream?.PublishMarksChangedImmediate(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotX));
                return;
            }

            if (player1Changed)
                _battleshipEventStream?.PublishMarksChangedImmediate(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotO));
        }

        private void PublishCurrentPlayerChange(int previousActivePlayerSlot, int currentActivePlayerSlot)
        {
            if (currentActivePlayerSlot >= 0 && previousActivePlayerSlot != currentActivePlayerSlot)
                _currentPlayerChangedPublisher.PublishCurrentPlayerChangedImmediate(currentActivePlayerSlot);
        }

        private static bool DidPlayerSnapshotChange(in BattleshipPlayerRecoverySnapshot previous, in BattleshipPlayerRecoverySnapshot current) =>
            previous.IsPlaced != current.IsPlaced
            || !BattleshipStateMath.AreShipPlacementsEqual(previous.Fleet, current.Fleet)
            || !BattleshipStateMath.AreMarksEqual(previous.OpponentMarks, current.OpponentMarks)
            || !BattleshipStateMath.AreMarksEqual(previous.OwnMarks, current.OwnMarks);
    }
}