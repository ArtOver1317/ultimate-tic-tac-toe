#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.GameModes.Wizard
{
    public sealed class NoOpGameplayNetworkBridge : IGameplayNetworkBridge
    {
        private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
        private readonly Subject<MoveCommand> _incomingMoves = new();
        private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();

        public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
        public Observable<MoveCommand> IncomingMoves => _incomingMoves;
        public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
        public UniTask UnbindAsync() => UniTask.CompletedTask;
        public UniTask SubmitMoveAsync(MoveCommand command) => UniTask.CompletedTask;
        public UniTask SubmitRoundReadyAsync(RoundReadySignal signal) => UniTask.CompletedTask;

        public void Dispose()
        {
            _snapshot.Dispose();
            _incomingMoves.Dispose();
            _incomingRoundReadySignals.Dispose();
        }
    }

    public sealed class FileGameplayNetworkBridge : IGameplayNetworkBridge
    {
        private const int SeenCommandsWindowSize = 256;

        private readonly IOnlineGameplaySessionContextStore _contextStore;
        private readonly IPhotonSessionTransport _transport;
        private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
        private readonly Subject<MoveCommand> _incomingMoves = new();
        private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
        private readonly HashSet<Guid> _seenCommands = new();
        private readonly Queue<Guid> _seenCommandOrder = new();

        private string? _localUserId;
        private bool _isBound;
        private bool _isDisposed;
        private ulong _authoritativeTick;
        private int _currentMatchRoundId = 1;

        public FileGameplayNetworkBridge(IOnlineGameplaySessionContextStore contextStore, IPhotonSessionTransport transport)
        {
            _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
        public Observable<MoveCommand> IncomingMoves => _incomingMoves;
        public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;

        public async UniTask BindAsync(string localUserId, bool isHost)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            await UnbindAsync();

            var session = _contextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.SessionId))
                return;

            _localUserId = localUserId;
            _seenCommands.Clear();
            _seenCommandOrder.Clear();
            _authoritativeTick = 0;
            _currentMatchRoundId = 1;
            _isBound = true;
            _transport.ReliableDataReceived += OnReliableDataReceived;
        }

        public async UniTask UnbindAsync()
        {
            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;

            _isBound = false;
            _localUserId = null;
            _seenCommands.Clear();
            _seenCommandOrder.Clear();
            _currentMatchRoundId = 1;
            await UniTask.CompletedTask;
        }

        public async UniTask SubmitMoveAsync(MoveCommand command)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (!_isBound)
                return;

            var payload = SerializeMove(command);
            await _transport.SendReliableDataAsync(payload);

            RememberCommandId(command.CommandId);
            UpdateSnapshot(command.ClientTick);
        }

        public async UniTask SubmitRoundReadyAsync(RoundReadySignal signal)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (!_isBound)
                return;

            _currentMatchRoundId = signal.MatchRoundId;
            var payload = SerializeRoundReady(signal);
            await _transport.SendReliableDataAsync(payload);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            UnbindAsync().Forget();
            _snapshot.Dispose();
            _incomingMoves.Dispose();
            _incomingRoundReadySignals.Dispose();
        }

        private void UpdateSnapshot(long tick)
        {
            _authoritativeTick++;
            var targetTick = tick > 0 ? tick : (long)_authoritativeTick;
            _snapshot.Value = new GameplayNetworkSnapshot(_currentMatchRoundId, isCompleted: false, winnerUserId: null, authoritativeTick: (long)_authoritativeTick, countdownTargetTick: targetTick);
        }

        private void OnReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (!_isBound || evt.Payload == null || evt.Payload.Length == 0)
                return;

            if (TryDeserializeMove(evt.Payload, out var move))
            {
                if (string.Equals(move.SenderUserId, _localUserId, StringComparison.Ordinal))
                    return;

                if (!RememberCommandId(move.CommandId))
                    return;

                UpdateSnapshot(move.ClientTick);
                _incomingMoves.OnNext(move);
                return;
            }

            if (!TryDeserializeRoundReady(evt.Payload, out var signal))
                return;

            if (string.Equals(signal.SenderUserId, _localUserId, StringComparison.Ordinal))
                return;

            _currentMatchRoundId = signal.MatchRoundId;
            _incomingRoundReadySignals.OnNext(signal);
        }

        private bool RememberCommandId(Guid commandId)
        {
            if (!_seenCommands.Add(commandId))
                return false;

            _seenCommandOrder.Enqueue(commandId);
            while (_seenCommandOrder.Count > SeenCommandsWindowSize)
            {
                var oldest = _seenCommandOrder.Dequeue();
                _seenCommands.Remove(oldest);
            }

            return true;
        }

        private static byte[] SerializeMove(MoveCommand command)
        {
            var line = string.Concat(
                "M|",
                command.CommandId.ToString("N"), "|",
                command.SenderUserId.Replace("|", string.Empty), "|",
                command.CellIndex.ToString(), "|",
                command.ClientTick.ToString());

            return Encoding.UTF8.GetBytes(line);
        }

        private static byte[] SerializeRoundReady(RoundReadySignal signal)
        {
            var line = string.Concat(
                "R|",
                signal.SenderUserId.Replace("|", string.Empty), "|",
                signal.IsReady ? "1" : "0", "|",
                signal.MatchRoundId.ToString(), "|",
                signal.ClientTick.ToString());

            return Encoding.UTF8.GetBytes(line);
        }

        private static bool TryDeserializeMove(byte[] payload, out MoveCommand command)
        {
            command = default;

            var line = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            var moveOffset = parts.Length == 5 && parts[0] == "M" ? 1 : 0;
            if (parts.Length - moveOffset != 4)
                return false;

            if (!Guid.TryParse(parts[moveOffset], out var commandId) || commandId == Guid.Empty)
                return false;

            var sender = parts[moveOffset + 1];
            if (string.IsNullOrWhiteSpace(sender))
                return false;

            if (!int.TryParse(parts[moveOffset + 2], out var cellIndex) || cellIndex < 0)
                return false;

            if (!long.TryParse(parts[moveOffset + 3], out var clientTick))
                clientTick = 0;

            command = new MoveCommand(commandId, sender, cellIndex, clientTick);
            return true;
        }

        private static bool TryDeserializeRoundReady(byte[] payload, out RoundReadySignal signal)
        {
            signal = default;

            var line = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 5 || parts[0] != "R")
                return false;

            var sender = parts[1];
            if (string.IsNullOrWhiteSpace(sender))
                return false;

            var isReady = parts[2] == "1";

            if (!int.TryParse(parts[3], out var matchRoundId) || matchRoundId < 1)
                return false;

            if (!long.TryParse(parts[4], out var clientTick))
                clientTick = 0;

            signal = new RoundReadySignal(sender, isReady, matchRoundId, clientTick);
            return true;
        }
    }
}

#nullable restore
