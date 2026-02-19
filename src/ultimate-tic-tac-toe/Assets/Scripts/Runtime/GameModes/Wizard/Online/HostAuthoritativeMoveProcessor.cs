#nullable enable

using System;
using System.Collections.Generic;

namespace Runtime.GameModes.Wizard
{
    public enum MoveProcessStatus
    {
        Accepted,
        Rejected,
        DuplicateIgnored,
    }

    public enum MoveRejectReason
    {
        None,
        NotPlayerTurn,
        InvalidCell,
        CellAlreadyOccupied,
        MatchAlreadyFinished,
    }

    public readonly struct MoveProcessResult
    {
        public MoveProcessStatus Status { get; }
        public MoveRejectReason RejectReason { get; }

        public MoveProcessResult(MoveProcessStatus status, MoveRejectReason rejectReason)
        {
            Status = status;
            RejectReason = rejectReason;
        }
    }

    public sealed class AuthoritativeMatchState
    {
        private readonly bool[] _occupiedCells;

        public string ActivePlayerUserId { get; private set; }
        public bool IsCompleted { get; private set; }

        public AuthoritativeMatchState(int cellsCount, string firstPlayerUserId)
        {
            if (cellsCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellsCount), cellsCount, "Value must be positive.");

            if (string.IsNullOrWhiteSpace(firstPlayerUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(firstPlayerUserId));

            _occupiedCells = new bool[cellsCount];
            ActivePlayerUserId = firstPlayerUserId;
            IsCompleted = false;
        }

        public bool IsCellOccupied(int cellIndex) => _occupiedCells[cellIndex];

        public int CellsCount => _occupiedCells.Length;

        public void MarkCellOccupied(int cellIndex) => _occupiedCells[cellIndex] = true;

        public void SetActivePlayer(string userId) => ActivePlayerUserId = userId;

        public void Complete() => IsCompleted = true;
    }

    public sealed class HostAuthoritativeMoveProcessor
    {
        private readonly int _dedupWindowSize;
        private readonly Dictionary<string, HashSet<Guid>> _seenByPlayer = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<Guid>> _orderByPlayer = new(StringComparer.Ordinal);

        public HostAuthoritativeMoveProcessor(int dedupWindowSize = 64)
        {
            if (dedupWindowSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(dedupWindowSize), dedupWindowSize, "Value must be positive.");

            _dedupWindowSize = dedupWindowSize;
        }

        public MoveProcessResult Process(
            MoveCommand command,
            AuthoritativeMatchState state,
            string nextPlayerUserId)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (string.IsNullOrWhiteSpace(nextPlayerUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(nextPlayerUserId));

            if (IsDuplicate(command))
                return new MoveProcessResult(MoveProcessStatus.DuplicateIgnored, MoveRejectReason.None);

            if (state.IsCompleted)
                return new MoveProcessResult(MoveProcessStatus.Rejected, MoveRejectReason.MatchAlreadyFinished);

            if (!string.Equals(command.SenderUserId, state.ActivePlayerUserId, StringComparison.Ordinal))
                return new MoveProcessResult(MoveProcessStatus.Rejected, MoveRejectReason.NotPlayerTurn);

            if (command.CellIndex < 0 || command.CellIndex >= state.CellsCount)
                return new MoveProcessResult(MoveProcessStatus.Rejected, MoveRejectReason.InvalidCell);

            if (state.IsCellOccupied(command.CellIndex))
                return new MoveProcessResult(MoveProcessStatus.Rejected, MoveRejectReason.CellAlreadyOccupied);

            state.MarkCellOccupied(command.CellIndex);
            state.SetActivePlayer(nextPlayerUserId);
            Remember(command);

            return new MoveProcessResult(MoveProcessStatus.Accepted, MoveRejectReason.None);
        }

        private bool IsDuplicate(MoveCommand command)
        {
            if (!_seenByPlayer.TryGetValue(command.SenderUserId, out var seen))
                return false;

            return seen.Contains(command.CommandId);
        }

        private void Remember(MoveCommand command)
        {
            if (!_seenByPlayer.TryGetValue(command.SenderUserId, out var seen))
            {
                seen = new HashSet<Guid>();
                _seenByPlayer[command.SenderUserId] = seen;
            }

            if (!_orderByPlayer.TryGetValue(command.SenderUserId, out var order))
            {
                order = new Queue<Guid>();
                _orderByPlayer[command.SenderUserId] = order;
            }

            if (!seen.Add(command.CommandId))
                return;

            order.Enqueue(command.CommandId);

            while (order.Count > _dedupWindowSize)
            {
                var oldest = order.Dequeue();
                seen.Remove(oldest);
            }
        }
    }
}

#nullable restore