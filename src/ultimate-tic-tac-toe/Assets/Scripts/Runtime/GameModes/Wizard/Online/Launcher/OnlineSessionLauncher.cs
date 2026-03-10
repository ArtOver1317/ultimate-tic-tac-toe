#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online.Launcher;
using Runtime.PlayerProfile;
using VContainer;

namespace Runtime.GameModes.Wizard.Online
{
    public readonly struct OnlineLaunchPreparationResult
    {
        public bool IsSuccess { get; }
        public WizardError? Error { get; }

        public OnlineLaunchPreparationResult(bool isSuccess, WizardError? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static OnlineLaunchPreparationResult Success() => new(true, null);

        public static OnlineLaunchPreparationResult Failed(WizardError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            return new OnlineLaunchPreparationResult(false, error);
        }
    }

    public interface IOnlineSessionLauncher
    {
        UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct);
        void BindMatchPlayerNamesStore(IOnlinePlayerNamesStore store);
        void UnbindMatchPlayerNamesStore(IOnlinePlayerNamesStore store);
    }

    public sealed class NoOpOnlineSessionLauncher : IOnlineSessionLauncher
    {
        public static readonly IOnlineSessionLauncher Instance = new NoOpOnlineSessionLauncher();

        public UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct) =>
            UniTask.FromResult(OnlineLaunchPreparationResult.Success());

        public void BindMatchPlayerNamesStore(IOnlinePlayerNamesStore store) { }

        public void UnbindMatchPlayerNamesStore(IOnlinePlayerNamesStore store) { }
    }

    public sealed class OnlineSessionLauncher : IOnlineSessionLauncher, IDisposable
    {
        private static readonly TimeSpan _defaultReconnectGraceTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _defaultReconnectRetryDelay = TimeSpan.FromSeconds(1);

        private readonly IPhotonSessionTransport _transport;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly OnlineDiagnosticsBuffer _diagnosticsBuffer;
        private readonly OnlineSessionPayloadCoordinator _payloadCoordinator;
        private readonly OnlineSessionLaunchPreparationCoordinator _preparationCoordinator;
        private readonly OnlineSessionLifecycleCoordinator _lifecycleCoordinator;
        private readonly CancellationTokenSource _runtimeCts = new();
        private readonly IDisposable _gatewayLifecycleSubscription;
        private bool _isDisposed;

        [Inject]
        public OnlineSessionLauncher(
            IPhotonSessionGateway gateway,
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineCountdownSyncService countdownSync,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineDiagnosticsBuffer diagnosticsBuffer,
            OnlineCleanupTracker cleanupTracker,
            IPlayerNameService playerNameService)
            : this(
                gateway,
                transport,
                onlineSessionFlow,
                countdownSync,
                sessionContextStore,
                diagnosticsBuffer,
                cleanupTracker,
                playerNameService,
                OnlineIdentityProvider.ResolveCurrentUserId(),
                _defaultReconnectGraceTimeout,
                _defaultReconnectRetryDelay)
        {
        }

        internal OnlineSessionLauncher(
            IPhotonSessionGateway gateway,
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineCountdownSyncService countdownSync,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineDiagnosticsBuffer diagnosticsBuffer,
            OnlineCleanupTracker cleanupTracker,
            IPlayerNameService? playerNameService,
            string localUserId,
            TimeSpan reconnectGraceTimeout,
            TimeSpan reconnectRetryDelay)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _diagnosticsBuffer = diagnosticsBuffer ?? throw new ArgumentNullException(nameof(diagnosticsBuffer));
            var resolvedLocalUserId = string.IsNullOrWhiteSpace(localUserId)
                ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId))
                : localUserId;
            var resolvedReconnectGraceTimeout = reconnectGraceTimeout > TimeSpan.Zero
                ? reconnectGraceTimeout
                : throw new ArgumentOutOfRangeException(nameof(reconnectGraceTimeout), reconnectGraceTimeout, "Value must be positive.");
            var resolvedReconnectRetryDelay = reconnectRetryDelay > TimeSpan.Zero
                ? reconnectRetryDelay
                : throw new ArgumentOutOfRangeException(nameof(reconnectRetryDelay), reconnectRetryDelay, "Value must be positive.");
            _payloadCoordinator = new OnlineSessionPayloadCoordinator(
                _transport,
                _onlineSessionFlow,
                countdownSync ?? throw new ArgumentNullException(nameof(countdownSync)),
                _sessionContextStore,
                playerNameService,
                () => gateway.NetworkTimeSeconds,
                () => _isDisposed,
                TrackDiagnostic);
            _preparationCoordinator = new OnlineSessionLaunchPreparationCoordinator(
                gateway,
                _transport,
                _onlineSessionFlow,
                _sessionContextStore,
                _payloadCoordinator,
                resolvedLocalUserId,
                MarkRunnerAllocated,
                TrackDiagnostic,
                ToWizardError);
            _lifecycleCoordinator = new OnlineSessionLifecycleCoordinator(
                gateway,
                _onlineSessionFlow,
                _sessionContextStore,
                _payloadCoordinator,
                cleanupTracker ?? throw new ArgumentNullException(nameof(cleanupTracker)),
                resolvedLocalUserId,
                resolvedReconnectGraceTimeout,
                resolvedReconnectRetryDelay,
                _runtimeCts.Token,
                () => _isDisposed,
                TrackDiagnostic);
            _gatewayLifecycleSubscription = gateway.LifecycleEvent
                .Where(evt => evt.HasValue)
                .Subscribe(evt => _lifecycleCoordinator.HandleGatewayLifecycleEvent(evt!.Value));
            _transport.ReliableDataReceived += _payloadCoordinator.HandleReliableDataReceived;
            _onlineSessionFlow.Snapshot
                .Subscribe(snapshot => _lifecycleCoordinator.HandleFlowSnapshotChanged(snapshot))
                .AddTo(_runtimeCts.Token);
            TrackDiagnostic("launcher_initialized");
        }

        public async UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct)
        {
            ThrowIfDisposed();

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return await _preparationCoordinator.PrepareForLaunchAsync(config, ct);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(OnlineSessionLauncher));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _runtimeCts.Cancel();
            _runtimeCts.Dispose();
            _gatewayLifecycleSubscription.Dispose();
            _transport.ReliableDataReceived -= _payloadCoordinator.HandleReliableDataReceived;

            var diagnosticsCount = _diagnosticsBuffer.Count;
            _lifecycleCoordinator.Dispose(diagnosticsCount);
        }

        public void BindMatchPlayerNamesStore(IOnlinePlayerNamesStore store)
        {
            if (_isDisposed)
                return;

            _payloadCoordinator.BindMatchPlayerNamesStore(store);
        }

        public void UnbindMatchPlayerNamesStore(IOnlinePlayerNamesStore store) =>
            _payloadCoordinator.UnbindMatchPlayerNamesStore(store);

        private void MarkRunnerAllocated()
        {
            _lifecycleCoordinator.MarkRunnerAllocated();
        }

        private void TrackDiagnostic(string eventName, string? reason = null, OnlineErrorCode errorCode = OnlineErrorCode.None)
        {
            var flow = _onlineSessionFlow.Snapshot.CurrentValue;
            var session = _sessionContextStore.Snapshot;

            _diagnosticsBuffer.Track(new OnlineDiagnosticEvent(
                DateTimeOffset.UtcNow,
                eventName,
                session.SessionId,
                flow.State,
                flow.FlowEpoch,
                reason,
                errorCode));
        }

        private static WizardError ToWizardError(OnlineErrorCode errorCode)
        {
            var key = OnlineLocalizationKeys.ErrorKey(errorCode) ?? "Errors.GameWizard.UnhandledException";
            return new WizardError(
                code: $"online.{errorCode}",
                messageKey: key,
                isBlocking: true,
                displayType: ErrorDisplayType.Modal);
        }

    }
}