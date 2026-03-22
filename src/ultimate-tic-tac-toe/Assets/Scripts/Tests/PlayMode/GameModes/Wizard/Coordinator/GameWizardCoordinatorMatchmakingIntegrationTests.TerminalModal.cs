using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorMatchmakingIntegrationTests
    {
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchmakingTransitionsToTerminalModal_ThenCoordinatorShowsBlockingModalAndDoesNotAutoClose() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45));

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);
            viewModel.NotifySessionStartFailed();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 2000);
            await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null, 2000);

            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue!.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue!.IsBlocking.Should().BeTrue();
            _navigator.ReplaceMatchmakingWithMatchSetupCalls.Should().Be(0);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTerminalModalAcknowledged_ThenCoordinatorReturnsToSetupWithPreservedParameters() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var expectedSnapshot = CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45);
            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(expectedSnapshot);

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);
            viewModel.NotifySessionStartFailed();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 2000);
            await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null, 2000);

            _sut.ClearCurrentError();
            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            _sut.TryGetSession(out var activeSession).Should().BeTrue();
            activeSession.Should().NotBeNull();
            activeSession!.Snapshot.CurrentValue.SelectedGameId.Should().Be(expectedSnapshot.SelectedGameId);
            activeSession.Snapshot.CurrentValue.MoveTimeLimitSeconds.Should().Be(45);
            viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenSessionStartFailsAfterMatchFound_ThenTerminalModalIsShownOnRootOverlay() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45));

            var service = new TwoStageMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            service.AllowFirstComplete.TrySetResult(true);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
            await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

            _sut.CompleteStartAttempt(false, new WizardError("launch.failed", "Errors.GameWizard.MatchmakingFailed", true, ErrorDisplayType.Modal));
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 2000);
            await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null, 2000);

            _sut.IsActive.Should().BeTrue();
            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue!.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue!.IsBlocking.Should().BeTrue();
            viewModel.State.CurrentValue.Should().Be(MatchmakingState.TerminalModal);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchFoundAndLaunchStarted_ThenWizardIsActiveUntilCompleteStartAttemptCalled() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45));

            var service = new TwoStageMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            service.AllowFirstComplete.TrySetResult(true);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
            await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

            _sut.IsActive.Should().BeTrue();
            _sut.IsSubmitting.CurrentValue.Should().BeTrue();

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => !_sut.IsActive, 4000);

            viewModel.Dispose();
            localization.Dispose();
        });
    }
}
