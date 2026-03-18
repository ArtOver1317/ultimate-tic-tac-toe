#nullable enable
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.Startup
{
    internal sealed class GameplayStartupOnlineMoveHandler
    {
        private readonly GameplayStartupDependencies _dependencies;
        private readonly GameplayStartupRuntimeState _state;

        private GameplayStartupCoreServices Core => _dependencies.Core;
        private GameplayStartupOnlineServices Online => _dependencies.Online;
        private GameplayStartupBattleshipServices Battleship => _dependencies.Battleship;
        private GameplayStartupUiState UiState => _state.Ui;
        private GameplayStartupOnlineState OnlineState => _state.Online;
        private GameplayStartupMatchState MatchState => _state.Match;
        private GameplayStartupBattleshipState BattleshipState => _state.Battleship;

        public GameplayStartupOnlineMoveHandler(GameplayStartupDependencies dependencies, GameplayStartupRuntimeState state)
        {
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal void HandleIncomingOnlineTimeoutSignal(OnlineTimeoutSignal signal)
        {
            if (!CanHandleIncomingTimeoutSignal())
                return;

            if (BattleshipState.IsBattleshipMatch && !CanApplyBattleshipTimeout())
                return;

            Online.MatchStateProvider!.SubmitCommand(new TimeoutCommand(signal.LoserSlot));
        }

        internal void HandleIncomingOnlineMove(MoveCommand move)
        {
            if (!CanHandleIncomingMove())
                return;

            if (!TryResolveIncomingCell(move, out var cellId, out var minorCount))
                return;

            if (!TryValidateIncomingMove(move, cellId, minorCount))
                return;

            Online.MatchStateProvider!.SubmitCommand(new MakeMoveCommand(cellId));
            ForwardAuthoritativeMoveIfNeeded(move);
        }

        private bool CanHandleIncomingTimeoutSignal() =>
            !MatchState.Disposed
            && Core.EcsLifecycle.IsActive
            && !OnlineState.OnlineIsHost
            && Online.MatchStateProvider != null;

        private bool CanApplyBattleshipTimeout() =>
            Battleship.BattleshipSnapshotProvider is { Phase: BattleshipPhase.Battle };

        private bool CanHandleIncomingMove() =>
            !MatchState.Disposed
            && Core.EcsLifecycle.IsActive
            && Online.MatchStateProvider != null
            && UiState.FieldSpec != null;

        private bool TryResolveIncomingCell(MoveCommand move, out CellId cellId, out int minorCount)
        {
            minorCount = OnlineMoveIndexCodec.ResolveMinorCount(UiState.FieldSpec!);

            try
            {
                cellId = OnlineMoveIndexCodec.ToCellId(move.CellIndex, minorCount);
                return true;
            }
            catch (Exception)
            {
                cellId = default;
                return false;
            }
        }

        private bool TryValidateIncomingMove(MoveCommand move, CellId cellId, int minorCount)
        {
            if (OnlineState.UseHostAuthoritativeFilter && !BattleshipState.IsBattleshipMatch)
                return TryValidateIncomingHostMove(move);

            if (BattleshipState.IsBattleshipMatch && OnlineState.UseHostAuthoritativeFilter)
                return TryValidateIncomingBattleshipShot(move, cellId, minorCount);

            return true;
        }

        private void ForwardAuthoritativeMoveIfNeeded(MoveCommand proposal)
        {
            if (OnlineState.OnlineIsHost)
                ForwardAuthoritativeHostMoveAsync(proposal).Forget();
        }

        private async UniTaskVoid ForwardAuthoritativeHostMoveAsync(MoveCommand proposal)
        {
            if (MatchState.Disposed || !OnlineState.OnlineIsHost || string.IsNullOrWhiteSpace(OnlineState.OnlineLocalUserId))
                return;

            var authoritativeMove = new MoveCommand(
                Guid.NewGuid(),
                OnlineState.OnlineLocalUserId,
                proposal.CellIndex,
                BattleshipState.IsBattleshipMatch ? proposal.ClientTick : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            try
            {
                await Online.NetworkBridge.SubmitMoveAsync(authoritativeMove);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Failed to forward authoritative move: {ex.Message}");
            }
        }

        private bool TryValidateIncomingHostMove(MoveCommand move)
        {
            if (string.IsNullOrWhiteSpace(OnlineState.OnlineLocalUserId) || Online.MatchStateProvider == null || UiState.FieldSpec == null)
                return false;

            if (!TryResolveRemoteUserId(move.SenderUserId, out var remoteUserId))
                return false;

            var cells = Online.MatchStateProvider.GetAllCells();
            
            if (cells.Count == 0)
                return false;

            if (!TryResolveAuthoritativeUsers(remoteUserId, out var activeUserId, out var nextUserId))
                return false;

            if (!TryBuildAuthoritativeState(cells, activeUserId, OnlineState.OnlineRoundFinished, out var state))
                return false;

            var result = Online.HostMoveProcessor.Process(move, state, nextUserId);
            return result.Status == MoveProcessStatus.Accepted;
        }

        private bool TryResolveRemoteUserId(string senderUserId, out string remoteUserId)
        {
            if (string.IsNullOrWhiteSpace(OnlineState.OnlineRemoteUserId))
                OnlineState.OnlineRemoteUserId = senderUserId;

            remoteUserId = OnlineState.OnlineRemoteUserId ?? string.Empty;
            
            return !string.IsNullOrWhiteSpace(remoteUserId)
                   && string.Equals(senderUserId, remoteUserId, StringComparison.Ordinal);
        }

        private bool TryResolveAuthoritativeUsers(string remoteUserId, out string activeUserId, out string nextUserId)
        {
            activeUserId = Online.MatchStateProvider!.ActivePlayerSlot == 0
                ? OnlineState.OnlineLocalUserId ?? string.Empty
                : remoteUserId;

            nextUserId = Online.MatchStateProvider.ActivePlayerSlot == 0
                ? remoteUserId
                : OnlineState.OnlineLocalUserId ?? string.Empty;

            return !string.IsNullOrWhiteSpace(activeUserId)
                   && !string.IsNullOrWhiteSpace(nextUserId);
        }

        private bool TryBuildAuthoritativeState(
            IReadOnlyList<CellSnapshot> cells,
            string activeUserId,
            bool isRoundCompleted,
            out AuthoritativeMatchState state)
        {
            try
            {
                state = BuildAuthoritativeState(cells, activeUserId, isRoundCompleted);
                return true;
            }
            catch
            {
                state = null!;
                return false;
            }
        }

        private bool TryValidateIncomingBattleshipShot(MoveCommand move, CellId cellId, int minorCount)
        {
            if (!CanValidateBattleshipShot())
                return false;

            if (!TryResolveRemoteUserId(move.SenderUserId, out _))
                return false;

            const int shooterSlot = PlayerSlotMapping.SlotO;
            
            if (Online.MatchStateProvider!.ActivePlayerSlot != shooterSlot)
                return false;

            return TryResolveBattleshipTarget(cellId, minorCount, shooterSlot, out _) 
                   && TryAcceptShotSequence(move.ClientTick);
        }

        private bool CanValidateBattleshipShot() =>
            OnlineState.OnlineIsHost
            && Battleship.BattleshipSnapshotProvider != null
            && Online.MatchStateProvider != null
            && Battleship.BattleshipSnapshotProvider.Phase == BattleshipPhase.Battle;

        private bool TryResolveBattleshipTarget(CellId cellId, int minorCount, int shooterSlot, out int cellIndex)
        {
            cellIndex = OnlineMoveIndexCodec.ToCellIndex(cellId, minorCount);
            var marks = Battleship.BattleshipSnapshotProvider!.GetOpponentMarks(shooterSlot);

            if (cellIndex < 0 || cellIndex >= marks.Count)
                return false;

            return marks[cellIndex] == BattleshipCellMark.Unknown;
        }

        private bool TryAcceptShotSequence(long sequence)
        {
            if (sequence <= 0)
                return false;

            var observedSequence = Online.NetworkBridge.Snapshot.CurrentValue?.ShotSequence ?? OnlineState.OnlineAcceptedShotSequence;
            
            if (observedSequence < OnlineState.OnlineAcceptedShotSequence)
                observedSequence = OnlineState.OnlineAcceptedShotSequence;

            var expectedSequence = observedSequence + 1;
            
            if (sequence != expectedSequence)
                return false;

            OnlineState.OnlineAcceptedShotSequence = sequence;
            return true;
        }

        private AuthoritativeMatchState BuildAuthoritativeState(
            IReadOnlyList<CellSnapshot> cells,
            string activeUserId,
            bool isRoundCompleted)
        {
            if (UiState.FieldSpec == null)
                throw new InvalidOperationException("FieldRenderSpec is not initialized.");

            var minorCount = OnlineMoveIndexCodec.ResolveMinorCount(UiState.FieldSpec);
            var cellsCount = ResolveCellsCount(UiState.FieldSpec);
            var state = new AuthoritativeMatchState(cellsCount, activeUserId);

            MarkOccupiedCells(cells, minorCount, cellsCount, state);

            if (isRoundCompleted)
                state.Complete();

            return state;
        }

        private static int ResolveCellsCount(FieldRenderSpec fieldSpec) =>
            fieldSpec.Kind == FieldKind.Classic
                ? fieldSpec.OuterSize * fieldSpec.OuterSize
                : fieldSpec.OuterSize * fieldSpec.OuterSize * fieldSpec.InnerSize * fieldSpec.InnerSize;

        private static void MarkOccupiedCells(
            IReadOnlyList<CellSnapshot> cells,
            int minorCount,
            int cellsCount,
            AuthoritativeMatchState state)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                var snapshot = cells[i];
                
                if (snapshot.Slot < 0)
                    continue;

                var index = OnlineMoveIndexCodec.ToCellIndex(snapshot.CellId, minorCount);
                
                if (index < 0 || index >= cellsCount)
                    continue;

                state.MarkCellOccupied(index);
            }
        }
    }
}