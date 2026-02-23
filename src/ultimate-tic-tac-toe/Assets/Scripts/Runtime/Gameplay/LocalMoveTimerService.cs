using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay.ECS;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay
{
    public sealed class LocalMoveTimerService : IMoveTimerService
    {
        private readonly IGameplayCommandSink _commandSink;
        private readonly ITimeSource _timeSource;
        private readonly ReactiveProperty<float> _remainingSeconds;
        private readonly ReactiveProperty<bool> _isActive;
        private readonly CompositeDisposable _disposables = new();
        private readonly int _moveTimeLimitSeconds;

        private CancellationTokenSource _countdownCts;
        private bool _isFrozen;
        private bool _isDisposed;

        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
        public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

        public LocalMoveTimerService(
            IGameLaunchConfigStore configStore,
            IGameplayEventStream eventStream,
            IGameplayCommandSink commandSink,
            ITimeSource timeSource)
        {
            if (configStore == null)
                throw new ArgumentNullException(nameof(configStore));

            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));

            _moveTimeLimitSeconds = ResolveMoveTimeLimitSeconds(configStore);
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
            RunCountdownLoop(playerSlot, _countdownCts.Token).Forget();
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

        private void OnCurrentPlayerChanged(CurrentPlayerChangedEvent evt)
        {
            StartOrResetForPlayer(evt.ActivePlayerSlot);
        }

        private async UniTaskVoid RunCountdownLoop(int loserSlot, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                    if (_isFrozen || !_isActive.Value)
                        continue;

                    var deltaTime = _timeSource.DeltaTime;
                    if (deltaTime <= 0f)
                        continue;

                    var next = _remainingSeconds.Value - deltaTime;
                    _remainingSeconds.Value = next > 0f ? next : 0f;

                    if (_remainingSeconds.Value > 0f)
                        continue;

                    _commandSink.SubmitCommand(new TimeoutCommand(loserSlot));
                    Stop();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                HandleCountdownException(ex);
            }
        }

        private static void HandleCountdownException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            Log.Error(LogTags.Infrastructure, $"[LocalMoveTimerService] Countdown loop failed: {ex}");
        }

        private void CancelAndDisposeCountdown()
        {
            if (_countdownCts == null)
                return;

            _countdownCts.Cancel();
            _countdownCts.Dispose();
            _countdownCts = null;
        }

        private static int ResolveMoveTimeLimitSeconds(IGameLaunchConfigStore configStore)
        {
            if (!configStore.TryPeek(out var config) || config == null)
                return 0;

            return config.MoveTimeLimitSeconds > 0 ? config.MoveTimeLimitSeconds : 0;
        }

    }
}
