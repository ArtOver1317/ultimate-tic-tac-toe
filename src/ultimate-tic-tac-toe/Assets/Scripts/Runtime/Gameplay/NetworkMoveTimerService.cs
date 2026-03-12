using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;

namespace Runtime.Gameplay
{
    public sealed class NetworkMoveTimerService : IMoveTimerService
    {
        private readonly IGameplayCommandSink _commandSink;
        private readonly ITimeSource _timeSource;
        private readonly ReactiveProperty<float> _remainingSeconds;
        private readonly ReactiveProperty<bool> _isActive;
        private readonly CompositeDisposable _disposables = new();
        private readonly int _moveTimeLimitSeconds;
        private readonly bool _isHost;

        private CancellationTokenSource _countdownCts;
        private bool _isFrozen;
        private bool _isDisposed;

        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
        public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

        public NetworkMoveTimerService(
            IGameLaunchConfigStore configStore,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            ITimeSource timeSource,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            if (configStore == null)
                throw new ArgumentNullException(nameof(configStore));

            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));

            var snapshot = (sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore))).Snapshot;
            _isHost = snapshot.IsHost;

            _moveTimeLimitSeconds = ResolveMoveTimeLimitSeconds(configStore, snapshot);
            _remainingSeconds = new ReactiveProperty<float>(_moveTimeLimitSeconds);
            _isActive = new ReactiveProperty<bool>(false);

            (eventStream ?? throw new ArgumentNullException(nameof(eventStream)))
                .CurrentPlayerChanged
                .Subscribe(OnCurrentPlayerChanged)
                .AddTo(_disposables);

            eventStream.RoundFinished
                .Subscribe(_ => Stop())
                .AddTo(_disposables);
        }

        public void StartOrResetForPlayer(int playerSlot)
        {
            if (_isDisposed)
                return;

            if (_moveTimeLimitSeconds <= 0)
                return;

            CancelAndDisposeCountdown();

            _remainingSeconds.Value = _moveTimeLimitSeconds;
            _isActive.Value = true;
            _isFrozen = false;

            _countdownCts = new CancellationTokenSource();
            RunCountdownLoop(playerSlot, submitTimeoutCommand: _isHost, _countdownCts.Token).Forget();
        }

        public void RestoreRemainingSeconds(float remainingSeconds, int activePlayerSlot)
        {
            StartOrResetForPlayer(activePlayerSlot);

            if (_isDisposed || !_isActive.Value)
                return;

            var clamped = remainingSeconds < 0f ? 0f : remainingSeconds;
            
            if (clamped < _remainingSeconds.Value)
                _remainingSeconds.Value = clamped;
        }

        public void Stop()
        {
            if (_isDisposed)
                return;

            CancelAndDisposeCountdown();
            _isFrozen = false;
            _isActive.Value = false;
        }

        public void Freeze()
        {
            if (_isDisposed || !_isActive.Value)
                return;

            _isFrozen = true;
        }

        public void Unfreeze()
        {
            if (_isDisposed || !_isActive.Value)
                return;

            _isFrozen = false;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            Stop();
            _disposables.Dispose();
            _remainingSeconds.Dispose();
            _isActive.Dispose();
        }

        private void OnCurrentPlayerChanged(CurrentPlayerChangedEvent evt) => StartOrResetForPlayer(evt.ActivePlayerSlot);

        private async UniTaskVoid RunCountdownLoop(int loserSlot, bool submitTimeoutCommand, CancellationToken ct)
        {
            try
            {
                var lastRealtime = Time.realtimeSinceStartupAsDouble;

                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                    if (ShouldStopLoop(ct))
                        return;

                    var deltaTime = ResolveDeltaTime(ref lastRealtime);
                    
                    if (ShouldSkipTick(deltaTime))
                        continue;

                    if (!UpdateCountdown(deltaTime))
                        continue;

                    HandleCountdownElapsed(loserSlot, submitTimeoutCommand);
                    return;
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) when (_isDisposed || ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                HandleCountdownException(ex);
            }
        }

        private static void HandleCountdownException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            Log.Error(LogTags.Infrastructure, $"[NetworkMoveTimerService] Countdown loop failed: {ex}");
        }

        private bool ShouldStopLoop(CancellationToken ct) => _isDisposed || ct.IsCancellationRequested;

        private float ResolveDeltaTime(ref double lastRealtime)
        {
            if (!Application.isPlaying)
                return _timeSource.DeltaTime;

            var nowRealtime = Time.realtimeSinceStartupAsDouble;
            var deltaTime = (float)(nowRealtime - lastRealtime);
            lastRealtime = nowRealtime;
            return deltaTime;
        }

        private bool ShouldSkipTick(float deltaTime) => _isFrozen || !_isActive.Value || deltaTime <= 0f;

        private bool UpdateCountdown(float deltaTime)
        {
            var next = _remainingSeconds.Value - deltaTime;
            _remainingSeconds.Value = next > 0f ? next : 0f;
            return _remainingSeconds.Value <= 0f;
        }

        private void HandleCountdownElapsed(int loserSlot, bool submitTimeoutCommand)
        {
            if (submitTimeoutCommand)
                _commandSink.SubmitCommand(new TimeoutCommand(loserSlot));

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

        private static int ResolveMoveTimeLimitSeconds(IGameLaunchConfigStore configStore, OnlineGameplaySessionSnapshot session)
        {
            if (session is { IsOnlineDirectInvite: true, MatchConfig: not null })
            {
                var onlineLimit = session.MatchConfig.Value.MoveTimeLimitSeconds;
                return onlineLimit > 0 ? onlineLimit : 0;
            }

            if (!configStore.TryPeek(out var config) || config == null)
                return 0;

            return config.MoveTimeLimitSeconds > 0 ? config.MoveTimeLimitSeconds : 0;
        }
    }
}
