using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    [TestFixture]
    [Category("Integration")]
    public partial class GameWizardCoordinatorMatchmakingIntegrationTests
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