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
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class GameModeWizardCoordinatorPlayModeTests
    {
        private SpyWizardNavigator _navigator;
        private SessionFactorySpy _sessionFactory;
        private GameModeWizardCoordinator _sut;

        [SetUp]
        public void SetUp()
        {
            _navigator = new SpyWizardNavigator();
            _sessionFactory = new SessionFactorySpy();
            _sut = new GameModeWizardCoordinator(_navigator, _sessionFactory.Create);
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

            var closeAllStarted = new UniTaskCompletionSource<bool>();
            var closeAllGate = new UniTaskCompletionSource<bool>();

            _navigator.CloseAllImpl = async ct =>
            {
                closeAllStarted.TrySetResult(true);
                await closeAllGate.Task.AttachExternalCancellation(ct);
            };

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await closeAllStarted.Task;

            var backAccepted = _sut.TryPublishIntent(WizardIntent.Back);

            // Assert
            backAccepted.Should().BeFalse();

            closeAllGate.TrySetResult(true);
            await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);
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
                    nameof(IGameModeWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

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
                    nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

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
        public IEnumerator WhenStartIntentProcessedInMatchSetup_ThenSetsSubmittingTrueAndAbortsWithGameStarted() => UniTask.ToCoroutine(async () =>
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
                    nameof(IGameModeWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
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
                    nameof(IGameModeWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));

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
                    nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));
                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync));

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
            _sut.CurrentError.CurrentValue.Code.Should().Be("wizard.unhandled_exception");
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
            _sut.CurrentError.CurrentValue.Code.Should().Be("wizard.unhandled_exception");
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
                .WithSelectedModeId(ClassicModeStrategy.DefaultModeId)
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Bot)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
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
                _sut.IsActive.Should().BeTrue("wizard должен оставаться активным при validation ошибке");
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
        public IEnumerator WhenFullFlowModeSelectionToMatchSetupToStart_ThenPublishesGameLaunchRequestedAndAbortsWithGameStartedReason() =>
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
                _navigator.CallHistory.Should().ContainInOrder(nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));

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

                var closeAllStarted = new UniTaskCompletionSource<bool>();
                var closeAllGate = new UniTaskCompletionSource<bool>();

                _navigator.CloseAllImpl = async ct =>
                {
                    closeAllStarted.TrySetResult(true);
                    await closeAllGate.Task.AttachExternalCancellation(ct);
                };

                // Act
                var first = _sut.TryPublishIntent(WizardIntent.Start);
                await closeAllStarted.Task;
                var second = _sut.TryPublishIntent(WizardIntent.Start);

                // Assert
                first.Should().BeTrue();
                second.Should().BeFalse();

                closeAllGate.TrySetResult(true);
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

        private sealed class SpyWizardNavigator : IGameModeWizardNavigator
        {
            private readonly object _lock = new();

            public Func<CancellationToken, UniTask> OpenModeSelectionImpl;
            public Func<CancellationToken, UniTask> CloseModeSelectionImpl;
            public Func<CancellationToken, UniTask> OpenMatchSetupImpl;
            public Func<CancellationToken, UniTask> CloseMatchSetupImpl;
            public Func<CancellationToken, UniTask<MatchmakingViewModel>> OpenMatchmakingImpl;
            public Func<CancellationToken, UniTask> CloseMatchmakingImpl;
            public Func<CancellationToken, UniTask> ReplaceModeSelectionWithMatchSetupImpl;
            public Func<CancellationToken, UniTask> ReplaceMatchSetupWithModeSelectionImpl;
            public Func<CancellationToken, UniTask<MatchmakingViewModel>> ReplaceMatchSetupWithMatchmakingImpl;
            public Func<CancellationToken, UniTask> ReplaceMatchmakingWithMatchSetupImpl;
            public Func<CancellationToken, UniTask> CloseAllImpl;

            public int OpenModeSelectionCalls { get; private set; }
            public int CloseModeSelectionCalls { get; private set; }
            public int OpenMatchSetupCalls { get; private set; }
            public int CloseMatchSetupCalls { get; private set; }
            public int OpenMatchmakingCalls { get; private set; }
            public int CloseMatchmakingCalls { get; private set; }
            public int ReplaceModeSelectionWithMatchSetupCalls { get; private set; }
            public int ReplaceMatchSetupWithModeSelectionCalls { get; private set; }
            public int ReplaceMatchSetupWithMatchmakingCalls { get; private set; }
            public int ReplaceMatchmakingWithMatchSetupCalls { get; private set; }
            public int CloseAllCalls { get; private set; }

            public int TotalCalls { get; private set; }

            public List<string> CallHistory { get; } = new();

            public SpyWizardNavigator()
            {
                OpenModeSelectionImpl = _ => UniTask.CompletedTask;
                CloseModeSelectionImpl = _ => UniTask.CompletedTask;
                OpenMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseMatchSetupImpl = _ => UniTask.CompletedTask;
                OpenMatchmakingImpl = _ => UniTask.FromResult<MatchmakingViewModel>(null);
                CloseMatchmakingImpl = _ => UniTask.CompletedTask;
                ReplaceModeSelectionWithMatchSetupImpl = _ => UniTask.CompletedTask;
                ReplaceMatchSetupWithModeSelectionImpl = _ => UniTask.CompletedTask;
                ReplaceMatchSetupWithMatchmakingImpl = _ => UniTask.FromResult<MatchmakingViewModel>(null);
                ReplaceMatchmakingWithMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseAllImpl = _ => UniTask.CompletedTask;
            }

            public void ClearHistory()
            {
                lock (_lock)
                {
                    CallHistory.Clear();
                }
            }

            public UniTask OpenModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenModeSelectionCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.OpenModeSelectionAsync));
                }

                return OpenModeSelectionImpl(ct);
            }

            public UniTask CloseModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseModeSelectionCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.CloseModeSelectionAsync));
                }

                return CloseModeSelectionImpl(ct);
            }

            public UniTask OpenMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenMatchSetupCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.OpenMatchSetupAsync));
                }

                return OpenMatchSetupImpl(ct);
            }

            public UniTask CloseMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseMatchSetupCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.CloseMatchSetupAsync));
                }

                return CloseMatchSetupImpl(ct);
            }

            public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenMatchmakingCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.OpenMatchmakingAsync));
                }

                return OpenMatchmakingImpl(ct);
            }

            public UniTask CloseMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseMatchmakingCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.CloseMatchmakingAsync));
                }

                return CloseMatchmakingImpl(ct);
            }

            public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceModeSelectionWithMatchSetupCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));
                }

                return ReplaceModeSelectionWithMatchSetupImpl(ct);
            }

            public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchSetupWithModeSelectionCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
                }

                return ReplaceMatchSetupWithModeSelectionImpl(ct);
            }

            public UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchSetupWithMatchmakingCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync));
                }

                return ReplaceMatchSetupWithMatchmakingImpl(ct);
            }

            public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    ReplaceMatchmakingWithMatchSetupCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.ReplaceMatchmakingWithMatchSetupAsync));
                }

                return ReplaceMatchmakingWithMatchSetupImpl(ct);
            }

            public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseAllCalls++;
                    TotalCalls++;
                    CallHistory.Add(nameof(IGameModeWizardNavigator.CloseAllWizardWindowsAsync));
                }

                return CloseAllImpl(ct);
            }
        }

        private sealed class SessionFactorySpy
        {
            public readonly List<FakeGameModeSession> CreatedSessions = new();

            public IGameModeSession Create()
            {
                var session = new FakeGameModeSession();
                CreatedSessions.Add(session);
                return session;
            }
        }

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly R3.ReactiveProperty<GameModeSessionSnapshot> _snapshot = new(GameModeSessionSnapshot.Default);
            private readonly R3.ReactiveProperty<bool> _canStart = new(false);
            private readonly R3.ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors = new(Array.Empty<ValidationError>());

            public bool ReturnFailureOnBuildLaunchConfig { get; set; }

            public int DisposeCallCount { get; private set; }

            public R3.ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public R3.ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public R3.ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig()
            {
                if (ReturnFailureOnBuildLaunchConfig)
                {
                    return Result<GameLaunchConfig>.Failure(
                        new ValidationError("wizard.validation_failed", "Errors.GameModeWizard.UnhandledException"));
                }

                var snapshot = _snapshot.Value;

                var modeId = string.IsNullOrWhiteSpace(snapshot.SelectedModeId)
                    ? ClassicModeStrategy.DefaultModeId
                    : snapshot.SelectedModeId;

                var modeConfig = snapshot.ModeConfig ?? new ClassicModeConfig(3);

                IOpponentConfig opponentConfig;

                switch (snapshot.OpponentType)
                {
                    case OpponentType.Bot:
                        opponentConfig = new BotOpponentConfig(snapshot.BotDifficultyId ?? "Easy");
                        break;

                    case OpponentType.Human:
                        switch (snapshot.HumanOpponentKind)
                        {
                            case HumanOpponentKind.Local:
                                opponentConfig = new LocalHumanConfig();
                                break;

                            case HumanOpponentKind.DirectInvite:
                                opponentConfig = new DirectInviteConfig(snapshot.TargetPlayerId ?? "TestPlayer");
                                break;

                            case HumanOpponentKind.Matchmaking:
                                opponentConfig = new MatchmakingConfig("Match", "Opponent");
                                break;

                            default:
                                opponentConfig = new LocalHumanConfig();
                                break;
                        }

                        break;

                    default:
                        opponentConfig = new LocalHumanConfig();
                        break;
                }

                return Result<GameLaunchConfig>.Success(new GameLaunchConfig(modeId, modeConfig, opponentConfig));
            }

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose() => DisposeCallCount++;
        }
    }

    internal static class GameModeWizardCoordinatorTestExtensions
    {
        public static async UniTask TryAbortBestEffortAsync(this GameModeWizardCoordinator coordinator)
        {
            try
            {
                await coordinator.AbortWizardAsync(AbortReason.SceneChange);
            }
            catch
            {
                // Best-effort cleanup in tests.
            }
        }
    }
}
