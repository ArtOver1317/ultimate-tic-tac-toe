using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;

namespace Tests.EditMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorTests
    {
        [Test]
        public void WhenWizardErrorFromExceptionCalled_ThenReturnsBlockingModalErrorWithExpectedCode()
        {
            var ex = new Exception("boom");

            var error = WizardError.FromException(ex);

            error.Code.Should().Be(WizardError.Codes.UnhandledException);
            error.MessageKey.Should().Be("Errors.GameWizard.UnhandledException");
            error.IsBlocking.Should().BeTrue();
            error.DisplayType.Should().Be(ErrorDisplayType.Modal);
        }

        [Test]
        public void WhenSessionAccessedWhileWizardIsNotActive_ThenThrowsInvalidOperationException()
        {
            Action act = () => _ = _sut.Session;

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenTryGetSessionCalledWhileWizardIsNotActive_ThenReturnsFalseAndNullSession()
        {
            var ok = _sut.TryGetSession(out var session);

            ok.Should().BeFalse();
            session.Should().BeNull();
            _sut.IsActive.Should().BeFalse();
        }

        [Test]
        public async Task WhenStartWizardCalledWithAlreadyCancelledToken_ThenThrowsOperationCanceledExceptionAndDoesNotCreateSession()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () => await _sut.StartWizardAsync(cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _sessionFactory.CreatedSessions.Should().BeEmpty();
            _navigator.OpenModeSelectionCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalled_ThenCreatesSessionAndOpensModeSelection()
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
            _sut.Session.Should().NotBeNull();
            _sut.IsActive.Should().BeTrue();
            _sut.TryGetSession(out var session).Should().BeTrue();
            session.Should().NotBeNull();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledWhileWizardIsAlreadyActive_ThenIsNoOpAndDoesNotCreateNewSession()
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await _sut.StartWizardAsync(CancellationToken.None);

            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardAsyncCalledTwice_ThenSecondCallIsNoOp()
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await _sut.StartWizardAsync(CancellationToken.None);

            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledConcurrently_ThenCreatesSingleSessionAndOpensOnce()
        {
            using var barrier = new Barrier(2);

            Task StartAsync() =>
                Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    await _sut.StartWizardAsync(CancellationToken.None);
                });

            await Task.WhenAll(StartAsync(), StartAsync());

            _sessionFactory.CreatedSessions.Should().HaveCount(1);
            _navigator.OpenModeSelectionCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenSessionFactoryThrows_ThenStartThrowsAndWizardDoesNotEnterZombieState()
        {
            _sessionFactory.ThrowOnCreate = new InvalidOperationException("factory failed");

            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

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
            _sessionFactory.ReturnNull = true;

            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _navigator.OpenModeSelectionCalls.Should().Be(0);

            Action sessionAccess = () => _ = _sut.Session;
            sessionAccess.Should().Throw<InvalidOperationException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCancelledBeforeFirstWindowOpens_ThenAbortsWithStartCancelledAndDisposesSession()
        {
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
            cts.Cancel();

            Func<Task> act = async () => await startTask;

            await act.Should().ThrowAsync<OperationCanceledException>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardCalledAndThenAbortWizardCalledBeforeOpenCompletes_ThenStartCompletesWithoutZombieState()
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

            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);
            await startTask.ContinueWith(_ => { });

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("after abort wizard should not be ready");

            await _sut.StartWizardAsync(CancellationToken.None);
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue("wizard should be startable again");
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortTriggersDuringStartAndOpenEventuallyCompletes_ThenNoLateNavigationOccurs()
        {
            var openStarted = new UniTaskCompletionSource<bool>();
            var openGate = new UniTaskCompletionSource<bool>();

            _navigator.OpenModeSelectionImpl = async _ =>
            {
                openStarted.TrySetResult(true);
                await openGate.Task;
            };

            var startTask = _sut.StartWizardAsync(CancellationToken.None).AsTask();
            await openStarted.Task.AsTask();

            await _sut.AbortWizardAsync(AbortReason.SceneChange);
            openGate.TrySetResult(true);
            await startTask;

            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse();
            _navigator.ReplaceModeSelectionWithMatchSetupCalls.Should().Be(0);
            _navigator.CloseModeSelectionCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardNavigatorOpenThrows_ThenAbortsWithErrorAndDisposesSession()
        {
            _navigator.OpenModeSelectionImpl = _ => throw new Exception("open failed");

            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            await act.Should().ThrowAsync<Exception>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);

            Action sessionAccess = () => _ = _sut.Session;
            sessionAccess.Should().Throw<InvalidOperationException>();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenStartWizardAsyncCancelledDuringOpenFirstWindow_ThenAbortsWithStartCancelledAndCleansUp()
        {
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

            cts.Cancel();

            Func<Task> act = async () => await startTask;

            await act.Should().ThrowAsync<OperationCanceledException>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);

            Action sessionAccess = () => _ = _sut.Session;
            sessionAccess.Should().Throw<InvalidOperationException>();

            openGate.TrySetResult(true);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenNavigatorOpenModeSelectionThrows_ThenAbortsWithErrorAndDisposesSession()
        {
            _navigator.OpenModeSelectionImpl = _ => throw new InvalidOperationException("open failed");

            Func<Task> act = async () => await _sut.StartWizardAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _sessionFactory.CreatedSessions.Should().ContainSingle();
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenNavigatorOpenModeSelectionSucceeds_ThenWizardBecomesReadyOnlyAfterOpenCompletes()
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

            var beforeOpenCompletes = _sut.TryPublishIntent(WizardIntent.Continue);
            openGate.TrySetResult(true);
            await startTask;
            var afterOpenCompletes = _sut.TryPublishIntent(WizardIntent.Continue);

            beforeOpenCompletes.Should().BeFalse();
            afterOpenCompletes.Should().BeTrue();
        }
    }
}