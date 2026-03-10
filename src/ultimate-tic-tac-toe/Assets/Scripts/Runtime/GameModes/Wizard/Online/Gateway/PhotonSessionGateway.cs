#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;

namespace Runtime.GameModes.Wizard.Online
{
    public sealed class PhotonSessionTransportException : Exception
    {
        public OnlineErrorCode ErrorCode { get; }

        public PhotonSessionTransportException(OnlineErrorCode errorCode, string message)
            : base(message) =>
            ErrorCode = errorCode;

        public PhotonSessionTransportException(OnlineErrorCode errorCode, string message, Exception innerException)
            : base(message, innerException) =>
            ErrorCode = errorCode;
    }

    public sealed class PhotonSessionGateway : IPhotonSessionGateway
    {
        private readonly IPhotonSessionTransport _transport;
        private readonly PhotonGatewayLifecycleTracker _lifecycleTracker;
        private readonly PhotonGatewayLeaveProtocol _leaveProtocol;
        private bool _isDisposed;

        public PhotonSessionGateway(IPhotonSessionTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _lifecycleTracker = new PhotonGatewayLifecycleTracker();
            _leaveProtocol = new PhotonGatewayLeaveProtocol(_transport, _lifecycleTracker.LifecycleEvent);
            _transport.LifecycleEvent += OnTransportLifecycleEvent;
        }

        public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycleTracker.LifecycleEvent;
        public bool IsLocalHost => _transport.IsServerRole;

        public double NetworkTimeSeconds => _transport.NetworkTimeSeconds;

        public async UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config)
        {
            ThrowIfDisposed();
            return await ExecuteGatewayOperationAsync(() => _transport.CreateHostSessionAsync(config));
        }

        public async UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
        {
            ThrowIfDisposed();
            ValidateRegionAndUser(region, currentUserId);
            
            return await ExecuteGatewayOperationAsync(
                () => _transport.JoinSessionAsync(sessionId, region, currentUserId),
                MapJoinError);
        }

        public async UniTask<MatchmakingRoomResult> JoinRandomOrCreateAsync(MatchmakingRoomOptions options, CancellationToken ct)
        {
            ThrowIfDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await _transport.JoinRandomOrCreateSessionAsync(options, ct);
                return new MatchmakingRoomResult(result.RoomName, result.PlayersCount, result.OpponentId, result.IsHost);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PhotonSessionTransportException(PhotonGatewayErrorMapper.Map(ex), "JoinRandomOrCreate failed.", ex);
            }
        }

        public async UniTask LeaveAsync(CancellationToken ct)
        {
            if (_isDisposed)
                return;

            await _leaveProtocol.LeaveAsync(ct);
        }

        public GatewayLifecycleEvent[] GetLifecycleEventsSince(int sequenceExclusive)
            => _lifecycleTracker.GetEventsSince(sequenceExclusive);

        public async UniTask LeaveSessionAsync()
        {
            if (_isDisposed)
                return;

            await _transport.LeaveSessionAsync();
        }

        public async UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId)
        {
            ThrowIfDisposed();
            ValidateRegionAndUser(region, currentUserId);
            return await ExecuteGatewayOperationAsync(() => _transport.ReconnectAsync(region, currentUserId));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _transport.LifecycleEvent -= OnTransportLifecycleEvent;
            _lifecycleTracker.Dispose();
        }

        private void OnTransportLifecycleEvent(PhotonTransportLifecycleEvent evt)
        {
            if (_isDisposed)
                return;

            _lifecycleTracker.Publish(evt);
        }

        private async UniTask<GatewayOperationResult> ExecuteGatewayOperationAsync(
            Func<UniTask> operation,
            Func<Exception, OnlineErrorCode>? errorMapper = null)
        {
            try
            {
                await operation();
                return GatewayOperationResult.Success();
            }
            catch (Exception ex)
            {
                return GatewayOperationResult.Failed((errorMapper ?? PhotonGatewayErrorMapper.Map)(ex));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PhotonSessionGateway));
        }

        private static void ValidateRegionAndUser(string region, string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));
        }

        private static OnlineErrorCode MapJoinError(Exception ex) => ex is PhotonSessionTransportException { ErrorCode: OnlineErrorCode.CannotJoinSelf } 
            ? OnlineErrorCode.CannotJoinSelf 
            : PhotonGatewayErrorMapper.Map(ex);
    }

    public readonly struct PhotonTransportLifecycleEvent
    {
        public string Kind { get; }
        public string? SessionId { get; }
        public string? UserId { get; }

        public PhotonTransportLifecycleEvent(string kind, string? sessionId, string? userId)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(kind));

            Kind = kind;
            SessionId = sessionId;
            UserId = userId;
        }
    }

    public readonly struct PhotonReliableDataEvent
    {
        public byte[] Payload { get; }

        public PhotonReliableDataEvent(byte[] payload) => Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public readonly struct PhotonTransportMatchmakingResult
    {
        public string RoomName { get; }
        public int PlayersCount { get; }
        public string? OpponentId { get; }
        public bool IsHost { get; }

        public PhotonTransportMatchmakingResult(string roomName, int playersCount, string? opponentId, bool isHost)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(roomName));

            if (playersCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(playersCount), playersCount, "Value must be positive.");

            RoomName = roomName;
            PlayersCount = playersCount;
            OpponentId = opponentId;
            IsHost = isHost;
        }
    }

    public interface IPhotonSessionTransport
    {
        event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
        event Action<PhotonReliableDataEvent>? ReliableDataReceived;
        double NetworkTimeSeconds { get; }
        bool IsInSession { get; }
        bool IsServerRole { get; }

        UniTask CreateHostSessionAsync(OnlineSessionConfig config);
        UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId);
        UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct);
        UniTask LeaveSessionAsync();
        UniTask ReconnectAsync(string region, string currentUserId);
        UniTask SendReliableDataAsync(byte[] payload);
    }

    public static class PhotonGatewayErrorMapper
    {
        public static OnlineErrorCode Map(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            if (ex is PhotonSessionTransportException transportException)
                return transportException.ErrorCode;

            var message = ex.Message ?? string.Empty;

            if (ContainsAny(message, "not found", "expired", "missing"))
                return OnlineErrorCode.SessionNotFound;

            if (ContainsAny(message, "full", "max players"))
                return OnlineErrorCode.SessionFull;

            if (ContainsAny(message, "already in game", "in progress"))
                return OnlineErrorCode.SessionAlreadyInGame;

            return ContainsAny(message, "region", "lobby") 
                ? OnlineErrorCode.RegionMismatchOrUnavailable 
                : OnlineErrorCode.NetworkUnavailable;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var value in values)
            {
                if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}