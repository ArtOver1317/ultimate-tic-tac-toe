using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    [TestFixture]
    [Category("Integration")]
    public partial class MatchmakingFsmPlayModeTests
    {
        private FakeMatchmakingService _service;
        private MatchmakingFsm _sut;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeMatchmakingService();
            _sut = new MatchmakingFsm(_service, TimeSpan.FromMilliseconds(300));
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCreated_ThenStateIsIdleAndResultAndFailureAreNull() => UniTask.ToCoroutine(async () =>
        {
            await UniTask.Yield();

            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
            _sut.Result.CurrentValue.Should().BeNull();
            _sut.Failure.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledAndServiceReturnsResult_ThenTransitionsToFoundStateWithResult() => UniTask.ToCoroutine(async () =>
        {
            var expected = new MatchmakingResult("match-123", "opponent-456");
            _service.EnqueueResult(expected);

            var started = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            started.Should().BeTrue();
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.Should().BeSameAs(expected);
            _sut.Failure.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchTimesOut_ThenTransitionsToFailedStateWithTimeoutFailure() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueNever();

            var started = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(100), CancellationToken.None);

            started.Should().BeTrue();
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.TerminalModal, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Failure.CurrentValue.IsTimeout.Should().BeTrue();
            _sut.Failure.CurrentValue.Code.Should().Be("matchmaking.terminal.timeout");
            _sut.Failure.CurrentValue.MessageKey.Should().Be("Errors.GameWizard.MatchmakingTimeout");
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenExternalCancellationTokenCancelledDuringSearch_ThenTransitionsToCancelledStateWithCleanup() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            using var cts = new CancellationTokenSource();

            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), cts.Token);
            await UniTask.Delay(100);
            cts.Cancel();
            await task;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Cancelled, 1000);

            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            _sut.Failure.CurrentValue.Should().BeNull();
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledWithPreCancelledToken_ThenThrowsOperationCanceledExceptionAndRemainsIdle() => UniTask.ToCoroutine(async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () => await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
        });

        private static MatchmakingRequest CreateValidRequest() =>
            new MatchmakingRequest("classic", new TicTacToeConfig(3));

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }
    }
}
