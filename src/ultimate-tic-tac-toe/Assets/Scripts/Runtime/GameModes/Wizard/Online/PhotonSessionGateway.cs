#nullable enable

using System;
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
        private readonly IPhotonSessionTransport _transport;
        private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycleEvent = new(null);

        private bool _isDisposed;

        public PhotonSessionGateway(IPhotonSessionTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.LifecycleEvent += OnTransportLifecycleEvent;
        }

        public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycleEvent;

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
            _lifecycleEvent.Dispose();
        }

        private void OnTransportLifecycleEvent(PhotonTransportLifecycleEvent evt)
        {
            if (_isDisposed)
                return;

            _lifecycleEvent.Value = new GatewayLifecycleEvent(evt.Kind, evt.SessionId, evt.UserId);
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

    public interface IPhotonSessionTransport
    {
        event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
        event Action<PhotonReliableDataEvent>? ReliableDataReceived;
        double NetworkTimeSeconds { get; }

        UniTask CreateHostSessionAsync(OnlineSessionConfig config);
        UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId);
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