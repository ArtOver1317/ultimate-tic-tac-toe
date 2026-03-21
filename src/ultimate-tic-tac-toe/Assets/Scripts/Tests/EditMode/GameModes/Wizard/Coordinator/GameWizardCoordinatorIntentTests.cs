using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorTests
    {
        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledBeforeWizardIsReady_ThenIsRejected()
        {
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            var accepted = _sut.TryPublishIntent(WizardIntent.Continue);

            accepted.Should().BeFalse();

            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenCancelIntentPublishedBeforeWizardIsReady_ThenAbortStillHappens()
        {
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            var accepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            accepted.Should().BeTrue();
            await _navigator.CloseAllCalled.Task;
            _navigator.CloseAllCalls.Should().BeGreaterThan(0);

            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenDirectInviteStartIntentHandled_ThenCoordinatorDoesNotCallOnlineFlowApis()
        {
            _sut?.Dispose();
            var onlineFlowSpy = new SpyOnlineSessionFlow();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineFlowSpy);

            await _sut.StartWizardAsync(CancellationToken.None);

            var continueAccepted = _sut.TryPublishIntent(WizardIntent.Continue);
            continueAccepted.Should().BeTrue();

            await UniTask.WaitUntil(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls > 0);

            _sut.Session.Update(state => state
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("AB2CD7"));

            var launchRequested = new UniTaskCompletionSource<bool>();
            using var launchSubscription = _sut.GameLaunchRequested.Subscribe(_ => launchRequested.TrySetResult(true));

            var startAccepted = _sut.TryPublishIntent(WizardIntent.Start);

            startAccepted.Should().BeTrue();
            await launchRequested.Task;

            onlineFlowSpy.EnterHumanSetupCalls.Should().Be(0);
            onlineFlowSpy.JoinBySessionIdCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledBeforeWizardIsReady_ThenRejectsNonCancelIntent()
        {
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            var accepted = _sut.TryPublishIntent(WizardIntent.Continue);

            accepted.Should().BeFalse();

            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledBeforeWizardIsReadyAndIntentIsCancel_ThenReturnsTrueAndTriggersAbort()
        {
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            var accepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            accepted.Should().BeTrue();
            await _navigator.CloseAllCalled.Task;
            _navigator.CloseAllCalls.Should().BeGreaterThan(0);

            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledAfterAbort_ThenRejectsNonCancelIntentBecauseWizardNotReady()
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            var accepted = _sut.TryPublishIntent(WizardIntent.Continue);

            accepted.Should().BeFalse();
        }

        [Test]
        public void WhenTryPublishIntentCalledAfterCoordinatorDisposed_ThenThrowsObjectDisposedException()
        {
            _sut.Dispose();

            Action act = () => _sut.TryPublishIntent(WizardIntent.Continue);

            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledConcurrentlyFromMultipleThreads_ThenExactlyOneNonCancelIsAccepted()
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            _navigator.CloseModeSelectionImpl = ct =>
            {
                ct.Register(() => closeGate.TrySetCanceled(ct));
                return closeGate.Task.AsUniTask();
            };

            using var barrier = new Barrier(8);
            var results = new bool[8];

            Task WorkerAsync(int i) =>
                Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    results[i] = _sut.TryPublishIntent(WizardIntent.Continue);
                });

            var tasks = Enumerable.Range(0, 8).Select(WorkerAsync).ToArray();
            await Task.WhenAll(tasks);

            results.Count(r => r).Should().Be(1);

            closeGate.TrySetResult(true);
            await _sut.AbortWizardAsync(AbortReason.SceneChange);
        }
    }
}