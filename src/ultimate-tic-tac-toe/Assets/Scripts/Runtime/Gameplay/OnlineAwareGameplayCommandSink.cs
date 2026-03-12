#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay
{
    public sealed class OnlineAwareGameplayCommandSink : IGameplayCommandSink
    {
        private readonly IMatchStateProvider _localCommandSink;
        private readonly IGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayNetworkBridge _networkBridge;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;

        public OnlineAwareGameplayCommandSink(
            IMatchStateProvider localCommandSink,
            IGameplaySnapshotProvider snapshotProvider,
            IGameplayNetworkBridge networkBridge,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _localCommandSink = localCommandSink ?? throw new ArgumentNullException(nameof(localCommandSink));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
        }

        public void SubmitCommand(IGameplayCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.LocalUserId))
            {
                _localCommandSink.SubmitCommand(command);
                return;
            }

            if (command is TimeoutCommand timeout)
            {
                if (!session.IsHost)
                    return;

                _localCommandSink.SubmitCommand(command);
                SubmitOnlineTimeoutAsync(new OnlineTimeoutSignal(
                    session.LocalUserId,
                    timeout.LoserSlot,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
                return;
            }

            if (command is not MakeMoveCommand move)
            {
                _localCommandSink.SubmitCommand(command);
                return;
            }

            var localPlayerSlot = session.IsHost ? 0 : 1;
            if (_localCommandSink.ActivePlayerSlot != localPlayerSlot)
                return;

            if (session.IsHost)
                _localCommandSink.SubmitCommand(command);

            var cells = _snapshotProvider.GetAllCells();
            var minorCount = ResolveMinorCount(cells);

            MoveCommand onlineMove;
            try
            {
                var cellIndex = OnlineMoveIndexCodec.ToCellIndex(move.CellId, minorCount);
                onlineMove = new MoveCommand(Guid.NewGuid(), session.LocalUserId, cellIndex, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            catch
            {
                Log.Error(LogTags.Infrastructure, "[OnlineAwareGameplayCommandSink] Failed to encode online move.");
                return;
            }

            SubmitOnlineMoveAsync(onlineMove).Forget();
        }

        private static int ResolveMinorCount(System.Collections.Generic.IReadOnlyList<CellSnapshot> cells)
        {
            if (cells == null || cells.Count == 0)
                return 3;

            var maxMinor = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var minor = cells[i].CellId.Minor;
                if (minor > maxMinor)
                    maxMinor = minor;
            }

            return maxMinor + 1;
        }

        private async UniTaskVoid SubmitOnlineMoveAsync(MoveCommand move)
        {
            try
            {
                await _networkBridge.SubmitMoveAsync(move);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[OnlineAwareGameplayCommandSink] Failed to submit online move: {ex.Message}");
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
                Log.Error(LogTags.Infrastructure, $"[OnlineAwareGameplayCommandSink] Failed to submit online timeout: {ex.Message}");
            }
        }
    }
}

#nullable restore
