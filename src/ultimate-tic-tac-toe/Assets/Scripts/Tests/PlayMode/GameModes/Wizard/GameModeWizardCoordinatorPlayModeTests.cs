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

            _navigator.CloseModeSelectionImpl = async ct =>
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
            _navigator.CloseModeSelectionImpl = ct => closeGate.Task.AttachExternalCancellation(ct);

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
                await WaitUntilAsync(() => _navigator.OpenMatchSetupCalls == 1);

                // Assert
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameModeWizardNavigator.CloseModeSelectionAsync),
                    nameof(IGameModeWizardNavigator.OpenMatchSetupAsync));

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
                await WaitUntilAsync(() => _navigator.OpenModeSelectionCalls >= 2);

                // Assert
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameModeWizardNavigator.CloseMatchSetupAsync),
                    nameof(IGameModeWizardNavigator.OpenModeSelectionAsync));

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

                // Deterministic: wait until wrong intent is consumed (gate released)
                await WaitUntilIntentIsAcceptedAsync(WizardIntent.Continue);
                await WaitUntilAsync(() => _navigator.OpenMatchSetupCalls == 1);

                // Assert
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameModeWizardNavigator.CloseModeSelectionAsync),
                    nameof(IGameModeWizardNavigator.OpenMatchSetupAsync));

                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.CloseMatchSetupAsync));
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

                // Deterministic: wait until wrong intent is consumed
                await WaitUntilIntentIsAcceptedAsync(WizardIntent.Continue);
                await WaitUntilAsync(() => _navigator.OpenMatchSetupCalls == 1);

                // Assert
                _navigator.CloseAllCalls.Should().Be(0, "Start in ModeSelection must be ignored and must not abort wizard");
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameModeWizardNavigator.CloseModeSelectionAsync),
                    nameof(IGameModeWizardNavigator.OpenMatchSetupAsync));

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

                // Deterministic: wait until wrong intent is consumed
                await WaitUntilIntentIsAcceptedAsync(WizardIntent.Back);
                await WaitUntilAsync(() => _navigator.OpenModeSelectionCalls == 2);

                // Assert
                _navigator.CloseAllCalls.Should().Be(0);
                _navigator.CallHistory.Should().ContainInOrder(
                    nameof(IGameModeWizardNavigator.CloseMatchSetupAsync),
                    nameof(IGameModeWizardNavigator.OpenModeSelectionAsync));

                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.CloseModeSelectionAsync));
                _navigator.CallHistory.Should().NotContain(nameof(IGameModeWizardNavigator.OpenMatchSetupAsync));

                await _sut.TryAbortBestEffortAsync();
            });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenNavigatorCloseThrowsDuringTransition_ThenSetsCurrentErrorAndAbortsWizard() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.CloseModeSelectionImpl = _ => throw new Exception("close failed");
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

            _navigator.OpenMatchSetupImpl = _ => throw new Exception("open failed");
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

            _navigator.CloseModeSelectionImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            // Act
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await closeStarted.Task;

            await _sut.AbortWizardAsync(AbortReason.SceneChange);

            // Assert
            _navigator.OpenMatchSetupCalls.Should().Be(0);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenUnhandledExceptionOccursInProcessingLoop_ThenWizardIsAbortedAndLoopStops() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            _navigator.CloseModeSelectionImpl = _ => throw new Exception("boom");

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
            _navigator.CloseModeSelectionImpl = _ => throw new Exception("boom");

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
            _navigator.CloseModeSelectionImpl = async ct =>
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
            _navigator.OpenMatchSetupCalls.Should().Be(0);

            closeGate.TrySetResult(true);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenProcessingLoopFaults_ThenNoLateNavigationOccursAfterAbort() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            var openFailedLogs = new List<string>();
            var oldIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception && condition != null && condition.Contains("open failed"))
                    openFailedLogs.Add(condition);
            }

            Application.logMessageReceived += OnLog;

            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();
            var openFinished = new UniTaskCompletionSource<bool>();

            _navigator.OpenMatchSetupImpl = async _ =>
            {
                openStarted.TrySetResult(true);

                try
                {
                    await openGate.Task;
                    throw new Exception("open failed");
                }
                finally
                {
                    openFinished.TrySetResult(true);
                }
            };

            try
            {
                // Act
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await openStarted.Task;

                await _sut.AbortWizardAsync(AbortReason.SceneChange);
                await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);
                var callsAfterAbort = _navigator.TotalCalls;

                openGate.TrySetResult(true);
                await openFinished.Task;

                // Assert
                openFailedLogs.Should().NotBeEmpty("processing loop exception must be logged");
                _navigator.TotalCalls.Should().Be(callsAfterAbort);
                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("wizard must not become ready again after abort");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                LogAssert.ignoreFailingMessages = oldIgnoreFailingMessages;
                await _sut.TryAbortBestEffortAsync();
            }
        });

        private UniTask WaitUntilIntentIsAcceptedAsync(WizardIntent intent) =>
            WaitUntilAsync(() => _sut.TryPublishIntent(intent));

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
            _navigator.CloseModeSelectionImpl = async ct =>
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
            _navigator.CloseModeSelectionImpl = async ct =>
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
            _navigator.OpenMatchSetupCalls.Should().Be(0);

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

        private async UniTask MoveToMatchSetupAsync()
        {
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
            await WaitUntilAsync(() => _navigator.OpenMatchSetupCalls == 1);
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate)
        {
            while (!predicate())
                await UniTask.Yield();
        }

        private sealed class SpyWizardNavigator : IGameModeWizardNavigator
        {
            private readonly object _lock = new();

            public Func<CancellationToken, UniTask> OpenModeSelectionImpl;
            public Func<CancellationToken, UniTask> CloseModeSelectionImpl;
            public Func<CancellationToken, UniTask> OpenMatchSetupImpl;
            public Func<CancellationToken, UniTask> CloseMatchSetupImpl;
            public Func<CancellationToken, UniTask> CloseAllImpl;

            public int OpenModeSelectionCalls { get; private set; }
            public int CloseModeSelectionCalls { get; private set; }
            public int OpenMatchSetupCalls { get; private set; }
            public int CloseMatchSetupCalls { get; private set; }
            public int CloseAllCalls { get; private set; }

            public int TotalCalls { get; private set; }

            public List<string> CallHistory { get; } = new();

            public SpyWizardNavigator()
            {
                OpenModeSelectionImpl = _ => UniTask.CompletedTask;
                CloseModeSelectionImpl = _ => UniTask.CompletedTask;
                OpenMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseMatchSetupImpl = _ => UniTask.CompletedTask;
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

            public int DisposeCallCount { get; private set; }

            public R3.ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public R3.ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public R3.ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

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
