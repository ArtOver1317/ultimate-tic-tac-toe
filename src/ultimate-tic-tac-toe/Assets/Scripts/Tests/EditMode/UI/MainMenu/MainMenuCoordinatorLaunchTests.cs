using System;
using System.Linq;
using System.Text.RegularExpressions;
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
using Runtime.Infrastructure.GameStateMachine.States;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.UI.MainMenu
{
    public partial class MainMenuCoordinatorTests
    {
        [Test]
        public async Task WhenInitialize_ThenSubscribesToViewModelEvents()
        {
            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();

            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenPlayButtonPressedAndWizardSucceeds_ThenHidesMainMenuView()
        {
            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
            _uiServiceMock.Received(1).Hide<Runtime.UI.MainMenu.MainMenuView>();
        }

        [Test]
        public async Task WhenWizardCancelled_ThenShowsMainMenuViewAndRestoresInteractability()
        {
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<Runtime.UI.MainMenu.MainMenuView>().Returns(view);

            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new OperationCanceledException()));

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await UniTask.WaitUntil(
                () => view.ShowCalls == 1 && _viewModel.IsInteractable.CurrentValue,
                cancellationToken: cts.Token);

            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenWizardThrowsException_ThenShowsMainMenuViewAndRestoresInteractability()
        {
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<Runtime.UI.MainMenu.MainMenuView>().Returns(view);

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));

            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new InvalidOperationException("boom")));

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await UniTask.WaitUntil(
                () => view.ShowCalls == 1 && _viewModel.IsInteractable.CurrentValue,
                cancellationToken: cts.Token);

            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenPlayClickedMultipleTimesWhileWizardActive_ThenStartsWizardOnlyOnce()
        {
            var started = new UniTaskCompletionSource<bool>();
            var gate = new UniTaskCompletionSource<bool>();

            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    started.TrySetResult(true);
                    return gate.Task;
                });

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await started.Task.AttachExternalCancellation(cts.Token);

            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());

            gate.TrySetResult(true);
        }

        [TestCase(AbortReason.UserCancel)]
        [TestCase(AbortReason.Error)]
        [TestCase(AbortReason.StartCancelled)]
        [TestCase(AbortReason.Disconnect)]
        public async Task WhenWizardAbortedAfterStart_ThenMainMenuIsRestoredByWizardAbortedEvent(AbortReason reason)
        {
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<Runtime.UI.MainMenu.MainMenuView>().Returns(view);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _wizardAborted.OnNext(reason);
            await UniTask.Yield();

            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [TestCase(AbortReason.GameStarted)]
        [TestCase(AbortReason.SceneChange)]
        public async Task WhenWizardAbortedWithNonRestoreReason_ThenDoesNotRestoreMainMenu(AbortReason reason)
        {
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<Runtime.UI.MainMenu.MainMenuView>().Returns(view);

            _coordinator.Initialize(_viewModel);
            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _viewModel.IsInteractable.CurrentValue.Should().BeFalse();

            _wizardAborted.OnNext(reason);
            await UniTask.Yield();

            view.ShowCalls.Should().Be(0);
            _viewModel.IsInteractable.CurrentValue.Should().BeFalse();
        }

        [Test]
        public async Task WhenWizardAbortedAndMainMenuViewMissing_ThenRestoresInteractabilityWithoutThrowing()
        {
            _coordinator.Initialize(_viewModel);
            _viewModel.RequestStartGame();
            await UniTask.Yield();

            Action act = () => _wizardAborted.OnNext(AbortReason.UserCancel);

            act.Should().NotThrow();
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenGameLaunchRequestedRaisedTwice_ThenLoadGameplayEnteredOnlyOnce()
        {
            _coordinator.Initialize(_viewModel);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            var enterStarted = new UniTaskCompletionSource<bool>();
            var enterGate = new UniTaskCompletionSource<bool>();

            _stateMachineMock
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    enterStarted.TrySetResult(true);
                    return enterGate.Task;
                });

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _gameLaunchRequested.OnNext(config);
            _gameLaunchRequested.OnNext(config);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await enterStarted.Task.AttachExternalCancellation(cts.Token);

            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());

            enterGate.TrySetResult(true);
            await UniTask.Yield();
        }

        [Test]
        public async Task WhenStartGameRequested_ThenEntersGameplayStateAndDisablesUI()
        {
            _coordinator.Initialize(_viewModel);
            bool? interactableValue = null;
            var subscription = _viewModel.IsInteractable.Subscribe(value => interactableValue = value);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            _viewModel.RequestStartGame();
            await UniTask.Yield();
            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();

            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());
            _wizardCoordinatorMock.Received(1).CompleteStartAttempt(true, null);

            interactableValue.Should().BeFalse("UI должен быть заблокирован во время перехода в игру");

            subscription.Dispose();
        }

        [Test]
        public async Task WhenStartGameRequestedAndStateMachineThrows_ThenExceptionIsHandled()
        {
            Exception unobservedException = null;
            _wizardCoordinatorMock.IsActive.Returns(true);

            UniTaskScheduler.UnobservedTaskException += OnUnobservedException;
            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));

            try
            {
                var enterStarted = new UniTaskCompletionSource<bool>();

                _stateMachineMock
                    .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        enterStarted.TrySetResult(true);
                        return UniTask.FromException(new InvalidOperationException("boom"));
                    });

                _coordinator.Initialize(_viewModel);

                var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

                _viewModel.Invoking(vm => vm.RequestStartGame()).Should().NotThrow();
                await UniTask.Yield();
                _gameLaunchRequested.OnNext(config);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await enterStarted.Task.AttachExternalCancellation(cts.Token);

                await UniTask.WaitUntil(
                    () => _wizardCoordinatorMock.ReceivedCalls()
                        .Any(call => call.GetMethodInfo().Name == nameof(IGameWizardCoordinator.CompleteStartAttempt)),
                    cancellationToken: cts.Token);

                unobservedException.Should().BeNull("MainMenuCoordinator should handle exceptions from fire-and-forget async handlers");
                _wizardCoordinatorMock.Received(1).CompleteStartAttempt(
                    false,
                    Arg.Is<WizardError>(err => err.Code == "wizard.start_failed"));
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobservedException;
            }

            return;

            void OnUnobservedException(Exception ex) => unobservedException = ex;
        }
    }
}