using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
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
        public IEnumerator WhenTryPublishIntentCalledDuringTransition_ThenRejectsNonCancelIntent() => UniTask.ToCoroutine(async () =>
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

            var backAccepted = _sut.TryPublishIntent(WizardIntent.Back);

            backAccepted.Should().BeFalse();

            await _sut.TryAbortBestEffortAsync();
            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenIntentSpamOccursWhilePendingIntentExists_ThenOnlyFirstIsAccepted() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeGate = new UniTaskCompletionSource<bool>();
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = ct => closeGate.Task.AttachExternalCancellation(ct);

            var first = _sut.TryPublishIntent(WizardIntent.Continue);
            var second = _sut.TryPublishIntent(WizardIntent.Continue);

            first.Should().BeTrue();
            second.Should().BeFalse();

            closeGate.TrySetResult(true);
            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenContinueIntentProcessedInModeSelection_ThenClosesModeSelectionThenOpensMatchSetup() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            var trueCount = 0;
            var falseAfterTrueCount = 0;
            var seenTrue = false;

            var subscription = _sut.IsTransitioning.Subscribe(v =>
            {
                if (v)
                {
                    trueCount++;
                    seenTrue = true;
                    return;
                }

                if (seenTrue)
                    falseAfterTrueCount++;
            });

            try
            {
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

                trueCount.Should().Be(1);
                falseAfterTrueCount.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
                await _sut.TryAbortBestEffortAsync();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBackIntentProcessedInMatchSetup_ThenClosesMatchSetupThenOpensModeSelection() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();
            _navigator.ClearHistory();

            var trueCount = 0;
            var falseAfterTrueCount = 0;
            var seenTrue = false;

            var subscription = _sut.IsTransitioning.Subscribe(v =>
            {
                if (v)
                {
                    trueCount++;
                    seenTrue = true;
                    return;
                }

                if (seenTrue)
                    falseAfterTrueCount++;
            });

            try
            {
                _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

                trueCount.Should().Be(1);
                falseAfterTrueCount.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
                await _sut.TryAbortBestEffortAsync();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBackIntentPublishedInModeSelection_ThenIsConsumedAndDoesNotAffectNextContinueTransition() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ClearHistory();

            _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();

            await PublishIntentWhenReadyAsync(WizardIntent.Continue);
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

            _navigator.CallHistory.Should().ContainInOrder(
                nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));
           
            _navigator.CallHistory.Should().NotContain(nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
            _navigator.CloseAllCalls.Should().Be(0);

            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStartIntentPublishedInModeSelection_ThenIsConsumedAndDoesNotAbortAndContinueStillTransitions() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ClearHistory();

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();

            await PublishIntentWhenReadyAsync(WizardIntent.Continue);
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

            _navigator.CloseAllCalls.Should().Be(0, "Start in ModeSelection must be ignored and must not abort wizard");
            
            _navigator.CallHistory.Should().ContainInOrder(
                nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenContinueIntentPublishedInMatchSetup_ThenIsConsumedAndDoesNotAffectNextBackTransition() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();
            _navigator.ClearHistory();

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();

            await PublishIntentWhenReadyAsync(WizardIntent.Back);
            await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

            _navigator.CloseAllCalls.Should().Be(0);
            
            _navigator.CallHistory.Should().ContainInOrder(
                nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
           
            _navigator.CallHistory.Should().NotContain(nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));
            _navigator.CallHistory.Should().NotContain(nameof(IGameWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync));

            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenNavigatorCloseThrowsDuringTransition_ThenSetsCurrentErrorAndAbortsWizard() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("close failed");
            var session = _sessionFactory.CreatedSessions.Single();

            LogAssert.Expect(LogType.Error, new Regex("close failed"));

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue.Code.Should().Be(WizardError.Codes.UnhandledException);
            _sut.CurrentError.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue.IsBlocking.Should().BeTrue();
            _navigator.CloseAllCalls.Should().Be(1);
            session.DisposeCallCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenNavigatorOpenThrowsDuringTransition_ThenSetsCurrentErrorAndAbortsWizard() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("open failed");
            var session = _sessionFactory.CreatedSessions.Single();

            LogAssert.Expect(LogType.Error, new Regex("open failed"));

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue.Code.Should().Be(WizardError.Codes.UnhandledException);
            _sut.CurrentError.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue.IsBlocking.Should().BeTrue();
            _navigator.CloseAllCalls.Should().Be(1);
            _sut.IsTransitioning.CurrentValue.Should().BeFalse();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenNavigatorCloseIsCancelled_ThenDoesNotOpenNextWindow() => UniTask.ToCoroutine(async () =>
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

            await _sut.AbortWizardAsync(AbortReason.SceneChange);

            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            closeGate.TrySetResult(true);
        });
    }
}
