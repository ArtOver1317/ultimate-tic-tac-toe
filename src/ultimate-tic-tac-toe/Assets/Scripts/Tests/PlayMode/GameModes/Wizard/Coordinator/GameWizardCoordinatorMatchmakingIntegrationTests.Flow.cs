using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
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
        public IEnumerator WhenCloseMatchmakingToSetupCalledTwice_ThenIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            viewModel.RequestCancel();
            viewModel.RequestCancel();

            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);

            _navigator.ReplaceMatchmakingWithMatchSetupCalls.Should().Be(1);
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenFoundArrivesAndUserCancelsNearlySimultaneously_ThenWizardDoesNotDoubleTransition() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            await service.SearchStarted.Task;
            viewModel.RequestCancel();
            service.AllowComplete.TrySetResult(true);

            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1 || _navigator.CloseMatchmakingCalls == 1, 4000);

            var transitionCount = _navigator.ReplaceMatchmakingWithMatchSetupCalls + _navigator.CloseMatchmakingCalls;
            transitionCount.Should().Be(1, "должен происходить ровно один исход из экрана matchmaking");

            var cancelPath = _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1;
            var foundPath = _navigator.CloseMatchmakingCalls == 1;
            (cancelPath ^ foundPath).Should().BeTrue();

            if (cancelPath)
                _navigator.CloseAllCalls.Should().Be(0);

            if (foundPath)
                _navigator.CloseAllCalls.Should().Be(1);

            viewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchmakingStateBecomesFound_ThenAutoClosesAndStartsExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new TwoStageMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            var launchCount = 0;
            var launchSub = _sut.GameLaunchRequested.Subscribe(_ => launchCount++);

            try
            {
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

                service.AllowFirstComplete.TrySetResult(true);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

                await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

                launchCount.Should().Be(1);
                _navigator.CloseMatchmakingCalls.Should().Be(1);
                _navigator.CloseAllCalls.Should().Be(0);
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
        public IEnumerator WhenQueueEntryIsImmediatelyPaired_ThenAutoClosesAndStarts() => UniTask.ToCoroutine(async () =>
        {
            var immediateService = new ImmediatePairMatchmakingService();
            using var sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineSessionFlow: null, matchmakingService: immediateService);

            await sut.StartWizardAsync(CancellationToken.None);
            sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, 2000);

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, immediateService);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            var launchCount = 0;
            var launchSub = sut.GameLaunchRequested.Subscribe(_ => launchCount++);

            try
            {
                sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

                launchCount.Should().Be(1);
                _navigator.CloseMatchmakingCalls.Should().Be(1);
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
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            AbortReason? aborted = null;
            var abortSub = _sut.WizardAborted.Subscribe(r => aborted = r);

            try
            {
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

                viewModel.RequestCancel();
                await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);

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

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenEnterQueueFails_ThenMatchmakingWindowDoesNotOpenAndInlineErrorIsShown() => UniTask.ToCoroutine(async () =>
        {
            var failingService = new FailingEnterQueueMatchmakingService();
            using var sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineSessionFlow: null, matchmakingService: failingService);

            await sut.StartWizardAsync(CancellationToken.None);

            sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, 2000);

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();

            await WaitUntilAsync(
                () => sut.CurrentError.CurrentValue != null || _navigator.ReplaceMatchSetupWithMatchmakingCalls > 0,
                2000);

            _navigator.ReplaceMatchSetupWithMatchmakingCalls.Should().Be(0);
            sut.CurrentError.CurrentValue.Should().NotBeNull();
            sut.CurrentError.CurrentValue!.DisplayType.Should().Be(ErrorDisplayType.Inline);
            sut.CurrentError.CurrentValue!.Code.Should().Be(WizardError.Codes.MatchmakingStartFailed);
        });
    }
}
