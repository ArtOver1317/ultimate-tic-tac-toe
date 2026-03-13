#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Games.Battleship.Core;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;

namespace Runtime.Games.Battleship.Placement
{
    public sealed class BattleshipPlacementTimerService : IBattleshipPlacementTimerService
    {
        private readonly ITimeSource _timeSource;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IMatchStateProvider _matchStateProvider;
        private readonly BattleshipPlacementTimeoutDispatcher _timeoutDispatcher;
        private readonly int _placementTimeLimitSeconds;
        private readonly bool _canSubmitTimeoutCommand;
        private readonly CompositeDisposable _subscriptions = new();
        private readonly ReactiveProperty<float> _remainingSeconds;
        private readonly ReactiveProperty<bool> _isActive;

        private CancellationTokenSource? _countdownCts;
        private bool _isFrozen;
        private bool _disposed;

        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
        public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

        public BattleshipPlacementTimerService(
            IGameLaunchConfigStore configStore,
            IBattleshipGameplayEventStream eventStream,
            IBattleshipGameplaySnapshotProvider snapshotProvider,
            IMatchStateProvider matchStateProvider,
            IGameplayCommandSink commandSink,
            ITimeSource timeSource,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            if (configStore == null)
                throw new ArgumentNullException(nameof(configStore));

            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _matchStateProvider = matchStateProvider ?? throw new ArgumentNullException(nameof(matchStateProvider));
            var commandSink1 = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
            _timeoutDispatcher = new BattleshipPlacementTimeoutDispatcher(_snapshotProvider, commandSink1);

            var sessionSnapshot = (sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore))).Snapshot;
            _canSubmitTimeoutCommand = !sessionSnapshot.IsOnlineDirectInvite || sessionSnapshot.IsHost;

            _placementTimeLimitSeconds = ResolvePlacementTimeLimitSeconds(configStore);
            _remainingSeconds = new ReactiveProperty<float>(_placementTimeLimitSeconds);
            _isActive = new ReactiveProperty<bool>(false);

            (eventStream ?? throw new ArgumentNullException(nameof(eventStream)))
                .PhaseChanged
                .Subscribe(_ => SyncFromSnapshot())
                .AddTo(_subscriptions);
        }

        public void SyncFromSnapshot()
        {
            if (_disposed)
                return;

            if (!CanRunCountdown())
            {
                Stop();
                return;
            }

            if (_isActive.Value)
                return;

            StartCountdown();
        }

        public void RestoreRemainingSeconds(float remainingSeconds)
        {
            SyncFromSnapshot();

            if (_disposed || !_isActive.Value)
                return;

            var clamped = remainingSeconds < 0f ? 0f : remainingSeconds;
            
            if (clamped < _remainingSeconds.Value)
                _remainingSeconds.Value = clamped;
        }

        public void Stop()
        {
            if (_disposed)
                return;

            CancelAndDisposeCountdown();
            _isFrozen = false;
            _isActive.Value = false;
        }

        public void Freeze()
        {
            if (_disposed || !_isActive.Value)
                return;

            _isFrozen = true;
        }

        public void Unfreeze()
        {
            if (_disposed || !_isActive.Value)
                return;

            _isFrozen = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _subscriptions.Dispose();
            _remainingSeconds.Dispose();
            _isActive.Dispose();
        }

        private bool CanRunCountdown()
        {
            if (_placementTimeLimitSeconds <= 0 || !_matchStateProvider.IsMatchActive)
                return false;

            var phase = _snapshotProvider.Phase;
            return phase == BattleshipPhase.Placement || phase == BattleshipPhase.Waiting;
        }

        private void StartCountdown()
        {
            _timeoutDispatcher.Reset();

            _remainingSeconds.Value = _placementTimeLimitSeconds;
            _isActive.Value = true;
            _isFrozen = false;

            _countdownCts = new CancellationTokenSource();
            RunCountdownLoop(_countdownCts.Token).Forget();
        }

        private async UniTaskVoid RunCountdownLoop(CancellationToken ct)
        {
            try
            {
                var lastRealtime = Time.realtimeSinceStartupAsDouble;

                while (!ct.IsCancellationRequested)
                {
                    await WaitForNextFrameAsync(ct);
                    
                    if (ShouldExitCountdownLoop(ct))
                        return;

                    ProcessCountdownFrame(ref lastRealtime);
                    
                    if (!_isActive.Value)
                        return;
                }
            }
            catch (Exception ex)
            {
                HandleCountdownException(ex, ct);
            }
        }

        private static UniTask WaitForNextFrameAsync(CancellationToken ct) =>
            UniTask.Yield(PlayerLoopTiming.Update, ct);

        private bool ShouldExitCountdownLoop(CancellationToken ct) =>
            _disposed || ct.IsCancellationRequested;

        private void ProcessCountdownFrame(ref double lastRealtime)
        {
            var deltaTime = ResolveDeltaTime(ref lastRealtime);
            TryAdvanceCountdown(deltaTime);
        }

        private float ResolveDeltaTime(ref double lastRealtime)
        {
            if (!Application.isPlaying)
                return _timeSource.DeltaTime;

            var nowRealtime = Time.realtimeSinceStartupAsDouble;
            var deltaTime = (float)(nowRealtime - lastRealtime);
            lastRealtime = nowRealtime;
            return deltaTime;
        }

        private bool TryAdvanceCountdown(float deltaTime)
        {
            if (_isFrozen || !_isActive.Value || deltaTime <= 0f)
                return false;

            var next = _remainingSeconds.Value - deltaTime;
            _remainingSeconds.Value = next > 0f ? next : 0f;
            
            if (_remainingSeconds.Value > 0f)
                return false;

            HandleTimerExpired();
            return true;
        }

        private void HandleTimerExpired()
        {
            if (ShouldStopAfterExpiry())
            {
                Stop();
                return;
            }

            if (!_canSubmitTimeoutCommand)
                return;

            SubmitPendingPlacementTimeouts();
        }

        private bool ShouldStopAfterExpiry()
        {
            if (!_matchStateProvider.IsMatchActive)
                return true;

            var phase = _snapshotProvider.Phase;
            return phase != BattleshipPhase.Placement && phase != BattleshipPhase.Waiting;
        }

        private void SubmitPendingPlacementTimeouts()
        {
            _timeoutDispatcher.SubmitTimeoutsForUnconfirmedPlayers();
            
            if (!_timeoutDispatcher.HasUnconfirmedPlayers() || _timeoutDispatcher.AreTimeoutsSubmittedForBothSlots)
                Stop();
        }

        private void CancelAndDisposeCountdown()
        {
            if (_countdownCts == null)
                return;

            _countdownCts.Cancel();
            _countdownCts.Dispose();
            _countdownCts = null;
        }

        private void HandleCountdownException(Exception ex, CancellationToken ct)
        {
            if (ex is OperationCanceledException)
                return;

            if (ex is ObjectDisposedException && (_disposed || ct.IsCancellationRequested))
                return;

            Log.Error(LogTags.Infrastructure, $"[BattleshipPlacementTimerService] Countdown loop failed: {ex}");
            ResetCountdownStateAfterFailure();
        }

        private void ResetCountdownStateAfterFailure()
        {
            CancelAndDisposeCountdown();
            _isFrozen = false;
            _isActive.Value = false;
        }

        private static int ResolvePlacementTimeLimitSeconds(IGameLaunchConfigStore configStore)
        {
            if (!configStore.TryPeek(out var config) || config == null)
                return 0;

            if (config.GameConfig is not BattleshipConfig battleshipConfig)
                return 0;

            return battleshipConfig.PlacementTimeLimitSeconds > 0
                ? battleshipConfig.PlacementTimeLimitSeconds
                : 0;
        }
    }
}
