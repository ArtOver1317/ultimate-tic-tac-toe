#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship
{
    public interface IBattleshipPlacementTimerService : IDisposable
    {
        ReadOnlyReactiveProperty<float> RemainingSeconds { get; }
        ReadOnlyReactiveProperty<bool> IsActive { get; }

        void SyncFromSnapshot();
        void RestoreRemainingSeconds(float remainingSeconds);
        void Stop();
        void Freeze();
        void Unfreeze();
    }

    public sealed class BattleshipPlacementTimerService : IBattleshipPlacementTimerService
    {
        private readonly IGameplayCommandSink _commandSink;
        private readonly ITimeSource _timeSource;
        private readonly IBattleshipGameplaySnapshotProvider _snapshotProvider;
        private readonly IMatchStateProvider _matchStateProvider;
        private readonly int _placementTimeLimitSeconds;
        private readonly bool _canSubmitTimeoutCommand;
        private readonly CompositeDisposable _subscriptions = new();
        private readonly ReactiveProperty<float> _remainingSeconds;
        private readonly ReactiveProperty<bool> _isActive;

        private CancellationTokenSource? _countdownCts;
        private bool _isFrozen;
        private bool _disposed;
        private bool _slot0TimeoutSubmitted;
        private bool _slot1TimeoutSubmitted;
        private int _seedBase;

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
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));

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

            if (_placementTimeLimitSeconds <= 0 || !_matchStateProvider.IsMatchActive)
            {
                Stop();
                return;
            }

            var phase = _snapshotProvider.Phase;
            if (phase != BattleshipPhase.Placement && phase != BattleshipPhase.Waiting)
            {
                Stop();
                return;
            }

            if (_isActive.Value)
                return;

            _slot0TimeoutSubmitted = false;
            _slot1TimeoutSubmitted = false;
            _seedBase = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            _remainingSeconds.Value = _placementTimeLimitSeconds;
            _isActive.Value = true;
            _isFrozen = false;

            _countdownCts = new CancellationTokenSource();
            RunCountdownLoop(_countdownCts.Token).Forget();
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

        private async UniTaskVoid RunCountdownLoop(CancellationToken ct)
        {
            try
            {
                var lastRealtime = Time.realtimeSinceStartupAsDouble;

                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);

                    if (_disposed || ct.IsCancellationRequested)
                        return;

                    float deltaTime;
                    if (Application.isPlaying)
                    {
                        var nowRealtime = Time.realtimeSinceStartupAsDouble;
                        deltaTime = (float)(nowRealtime - lastRealtime);
                        lastRealtime = nowRealtime;
                    }
                    else
                    {
                        deltaTime = _timeSource.DeltaTime;
                    }

                    if (_isFrozen || !_isActive.Value)
                        continue;

                    if (deltaTime <= 0f)
                        continue;

                    var next = _remainingSeconds.Value - deltaTime;
                    _remainingSeconds.Value = next > 0f ? next : 0f;

                    if (_remainingSeconds.Value > 0f)
                        continue;

                    HandleTimerExpired();

                    if (!_isActive.Value)
                        return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException) when (_disposed || ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                HandleCountdownException(ex);
            }
        }

        private void HandleTimerExpired()
        {
            if (!_matchStateProvider.IsMatchActive)
            {
                Stop();
                return;
            }

            var phase = _snapshotProvider.Phase;
            if (phase != BattleshipPhase.Placement && phase != BattleshipPhase.Waiting)
            {
                Stop();
                return;
            }

            if (!_canSubmitTimeoutCommand)
                return;

            SubmitPlacementTimeoutIfNeeded(PlayerSlotMapping.SlotX, ref _slot0TimeoutSubmitted);
            SubmitPlacementTimeoutIfNeeded(PlayerSlotMapping.SlotO, ref _slot1TimeoutSubmitted);

            if (!HasUnconfirmedPlayers())
            {
                Stop();
                return;
            }

            if (_slot0TimeoutSubmitted && _slot1TimeoutSubmitted)
                Stop();
        }

        private void SubmitPlacementTimeoutIfNeeded(int playerSlot, ref bool alreadySubmitted)
        {
            if (alreadySubmitted)
                return;

            if (_snapshotProvider.IsPlacementConfirmed(playerSlot))
                return;

            alreadySubmitted = true;
            _commandSink.SubmitCommand(new PlacementTimeoutCommand(playerSlot, CreateAutoPlacementSeed(playerSlot)));
        }

        private bool HasUnconfirmedPlayers() =>
            !_snapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotX) ||
            !_snapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotO);

        private int CreateAutoPlacementSeed(int playerSlot) =>
            unchecked((_seedBase * 397) ^ (playerSlot + 1) * 7919);

        private void CancelAndDisposeCountdown()
        {
            if (_countdownCts == null)
                return;

            _countdownCts.Cancel();
            _countdownCts.Dispose();
            _countdownCts = null;
        }

        private static void HandleCountdownException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            Log.Error(LogTags.Infrastructure, $"[BattleshipPlacementTimerService] Countdown loop failed: {ex}");
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

    public interface IBattleshipPlacementTimerHudViewModel : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsVisible { get; }
        ReadOnlyReactiveProperty<string> FormattedTime { get; }
        ReadOnlyReactiveProperty<bool> IsWarning { get; }
    }

    public sealed class BattleshipPlacementTimerHudViewModel : IBattleshipPlacementTimerHudViewModel
    {
        private readonly IBattleshipPlacementTimerService _timerService;
        private readonly ReactiveProperty<bool> _isVisible = new(false);
        private readonly ReactiveProperty<string> _formattedTime = new("00");
        private readonly ReactiveProperty<bool> _isWarning = new(false);
        private readonly CompositeDisposable _subscriptions = new();
        private bool _disposed;
        private int _lastDisplaySeconds = int.MinValue;
        private bool _lastVisible;
        private bool _lastWarning;

        public ReadOnlyReactiveProperty<bool> IsVisible => _isVisible;
        public ReadOnlyReactiveProperty<string> FormattedTime => _formattedTime;
        public ReadOnlyReactiveProperty<bool> IsWarning => _isWarning;

        public BattleshipPlacementTimerHudViewModel(IBattleshipPlacementTimerService timerService)
        {
            _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));

            _timerService.IsActive
                .Subscribe(_ => UpdateState())
                .AddTo(_subscriptions);

            _timerService.RemainingSeconds
                .Subscribe(_ => UpdateState())
                .AddTo(_subscriptions);

            UpdateState();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscriptions.Dispose();
            _isVisible.Dispose();
            _formattedTime.Dispose();
            _isWarning.Dispose();
        }

        private void UpdateState()
        {
            var isVisible = _timerService.IsActive.CurrentValue;
            var remainingSeconds = _timerService.RemainingSeconds.CurrentValue;
            var displaySeconds = NormalizeDisplaySeconds(remainingSeconds);
            var isWarning = isVisible && displaySeconds <= MoveTimerConstants.WarningThresholdSeconds;

            if (_lastVisible != isVisible)
            {
                _lastVisible = isVisible;
                _isVisible.Value = isVisible;
            }

            if (_lastDisplaySeconds != displaySeconds)
            {
                _lastDisplaySeconds = displaySeconds;
                _formattedTime.Value = FormatSeconds(displaySeconds);
            }

            if (_lastWarning != isWarning)
            {
                _lastWarning = isWarning;
                _isWarning.Value = isWarning;
            }
        }

        private static int NormalizeDisplaySeconds(float remainingSeconds)
        {
            var ceil = (int)Math.Ceiling(remainingSeconds);
            return ceil > 0 ? ceil : 0;
        }

        private static string FormatSeconds(int totalSeconds)
        {
            if (totalSeconds >= 60)
            {
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes:00}:{seconds:00}";
            }

            return totalSeconds.ToString("00");
        }
    }

    public sealed class BattleshipPlacementTimerHudBinder : IDisposable
    {
        private const string WarningClass = "move-timer-label--warning";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly IBattleshipPlacementTimerHudViewModel _viewModel;

        private CompositeDisposable? _subscriptions;
        private Label? _timerLabel;
        private bool _isBound;
        private bool _disposed;

        public BattleshipPlacementTimerHudBinder(IGameplayFieldUiAdapter ui, IBattleshipPlacementTimerHudViewModel viewModel)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
                return;

            _timerLabel = _ui.MoveTimerLabel;
            if (_timerLabel == null)
            {
                GameLog.Warning("[BattleshipPlacementTimerHudBinder] Bind skipped: MoveTimerLabel is null.");
                return;
            }

            _subscriptions = new CompositeDisposable();

            _viewModel.FormattedTime
                .Subscribe(UpdateText)
                .AddTo(_subscriptions);

            _viewModel.IsVisible
                .Subscribe(UpdateVisibility)
                .AddTo(_subscriptions);

            _viewModel.IsWarning
                .Subscribe(UpdateWarningStyle)
                .AddTo(_subscriptions);

            UpdateText(_viewModel.FormattedTime.CurrentValue);
            UpdateVisibility(_viewModel.IsVisible.CurrentValue);
            UpdateWarningStyle(_viewModel.IsWarning.CurrentValue);

            _isBound = true;
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;

            if (_timerLabel != null)
            {
                _timerLabel.RemoveFromClassList(WarningClass);
                _timerLabel.style.display = DisplayStyle.None;
                _timerLabel.text = "00";
            }

            _timerLabel = null;
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void UpdateText(string text)
        {
            if (_timerLabel == null)
                return;

            _timerLabel.text = string.IsNullOrEmpty(text) ? "00" : text;
        }

        private void UpdateVisibility(bool isVisible)
        {
            if (_timerLabel == null)
                return;

            _timerLabel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateWarningStyle(bool isWarning)
        {
            if (_timerLabel == null)
                return;

            if (isWarning)
                _timerLabel.AddToClassList(WarningClass);
            else
                _timerLabel.RemoveFromClassList(WarningClass);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleshipPlacementTimerHudBinder));
        }
    }
}
