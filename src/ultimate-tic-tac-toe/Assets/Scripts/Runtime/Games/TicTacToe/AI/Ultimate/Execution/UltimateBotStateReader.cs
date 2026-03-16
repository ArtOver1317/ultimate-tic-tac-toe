#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    public sealed class UltimateBotStateReader : IUltimateBotStateReader
    {
        private readonly IGameplaySnapshotProvider _gameplaySnapshot;
        private readonly IUltimateGameplaySnapshotProvider _ultimateSnapshot;

        public UltimateBotStateReader(
            IGameplaySnapshotProvider gameplaySnapshot,
            IUltimateGameplaySnapshotProvider ultimateSnapshot)
        {
            _gameplaySnapshot = gameplaySnapshot ?? throw new ArgumentNullException(nameof(gameplaySnapshot));
            _ultimateSnapshot = ultimateSnapshot ?? throw new ArgumentNullException(nameof(ultimateSnapshot));
        }

        public bool TryBuildDecisionRequest(
            int botSlot,
            BotTurnId turnId,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng,
            out UltimateBotDecisionRequest request,
            out BotFailureReason? failReason)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            request = default;
            failReason = null;

            if (!TryResolveActiveTurn(botSlot, turnId, out var activePlayerSlot))
                return false;

            var allowedMajors = _ultimateSnapshot.CurrentAllowedMajors;
            var miniBoards = ReadMiniBoards();
            var cells = ReadCells();
            var legalMoves = UltimateBotBoardUtilities.BuildLegalMoves(cells, miniBoards, allowedMajors);

            if (legalMoves.Count == 0)
            {
                failReason = BotFailureReason.NoLegalMovesInconsistentState;
                return false;
            }

            request = BuildDecisionRequest(turnId, activePlayerSlot, profile, rng, cells, miniBoards, allowedMajors, legalMoves);
            return true;
        }

        private bool TryResolveActiveTurn(int botSlot, BotTurnId turnId, out int activePlayerSlot)
        {
            activePlayerSlot = _gameplaySnapshot.ActivePlayerSlot;

            if (_ultimateSnapshot.CurrentMatch.Status != GameStatus.InProgress)
                return false;

            if (activePlayerSlot != botSlot)
                return false;

            return turnId.ActivePlayerSlot == activePlayerSlot
                   && turnId.CommandSequenceBeforeTurn == _gameplaySnapshot.CommandSequence;
        }

        private MiniBoardStatus[] ReadMiniBoards()
        {
            var miniBoards = new MiniBoardStatus[UltimateBoardConstants.MajorCount];
            _ultimateSnapshot.CopyMiniBoardsTo(miniBoards);
            return miniBoards;
        }

        private PlayerMark[] ReadCells()
        {
            var cells = new PlayerMark[UltimateBoardConstants.CellCount];

            for (var major = 0; major < UltimateBoardConstants.MajorCount; major++)
            {
                for (var minor = 0; minor < UltimateBoardConstants.MinorCount; minor++)
                {
                    var cellId = new CellId(major, minor);
                    var index = major * UltimateBoardConstants.MinorCount + minor;
                    cells[index] = UltimateBotBoardUtilities.SlotToMark(_gameplaySnapshot.GetCellSlot(cellId));
                }
            }

            return cells;
        }

        private static UltimateBotDecisionRequest BuildDecisionRequest(
            BotTurnId turnId,
            int activePlayerSlot,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            IReadOnlyList<CellId> legalMoves)
        {
            var snapshot = new UltimateBoardSnapshot(
                cells,
                miniBoards,
                allowedMajors,
                activePlayerSlot);

            return new UltimateBotDecisionRequest(turnId, snapshot, legalMoves, profile, rng);
        }
    }
}