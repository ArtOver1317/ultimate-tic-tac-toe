using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NSubstitute;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    public sealed class GameModeSelectionE2ESmokeTests
    {
        private sealed class TestLocalizationService : ILocalizationService, IDisposable
        {
            private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
            private readonly ReactiveProperty<bool> _isBusy = new(false);
            private readonly Subject<LocalizationError> _errors = new();
            private readonly Dictionary<(LocaleId, string), ReactiveProperty<string>> _texts = new();

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
                GetOrCreate(_currentLocale.CurrentValue, key.Value).Value;

            public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
                GetOrCreate(_currentLocale.CurrentValue, key.Value);

            public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
                GetOrCreate(_currentLocale.CurrentValue, key.Value);

            public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs, LocaleId.Russian };

            public void Dispose()
            {
                foreach (var entry in _texts.Values)
                    entry.Dispose();

                _errors.Dispose();
                _isBusy.Dispose();
                _currentLocale.Dispose();
                _texts.Clear();
            }

            private ReactiveProperty<string> GetOrCreate(LocaleId locale, string key)
            {
                var id = (locale, key ?? string.Empty);
                if (!_texts.TryGetValue(id, out var value))
                {
                    value = new ReactiveProperty<string>(key ?? string.Empty);
                    _texts.Add(id, value);
                }

                return value;
            }
        }

        private sealed class TestMainMenuViewForCoordinator : MainMenuView
        {
            public int ShowCalls { get; private set; }

            public override void Show() => ShowCalls++;
            public override void Hide() { }
        }

        private sealed class E2ENavigatorSpy : IGameModeWizardNavigator
        {
            public int OpenModeSelectionCalls { get; private set; }
            public int ReplaceModeSelectionWithMatchSetupCalls { get; private set; }
            public int CloseAllCalls { get; private set; }

            public UniTask OpenModeSelectionAsync(CancellationToken ct)
            {
                OpenModeSelectionCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask CloseModeSelectionAsync(CancellationToken ct) => UniTask.CompletedTask;

            public UniTask OpenMatchSetupAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask CloseMatchSetupAsync(CancellationToken ct) => UniTask.CompletedTask;

            public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct) =>
                throw new NotSupportedException("E2E smoke uses LocalHuman and must not open matchmaking.");

            public UniTask CloseMatchmakingAsync(CancellationToken ct) => UniTask.CompletedTask;

            public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
            {
                ReplaceModeSelectionWithMatchSetupCalls++;
                return UniTask.CompletedTask;
            }

            public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct) => UniTask.CompletedTask;

            public UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct) =>
                throw new NotSupportedException("E2E smoke uses LocalHuman and must not open matchmaking.");

            public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct) => UniTask.CompletedTask;

            public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
            {
                CloseAllCalls++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class E2ESession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

            public E2ESession(string modeId)
            {
                _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(
                    GameModeSessionSnapshot.Default
                        .WithSelectedModeId(modeId)
                        .WithOpponentType(OpponentType.Human)
                        .WithHumanOpponentKind(HumanOpponentKind.Local));

                _canStart = new ReactiveProperty<bool>(true);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) { }

            public Result<GameLaunchConfig> BuildLaunchConfig()
            {
                var modeId = string.IsNullOrWhiteSpace(_snapshot.Value.SelectedModeId)
                    ? ClassicModeStrategy.DefaultModeId
                    : _snapshot.Value.SelectedModeId;

                return Result<GameLaunchConfig>.Success(
                    new GameLaunchConfig(modeId, new ClassicModeConfig(3), new LocalHumanConfig()));
            }

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose() { }
        }

        [UnityTest]
        [Explicit]
        [Timeout(20000)]
        public System.Collections.IEnumerator WhenFullFlowFromMainMenuToStart_ThenLoadGameplayIsEnteredOnce() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var localization = new TestLocalizationService();

                var navigator = new E2ENavigatorSpy();
                var wizard = new GameModeWizardCoordinator(navigator, () => new E2ESession("Classic"));

                var stateMachine = Substitute.For<IGameStateMachine>();
                var entered = new UniTaskCompletionSource<GameLaunchConfig>();

                stateMachine
                    .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        entered.TrySetResult(callInfo.ArgAt<GameLaunchConfig>(0));
                        return UniTask.CompletedTask;
                    });

                var ui = Substitute.For<IUIService>();
                ui.Get<MainMenuView>().Returns(new TestMainMenuViewForCoordinator());

                var coordinator = new MainMenuCoordinator(stateMachine, ui, localization, wizard);
                var viewModel = new MainMenuViewModel(localization);
                coordinator.Initialize(viewModel);

                // Act
                viewModel.RequestStartGame();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await UniTask.WaitUntil(() => navigator.OpenModeSelectionCalls == 1, cancellationToken: cts.Token);

                wizard.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await UniTask.WaitUntil(() => navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, cancellationToken: cts.Token);

                wizard.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                var config = await entered.Task.AttachExternalCancellation(cts.Token);

                // Assert
                ui.Received(1).Hide<MainMenuView>();
                config.Should().NotBeNull();
                config.GameModeId.Should().Be("Classic");

                await wizard.TryAbortBestEffortAsync();
            });

        [UnityTest]
        [Explicit]
        [Timeout(20000)]
        public System.Collections.IEnumerator WhenCancelFromMatchSetup_ThenMainMenuIsRestoredAndInteractable() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                using var localization = new TestLocalizationService();

                var navigator = new E2ENavigatorSpy();
                var wizard = new GameModeWizardCoordinator(navigator, () => new E2ESession("Classic"));

                var stateMachine = Substitute.For<IGameStateMachine>();
                var ui = Substitute.For<IUIService>();
                var view = new TestMainMenuViewForCoordinator();
                ui.Get<MainMenuView>().Returns(view);

                var coordinator = new MainMenuCoordinator(stateMachine, ui, localization, wizard);
                var viewModel = new MainMenuViewModel(localization);
                coordinator.Initialize(viewModel);

                viewModel.RequestStartGame();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await UniTask.WaitUntil(() => navigator.OpenModeSelectionCalls == 1, cancellationToken: cts.Token);

                wizard.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await UniTask.WaitUntil(() => navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, cancellationToken: cts.Token);

                // Act
                wizard.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();

                await UniTask.WaitUntil(() => view.ShowCalls == 1 && viewModel.IsInteractable.CurrentValue, cancellationToken: cts.Token);

                // Assert
                view.ShowCalls.Should().Be(1);
                viewModel.IsInteractable.CurrentValue.Should().BeTrue();

                await wizard.TryAbortBestEffortAsync();
            });
    }
}
