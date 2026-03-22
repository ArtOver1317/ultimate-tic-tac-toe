using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.UI.MainMenu;

namespace Tests.EditMode.UI.MainMenu
{
    public partial class MainMenuCoordinatorTests
    {
        [TestCase(OnlineFlowState.Terminated)]
        [TestCase(OnlineFlowState.Failed)]
        [TestCase(OnlineFlowState.Idle)]
        public async Task WhenOnlineFlowReturnsToTerminalOrActiveToIdleDuringLaunch_ThenCancelsStartAttempt(OnlineFlowState state)
        {
            _coordinator.Dispose();

            var onlineFlow = Substitute.For<IOnlineSessionFlowService>();
            var flowSnapshot = new ReactiveProperty<OnlineFlowSnapshot>(MainMenuCoordinatorTestsHelpers.CreateFlowSnapshot(OnlineFlowState.WaitingForPlayer));
            onlineFlow.Snapshot.Returns(flowSnapshot);

            var launcher = Substitute.For<IOnlineSessionLauncher>();
            launcher.PrepareForLaunchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => MainMenuCoordinatorTestsHelpers.WaitLaunchCancellationAsync(callInfo.Arg<CancellationToken>()));

            _wizardCoordinatorMock.IsActive.Returns(true);

            _coordinator = new MainMenuCoordinator(
                _stateMachineMock,
                _uiServiceMock,
                _localizationMock,
                _wizardCoordinatorMock,
                launcher,
                onlineFlow);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new DirectInviteConfig("AB2CD7"));

            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();
            flowSnapshot.Value = MainMenuCoordinatorTestsHelpers.CreateFlowSnapshot(state);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            await UniTask.WaitUntil(
                () => _wizardCoordinatorMock.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IGameWizardCoordinator.CancelStartAttempt)),
                cancellationToken: cts.Token);

            _wizardCoordinatorMock.Received(1).CancelStartAttempt();

            await _stateMachineMock.DidNotReceiveWithAnyArgs()
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(default!, default);
        }

        [Test]
        public async Task WhenOnlineFlowIsIdleWithoutActiveTransitionDuringLaunch_ThenDoesNotCancelStartAttempt()
        {
            _coordinator.Dispose();

            var onlineFlow = Substitute.For<IOnlineSessionFlowService>();
            var flowSnapshot = new ReactiveProperty<OnlineFlowSnapshot>(MainMenuCoordinatorTestsHelpers.CreateFlowSnapshot(OnlineFlowState.Idle));
            onlineFlow.Snapshot.Returns(flowSnapshot);

            var launcher = Substitute.For<IOnlineSessionLauncher>();
            launcher.PrepareForLaunchAsync(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(_ => UniTask.FromResult(OnlineLaunchPreparationResult.Success()));

            _wizardCoordinatorMock.IsActive.Returns(true);

            _coordinator = new MainMenuCoordinator(
                _stateMachineMock,
                _uiServiceMock,
                _localizationMock,
                _wizardCoordinatorMock,
                launcher,
                onlineFlow);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new DirectInviteConfig("AB2CD7"));

            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();
            flowSnapshot.Value = MainMenuCoordinatorTestsHelpers.CreateFlowSnapshot(OnlineFlowState.Idle);
            await UniTask.Yield();

            _wizardCoordinatorMock.DidNotReceive().CancelStartAttempt();
        }
    }
}