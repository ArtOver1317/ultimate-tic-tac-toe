#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using UnityEngine;

namespace Runtime.Games.TicTacToe.AI.Turns
{
    /// <summary>
    /// Runtime driver that listens to gameplay events and submits bot moves (ADR-1, ADR-6, ADR-10).
    /// One driver per bot slot. Dispose cancels any in-flight computation.
    /// </summary>
    public sealed class BotTurnDriver : IBotTurnDriver
    {
        private const int _slotSeedMixer = 31337;

        private readonly IMatchStateProvider _matchState;
        private readonly IBotProfileCatalog _profileCatalog;
        private readonly IClassicWinLengthProvider _winLengthProvider;
        private readonly BotTurnRequestBuilder _requestBuilder;
        private readonly BotTurnExecutionRunner _executionRunner;

        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<bool> _isDisabled = new(false);
        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _turnCts;
        private CancellationTokenSource? _lifetimeCts;

        private BotProfileData _profileData;
        private BotProfile? _profile;
        private int _botSlot;
        private bool _started;
        private bool _botDisabled;
        private bool _disposed;

        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<bool> IsDisabled => _isDisabled;

        public BotTurnDriver(
            IMatchStateProvider matchState,
            IBotDecisionEngine engine,
            IBotProfileCatalog profileCatalog,
            IClassicWinLengthProvider winLengthProvider)
        {
            _matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
            _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
            _winLengthProvider = winLengthProvider ?? throw new ArgumentNullException(nameof(winLengthProvider));

            _requestBuilder = new BotTurnRequestBuilder(_matchState);
            
            _executionRunner = new BotTurnExecutionRunner(
                _matchState,
                engine ?? throw new ArgumentNullException(nameof(engine)),
                _requestBuilder);
        }

        public UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            string difficultyId,
            CancellationToken ct)
        {
            if (_disposed)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Driver disposed."));

            if (_started)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Already started."));

            if (!TryResolveStartDependencies(config, difficultyId, out var ticTacToeConfig, out var profile, out var failure))
                return UniTask.FromResult(failure);

            InitializeDriverState(ticTacToeConfig, botSlot, profile, ct);
            Subscribe();

            if (_matchState.IsMatchActive && _matchState.ActivePlayerSlot == _botSlot)
                TriggerTurnAsync().Forget();

            return UniTask.FromResult(new BotStartResult(BotStartStatus.Started));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            CancelCurrentTurn();
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            _subscriptions?.Dispose();
            _subscriptions = null;
            _isBusy.Dispose();
            _isDisabled.Dispose();
            _profile = null;
            _requestBuilder.Reset();
        }

        private void Subscribe()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _matchState.CurrentPlayerChanged
                .Subscribe(OnCurrentPlayerChanged)
                .AddTo(_subscriptions);

            _matchState.RoundFinished
                .Subscribe(OnRoundFinished)
                .AddTo(_subscriptions);
        }

        private void OnCurrentPlayerChanged(CurrentPlayerChangedEvent evt)
        {
            if (_disposed || _botDisabled)
                return;

            if (evt.ActivePlayerSlot != _botSlot)
                return;

            TriggerTurnAsync().Forget();
        }

        private void OnRoundFinished(RoundFinishedEvent _) => CancelCurrentTurn();

        private async UniTaskVoid TriggerTurnAsync()
        {
            if (_disposed || _botDisabled || !_matchState.IsMatchActive)
                return;

            CancelCurrentTurn();
            _turnCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts!.Token);
            var ct = _turnCts.Token;

            _isBusy.Value = true;

            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await ExecuteTurnAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[BotTurnDriver] Unexpected error during turn: {ex}");
            }
            finally
            {
                if (!_disposed)
                    _isBusy.Value = false;
            }
        }

        private async UniTask ExecuteTurnAsync(CancellationToken ct)
        {
            if (_profile == null)
                return;

            var executionStatus = await _executionRunner.ExecuteAsync(_botSlot, _profile, _profileData, ct);
            
            if (executionStatus != BotTurnExecutionStatus.DisableBot)
                return;

            _botDisabled = true;
            _isDisabled.Value = true;
            Debug.LogError("[BotTurnDriver] All attempts rejected. Bot disabled for remainder of match.");
        }

        private void CancelCurrentTurn()
        {
            _turnCts?.Cancel();
            _turnCts?.Dispose();
            _turnCts = null;
        }

        private bool TryResolveStartDependencies(
            GameLaunchConfig config,
            string difficultyId,
            out TicTacToeConfig ticTacToeConfig,
            out BotProfile profile,
            out BotStartResult failure)
        {
            ticTacToeConfig = null!;
            profile = null!;

            if (config.GameConfig is not TicTacToeConfig resolvedConfig)
            {
                failure = new BotStartResult(BotStartStatus.UnsupportedConfig, "Not a TicTacToe config.");
                return false;
            }

            if (resolvedConfig.IsUltimate)
            {
                failure = new BotStartResult(BotStartStatus.UnsupportedConfig, "Ultimate mode not supported by bot.");
                return false;
            }

            if (!_profileCatalog.TryGet(difficultyId, out var resolvedProfile) || resolvedProfile == null)
            {
                failure = new BotStartResult(BotStartStatus.Failed, $"Profile '{difficultyId}' not found.");
                return false;
            }

            ticTacToeConfig = resolvedConfig;
            profile = resolvedProfile;
            failure = default;
            return true;
        }

        private void InitializeDriverState(
            TicTacToeConfig ticTacToeConfig,
            int botSlot,
            BotProfile profile,
            CancellationToken ct)
        {
            _profile = profile;
            _profileData = profile.ToValidatedData();
            _botSlot = botSlot;
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _started = true;

            var boardSize = ticTacToeConfig.BoardSize;
            var winLength = _winLengthProvider.GetWinLength(boardSize);
            _requestBuilder.Configure(botSlot, boardSize, winLength, CreateRandom(profile, botSlot));
        }

        private static IBotRandom CreateRandom(BotProfile profile, int botSlot)
        {
            var seed = profile.UseFixedSeed
                ? unchecked(profile.FixedSeed + botSlot * _slotSeedMixer)
                : unchecked(Environment.TickCount + botSlot * _slotSeedMixer);

            return new BotRandom(seed);
        }
    }
}