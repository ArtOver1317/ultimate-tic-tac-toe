#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Infrastructure.Logging;
using Runtime.PlayerProfile;

namespace Runtime.GameModes.Wizard.Online.Launcher
{
    internal sealed class OnlineSessionPayloadCoordinator
    {
        private static readonly TimeSpan _matchConfigSyncTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _countdownSyncTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan _countdownTickInterval = TimeSpan.FromSeconds(0.1f);
        private static readonly TimeSpan _payloadPollInterval = TimeSpan.FromSeconds(0.05f);

        private readonly IPhotonSessionTransport _transport;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineCountdownSyncService _countdownSync;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly IPlayerNameService? _playerNameService;
        private readonly Func<double> _networkTimeProvider;
        private readonly Func<bool> _isDisposedProvider;
        private readonly Action<string, string?, OnlineErrorCode> _trackDiagnostic;

        private double? _pendingCountdownTargetNetworkTimeSeconds;
        private OnlineMatchConfigPayload? _pendingMatchConfigBuffer;
        private IOnlinePlayerNamesStore? _boundPlayerNamesStore;
        private bool _hasPendingHostNameBuffer;
        private string? _pendingHostCustomNameBuffer;
        private bool _hasPendingGuestNameBuffer;
        private string? _pendingGuestCustomNameBuffer;

        public OnlineSessionPayloadCoordinator(
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineCountdownSyncService countdownSync,
            IOnlineGameplaySessionContextStore sessionContextStore,
            IPlayerNameService? playerNameService,
            Func<double> networkTimeProvider,
            Func<bool> isDisposedProvider,
            Action<string, string?, OnlineErrorCode> trackDiagnostic)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _countdownSync = countdownSync ?? throw new ArgumentNullException(nameof(countdownSync));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _playerNameService = playerNameService;
            _networkTimeProvider = networkTimeProvider ?? throw new ArgumentNullException(nameof(networkTimeProvider));
            _isDisposedProvider = isDisposedProvider ?? throw new ArgumentNullException(nameof(isDisposedProvider));
            _trackDiagnostic = trackDiagnostic ?? throw new ArgumentNullException(nameof(trackDiagnostic));
        }

        public void BindMatchPlayerNamesStore(IOnlinePlayerNamesStore store)
        {
            _boundPlayerNamesStore = store ?? throw new ArgumentNullException(nameof(store));
            TryFlushBufferedPlayerNamesToStore();
        }

        public void UnbindMatchPlayerNamesStore(IOnlinePlayerNamesStore store)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            if (!ReferenceEquals(_boundPlayerNamesStore, store))
                return;

            _boundPlayerNamesStore = null;
            ClearPendingPlayerNameBuffers();
        }

        public void ClearPendingPayloadBuffers()
        {
            _pendingMatchConfigBuffer = null;
            _pendingCountdownTargetNetworkTimeSeconds = null;
            ClearPendingPlayerNameBuffers();
        }

        public void TryApplyBufferedMatchConfig()
        {
            if (!_pendingMatchConfigBuffer.HasValue)
                return;

            if (!_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                return;

            var payload = _pendingMatchConfigBuffer.Value;
            _sessionContextStore.SetMatchConfig(payload);
            _pendingMatchConfigBuffer = null;
            _trackDiagnostic("match_config_applied_from_buffer", payload.GameId, OnlineErrorCode.None);
        }

        public async UniTask<bool> TrySendLocalPlayerNameAsync(bool isHost)
        {
            if (_playerNameService == null)
                return true;

            var customName = _playerNameService.Snapshot.CurrentValue.CustomName;

            try
            {
                var payload = OnlinePlayerNamePayload.Serialize(isHost, customName);
                await _transport.SendReliableDataAsync(payload);
                return true;
            }
            catch (ArgumentException ex)
            {
                GameLog.Warning($"[OnlineSessionLauncher] Local player name payload is invalid and will not be sent. IsHost={isHost}, Message={ex.Message}");
                _trackDiagnostic("local_name_send_invalid", ex.Message, OnlineErrorCode.None);
                return false;
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                _trackDiagnostic("local_name_send_failed", ex.Message, OnlineErrorCode.NetworkUnavailable);
                return false;
            }
        }

        public async UniTask<bool> TrySendHostMatchConfigAsync(GameLaunchConfig config)
        {
            if (!OnlineMatchConfigPayload.TryFromLaunchConfig(config, out var payload))
                return false;

            try
            {
                await _transport.SendReliableDataAsync(OnlinePayloadSerialization.SerializeMatchConfig(payload));
                return true;
            }
            catch
            {
                _trackDiagnostic("host_match_config_send_failed", null, OnlineErrorCode.NetworkUnavailable);
                return false;
            }
        }

        public async UniTask<bool> TrySendCountdownSignalAsync(double targetNetworkTimeSeconds)
        {
            try
            {
                await _transport.SendReliableDataAsync(OnlinePayloadSerialization.SerializeCountdownTarget(targetNetworkTimeSeconds));
                return true;
            }
            catch
            {
                _trackDiagnostic("host_countdown_sync_send_failed", null, OnlineErrorCode.NetworkUnavailable);
                return false;
            }
        }

        public CountdownPlan StartAuthoritativeCountdown(double networkTimeSeconds, int durationSeconds) =>
            _countdownSync.StartAuthoritativeCountdown(networkTimeSeconds, durationSeconds);

        public async UniTask<bool> WaitForHostMatchConfigAsync(CancellationToken ct)
        {
            if (_sessionContextStore.Snapshot.MatchConfig.HasValue)
                return true;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_matchConfigSyncTimeout);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    if (_sessionContextStore.Snapshot.MatchConfig.HasValue)
                        return true;

                    await UniTask.Delay(_payloadPollInterval, cancellationToken: timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return _sessionContextStore.Snapshot.MatchConfig.HasValue;
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }

        public async UniTask<double?> WaitForHostCountdownTargetAsync(CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_countdownSyncTimeout);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    if (_pendingCountdownTargetNetworkTimeSeconds.HasValue)
                        return _pendingCountdownTargetNetworkTimeSeconds.Value;

                    await UniTask.Delay(_payloadPollInterval, cancellationToken: timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return _pendingCountdownTargetNetworkTimeSeconds;
            }

            ct.ThrowIfCancellationRequested();
            return _pendingCountdownTargetNetworkTimeSeconds;
        }

        public async UniTask DriveCountdownToGameplayAsync(double targetNetworkTimeSeconds, CancellationToken ct, string completionDiagnosticEventName)
        {
            var lastReportedSecond = int.MaxValue;

            while (!_countdownSync.ShouldEnterGameplay(targetNetworkTimeSeconds, _networkTimeProvider()))
            {
                ct.ThrowIfCancellationRequested();

                var remaining = _countdownSync.GetRemainingSeconds(targetNetworkTimeSeconds, _networkTimeProvider());
                
                if (remaining != lastReportedSecond)
                {
                    await _onlineSessionFlow.OnCountdownTickAsync(remaining);
                    lastReportedSecond = remaining;
                }

                await UniTask.Delay(_countdownTickInterval, cancellationToken: ct);
            }

            await _onlineSessionFlow.OnCountdownTickAsync(0);
            await _onlineSessionFlow.OnGameplayEnteredAsync();
            _trackDiagnostic(completionDiagnosticEventName, null, OnlineErrorCode.None);
        }

        public void HandleReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (_isDisposedProvider() || evt.Payload.Length == 0)
                return;

            if (!PlayerLoopHelper.IsMainThread)
            {
                var payloadCopy = (byte[])evt.Payload.Clone();
                UniTask.Post(() => HandleReliableDataReceived(new PhotonReliableDataEvent(payloadCopy)));
                return;
            }

            if (OnlinePlayerNamePayload.TryDeserialize(evt.Payload, out var namePayload))
            {
                ApplyPlayerNameToStoreOrBuffer(namePayload.IsHost, namePayload.CustomName);
                _trackDiagnostic(namePayload.IsHost ? "host_name_received" : "guest_name_received", null, OnlineErrorCode.None);
                return;
            }

            if (OnlinePayloadSerialization.TryDeserializeMatchConfig(evt.Payload, out var payload))
            {
                if (_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                {
                    _sessionContextStore.SetMatchConfig(payload);
                    _trackDiagnostic("match_config_received", payload.GameId, OnlineErrorCode.None);
                }
                else
                {
                    _pendingMatchConfigBuffer = payload;
                    _trackDiagnostic("match_config_buffered_prejoin", payload.GameId, OnlineErrorCode.None);
                }

                return;
            }

            if (!OnlinePayloadSerialization.TryDeserializeCountdownTarget(evt.Payload, out var targetNetworkTimeSeconds))
                return;

            _pendingCountdownTargetNetworkTimeSeconds = targetNetworkTimeSeconds;
            _trackDiagnostic("countdown_target_received", null, OnlineErrorCode.None);
        }

        private void ApplyPlayerNameToStoreOrBuffer(bool isHost, string? customName)
        {
            if (_sessionContextStore.Snapshot.IsOnlineDirectInvite && _boundPlayerNamesStore != null)
            {
                if (isHost)
                    _boundPlayerNamesStore.TrySetHostCustomNameOnce(customName);
                else
                    _boundPlayerNamesStore.TrySetGuestCustomNameOnce(customName);

                return;
            }

            if (isHost)
            {
                _hasPendingHostNameBuffer = true;
                _pendingHostCustomNameBuffer = customName;
            }
            else
            {
                _hasPendingGuestNameBuffer = true;
                _pendingGuestCustomNameBuffer = customName;
            }
        }

        private void TryFlushBufferedPlayerNamesToStore()
        {
            if (_boundPlayerNamesStore == null)
                return;

            if (_hasPendingHostNameBuffer)
            {
                _boundPlayerNamesStore.TrySetHostCustomNameOnce(_pendingHostCustomNameBuffer);
                _hasPendingHostNameBuffer = false;
                _pendingHostCustomNameBuffer = null;
            }

            if (_hasPendingGuestNameBuffer)
            {
                _boundPlayerNamesStore.TrySetGuestCustomNameOnce(_pendingGuestCustomNameBuffer);
                _hasPendingGuestNameBuffer = false;
                _pendingGuestCustomNameBuffer = null;
            }
        }

        private void ClearPendingPlayerNameBuffers()
        {
            _hasPendingHostNameBuffer = false;
            _pendingHostCustomNameBuffer = null;
            _hasPendingGuestNameBuffer = false;
            _pendingGuestCustomNameBuffer = null;
        }
    }
}