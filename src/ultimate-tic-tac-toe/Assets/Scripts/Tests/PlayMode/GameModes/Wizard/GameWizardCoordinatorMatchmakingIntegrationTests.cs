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
    public class GameWizardCoordinatorMatchmakingIntegrationTests
    {
        private SpyWizardNavigator _navigator;
        private SessionFactorySpy _sessionFactory;
        private GameWizardCoordinator _sut;

        [SetUp]
        public void SetUp()
        {
            _navigator = new SpyWizardNavigator();
            _sessionFactory = new SessionFactorySpy();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create);
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
            var cancelPath = _navigator.ReplaceMatchmakingWithMatchSetupCalls == 1 && _navigator.CloseAllCalls == 0;
            var foundPath = _navigator.CloseMatchmakingCalls == 1 && _navigator.CloseAllCalls == 1;

            (cancelPath || foundPath).Should().BeTrue("������ ����������� ������ ���� ���� ��������");

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

                // Force a second Found emission before auto-close delay elapses.
                viewModel.BeginSearch(new MatchmakingRequest(TicTacToeStrategy.DefaultGameId, new TicTacToeConfig(3)), CancellationToken.None);

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
    }
}