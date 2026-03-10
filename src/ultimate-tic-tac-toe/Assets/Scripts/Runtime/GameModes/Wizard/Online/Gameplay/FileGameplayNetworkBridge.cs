#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.GameModes.Wizard.Online
{
    public sealed class NoOpGameplayNetworkBridge : IGameplayNetworkBridge
    {
        private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
        private readonly Subject<MoveCommand> _incomingMoves = new();
        private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
        private readonly Subject<OnlineTimeoutSignal> _incomingTimeoutSignals = new();

        public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
        public Observable<MoveCommand> IncomingMoves => _incomingMoves;
        public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;
        public Observable<OnlineTimeoutSignal> IncomingTimeoutSignals => _incomingTimeoutSignals;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
        public UniTask UnbindAsync() => UniTask.CompletedTask;
        public UniTask SubmitMoveAsync(MoveCommand command) => UniTask.CompletedTask;
        public UniTask SubmitRoundReadyAsync(RoundReadySignal signal) => UniTask.CompletedTask;
        public UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal) => UniTask.CompletedTask;

        public void Dispose()
        {
            _snapshot.Dispose();
            _incomingMoves.Dispose();
            _incomingRoundReadySignals.Dispose();
            _incomingTimeoutSignals.Dispose();
        }
    }

    public sealed class FileGameplayNetworkBridge : IGameplayNetworkBridge
    {
        private const int _seenCommandsWindowSize = 256;

        private readonly IOnlineGameplaySessionContextStore _contextStore;
        private readonly IPhotonSessionTransport _transport;
        private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
        private readonly Subject<MoveCommand> _incomingMoves = new();
        private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
        private readonly Subject<OnlineTimeoutSignal> _incomingTimeoutSignals = new();
        private readonly HashSet<Guid> _seenCommands = new();
        private readonly Queue<Guid> _seenCommandOrder = new();

        private string? _localUserId;
        private bool _isBound;
        private bool _isHost;
        private bool _isDisposed;
        private ulong _authoritativeTick;
        private int _currentMatchRoundId = 1;
        private long _shotSequence;

        public FileGameplayNetworkBridge(IOnlineGameplaySessionContextStore contextStore, IPhotonSessionTransport transport)
        {
            _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
        public Observable<MoveCommand> IncomingMoves => _incomingMoves;
        public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;
        public Observable<OnlineTimeoutSignal> IncomingTimeoutSignals => _incomingTimeoutSignals;

        public async UniTask BindAsync(string localUserId, bool isHost)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            await UnbindAsync();

            if (!CanBindToCurrentSession())
                return;

            _localUserId = localUserId;
            ResetRuntimeState();
            _isHost = isHost;
            _isBound = true;
            _transport.ReliableDataReceived += OnReliableDataReceived;
        }

        public UniTask UnbindAsync()
        {
            UnsubscribeFromTransportIfNeeded();
            ResetBindingState();
            return UniTask.CompletedTask;
        }

        public async UniTask SubmitMoveAsync(MoveCommand command)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (!_isBound)
                return;

            var payload = GameplayNetworkBridgePayloadCodec.SerializeMove(command);
            await _transport.SendReliableDataAsync(payload);

            RememberCommandId(command.CommandId);
            // Sequence advances only on authoritative path:
            // host submits accepted moves directly; guest waits for host echo.
            UpdateSnapshot(command.ClientTick, updateShotSequence: _isHost);
        }

        public async UniTask SubmitRoundReadyAsync(RoundReadySignal signal)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (!_isBound)
                return;

            _currentMatchRoundId = signal.MatchRoundId;
            _shotSequence = 0;
            var payload = GameplayNetworkBridgePayloadCodec.SerializeRoundReady(signal);
            await _transport.SendReliableDataAsync(payload);
            UpdateSnapshot(0);
        }

        public async UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileGameplayNetworkBridge));

            if (!_isBound)
                return;

            var payload = GameplayNetworkBridgePayloadCodec.SerializeTimeout(signal);
            await _transport.SendReliableDataAsync(payload);
            UpdateSnapshot(signal.ClientTick);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            UnsubscribeFromTransportIfNeeded();
            ResetBindingState();

            _snapshot.Dispose();
            _incomingMoves.Dispose();
            _incomingRoundReadySignals.Dispose();
            _incomingTimeoutSignals.Dispose();
        }

        private void UpdateSnapshot(long tick, bool updateShotSequence = false)
        {
            _authoritativeTick++;
            
            if (updateShotSequence)
                TryUpdateShotSequence(tick);

            var targetTick = tick > 0 ? tick : (long)_authoritativeTick;
            
            _snapshot.Value = new GameplayNetworkSnapshot(
                _currentMatchRoundId,
                isCompleted: false,
                winnerUserId: null,
                authoritativeTick: (long)_authoritativeTick,
                countdownTargetTick: targetTick,
                shotSequence: _shotSequence);
        }

        private void TryUpdateShotSequence(long sequence)
        {
            if (sequence <= 0)
                return;

            if (_shotSequence == 0)
            {
                if (sequence == 1)
                    _shotSequence = 1;

                return;
            }

            if (sequence == _shotSequence + 1)
                _shotSequence = sequence;
        }

        private void OnReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (!_isBound || evt.Payload.Length == 0)
                return;

            if (GameplayNetworkBridgePayloadCodec.TryDeserializeMove(evt.Payload, out var move))
            {
                if (string.Equals(move.SenderUserId, _localUserId, StringComparison.Ordinal))
                    return;

                if (!RememberCommandId(move.CommandId))
                    return;

                // Remote move updates sequence only for guest clients (authoritative host stream).
                // Host receives guest proposals here; sequence advances after host accepts and re-broadcasts.
                UpdateSnapshot(move.ClientTick, updateShotSequence: !_isHost);
                _incomingMoves.OnNext(move);
                return;
            }

            if (GameplayNetworkBridgePayloadCodec.TryDeserializeTimeout(evt.Payload, out var timeoutSignal))
            {
                if (string.Equals(timeoutSignal.SenderUserId, _localUserId, StringComparison.Ordinal))
                    return;

                UpdateSnapshot(timeoutSignal.ClientTick);
                _incomingTimeoutSignals.OnNext(timeoutSignal);
                return;
            }

            if (!GameplayNetworkBridgePayloadCodec.TryDeserializeRoundReady(evt.Payload, out var signal))
                return;

            if (string.Equals(signal.SenderUserId, _localUserId, StringComparison.Ordinal))
                return;

            _currentMatchRoundId = signal.MatchRoundId;
            _shotSequence = 0;
            UpdateSnapshot(0);
            _incomingRoundReadySignals.OnNext(signal);
        }

        private bool RememberCommandId(Guid commandId)
        {
            if (!_seenCommands.Add(commandId))
                return false;

            _seenCommandOrder.Enqueue(commandId);
            
            while (_seenCommandOrder.Count > _seenCommandsWindowSize)
            {
                var oldest = _seenCommandOrder.Dequeue();
                _seenCommands.Remove(oldest);
            }

            return true;
        }

        private bool CanBindToCurrentSession()
        {
            var session = _contextStore.Snapshot;
            return session.IsOnlineDirectInvite && !string.IsNullOrWhiteSpace(session.SessionId);
        }

        private void ResetRuntimeState()
        {
            _seenCommands.Clear();
            _seenCommandOrder.Clear();
            _authoritativeTick = 0;
            _currentMatchRoundId = 1;
            _shotSequence = 0;
            _snapshot.Value = null;
        }

        private void ResetBindingState()
        {
            _isBound = false;
            _localUserId = null;
            _isHost = false;
            ResetRuntimeState();
        }

        private void UnsubscribeFromTransportIfNeeded()
        {
            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;
        }
    }

    internal static class GameplayNetworkBridgePayloadCodec
    {
        private const string _movePrefix = "M";
        private const string _roundReadyPrefix = "R";
        private const string _timeoutPrefix = "X";

        public static byte[] SerializeMove(MoveCommand command)
        {
            var line = string.Concat(
                _movePrefix, "|",
                command.CommandId.ToString("N"), "|",
                Sanitize(command.SenderUserId), "|",
                command.CellIndex.ToString(), "|",
                command.ClientTick.ToString());

            return Encoding.UTF8.GetBytes(line);
        }

        public static byte[] SerializeRoundReady(RoundReadySignal signal)
        {
            var line = string.Concat(
                _roundReadyPrefix, "|",
                Sanitize(signal.SenderUserId), "|",
                signal.IsReady ? "1" : "0", "|",
                signal.MatchRoundId.ToString(), "|",
                signal.ClientTick.ToString());

            return Encoding.UTF8.GetBytes(line);
        }

        public static byte[] SerializeTimeout(OnlineTimeoutSignal signal)
        {
            var line = string.Concat(
                _timeoutPrefix, "|",
                Sanitize(signal.SenderUserId), "|",
                signal.LoserSlot.ToString(), "|",
                signal.ClientTick.ToString());

            return Encoding.UTF8.GetBytes(line);
        }

        public static bool TryDeserializeMove(byte[] payload, out MoveCommand command)
        {
            command = default;

            var line = Encoding.UTF8.GetString(payload);
            
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            var moveOffset = parts.Length == 5 && parts[0] == _movePrefix ? 1 : 0;
            
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

        public static bool TryDeserializeRoundReady(byte[] payload, out RoundReadySignal signal)
        {
            signal = default;

            var line = Encoding.UTF8.GetString(payload);
            
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            
            if (parts.Length != 5 || parts[0] != _roundReadyPrefix)
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

        public static bool TryDeserializeTimeout(byte[] payload, out OnlineTimeoutSignal signal)
        {
            signal = default;

            var line = Encoding.UTF8.GetString(payload);
            
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            
            if (parts.Length != 4 || parts[0] != _timeoutPrefix)
                return false;

            var sender = parts[1];
            
            if (string.IsNullOrWhiteSpace(sender))
                return false;

            if (!int.TryParse(parts[2], out var loserSlot) || loserSlot < 0)
                return false;

            if (!long.TryParse(parts[3], out var clientTick))
                clientTick = 0;

            signal = new OnlineTimeoutSignal(sender, loserSlot, clientTick);
            return true;
        }

        private static string Sanitize(string value) => value.Replace("|", string.Empty);
    }
}