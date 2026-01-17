using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameModeWizardCoordinatorTests
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

        [Test]
        public void WhenWizardErrorFromExceptionCalled_ThenReturnsBlockingModalErrorWithExpectedCode()
        {
            // Arrange
            var ex = new Exception("boom");

            // Act
            var error = WizardError.FromException(ex);

            // Assert
            error.Code.Should().Be("wizard.unhandled_exception");
            error.Message.Should().NotBeNullOrWhiteSpace();
            error.IsBlocking.Should().BeTrue();
            error.DisplayType.Should().Be(ErrorDisplayType.Modal);
        }

        [Test]
        public void WhenSessionAccessedWhileWizardIsNotActive_ThenThrowsInvalidOperationException()
        {
            // Arrange
            // (no start)

            // Act
            Action act = () => _ = _sut.Session;

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public async Task WhenStartWizardCalledWithAlreadyCancelledToken_ThenThrowsOperationCanceledExceptionAndDoesNotCreateSession()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> act = async () => await _sut.StartWizardAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _sessionFactory.CreatedSessions.Should().BeEmpty();
            _navigator.OpenModeSelectionCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalled_ThenCreatesSessionAndOpensModeSelection()
        {
            // Arrange

            // Act
            await _sut.StartWizardAsync(CancellationToken.None);

            // Assert
            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
            _sut.Session.Should().NotBeNull();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledWhileWizardIsAlreadyActive_ThenIsNoOpAndDoesNotCreateNewSession()
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Act
            await _sut.StartWizardAsync(CancellationToken.None);

            // Assert
            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledConcurrently_ThenCreatesSingleSessionAndOpensOnce()
        {
            // Arrange
            using var barrier = new Barrier(2);

            Task StartAsync()
            {
                return Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    await _sut.StartWizardAsync(CancellationToken.None);
                });
            }

            // Act
            await Task.WhenAll(StartAsync(), StartAsync());

            // Assert
            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenSessionFactoryThrows_ThenStartThrowsAndWizardDoesNotEnterZombieState()
        {
            // Arrange
            _sessionFactory.ThrowOnCreate = new InvalidOperationException("factory failed");

            // Act
            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();

            _sessionFactory.ThrowOnCreate = null;
            await _sut.StartWizardAsync(CancellationToken.None);

            _navigator.OpenModeSelectionCalls.Should().Be(1);
            _sessionFactory.CreatedSessions.Should().HaveCount(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledAndSessionFactoryReturnsNull_ThenThrowsInvalidOperationExceptionAndDoesNotLeakWizard()
        {
            // Arrange
            _sessionFactory.ReturnNull = true;

            // Act
            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
            _navigator.OpenModeSelectionCalls.Should().Be(0);

            Action sessionAccess = () => _ = _sut.Session;
            sessionAccess.Should().Throw<InvalidOperationException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCancelledBeforeFirstWindowOpens_ThenAbortsWithStartCancelledAndDisposesSession()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            using var cts = new CancellationTokenSource();
            var startTask = _sut.StartWizardAsync(cts.Token).AsTask();

            await openStarted.Task.AsTask();

            // Act
            cts.Cancel();

            // Assert
            Func<Task> act = async () => await startTask;
            await act.Should().ThrowAsync<OperationCanceledException>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledAndThenAbortWizardCalledBeforeOpenCompletes_ThenStartCompletesWithoutZombieState()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            // Act
            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);

            // Assert
            await startTask.ContinueWith(_ => { });

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("after abort wizard should not be ready");

            await _sut.StartWizardAsync(CancellationToken.None);
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue("wizard should be startable again");
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortTriggersDuringStartAndOpenEventuallyCompletes_ThenNoLateNavigationOccurs()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async _ =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task;
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            // Act
            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);

            // Assert
            await startTask;
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse();
            _navigator.OpenMatchSetupCalls.Should().Be(0);
            _navigator.CloseModeSelectionCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardNavigatorOpenThrows_ThenAbortsWithErrorAndDisposesSession()
        {
            // Arrange
            _navigator.OpenModeSelectionImpl = _ => throw new Exception("open failed");

            // Act
            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<Exception>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);

            Action sessionAccess = () => _ = _sut.Session;
            sessionAccess.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotentAndDoesNotThrow()
        {
            // Arrange

            // Act
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenDisposeCalledWhileWizardIsActive_ThenAbortsBestEffortAndDisposesSession()
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Act
            _sut.Dispose();

            // Assert
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            await _sessionFactory.CreatedSessions.Single().Disposed.Task;
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);

            Action act = () => _sut.TryPublishIntent(WizardIntent.Continue);
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public async Task WhenAbortWizardCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.UserCancel);

            // Assert
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledBeforeWizardIsReady_ThenRejectsNonCancelIntent()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            // Act
            var accepted = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            accepted.Should().BeFalse();

            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenNavigatorOpenModeSelectionSucceeds_ThenWizardBecomesReadyOnlyAfterOpenCompletes()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            // Act
            var beforeOpenCompletes = _sut.TryPublishIntent(WizardIntent.Continue);
            openGate.TrySetResult(true);
            await startTask;
            var afterOpenCompletes = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            beforeOpenCompletes.Should().BeFalse();
            afterOpenCompletes.Should().BeTrue();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledBeforeWizardIsReadyAndIntentIsCancel_ThenReturnsTrueAndTriggersAbort()
        {
            // Arrange
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async ct =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task.AttachExternalCancellation(ct);
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            // Act
            var accepted = _sut.TryPublishIntent(WizardIntent.Cancel);

            // Assert
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
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            // Act
            var accepted = _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            accepted.Should().BeFalse();
        }

        [Test]
        public void WhenTryPublishIntentCalledAfterCoordinatorDisposed_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _sut.TryPublishIntent(WizardIntent.Continue);

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenTryPublishIntentCalledConcurrentlyFromMultipleThreads_ThenExactlyOneNonCancelIsAccepted()
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            var closeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigator.CloseModeSelectionImpl = ct =>
            {
                ct.Register(() => closeGate.TrySetCanceled(ct));
                return closeGate.Task.AsUniTask();
            };

            using var barrier = new Barrier(8);
            var results = new bool[8];

            Task WorkerAsync(int i)
            {
                return Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    results[i] = _sut.TryPublishIntent(WizardIntent.Continue);
                });
            }

            // Act
            var tasks = Enumerable.Range(0, 8).Select(WorkerAsync).ToArray();
            await Task.WhenAll(tasks);

            // Assert
            results.Count(r => r).Should().Be(1);

            closeGate.TrySetResult(true);
            await _sut.AbortWizardAsync(AbortReason.SceneChange);
        }



        [Test]
        public async Task WhenAbortWizardCalledWhileWizardIsNotActive_ThenIsNoOpAndDoesNotThrow()
        {
            // Arrange

            // Act
            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.SceneChange);

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortReasonIsUserCancel_ThenCloseAllIsInvokedAndNoErrorIsSet()
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);

            // Act
            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            // Assert
            _navigator.CloseAllCalls.Should().Be(1);
            _sut.CurrentError.CurrentValue.Should().BeNull();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortWizardCalledConcurrentlyMultipleTimes_ThenDoesNotThrowAndSessionDisposedExactlyOnce()
        {
            // Arrange
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            // Act
            var tasks = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () => await _sut.AbortWizardAsync(AbortReason.SceneChange)))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            session.DisposeCallCount.Should().Be(1);
            _navigator.CloseAllCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortWizardCalledAndCloseAllThrows_ThenDoesNotThrowSetsCurrentErrorAndDisposesSession()
        {
            // Arrange
            _navigator.CloseAllImpl = _ => throw new Exception("close all failed");
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            // Act
            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.SceneChange);

            // Assert
            LogAssert.Expect(LogType.Error, new Regex("close all failed"));
            await act.Should().NotThrowAsync();
            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue.Code.Should().Be("wizard.unhandled_exception");
            _sut.CurrentError.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue.IsBlocking.Should().BeTrue();
            session.DisposeCallCount.Should().Be(1);

            // Prevent TearDown from triggering a second best-effort Abort/CloseAll that would log again.
            _navigator.CloseAllImpl = _ => UniTask.CompletedTask;
            _sut.Dispose();
            _sut = null;
        }

        private sealed class SpyWizardNavigator : IGameModeWizardNavigator
        {
            private readonly object _lock = new();

            public TaskCompletionSource<bool> CloseAllCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

            public List<string> CallHistory { get; } = new();

            public SpyWizardNavigator()
            {
                OpenModeSelectionImpl = _ => UniTask.CompletedTask;
                CloseModeSelectionImpl = _ => UniTask.CompletedTask;
                OpenMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseMatchSetupImpl = _ => UniTask.CompletedTask;
                CloseAllImpl = _ => UniTask.CompletedTask;
            }

            public UniTask OpenModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenModeSelectionCalls++;
                    CallHistory.Add(nameof(OpenModeSelectionAsync));
                }

                return OpenModeSelectionImpl(ct);
            }

            public UniTask CloseModeSelectionAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseModeSelectionCalls++;
                    CallHistory.Add(nameof(CloseModeSelectionAsync));
                }

                return CloseModeSelectionImpl(ct);
            }

            public UniTask OpenMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    OpenMatchSetupCalls++;
                    CallHistory.Add(nameof(OpenMatchSetupAsync));
                }

                return OpenMatchSetupImpl(ct);
            }

            public UniTask CloseMatchSetupAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseMatchSetupCalls++;
                    CallHistory.Add(nameof(CloseMatchSetupAsync));
                }

                return CloseMatchSetupImpl(ct);
            }

            public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
            {
                lock (_lock)
                {
                    CloseAllCalls++;
                    CallHistory.Add(nameof(CloseAllWizardWindowsAsync));
                }

                CloseAllCalled.TrySetResult(true);

                return CloseAllImpl(ct);
            }
        }

        private sealed class SessionFactorySpy
        {
            public readonly List<FakeGameModeSession> CreatedSessions = new();
            public Exception ThrowOnCreate;
            public bool ReturnNull;

            public IGameModeSession Create()
            {
                if (ThrowOnCreate != null)
                    throw ThrowOnCreate;

                if (ReturnNull)
                    return null;

                var session = new FakeGameModeSession();
                CreatedSessions.Add(session);
                return session;
            }
        }

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly R3.ReactiveProperty<GameModeSessionSnapshot> _snapshot =
                new(GameModeSessionSnapshot.Default);

            private readonly R3.ReactiveProperty<bool> _canStart = new(false);
            private readonly R3.ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors = new(Array.Empty<ValidationError>());

            public int DisposeCallCount { get; private set; }
            public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public R3.ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public R3.ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public R3.ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose()
            {
                DisposeCallCount++;
                Disposed.TrySetResult(true);
            }
        }
    }
}
