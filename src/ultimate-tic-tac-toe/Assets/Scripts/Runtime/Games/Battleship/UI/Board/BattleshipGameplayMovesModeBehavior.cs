using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Gameplay.Shared;

namespace Runtime.Games.Battleship.UI.Board
{
    public sealed class BattleshipGameplayMovesModeBehavior : IGameplayMovesModeBehavior
    {
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;

        public BattleshipGameplayMovesModeBehavior(
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
        }

        public void Initialize(GameplayMovesFieldRenderer renderer, IReadOnlyList<CellValue> cells)
        {
            renderer.Reset();
            RefreshOpponentInteractivity(renderer);
        }

        public bool CanSubmitCellClick() => _snapshotProvider.Phase == BattleshipPhase.Battle;

        public void HandleCellChanged(GameplayMovesFieldRenderer renderer, CellChangedEvent evt) => 
            RefreshOpponentInteractivity(renderer);

        public void HandleLastMoveChanged(GameplayMovesFieldRenderer renderer, LastMoveChangedEvent evt) => 
            renderer.ClearLastMoveHighlight();

        private void RefreshOpponentInteractivity(GameplayMovesFieldRenderer renderer)
        {
            var localSlot = ResolveLocalSlot();
            var marks = _snapshotProvider.GetOpponentMarks(localSlot);
            
            if (marks.Count == 0)
                return;

            var boardSize = ResolveBoardSize(marks.Count);
            
            for (var index = 0; index < marks.Count; index++)
            {
                var row = index / boardSize;
                var col = index % boardSize;
                renderer.UpdateCellInteractivity(new CellId(row, col), marks[index] != BattleshipCellMark.Unknown);
            }
        }

        private int ResolveLocalSlot()
        {
            var session = _sessionContextStore.Snapshot;
            
            if (!session.IsOnlineDirectInvite)
                return PlayerSlotMapping.SlotX;

            return session.IsHost
                ? PlayerSlotMapping.SlotX
                : PlayerSlotMapping.SlotO;
        }

        private static int ResolveBoardSize(int cellCount)
        {
            if (cellCount <= 0)
                return BattleshipEcsBoard.DefaultBoardSize;

            var root = (int)Math.Sqrt(cellCount);
            
            return root > 0 && root * root == cellCount
                ? root
                : BattleshipEcsBoard.DefaultBoardSize;
        }
    }
}