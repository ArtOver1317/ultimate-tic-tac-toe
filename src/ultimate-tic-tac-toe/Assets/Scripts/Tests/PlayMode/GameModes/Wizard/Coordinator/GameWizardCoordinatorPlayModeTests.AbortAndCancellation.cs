using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Coordinator;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorPlayModeTests
    {
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenUnhandledExceptionOccursInProcessingLoop_ThenWizardIsAbortedAndLoopStops() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("boom");

            LogAssert.Expect(LogType.Error, new Regex("boom"));

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);

            var acceptedAfterAbort = _sut.TryPublishIntent(WizardIntent.Continue);

            acceptedAfterAbort.Should().BeFalse("wizard should not be ready after abort");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortIsTriggeredFromInsideProcessingLoop_ThenDoesNotDeadlock() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("boom");

            LogAssert.Expect(LogType.Error, new Regex("boom"));

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            _navigator.CloseAllCalls.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelPublishedWhileContinueIsQueuedButNotProcessed_ThenContinueIsNotExecuted() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();
           
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            var continueAccepted = _sut.TryPublishIntent(WizardIntent.Continue);

            await closeStarted.Task;
            var cancelAccepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            continueAccepted.Should().BeTrue();
            cancelAccepted.Should().BeTrue();

            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortOccursDuringTransition_ThenNoLateNavigationOccursAfterAbort() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();
            var openFinished = new UniTaskCompletionSource<bool>();
            var openWasCancelled = false;

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                openStarted.TrySetResult(true);

                try
                {
                    await openGate.Task.AttachExternalCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    openWasCancelled = true;
                    throw;
                }
                finally
                {
                    openFinished.TrySetResult(true);
                }
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await openStarted.Task.AttachExternalCancellation(cts.Token);

                await _sut.AbortWizardAsync(AbortReason.SceneChange);
               
                await WaitUntilAsync(
                    () => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1,
                    timeoutMs: 4000,
                    because: "session must be disposed on abort");
                
                var callsAfterAbort = _navigator.TotalCalls;

                openGate.TrySetResult(true);
                await openFinished.Task.AttachExternalCancellation(cts.Token);

                _navigator.TotalCalls.Should().Be(callsAfterAbort);
                openWasCancelled.Should().BeTrue("transition must be cancelled by abort");
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("wizard must not become ready again after abort");
            }
            finally
            {
                openGate.TrySetResult(true);
                await _sut.TryAbortBestEffortAsync();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortWizardCalledWhileWizardIsActive_ThenCancelsTokensClosesWindowsAndDisposesSession() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            _navigator.CloseAllCalls.Should().Be(1);
            session.DisposeCallCount.Should().Be(1);
            _sut.IsTransitioning.CurrentValue.Should().BeFalse();
            _sut.IsSubmitting.CurrentValue.Should().BeFalse();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublished_ThenAbortIsTriggeredOutOfBandAndReturnsTrue() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();
           
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await closeStarted.Task;

            var cancelAccepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            cancelAccepted.Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishCancelConcurrentlyWithAbortWizardAsync_ThenDoesNotThrowAndAbortsOnce() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            var cancelTask = Task.Run(() => _sut.TryPublishIntent(WizardIntent.Cancel));
            var abortTask = Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));

            await Task.WhenAll(cancelTask, abortTask);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            cancelTask.Result.Should().BeTrue();
            session.DisposeCallCount.Should().Be(1);
            _navigator.CloseAllCalls.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelOccursDuringTransition_ThenInFlightNavigationIsCancelledAndNextWindowIsNotOpened() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();
            
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await closeStarted.Task;

            _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortCalledFromNonMainThread_ThenStillClosesWindowsOnMainThreadBestEffort() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            await Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));

            _navigator.CloseAllCalls.Should().Be(1);
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortWizardAsyncCalledOffMainThread_ThenDoesNotThrowAndPublishesWizardAborted() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            AbortReason? published = null;
            var subscription = _sut.WizardAborted.Subscribe(r => published = r);

            try
            {
                await Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));
                await WaitUntilAsync(() => published != null);

                published.Should().Be(AbortReason.SceneChange);
                _navigator.CloseAllCalls.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortTimeoutClosingWindows_ThenStillDisposesSessionAndResetsBusyFlags() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            _navigator.CloseAllImpl = async ct =>
            {
                await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: ct);
            };

            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            _navigator.CloseAllCalls.Should().Be(1);
            session.DisposeCallCount.Should().Be(1);
            _sut.IsTransitioning.CurrentValue.Should().BeFalse();
            _sut.IsSubmitting.CurrentValue.Should().BeFalse();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublishedAtModeSelection_ThenAbortsWithUserCancelReasonAndDisposesSession() => UniTask.ToCoroutine(async () =>
        {
            AbortReason? reason = null;
            var subscription = _sut.WizardAborted.Subscribe(r => reason = r);

            try
            {
                await _sut.StartWizardAsync(CancellationToken.None);
                var session = _sessionFactory.CreatedSessions.Single();

                _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                await WaitUntilAsync(() => reason != null);

                reason.Should().Be(AbortReason.UserCancel);
                session.DisposeCallCount.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublishedAtMatchSetup_ThenAbortsWithUserCancelReasonAndDisposesSession() => UniTask.ToCoroutine(async () =>
        {
            AbortReason? reason = null;
            var subscription = _sut.WizardAborted.Subscribe(r => reason = r);

            try
            {
                await _sut.StartWizardAsync(CancellationToken.None);
                await MoveToMatchSetupAsync();
                var session = _sessionFactory.CreatedSessions.Single();

                _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                await WaitUntilAsync(() => reason != null);

                reason.Should().Be(AbortReason.UserCancel);
                session.DisposeCallCount.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBackIntentPublishedAtMatchSetup_ThenReplacesWithModeSelectionAndDoesNotDisposeSession() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();
            var session = _sessionFactory.CreatedSessions.Single();
            _navigator.ClearHistory();

            _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

            session.DisposeCallCount.Should().Be(0);
            _navigator.CallHistory.Should().ContainInOrder(nameof(Runtime.GameModes.Wizard.Coordinator.IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublishedDuringTransition_ThenCancelsTransitionAndAbortsWizard() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var transitionStarted = new UniTaskCompletionSource<bool>();
            var transitionGate = new UniTaskCompletionSource<bool>();
            CancellationToken transitionToken = default;

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                transitionToken = ct;
                transitionStarted.TrySetResult(true);
                await transitionGate.Task.AttachExternalCancellation(ct);
            };

            AbortReason? reason = null;
            var aborted = _sut.WizardAborted.Subscribe(r => reason = r);

            try
            {
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await transitionStarted.Task;

                _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                await WaitUntilAsync(() => reason != null);

                transitionToken.IsCancellationRequested.Should().BeTrue();
                reason.Should().Be(AbortReason.UserCancel);
            }
            finally
            {
                aborted.Dispose();
                transitionGate.TrySetResult(true);
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortCalledWithActiveAsyncOperations_ThenDisposesSessionAndPublishesWizardAborted() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            var transitionStarted = new UniTaskCompletionSource<bool>();
            var transitionGate = new UniTaskCompletionSource<bool>();
            CancellationToken transitionToken = default;

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                transitionToken = ct;
                transitionStarted.TrySetResult(true);
                await transitionGate.Task.AttachExternalCancellation(ct);
            };

            AbortReason? reason = null;
            var subscription = _sut.WizardAborted.Subscribe(r => reason = r);

            try
            {
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await transitionStarted.Task;

                await _sut.AbortWizardAsync(AbortReason.UserCancel);

                transitionToken.IsCancellationRequested.Should().BeTrue();
                session.DisposeCallCount.Should().Be(1);
                reason.Should().Be(AbortReason.UserCancel);
            }
            finally
            {
                subscription.Dispose();
                transitionGate.TrySetResult(true);
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortCalledMultipleTimes_ThenIsIdempotentAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            Func<Task> act = async () =>
            {
                await _sut.AbortWizardAsync(AbortReason.UserCancel);
                await _sut.AbortWizardAsync(AbortReason.UserCancel);
                await _sut.AbortWizardAsync(AbortReason.UserCancel);
            };

            await act.Should().NotThrowAsync();
            session.DisposeCallCount.Should().Be(1);
        });
    }
}
