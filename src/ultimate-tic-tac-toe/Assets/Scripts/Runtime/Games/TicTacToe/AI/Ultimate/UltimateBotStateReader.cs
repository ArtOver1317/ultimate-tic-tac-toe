#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public sealed class UltimateBotStateReader : IUltimateBotStateReader
    {
        private const int MajorCount = 9;
        private const int MinorCount = 9;
        private const int CellCount = MajorCount * MinorCount;

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

            var match = _ultimateSnapshot.CurrentMatch;
            if (match.Status != GameStatus.InProgress)
            {
                return false;
            }

            var activePlayerSlot = _gameplaySnapshot.ActivePlayerSlot;
            if (activePlayerSlot != botSlot)
            {
                return false;
            }

            var commandSequence = _gameplaySnapshot.CommandSequence;
            if (turnId.ActivePlayerSlot != activePlayerSlot || turnId.CommandSequenceBeforeTurn != commandSequence)
            {
                return false;
            }

            var miniBoards = new MiniBoardStatus[MajorCount];
            _ultimateSnapshot.CopyMiniBoardsTo(miniBoards);

            var cells = new PlayerMark[CellCount];
            var legalMoves = new List<CellId>(CellCount);
            var allowedMajors = _ultimateSnapshot.CurrentAllowedMajors;

            for (var major = 0; major < MajorCount; major++)
            {
                for (var minor = 0; minor < MinorCount; minor++)
                {
                    var cellId = new CellId(major, minor);
                    var slot = _gameplaySnapshot.GetCellSlot(cellId);
                    cells[(major * MinorCount) + minor] = SlotToMark(slot);

                    if (slot >= 0)
                    {
                        continue;
                    }

                    if (!allowedMajors.ContainsMajor(major))
                    {
                        continue;
                    }

                    if (miniBoards[major] != MiniBoardStatus.InProgress)
                    {
                        continue;
                    }

                    legalMoves.Add(cellId);
                }
            }

            if (legalMoves.Count == 0)
            {
                failReason = BotFailureReason.NoLegalMovesInconsistentState;
                return false;
            }

            var lastMove = _gameplaySnapshot.LastMove;
            var snapshot = new UltimateBoardSnapshot(
                cells,
                miniBoards,
                allowedMajors,
                activePlayerSlot,
                lastMove ?? default,
                lastMove.HasValue,
                match.Status);

            request = new UltimateBotDecisionRequest(turnId, snapshot, legalMoves, profile, rng);
            return true;
        }

        private static PlayerMark SlotToMark(int slot)
        {
            return slot switch
            {
                0 => PlayerMark.X,
                1 => PlayerMark.O,
                _ => PlayerMark.None,
            };
        }
    }
}
