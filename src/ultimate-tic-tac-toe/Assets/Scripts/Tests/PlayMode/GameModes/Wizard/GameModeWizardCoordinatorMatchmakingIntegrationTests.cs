using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class GameModeWizardCoordinatorMatchmakingIntegrationTests
    {
        private SpyWizardNavigator _navigator;
        private SessionFactorySpy _sessionFactory;
        private GameModeWizardCoordinator _sut;

        [SetUp]
        public void SetUp()
        {
            _navigator = new SpyWizardNavigator();
            _sessionFactory = new SessionFactorySpy();
            _sut = new GameModeWizardCoordinator(_navigator, _sessionFactory.Create);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCloseMatchmakingToSetupCalledTwice_ThenIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new TestLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            viewModel.RequestCancel();
            viewModel.RequestCancel();

            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);

            // Assert
            _navigator.ReplaceMatchmakingWithMatchSetupCalls.Should().Be(1);
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenFoundArrivesAndUserCancelsNearlySimultaneously_ThenWizardDoesNotDoubleTransition() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new TestLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            await service.SearchStarted.Task;
            viewModel.RequestCancel();
            service.AllowComplete.TrySetResult(true);

            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1 || _navigator.CloseMatchmakingCalls == 1, 4000);

            // Assert
            var cancelPath = _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1 && _navigator.CloseAllCalls == 0;
            var foundPath = _navigator.CloseMatchmakingCalls == 1 && _navigator.CloseAllCalls == 1;

            (cancelPath || foundPath).Should().BeTrue("должен выполниться только один путь перехода");

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchmakingStateBecomesFound_ThenAutoClosesAndStartsExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new TwoStageMatchmakingService();
            var localization = new TestLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            var launchCount = 0;
            var launchSub = _sut.GameLaunchRequested.Subscribe(_ => launchCount++);

            try
            {
                // Act
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

                service.AllowFirstComplete.TrySetResult(true);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

                // Force a second Found emission before auto-close delay elapses.
                viewModel.BeginSearch(new MatchmakingRequest(ClassicModeStrategy.DefaultModeId, new ClassicModeConfig(3)), CancellationToken.None);

                await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1 && _navigator.CloseAllCalls == 1, 4000);

                // Assert
                launchCount.Should().Be(1);
                _navigator.CloseMatchmakingCalls.Should().Be(1);
                _navigator.CloseAllCalls.Should().Be(1);
            }
            finally
            {
                launchSub.Dispose();
                viewModel.Dispose();
                localization.Dispose();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchmakingCancelOrBackRequested_ThenReturnsToMatchSetupAndDoesNotDisposeSession() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new TestLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            AbortReason? aborted = null;
            var abortSub = _sut.WizardAborted.Subscribe(r => aborted = r);

            try
            {
                // Act
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

                viewModel.RequestCancel();
                await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);

                // Assert
                _sut.IsActive.Should().BeTrue();
                _sut.TryGetSession(out var activeSession).Should().BeTrue();
                activeSession.Should().NotBeNull();
                aborted.Should().BeNull("Cancel/Back в matchmaking должен возвращать в MatchSetup без abort wizard");
            }
            finally
            {
                abortSub.Dispose();
                viewModel.Dispose();
                localization.Dispose();
            }
        });

        private async UniTask MoveToMatchSetupAsync()
        {
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, 2000);
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private static GameModeSessionSnapshot CreateMatchmakingSnapshot() =>
            GameModeSessionSnapshot.Default
                .WithSelectedModeId(ClassicModeStrategy.DefaultModeId)
                .WithModeConfig(new ClassicModeConfig(3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithVersion(1);

        private sealed class HarnessMatchmakingService : IMatchmakingService
        {
            public UniTaskCompletionSource<bool> SearchStarted { get; } = new();
            public UniTaskCompletionSource<bool> AllowComplete { get; } = new();

            public async UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                SearchStarted.TrySetResult(true);
                await AllowComplete.Task.AttachExternalCancellation(ct);
                return new MatchmakingResult("match-1", "opponent-1");
            }
        }

        private sealed class TwoStageMatchmakingService : IMatchmakingService
        {
            private int _callCount;

            public UniTaskCompletionSource<bool> AllowFirstComplete { get; } = new();

            public async UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                var call = Interlocked.Increment(ref _callCount);

                if (call == 1)
                {
                    await AllowFirstComplete.Task.AttachExternalCancellation(ct);
                    return new MatchmakingResult("match-1", "opponent-1");
                }

                return new MatchmakingResult("match-2", "opponent-2");
            }
        }

        private sealed class TestLocalizationService : ILocalizationService, IDisposable
        {
            private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
            private readonly ReactiveProperty<bool> _isBusy = new(false);
            private readonly Subject<LocalizationError> _errors = new();

            public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _currentLocale;
            public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
            public Observable<LocalizationError> Errors => _errors;

            public UniTask InitializeAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
            {
                _currentLocale.Value = locale;
                return UniTask.CompletedTask;
            }

            public UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
                key.Value ?? string.Empty;

            public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
                Observable.Return(key.Value ?? string.Empty);

            public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
                Observable.Return(key.Value ?? string.Empty);

            public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs };

            public void Dispose()
            {
                _errors.Dispose();
                _isBusy.Dispose();
                _currentLocale.Dispose();
            }
        }

        private sealed class SpyWizardNavigator : IGameModeWizardNavigator
        {
            public Func<CancellationToken, UniTask> OpenModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> CloseModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> OpenMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> CloseMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask<MatchmakingViewModel>> OpenMatchmakingImpl { get; set; } = _ => UniTask.FromResult<MatchmakingViewModel>(null);
            public Func<CancellationToken, UniTask> CloseMatchmakingImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> ReplaceModeSelectionWithMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> ReplaceMatchSetupWithModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask<MatchmakingViewModel>> ReplaceMatchSetupWithMatchmakingImpl { get; set; } = _ => UniTask.FromResult<MatchmakingViewModel>(null);
            public Func<CancellationToken, UniTask> ReplaceMatchmakingWithMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
            public Func<CancellationToken, UniTask> CloseAllImpl { get; set; } = _ => UniTask.CompletedTask;

            public int OpenModeSelectionCalls { get; private set; }
            public int CloseModeSelectionCalls { get; private set; }
            public int OpenMatchSetupCalls { get; private set; }
            public int CloseMatchSetupCalls { get; private set; }
            public int OpenMatchmakingCalls { get; private set; }
            public int CloseMatchmakingCalls { get; private set; }
            public int ReplaceModeSelectionWithMatchSetupCalls { get; private set; }
            public int ReplaceMatchSetupWithModeSelectionCalls { get; private set; }
            public int ReplaceMatchSetupWithMatchmakingCalls { get; private set; }
            public int ReplaceMatchmakingWithMatchSetupCalls { get; private set; }
            public int CloseAllCalls { get; private set; }

            public UniTask OpenModeSelectionAsync(CancellationToken ct)
            {
                OpenModeSelectionCalls++;
                return OpenModeSelectionImpl(ct);
            }

            public UniTask CloseModeSelectionAsync(CancellationToken ct)
            {
                CloseModeSelectionCalls++;
                return CloseModeSelectionImpl(ct);
            }

            public UniTask OpenMatchSetupAsync(CancellationToken ct)
            {
                OpenMatchSetupCalls++;
                return OpenMatchSetupImpl(ct);
            }

            public UniTask CloseMatchSetupAsync(CancellationToken ct)
            {
                CloseMatchSetupCalls++;
                return CloseMatchSetupImpl(ct);
            }

            public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
            {
                OpenMatchmakingCalls++;
                return OpenMatchmakingImpl(ct);
            }

            public UniTask CloseMatchmakingAsync(CancellationToken ct)
            {
                CloseMatchmakingCalls++;
                return CloseMatchmakingImpl(ct);
            }

            public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
            {
                ReplaceModeSelectionWithMatchSetupCalls++;
                return ReplaceModeSelectionWithMatchSetupImpl(ct);
            }

            public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct)
            {
                ReplaceMatchSetupWithModeSelectionCalls++;
                return ReplaceMatchSetupWithModeSelectionImpl(ct);
            }

            public UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct)
            {
                ReplaceMatchSetupWithMatchmakingCalls++;
                return ReplaceMatchSetupWithMatchmakingImpl(ct);
            }

            public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct)
            {
                ReplaceMatchmakingWithMatchSetupCalls++;
                return ReplaceMatchmakingWithMatchSetupImpl(ct);
            }

            public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
            {
                CloseAllCalls++;
                return CloseAllImpl(ct);
            }
        }

        private sealed class SessionFactorySpy
        {
            public readonly List<FakeGameModeSession> CreatedSessions = new();

            public IGameModeSession Create()
            {
                var session = new FakeGameModeSession();
                CreatedSessions.Add(session);
                return session;
            }
        }

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot = new(GameModeSessionSnapshot.Default);
            private readonly ReactiveProperty<bool> _canStart = new(false);
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors = new(Array.Empty<ValidationError>());

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void SetSnapshot(GameModeSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig()
            {
                var snapshot = _snapshot.Value;

                var modeId = string.IsNullOrWhiteSpace(snapshot.SelectedModeId)
                    ? ClassicModeStrategy.DefaultModeId
                    : snapshot.SelectedModeId;

                var modeConfig = snapshot.ModeConfig ?? new ClassicModeConfig(3);

                IOpponentConfig opponentConfig;

                switch (snapshot.OpponentType)
                {
                    case OpponentType.Bot:
                        opponentConfig = new BotOpponentConfig(snapshot.BotDifficultyId ?? "Easy");
                        break;

                    case OpponentType.Human:
                        switch (snapshot.HumanOpponentKind)
                        {
                            case HumanOpponentKind.Local:
                                opponentConfig = new LocalHumanConfig();
                                break;

                            case HumanOpponentKind.DirectInvite:
                                opponentConfig = new DirectInviteConfig(snapshot.TargetPlayerId ?? "TestPlayer");
                                break;

                            case HumanOpponentKind.Matchmaking:
                                opponentConfig = new MatchmakingConfig("Match", "Opponent");
                                break;

                            default:
                                opponentConfig = new LocalHumanConfig();
                                break;
                        }

                        break;

                    default:
                        opponentConfig = new LocalHumanConfig();
                        break;
                }

                return Result<GameLaunchConfig>.Success(new GameLaunchConfig(modeId, modeConfig, opponentConfig));
            }

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose()
            {
                _snapshot.Dispose();
                _canStart.Dispose();
                _validationErrors.Dispose();
            }
        }
    }
}