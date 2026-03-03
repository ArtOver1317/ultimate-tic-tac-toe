#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.GameModes.Wizard
{
    public sealed class PhotonSessionTransportException : Exception
    {
        public OnlineErrorCode ErrorCode { get; }

        public PhotonSessionTransportException(OnlineErrorCode errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public PhotonSessionTransportException(OnlineErrorCode errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    public sealed class PhotonSessionGateway : IPhotonSessionGateway
    {
        private static readonly TimeSpan LeaveAckPollDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan LeaveAckTimeout = TimeSpan.FromSeconds(15);
        private const int LifecycleHistoryCapacity = 128;

        private readonly IPhotonSessionTransport _transport;
        private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycleEvent = new(null);
        private readonly object _lifecycleHistoryLock = new();
        private readonly Queue<GatewayLifecycleEvent> _lifecycleHistory = new();

        private int _lifecycleSequence;
        private bool _isDisposed;

        public PhotonSessionGateway(IPhotonSessionTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.LifecycleEvent += OnTransportLifecycleEvent;
        }

        public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycleEvent;
        public bool IsInSession => _transport.IsInSession;
        public bool IsLocalHost => _transport.IsServerRole;

        public double NetworkTimeSeconds => _transport.NetworkTimeSeconds;

        public async UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PhotonSessionGateway));

            try
            {
                await _transport.CreateHostSessionAsync(config);
                return GatewayOperationResult.Success();
            }
            catch (Exception ex)
            {
                return GatewayOperationResult.Failed(PhotonGatewayErrorMapper.Map(ex));
            }
        }

        public async UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PhotonSessionGateway));

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            try
            {
                await _transport.JoinSessionAsync(sessionId, region, currentUserId);
                return GatewayOperationResult.Success();
            }
            catch (Exception ex)
            {
                if (ex is PhotonSessionTransportException transportException && transportException.ErrorCode == OnlineErrorCode.CannotJoinSelf)
                    return GatewayOperationResult.Failed(OnlineErrorCode.CannotJoinSelf);

                return GatewayOperationResult.Failed(PhotonGatewayErrorMapper.Map(ex));
            }
        }

        public async UniTask<MatchmakingRoomResult> JoinRandomOrCreateAsync(MatchmakingRoomOptions options, CancellationToken ct)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PhotonSessionGateway));

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

            if (!_transport.IsInSession)
            {
                var lastEvent = _lifecycleEvent.CurrentValue;
                if (lastEvent.HasValue && IsTerminalDisconnectKind(lastEvent.Value.Kind))
                    throw new ConnectionLostException("Connection lost while leaving matchmaking room.");

                return;
            }

            ct.ThrowIfCancellationRequested();

            var leaveFence = _lifecycleEvent.CurrentValue?.Sequence ?? 0;
            var tcs = new TaskCompletionSource<LeaveAckOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = _lifecycleEvent.Subscribe(evt =>
            {
                if (!evt.HasValue)
                    return;

                var value = evt.Value;
                if (value.Sequence <= leaveFence)
                    return;

                if (string.Equals(value.Kind, "left_room", StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult(LeaveAckOutcome.Acknowledged);
                    return;
                }

                if (IsTerminalDisconnectKind(value.Kind))
                    tcs.TrySetResult(LeaveAckOutcome.ConnectionLost);
            });

            try
            {
                await _transport.LeaveSessionAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new MatchmakingCancelAckTimeoutException("Timed out waiting for leave-room acknowledgement.");
            }
            catch (Exception ex)
            {
                throw new ConnectionLostException("Failed to leave matchmaking room.", ex);
            }

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(LeaveAckTimeout);
            var waitToken = waitCts.Token;

            if (_transport.IsInSession)
            {
                while (_transport.IsInSession)
                {
                    if (waitToken.IsCancellationRequested)
                        throw new MatchmakingCancelAckTimeoutException("Timed out waiting for leave-room acknowledgement.");

                    await UniTask.Delay(LeaveAckPollDelay, cancellationToken: CancellationToken.None);
                }
            }

            var outcomeTask = tcs.Task;
            var timeoutTask = Task.Delay(Timeout.Infinite, waitToken);
            var completed = await Task.WhenAny(outcomeTask, timeoutTask);

            if (completed != outcomeTask)
                throw new MatchmakingCancelAckTimeoutException("Timed out waiting for leave-room acknowledgement.");

            var outcome = await outcomeTask;
            if (outcome == LeaveAckOutcome.ConnectionLost)
                throw new ConnectionLostException("Connection lost while waiting for leave-room acknowledgement.");

            waitCts.Cancel();
        }

        public GatewayLifecycleEvent[] GetLifecycleEventsSince(int sequenceExclusive)
        {
            lock (_lifecycleHistoryLock)
            {
                if (_lifecycleHistory.Count == 0)
                    return Array.Empty<GatewayLifecycleEvent>();

                var result = new List<GatewayLifecycleEvent>(_lifecycleHistory.Count);
                foreach (var evt in _lifecycleHistory)
                {
                    if (evt.Sequence > sequenceExclusive)
                        result.Add(evt);
                }

                return result.ToArray();
            }
        }

        private static bool IsTerminalDisconnectKind(string? kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return false;

            return string.Equals(kind, "disconnected", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(kind, "shutdown", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(kind, "connect_failed", StringComparison.OrdinalIgnoreCase);
        }

        private enum LeaveAckOutcome
        {
            Acknowledged = 0,
            ConnectionLost = 1,
        }

        public async UniTask LeaveSessionAsync()
        {
            if (_isDisposed)
                return;

            await _transport.LeaveSessionAsync();
        }

        public async UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(PhotonSessionGateway));

            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            try
            {
                await _transport.ReconnectAsync(region, currentUserId);
                return GatewayOperationResult.Success();
            }
            catch (Exception ex)
            {
                return GatewayOperationResult.Failed(PhotonGatewayErrorMapper.Map(ex));
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _transport.LifecycleEvent -= OnTransportLifecycleEvent;

            lock (_lifecycleHistoryLock)
                _lifecycleHistory.Clear();

            _lifecycleEvent.Dispose();
        }

        private void OnTransportLifecycleEvent(PhotonTransportLifecycleEvent evt)
        {
            if (_isDisposed)
                return;

            var sequence = Interlocked.Increment(ref _lifecycleSequence);
            var mapped = new GatewayLifecycleEvent(evt.Kind, evt.SessionId, evt.UserId, sequence);

            lock (_lifecycleHistoryLock)
            {
                _lifecycleHistory.Enqueue(mapped);
                while (_lifecycleHistory.Count > LifecycleHistoryCapacity)
                    _lifecycleHistory.Dequeue();
            }

            _lifecycleEvent.Value = mapped;
        }

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

        public PhotonReliableDataEvent(byte[] payload)
        {
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
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

            if (ContainsAny(message, "region", "lobby"))
                return OnlineErrorCode.RegionMismatchOrUnavailable;

            if (ex is OperationCanceledException)
                return OnlineErrorCode.NetworkUnavailable;

            return OnlineErrorCode.NetworkUnavailable;
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

#nullable restore