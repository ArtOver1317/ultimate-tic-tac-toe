#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Online;

namespace Runtime.Games.Battleship.Networking
{
    public sealed class PhotonBattleshipNetworkBridge : IBattleshipNetworkBridge
    {
        private readonly IOnlineGameplaySessionContextStore _contextStore;
        private readonly IPhotonSessionTransport _transport;
        private readonly Subject<BattleshipPlacementMessage> _incomingPlacements = new();
        private readonly Subject<BattleshipPlacementTimeoutMessage> _incomingPlacementTimeouts = new();
        private readonly Subject<BattleshipRecoveryMessage> _incomingRecoverySnapshots = new();

        private string? _localUserId;
        private bool _isBound;
        private bool _disposed;

        public PhotonBattleshipNetworkBridge(IOnlineGameplaySessionContextStore contextStore, IPhotonSessionTransport transport)
        {
            _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public Observable<BattleshipPlacementMessage> IncomingPlacements => _incomingPlacements;
        public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => _incomingPlacementTimeouts;
        public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => _incomingRecoverySnapshots;

        public async UniTask BindAsync(string localUserId, bool isHost)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhotonBattleshipNetworkBridge));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            await UnbindAsync();

            var session = _contextStore.Snapshot;
            
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.SessionId))
                return;

            _localUserId = localUserId;
            _isBound = true;
            _transport.ReliableDataReceived += OnReliableDataReceived;
        }

        public async UniTask UnbindAsync()
        {
            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;

            _isBound = false;
            _localUserId = null;

            await UniTask.CompletedTask;
        }

        public async UniTask SubmitPlacementAsync(BattleshipPlacementMessage message)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhotonBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(BattleshipReliablePayloadCodec.SerializePlacement(message));
        }

        public async UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhotonBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(BattleshipReliablePayloadCodec.SerializePlacementTimeout(message));
        }

        public async UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhotonBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(BattleshipReliablePayloadCodec.SerializeRecovery(message));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;

            _isBound = false;
            _localUserId = null;

            _incomingPlacements.Dispose();
            _incomingPlacementTimeouts.Dispose();
            _incomingRecoverySnapshots.Dispose();
        }

        private void OnReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (!_isBound || evt.Payload.Length == 0)
                return;

            if (TryPublishPlacement(evt.Payload))
                return;

            if (TryPublishPlacementTimeout(evt.Payload))
                return;

            TryPublishRecovery(evt.Payload);
        }

        private bool TryPublishPlacement(byte[] payload)
        {
            if (!BattleshipReliablePayloadCodec.TryDeserializePlacement(payload, out var message))
                return false;

            if (!IsOwnMessage(message.SenderUserId))
                _incomingPlacements.OnNext(message);

            return true;
        }

        private bool TryPublishPlacementTimeout(byte[] payload)
        {
            if (!BattleshipReliablePayloadCodec.TryDeserializePlacementTimeout(payload, out var message))
                return false;

            if (!IsOwnMessage(message.SenderUserId))
                _incomingPlacementTimeouts.OnNext(message);

            return true;
        }

        private bool TryPublishRecovery(byte[] payload)
        {
            if (!BattleshipReliablePayloadCodec.TryDeserializeRecovery(payload, out var message))
                return false;

            if (!IsOwnMessage(message.SenderUserId))
                _incomingRecoverySnapshots.OnNext(message);

            return true;
        }

        private bool IsOwnMessage(string senderUserId) =>
            string.Equals(senderUserId, _localUserId, StringComparison.Ordinal);
    }
}