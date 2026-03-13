#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Games.Battleship.Networking
{
    public sealed class BattleshipOnlineCommandSink : IGameplayCommandSink, IDisposable
    {
        private readonly IMatchStateProvider _localCommandSink;
        private readonly IGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayNetworkBridge _networkBridge;
        private readonly IBattleshipNetworkBridge _battleshipNetworkBridge;
        private readonly IBattleshipLayoutSerializer _layoutSerializer;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly BattleshipOnlinePlacementMessageHandler _incomingPlacementHandler;
        private readonly CompositeDisposable _subscriptions = new();

        private bool _disposed;

        public BattleshipOnlineCommandSink(
            IMatchStateProvider localCommandSink,
            IGameplaySnapshotProvider snapshotProvider,
            IGameplayNetworkBridge networkBridge,
            IBattleshipNetworkBridge battleshipNetworkBridge,
            IBattleshipLayoutSerializer layoutSerializer,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _localCommandSink = localCommandSink ?? throw new ArgumentNullException(nameof(localCommandSink));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));
            _battleshipNetworkBridge = battleshipNetworkBridge ?? throw new ArgumentNullException(nameof(battleshipNetworkBridge));
            _layoutSerializer = layoutSerializer ?? throw new ArgumentNullException(nameof(layoutSerializer));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _incomingPlacementHandler = new BattleshipOnlinePlacementMessageHandler(_localCommandSink, _layoutSerializer, _sessionContextStore);

            _battleshipNetworkBridge.IncomingPlacements
                .Subscribe(_incomingPlacementHandler.ApplyIncomingPlacement)
                .AddTo(_subscriptions);

            _battleshipNetworkBridge.IncomingPlacementTimeouts
                .Subscribe(_incomingPlacementHandler.ApplyIncomingPlacementTimeout)
                .AddTo(_subscriptions);
        }

        public void SubmitCommand(IGameplayCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var session = _sessionContextStore.Snapshot;
            
            if (!CanSubmitOnline(session))
            {
                _localCommandSink.SubmitCommand(command);
                return;
            }

            if (TrySubmitOnlineCommand(session, command))
                return;

            _localCommandSink.SubmitCommand(command);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscriptions.Dispose();
            _incomingPlacementHandler.Reset();
        }

        private static bool CanSubmitOnline(in OnlineGameplaySessionSnapshot session) =>
            session.IsOnlineDirectInvite && !string.IsNullOrWhiteSpace(session.LocalUserId);

        private bool TrySubmitOnlineCommand(in OnlineGameplaySessionSnapshot session, IGameplayCommand command)
        {
            switch (command)
            {
                case SubmitPlacementCommand submitPlacement:
                    SubmitPlacementCommand(session, submitPlacement);
                    return true;

                case PlacementTimeoutCommand placementTimeout:
                    SubmitPlacementTimeoutCommand(session, placementTimeout);
                    return true;

                case TimeoutCommand timeout:
                    SubmitTimeoutCommand(session, timeout);
                    return true;

                case MakeMoveCommand move:
                    SubmitMoveCommand(session, move);
                    return true;

                default:
                    return false;
            }
        }

        private void SubmitPlacementCommand(in OnlineGameplaySessionSnapshot session, SubmitPlacementCommand command)
        {
            _localCommandSink.SubmitCommand(command);

            string payload;
            
            try
            {
                payload = _layoutSerializer.Serialize(command.Layout);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to serialize placement: {ex.Message}");
                return;
            }

            SubmitPlacementAsync(new BattleshipPlacementMessage(
                Guid.NewGuid(),
                session.LocalUserId!,
                payload,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
        }

        private void SubmitPlacementTimeoutCommand(in OnlineGameplaySessionSnapshot session, PlacementTimeoutCommand command)
        {
            if (!session.IsHost)
                return;

            _localCommandSink.SubmitCommand(command);

            SubmitPlacementTimeoutAsync(new BattleshipPlacementTimeoutMessage(
                Guid.NewGuid(),
                session.LocalUserId!,
                command.PlayerSlot,
                command.AutoPlaceSeed,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
        }

        private void SubmitTimeoutCommand(in OnlineGameplaySessionSnapshot session, TimeoutCommand command)
        {
            if (!session.IsHost)
                return;

            _localCommandSink.SubmitCommand(command);
            
            SubmitOnlineTimeoutAsync(new OnlineTimeoutSignal(
                session.LocalUserId!,
                command.LoserSlot,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
        }

        private void SubmitMoveCommand(in OnlineGameplaySessionSnapshot session, MakeMoveCommand command)
        {
            if (!CanSubmitMove(session))
                return;

            if (session.IsHost)
                _localCommandSink.SubmitCommand(command);

            if (!TryBuildOnlineMove(session, command, out var onlineMove))
                return;

            SubmitOnlineMoveAsync(onlineMove).Forget();
        }

        private bool CanSubmitMove(in OnlineGameplaySessionSnapshot session)
        {
            var localPlayerSlot = session.IsHost ? PlayerSlotMapping.SlotX : PlayerSlotMapping.SlotO;
            return _localCommandSink.ActivePlayerSlot == localPlayerSlot;
        }

        private bool TryBuildOnlineMove(
            in OnlineGameplaySessionSnapshot session,
            MakeMoveCommand command,
            out MoveCommand onlineMove)
        {
            onlineMove = default;

            try
            {
                var cellIndex = OnlineMoveIndexCodec.ToCellIndex(command.CellId, ResolveMinorCount(_snapshotProvider.GetAllCells()));
                onlineMove = new MoveCommand(Guid.NewGuid(), session.LocalUserId!, cellIndex, ResolveNextShotSequence());
                return true;
            }
            catch
            {
                Log.Error(LogTags.Infrastructure, "[BattleshipOnlineCommandSink] Failed to encode online move.");
                return false;
            }
        }

        private static int ResolveMinorCount(IReadOnlyList<CellSnapshot>? cells)
        {
            if (cells == null || cells.Count == 0)
                return 10;

            var maxMinor = 0;
            
            for (var i = 0; i < cells.Count; i++)
            {
                var minor = cells[i].CellId.Minor;
                
                if (minor > maxMinor)
                    maxMinor = minor;
            }

            return maxMinor + 1;
        }

        private long ResolveNextShotSequence()
        {
            var snapshot = _networkBridge.Snapshot.CurrentValue;
            var currentSequence = snapshot?.ShotSequence ?? 0;
            
            if (currentSequence < 0)
                currentSequence = 0;

            return currentSequence + 1;
        }

        private async UniTaskVoid SubmitOnlineMoveAsync(MoveCommand move)
        {
            try
            {
                await _networkBridge.SubmitMoveAsync(move);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit online move: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitOnlineTimeoutAsync(OnlineTimeoutSignal signal)
        {
            try
            {
                await _networkBridge.SubmitTimeoutAsync(signal);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit online timeout: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitPlacementAsync(BattleshipPlacementMessage message)
        {
            try
            {
                await _battleshipNetworkBridge.SubmitPlacementAsync(message);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit placement: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message)
        {
            try
            {
                await _battleshipNetworkBridge.SubmitPlacementTimeoutAsync(message);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit placement timeout: {ex.Message}");
            }
        }
    }
}