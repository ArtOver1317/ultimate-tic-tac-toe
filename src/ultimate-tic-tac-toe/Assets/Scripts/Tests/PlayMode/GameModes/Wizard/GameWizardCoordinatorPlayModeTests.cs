using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class GameWizardCoordinatorPlayModeTests
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
        public IEnumerator WhenTryPublishIntentCalledDuringTransition_ThenRejectsNonCancelIntent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await closeStarted.Task;

            var backAccepted = _sut.TryPublishIntent(WizardIntent.Back);

            // Assert
            backAccepted.Should().BeFalse();

            await _sut.TryAbortBestEffortAsync();
            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishIntentCalledDuringSubmit_ThenRejectsNonCancelIntent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            var backAccepted = _sut.TryPublishIntent(WizardIntent.Back);

            // Assert
            backAccepted.Should().BeFalse();

            _sut.CompleteStartAttempt(false, new WizardError("wizard.start_failed", "Errors.GameWizard.UnhandledException", true, ErrorDisplayType.Modal));
            await WaitUntilAsync(() => !_sut.IsSubmitting.CurrentValue);
            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenIntentSpamOccursWhilePendingIntentExists_ThenOnlyFirstIsAccepted() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Make processing/transition slow so the second publish happens while the first is still pending/in-flight.
            var closeGate = new UniTaskCompletionSource<bool>();
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = ct => closeGate.Task.AttachExternalCancellation(ct);

            // Act
            var first = _sut.TryPublishIntent(WizardIntent.Continue);
            var second = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            first.Should().BeTrue();
            second.Should().BeFalse();

            closeGate.TrySetResult(true);
            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenContinueIntentProcessedInModeSelection_ThenClosesModeSelectionThenOpensMatchSetup() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
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
                // Act
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                // Assert
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
            // Arrange
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
                // Act
                _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

                // Assert
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
        public IEnumerator WhenStartIntentProcessedInMatchSetup_ThenSetsSubmittingTrueAndAbortsOnlyAfterCompletion() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var submittingSeenTrue = false;
            var subscription = _sut.IsSubmitting.Subscribe(v =>
            {
                if (v)
                    submittingSeenTrue = true;
            });

            try
            {
                // Act
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

                _sut.CompleteStartAttempt(true);
                await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);

                // Assert
                submittingSeenTrue.Should().BeTrue();
                _navigator.CloseAllCalls.Should().Be(1);
                _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
            }
            finally
            {
                subscription.Dispose();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBackIntentPublishedInModeSelection_ThenIsConsumedAndDoesNotAffectNextContinueTransition() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                _navigator.ClearHistory();

                // Act 1: wrong-step intent
                _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();

                await PublishIntentWhenReadyAsync(WizardIntent.Continue);
                await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                // Assert
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

                _navigator.CallHistory.Should().NotContain(nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
                _navigator.CloseAllCalls.Should().Be(0);

                await _sut.TryAbortBestEffortAsync();
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStartIntentPublishedInModeSelection_ThenIsConsumedAndDoesNotAbortAndContinueStillTransitions() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                _navigator.ClearHistory();

                // Act 1: wrong-step intent
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();

                await PublishIntentWhenReadyAsync(WizardIntent.Continue);
                await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                // Assert
                _navigator.CloseAllCalls.Should().Be(0, "Start in ModeSelection must be ignored and must not abort wizard");
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

                await _sut.TryAbortBestEffortAsync();
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenContinueIntentPublishedInMatchSetup_ThenIsConsumedAndDoesNotAffectNextBackTransition() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                await MoveToMatchSetupAsync();
                _navigator.ClearHistory();

                // Act 1: wrong-step intent
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();

                await PublishIntentWhenReadyAsync(WizardIntent.Back);
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

                // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("close failed");
            var session = _sessionFactory.CreatedSessions.Single();

            LogAssert.Expect(LogType.Error, new Regex("close failed"));

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("open failed");
            var session = _sessionFactory.CreatedSessions.Single();

            LogAssert.Expect(LogType.Error, new Regex("open failed"));

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();

            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await closeStarted.Task;

            await _sut.AbortWizardAsync(AbortReason.SceneChange);

            // Assert
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenUnhandledExceptionOccursInProcessingLoop_ThenWizardIsAbortedAndLoopStops() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("boom");

            LogAssert.Expect(LogType.Error, new Regex("boom"));

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);

            var acceptedAfterAbort = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            acceptedAfterAbort.Should().BeFalse("wizard should not be ready after abort");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortIsTriggeredFromInsideProcessingLoop_ThenDoesNotDeadlock() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = _ => throw new Exception("boom");

            LogAssert.Expect(LogType.Error, new Regex("boom"));

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            // Assert
            _navigator.CloseAllCalls.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelPublishedWhileContinueIsQueuedButNotProcessed_ThenContinueIsNotExecuted() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Block transition so Cancel can win deterministically.
            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            // Act
            var continueAccepted = _sut.TryPublishIntent(WizardIntent.Continue);

            await closeStarted.Task;
            var cancelAccepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            // Assert
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
            // Arrange
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
                // Act
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

                // Assert
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

        private async UniTask PublishIntentWhenReadyAsync(WizardIntent intent)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            while (!cts.IsCancellationRequested)
            {
                if (_sut.TryPublishIntent(intent))
                    return;

                await UniTask.Yield(PlayerLoopTiming.Update, cts.Token);
            }

            Assert.Fail($"Intent was never accepted within timeout: {intent}");
        }

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortWizardCalledWhileWizardIsActive_ThenCancelsTokensClosesWindowsAndDisposesSession() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            // Act
            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            // Assert
            _navigator.CloseAllCalls.Should().Be(1);
            session.DisposeCallCount.Should().Be(1);
            _sut.IsTransitioning.CurrentValue.Should().BeFalse();
            _sut.IsSubmitting.CurrentValue.Should().BeFalse();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublished_ThenAbortIsTriggeredOutOfBandAndReturnsTrue() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
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

            // Act
            var cancelAccepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            // Assert
            cancelAccepted.Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishCancelConcurrentlyWithAbortWizardAsync_ThenDoesNotThrowAndAbortsOnce() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            // Act
            var cancelTask = Task.Run(() => _sut.TryPublishIntent(WizardIntent.Cancel));
            var abortTask = Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));

            await Task.WhenAll(cancelTask, abortTask);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            // Assert
            cancelTask.Result.Should().BeTrue();
            session.DisposeCallCount.Should().Be(1);
            _navigator.CloseAllCalls.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelOccursDuringTransition_ThenInFlightNavigationIsCancelledAndNextWindowIsNotOpened() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
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

            // Act
            _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.CloseAllCalls == 1);

            // Assert
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(1);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStartIntentProcessedInMatchSetup_ThenIsSubmittingResetsToFalseAndNoFurtherNavigationOccurs() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            // Assert
            _sut.IsSubmitting.CurrentValue.Should().BeFalse();

            var callsAfterAbort = _navigator.TotalCalls;
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("wizard must not accept intents after GameStarted abort");
            _navigator.TotalCalls.Should().Be(callsAfterAbort);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenOpponentIsBotButHumanKindIsMatchmaking_ThenStartDoesNotOpenMatchmaking() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();
            session.Update(s => s
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Bot)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            // Assert
            _navigator.ReplaceMatchSetupWithMatchmakingCalls.Should().Be(0);
            _navigator.ReplaceMatchmakingWithMatchSetupCalls.Should().Be(0);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortCalledFromNonMainThread_ThenStillClosesWindowsOnMainThreadBestEffort() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Act
            await Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));

            // Assert
            _navigator.CloseAllCalls.Should().Be(1);
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBuildLaunchConfigFailsOnStart_ThenDoesNotAbortAndSetsCurrentError() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();
            session.ReturnFailureOnBuildLaunchConfig = true;

            var gameLaunchCount = 0;
            var subscription = _sut.GameLaunchRequested.Subscribe(_ => gameLaunchCount++);

            try
            {
                // Act
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null);

                // Assert
                _sut.IsActive.Should().BeTrue("wizard ������ ���������� �������� ��� validation ������");
                _sut.CurrentError.CurrentValue.Should().NotBeNull();
                _navigator.CloseAllCalls.Should().Be(0);
                session.DisposeCallCount.Should().Be(0);
                gameLaunchCount.Should().Be(0);
            }
            finally
            {
                subscription.Dispose();
                await _sut.TryAbortBestEffortAsync();
            }
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortWizardAsyncCalledOffMainThread_ThenDoesNotThrowAndPublishesWizardAborted() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            AbortReason? published = null;
            var subscription = _sut.WizardAborted.Subscribe(r => published = r);

            try
            {
                // Act
                await Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange));
                await WaitUntilAsync(() => published != null);

                // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            _navigator.CloseAllImpl = async ct =>
            {
                // Must respect cancellation; coordinator uses 2s timeout.
                await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: ct);
            };

            // Act
            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            // Assert
            _navigator.CloseAllCalls.Should().Be(1);
            session.DisposeCallCount.Should().Be(1);
            _sut.IsTransitioning.CurrentValue.Should().BeFalse();
            _sut.IsSubmitting.CurrentValue.Should().BeFalse();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishIntentCalledTwiceQuickly_ThenSecondRejectedDueToPendingInFlightGate() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeGate = new UniTaskCompletionSource<bool>();
            _navigator.ReplaceModeSelectionWithMatchSetupImpl = ct => closeGate.Task.AttachExternalCancellation(ct);

            // Act
            var first = _sut.TryPublishIntent(WizardIntent.Continue);
            var second = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            first.Should().BeTrue();
            second.Should().BeFalse();

            closeGate.TrySetResult(true);
            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenFullFlowModeSelectionToMatchSetupToStart_ThenPublishesGameLaunchRequestedAndAbortsAfterCompletion() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                var launchConfigs = new List<GameLaunchConfig>();
                AbortReason? abortReason = null;

                var launchSub = _sut.GameLaunchRequested.Subscribe(c => launchConfigs.Add(c));
                var abortSub = _sut.WizardAborted.Subscribe(r => abortReason = r);

                try
                {
                    // Act
                    await _sut.StartWizardAsync(CancellationToken.None);
                    await WaitUntilAsync(() => _navigator.OpenModeSelectionCalls == 1);

                    _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                    await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                    _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                    await WaitUntilAsync(() => launchConfigs.Count == 1);

                    _sut.CompleteStartAttempt(true);
                    await WaitUntilAsync(() => abortReason != null);

                    // Assert
                    launchConfigs.Should().HaveCount(1);
                    abortReason.Should().Be(AbortReason.GameStarted);
                }
                finally
                {
                    launchSub.Dispose();
                    abortSub.Dispose();
                }
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublishedAtModeSelection_ThenAbortsWithUserCancelReasonAndDisposesSession() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                AbortReason? reason = null;
                var subscription = _sut.WizardAborted.Subscribe(r => reason = r);

                try
                {
                    await _sut.StartWizardAsync(CancellationToken.None);
                    var session = _sessionFactory.CreatedSessions.Single();

                    // Act
                    _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                    await WaitUntilAsync(() => reason != null);

                    // Assert
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
        public IEnumerator WhenCancelIntentPublishedAtMatchSetup_ThenAbortsWithUserCancelReasonAndDisposesSession() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                AbortReason? reason = null;
                var subscription = _sut.WizardAborted.Subscribe(r => reason = r);

                try
                {
                    await _sut.StartWizardAsync(CancellationToken.None);
                    await MoveToMatchSetupAsync();
                    var session = _sessionFactory.CreatedSessions.Single();

                    // Act
                    _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                    await WaitUntilAsync(() => reason != null);

                    // Assert
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
        public IEnumerator WhenBackIntentPublishedAtMatchSetup_ThenReplacesWithModeSelectionAndDoesNotDisposeSession() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                await MoveToMatchSetupAsync();
                var session = _sessionFactory.CreatedSessions.Single();
                _navigator.ClearHistory();

                // Act
                _sut.TryPublishIntent(WizardIntent.Back).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceMatchSetupWithModeSelectionCalls == 1);

                // Assert
                session.DisposeCallCount.Should().Be(0);
                _navigator.CallHistory.Should().ContainInOrder(nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

                await _sut.TryAbortBestEffortAsync();
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenCancelIntentPublishedDuringTransition_ThenCancelsTransitionAndAbortsWizard() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
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
                    // Act
                    _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                    await transitionStarted.Task;

                    _sut.TryPublishIntent(WizardIntent.Cancel).Should().BeTrue();
                    await WaitUntilAsync(() => reason != null);

                    // Assert
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
        public IEnumerator WhenStartIntentPublishedDuringSubmitInProgress_ThenIgnoresSecondStart() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                await MoveToMatchSetupAsync();

                // Act
                var first = _sut.TryPublishIntent(WizardIntent.Start);
                await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);
                var second = _sut.TryPublishIntent(WizardIntent.Start);

                // Assert
                first.Should().BeTrue();
                second.Should().BeFalse();

                _sut.CompleteStartAttempt(true);
                await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenWizardOpenedAndAborted10Times_ThenAllSessionsDisposedAndNoNavigatorLeaks() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                const int cycles = 10;

                // Act
                for (var i = 0; i < cycles; i++)
                {
                    await _sut.StartWizardAsync(CancellationToken.None);
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);
                }

                // Assert
                _sessionFactory.CreatedSessions.Should().HaveCount(cycles);
                _sessionFactory.CreatedSessions.All(s => s.DisposeCallCount == 1).Should().BeTrue();
                _navigator.OpenModeSelectionCalls.Should().Be(cycles);
                _navigator.CloseAllCalls.Should().Be(cycles);
            });

        [UnityTest]
        [Explicit]
        [Timeout(60000)]
        public IEnumerator WhenWizardOpenedAndAborted100Times_ThenAllSessionsDisposedAndNoNavigatorLeaks() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                const int cycles = 100;

                // Act
                for (var i = 0; i < cycles; i++)
                {
                    await _sut.StartWizardAsync(CancellationToken.None);
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);
                }

                // Assert
                _sessionFactory.CreatedSessions.Should().HaveCount(cycles);
                _sessionFactory.CreatedSessions.All(s => s.DisposeCallCount == 1).Should().BeTrue();
                _navigator.OpenModeSelectionCalls.Should().Be(cycles);
                _navigator.CloseAllCalls.Should().Be(cycles);
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenAbortCalledWithActiveAsyncOperations_ThenDisposesSessionAndPublishesWizardAborted() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
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

                    // Act
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);

                    // Assert
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
        public IEnumerator WhenAbortCalledMultipleTimes_ThenIsIdempotentAndDoesNotThrow() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                await _sut.StartWizardAsync(CancellationToken.None);
                var session = _sessionFactory.CreatedSessions.Single();

                // Act
                Func<Task> act = async () =>
                {
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);
                    await _sut.AbortWizardAsync(AbortReason.UserCancel);
                };

                // Assert
                await act.Should().NotThrowAsync();
                session.DisposeCallCount.Should().Be(1);
            });

        private async UniTask MoveToMatchSetupAsync()
        {
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000, string because = null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Timed out after {timeoutMs}ms waiting for condition" +
                            (string.IsNullOrWhiteSpace(because) ? string.Empty : $": {because}"));
            }
        }
    }
}
