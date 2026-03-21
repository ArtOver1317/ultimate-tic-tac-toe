using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.Core;

#pragma warning disable CS8632

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    public abstract class MatchSetupViewModelTestsBase
    {
        private const int _waitUntilTimeoutMs = 3000;

        protected IGameCatalog Catalog;
        protected IGameWizardCoordinator Coordinator;
        protected ILocalizationService Localization;
        protected IBotDifficultyCatalog DifficultyCatalog;
        protected ReactiveProperty<bool> IsTransitioning;
        protected ReactiveProperty<bool> IsSubmitting;
        protected ReactiveProperty<WizardError?> CurrentError;

        [SetUp]
        public void SetUp()
        {
            Catalog = Substitute.For<IGameCatalog>();
            Coordinator = Substitute.For<IGameWizardCoordinator>();
            Localization = Substitute.For<ILocalizationService>();

            IsTransitioning = new ReactiveProperty<bool>(false);
            IsSubmitting = new ReactiveProperty<bool>(false);
            CurrentError = new ReactiveProperty<WizardError?>(null);

            Coordinator.IsTransitioning.Returns(IsTransitioning);
            Coordinator.IsSubmitting.Returns(IsSubmitting);
            Coordinator.CurrentError.Returns(CurrentError);

            Localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));

            Localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => $"resolved:{callInfo.Arg<TextKey>().Value}");

            DifficultyCatalog = new BotDifficultyCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            IsTransitioning?.Dispose();
            IsSubmitting?.Dispose();
            CurrentError?.Dispose();
        }

        protected void SetupCoordinatorWithSession(FakeGameSession session) =>
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(callInfo =>
            {
                callInfo[0] = session;
                return true;
            });

        protected BattleshipStrategy CreateBattleshipStrategy() =>
            new(() => new BattleshipSettingsViewModel(MoveTimerPresetsConfig.CreateRuntimeDefault(), Localization));

        protected void SetupStrategy(string gameId, IGameStrategy strategy) =>
            Catalog.TryGetStrategy(gameId, out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = strategy;
                return true;
            });

        protected static void SetAvailableDifficulties(MatchSetupViewModel sut, IReadOnlyList<BotDifficulty> difficulties)
        {
            var property = sut.AvailableDifficulties as ReactiveProperty<IReadOnlyList<BotDifficulty>>;
            property.Should().NotBeNull();
            property!.Value = difficulties;
        }

        protected static async Task WaitForDifficultyItemsAsync(
            MatchSetupViewModel sut,
            Func<IReadOnlyList<DifficultyChipItem>, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
           
            using var subscription = sut.DifficultyItems.Subscribe(items =>
            {
                if (predicate(items))
                    tcs.TrySetResult(true);
            });

            if (predicate(sut.DifficultyItems.CurrentValue))
                return;

            using var cts = new CancellationTokenSource(_waitUntilTimeoutMs);
           
            using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException(
                       $"Condition was not met within {_waitUntilTimeoutMs} ms."))))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        protected static async Task WaitForSelectedDifficultyAsync(
            MatchSetupViewModel sut,
            Func<string?, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            using var subscription = sut.SelectedDifficultyId.Subscribe(value =>
            {
                if (predicate(value))
                    tcs.TrySetResult(true);
            });

            if (predicate(sut.SelectedDifficultyId.CurrentValue))
                return;

            using var cts = new CancellationTokenSource(_waitUntilTimeoutMs);
            
            using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException(
                       $"Condition was not met within {_waitUntilTimeoutMs} ms."))))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        protected static async Task WaitUntilAsync(Func<bool> predicate, int maxFrames = 120)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                if (predicate())
                    return;

                await UniTask.DelayFrame(1);
            }

            Assert.Fail($"Condition was not met within {maxFrames} frames.");
        }

        protected MatchSetupViewModel CreateSut() =>
            CreateSutWithDefaults();

        protected MatchSetupViewModel CreateSutWithDefaults()
        {
            var sut = new MatchSetupViewModel(Catalog, Coordinator, Localization, DifficultyCatalog);
            sut.DisablePlayerLoopForTests();
            return sut;
        }

        protected sealed class SpyMatchSetupOnlineFlow : IOnlineSessionFlowService
        {
            private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot;

            public SpyMatchSetupOnlineFlow(OnlineFlowSnapshot initialSnapshot) => 
                _snapshot = new ReactiveProperty<OnlineFlowSnapshot>(initialSnapshot);

            public int EnterHumanSetupCalls { get; private set; }

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public UniTask EnterHumanSetupAsync(string region, string currentUserId)
            {
                EnterHumanSetupCalls++;

                var current = _snapshot.Value;
                
                if (current.State is OnlineFlowState.Idle or OnlineFlowState.Terminated or OnlineFlowState.Failed &&
                    string.IsNullOrWhiteSpace(current.CandidateSessionId))
                {
                    _snapshot.Value = new OnlineFlowSnapshot(
                        state: OnlineFlowState.Idle,
                        previousStableState: null,
                        candidateSessionId: "ABCDEF",
                        activeSessionId: null,
                        flowEpoch: current.FlowEpoch + 1,
                        region: current.Region,
                        canStart: false,
                        isBusy: false,
                        errorCode: OnlineErrorCode.None,
                        errorLocalizationKey: null,
                        statusLocalizationKey: null,
                        countdownRemainingSeconds: null,
                        graceDeadlineUtc: null);
                }

                return UniTask.CompletedTask;
            }

            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;
            public UniTask ExitAsync() => UniTask.CompletedTask;
            public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
            public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
            public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
            public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
            public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
            public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
            public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
            public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
            public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

            public void Dispose() => _snapshot.Dispose();
        }

        protected sealed class FakeGameSession : IGameSession
        {
            private readonly ReactiveProperty<GameSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;
            private bool _isDisposed;

            public FakeGameSession(GameSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public int SnapshotGetCount { get; private set; }
            public int CanStartGetCount { get; private set; }
            public int ValidationErrorsGetCount { get; private set; }

            public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot
            {
                get
                {
                    SnapshotGetCount++;
                    return _snapshot;
                }
            }

            public ReadOnlyReactiveProperty<bool> CanStart
            {
                get
                {
                    CanStartGetCount++;
                    return _canStart;
                }
            }

            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors
            {
                get
                {
                    ValidationErrorsGetCount++;
                    return _validationErrors;
                }
            }

            public int UpdateCallCount { get; private set; }
            public int SetModeConfigCallCount { get; private set; }
            public IGameConfig LastModeConfig { get; private set; }

            public void EmitSnapshot(GameSessionSnapshot snapshot) => _snapshot.Value = snapshot;
            public void EmitCanStart(bool value) => _canStart.Value = value;
            public void EmitValidationErrors(IReadOnlyList<ValidationError> errors) => _validationErrors.Value = errors;

            public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
            {
                EnsureNotDisposed();
                UpdateCallCount++;
                var current = _snapshot.Value ?? GameSessionSnapshot.Default;
                var updated = reducer(current) ?? GameSessionSnapshot.Default;
                var nextVersion = current.Version + 1;
                
                if (updated.Version < nextVersion)
                    updated = updated.WithVersion(nextVersion);
                
                _snapshot.Value = updated;
            }

            public void SetModeConfig(IGameConfig config)
            {
                EnsureNotDisposed();
                SetModeConfigCallCount++;
                LastModeConfig = config;
            }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();
            public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

            public void Dispose()
            {
                _isDisposed = true;
                _snapshot.Dispose();
                _canStart.Dispose();
                _validationErrors.Dispose();
            }

            private void EnsureNotDisposed()
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FakeGameSession));
            }
        }

        protected sealed class TestStrategy : IGameStrategy
        {
            private readonly TestSettingsViewModel _viewModel;

            public TestStrategy(string gameId, string iconKey, string displayNameKey, TestSettingsViewModel viewModel)
            {
                GameId = gameId;
                
                Metadata = new GameMetadata(
                    id: gameId,
                    displayNameKey: displayNameKey,
                    descriptionKey: "desc",
                    iconAssetKey: iconKey,
                    sortOrder: 0,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true);
                
                _viewModel = viewModel;
            }

            public string GameId { get; }
            public GameMetadata Metadata { get; }
            public int CreatePresentationCallCount { get; private set; }

            public GameSettingsPresentation CreatePresentation()
            {
                CreatePresentationCallCount++;
                return new GameSettingsPresentation($"ui/mode-settings/{GameId}", _viewModel);
            }

            public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config) => Array.Empty<ValidationError>();
            public IEnumerable<string> GetSupportedBotDifficultyIds() => Array.Empty<string>();
        }

        protected sealed class TestSettingsViewModel : BaseViewModel, IGameSettingsViewModel
        {
            private readonly ReactiveProperty<IGameConfig> _config;
            private readonly ReactiveProperty<bool> _isValid = new(true);

            public TestSettingsViewModel(IGameConfig config) =>
                _config = new ReactiveProperty<IGameConfig>(config);

            public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
            public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

            public bool TryApplyConfig(IGameConfig config)
            {
                if (config == null)
                    return false;

                _config.Value = config;
                return true;
            }

            public int InitializeCallCount { get; private set; }
            public int DisposeCallCount { get; private set; }

            public void EmitConfig(IGameConfig config) => _config.Value = config;

            public override void Initialize()
            {
                base.Initialize();
                InitializeCallCount++;
            }

            protected override void OnDispose()
            {
                DisposeCallCount++;
                _config.Dispose();
                _isValid.Dispose();
                base.OnDispose();
            }
        }

        protected sealed class TestGameModeConfig : IGameConfig
        {
            public TestGameModeConfig(string value) => Value = value;
            public string Value { get; }

            public IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams() =>
                Array.Empty<KeyValuePair<string, string>>();
        }
    }
}