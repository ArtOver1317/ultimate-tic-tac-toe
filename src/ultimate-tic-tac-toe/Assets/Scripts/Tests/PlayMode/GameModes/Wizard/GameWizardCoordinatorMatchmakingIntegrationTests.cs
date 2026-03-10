using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class GameWizardCoordinatorMatchmakingIntegrationTests
    {
        private SpyWizardNavigator _navigator;
        private SessionFactorySpy _sessionFactory;
        private PreflightMatchmakingService _preflightService;
        private GameWizardCoordinator _sut;

        [SetUp]
        public void SetUp()
        {
            _navigator = new SpyWizardNavigator();
            _sessionFactory = new SessionFactorySpy();
            _preflightService = new PreflightMatchmakingService();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineSessionFlow: null, matchmakingService: _preflightService);
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
            var localization = new StubLocalizationService();
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
            var localization = new StubLocalizationService();
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
            // Arrange
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
                // Act
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithMatchmakingCalls == 1, 2000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

                service.AllowFirstComplete.TrySetResult(true);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

                await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

                // Assert
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
            // Arrange
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
                // Act
                sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

                // Assert
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
            // Arrange
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
                aborted.Should().BeNull("Cancel/Back � matchmaking ������ ���������� � MatchSetup ��� abort wizard");
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
            // Arrange
            var failingService = new FailingEnterQueueMatchmakingService();
            using var sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineSessionFlow: null, matchmakingService: failingService);

            await sut.StartWizardAsync(CancellationToken.None);

            sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1, 2000);

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot());

            // Act
            sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();

            await WaitUntilAsync(() =>
                sut.CurrentError.CurrentValue != null
                || _navigator.ReplaceMatchSetupWithMatchmakingCalls > 0,
                2000);

            // Assert
            _navigator.ReplaceMatchSetupWithMatchmakingCalls.Should().Be(0);
            sut.CurrentError.CurrentValue.Should().NotBeNull();
            sut.CurrentError.CurrentValue!.DisplayType.Should().Be(ErrorDisplayType.Inline);
            sut.CurrentError.CurrentValue!.Code.Should().Be("wizard.matchmaking_start_failed");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenMatchmakingTransitionsToTerminalModal_ThenCoordinatorShowsBlockingModalAndDoesNotAutoClose() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45));

            var service = new HarnessMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);
            viewModel.NotifySessionStartFailed();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 2000);
            await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null, 2000);

            // Assert
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
            // Arrange
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

            // Act
            _sut.ClearCurrentError();
            await WaitUntilAsync(() => _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1, 2000);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            // Assert
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
            // Arrange
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

            // Act
            _sut.CompleteStartAttempt(false, new WizardError("launch.failed", "Errors.GameWizard.MatchmakingFailed", true, ErrorDisplayType.Modal));
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 2000);
            await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null, 2000);

            // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions[0];
            session.SetSnapshot(CreateMatchmakingSnapshot().WithMoveTimeLimitSeconds(45));

            var service = new TwoStageMatchmakingService();
            var localization = new StubLocalizationService();
            var viewModel = new MatchmakingViewModel(localization, service);
            _navigator.ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult(viewModel);

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);

            service.AllowFirstComplete.TrySetResult(true);
            await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
            await WaitUntilAsync(() => _navigator.CloseMatchmakingCalls == 1, 4000);

            // Assert
            _sut.IsActive.Should().BeTrue();
            _sut.IsSubmitting.CurrentValue.Should().BeTrue();

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => _sut.IsActive == false, 4000);

            viewModel.Dispose();
            localization.Dispose();
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

        private static GameSessionSnapshot CreateMatchmakingSnapshot() =>
            GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithVersion(1);

        private sealed class HarnessMatchmakingService : IMatchmakingService
        {
            public UniTaskCompletionSource<bool> SearchStarted { get; } = new();
            public UniTaskCompletionSource<bool> AllowComplete { get; } = new();

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
                UniTask.FromResult(new QueueEntry("match-1", immediateResult: null));

            public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                SearchStarted.TrySetResult(true);
                await AllowComplete.Task.AttachExternalCancellation(ct);
                return new MatchmakingResult("match-1", "opponent-1");
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }

        private sealed class TwoStageMatchmakingService : IMatchmakingService
        {
            private int _callCount;

            public UniTaskCompletionSource<bool> AllowFirstComplete { get; } = new();

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
                UniTask.FromResult(new QueueEntry("match", immediateResult: null));

            public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                var call = Interlocked.Increment(ref _callCount);

                if (call == 1)
                {
                    await AllowFirstComplete.Task.AttachExternalCancellation(ct);
                    return new MatchmakingResult("match-1", "opponent-1");
                }

                return new MatchmakingResult("match-2", "opponent-2");
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }

        private sealed class FailingEnterQueueMatchmakingService : IMatchmakingService
        {
            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromException<QueueEntry>(new InvalidOperationException("enter queue failed"));
            }

            public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("not expected"));
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }

        private sealed class PreflightMatchmakingService : IMatchmakingService
        {
            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(new QueueEntry("preflight-room", immediateResult: null));
            }

            public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("Coordinator preflight service must not be used for wait."));
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }

        private sealed class ImmediatePairMatchmakingService : IMatchmakingService
        {
            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                var result = new MatchmakingResult("match-immediate", "opponent-1", isHost: false);
                return UniTask.FromResult(new QueueEntry("match-immediate", result));
            }

            public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("Immediate pair should not wait."));
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }
    }
}