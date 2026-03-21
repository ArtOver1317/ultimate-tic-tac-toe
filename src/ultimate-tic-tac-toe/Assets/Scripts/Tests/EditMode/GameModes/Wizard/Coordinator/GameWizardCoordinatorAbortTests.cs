using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorTests
    {
        [Test]
        [Timeout(5000)]
        public async Task WhenAbortReasonIsSceneChange_ThenCoordinatorDoesNotCallOnlineFlowExit()
        {
            _sut?.Dispose();
            var onlineFlowSpy = new SpyOnlineSessionFlow();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineFlowSpy);
            await _sut.StartWizardAsync(CancellationToken.None);

            await _sut.AbortWizardAsync(AbortReason.SceneChange);

            onlineFlowSpy.ExitCalls.Should().Be(0);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortReasonIsUserCancel_ThenCoordinatorCallsOnlineFlowExit()
        {
            _sut?.Dispose();
            var onlineFlowSpy = new SpyOnlineSessionFlow();
            _sut = new GameWizardCoordinator(_navigator, _sessionFactory.Create, onlineFlowSpy);
            await _sut.StartWizardAsync(CancellationToken.None);

            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            onlineFlowSpy.ExitCalls.Should().Be(1);
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotentAndDoesNotThrow()
        {
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
            };

            act.Should().NotThrow();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenDisposeCalledWhileWizardIsActive_ThenAbortsBestEffortAndDisposesSession()
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            _sut.Dispose();

            _sessionFactory.CreatedSessions.Should().ContainSingle();
            await _sessionFactory.CreatedSessions.Single().Disposed.Task;
            _sessionFactory.CreatedSessions.Single().DisposeCallCount.Should().Be(1);

            Action act = () => _sut.TryPublishIntent(WizardIntent.Continue);
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public async Task WhenAbortWizardCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            _sut.Dispose();

            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.UserCancel);

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Test]
        public async Task WhenAbortWizardCalledWhileWizardIsNotActive_ThenIsNoOpAndDoesNotThrow()
        {
            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.SceneChange);

            await act.Should().NotThrowAsync();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortReasonIsUserCancel_ThenCloseAllIsInvokedAndNoErrorIsSet()
        {
            await _sut.StartWizardAsync(CancellationToken.None);

            await _sut.AbortWizardAsync(AbortReason.UserCancel);

            _navigator.CloseAllCalls.Should().Be(1);
            _sut.CurrentError.CurrentValue.Should().BeNull();
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortWizardCalledConcurrentlyMultipleTimes_ThenDoesNotThrowAndSessionDisposedExactlyOnce()
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();
            var closeStarted = new UniTaskCompletionSource<bool>();
            var closeGate = new UniTaskCompletionSource<bool>();

            _navigator.CloseAllImpl = async ct =>
            {
                closeStarted.TrySetResult(true);
                await closeGate.Task.AttachExternalCancellation(ct);
            };

            var firstAbortTask = _sut.AbortWizardAsync(AbortReason.SceneChange).AsTask();
            await closeStarted.Task.AsTask();

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => _sut.AbortWizardAsync(AbortReason.SceneChange).AsTask())
                .ToArray();

            closeGate.TrySetResult(true);
            await Task.WhenAll(tasks.Prepend(firstAbortTask));

            session.DisposeCallCount.Should().Be(1);
            _navigator.CloseAllCalls.Should().Be(1);
        }

        [Test]
        [Timeout(5000)]
        public async Task WhenAbortWizardCalledAndCloseAllThrows_ThenDoesNotThrowSetsCurrentErrorAndDisposesSession()
        {
            _navigator.CloseAllImpl = _ => throw new Exception("close all failed");
            await _sut.StartWizardAsync(CancellationToken.None);
            var session = _sessionFactory.CreatedSessions.Single();

            Func<Task> act = async () => await _sut.AbortWizardAsync(AbortReason.SceneChange);

            LogAssert.Expect(LogType.Error, new Regex("close all failed"));
            await act.Should().NotThrowAsync();
            _sut.CurrentError.CurrentValue.Should().NotBeNull();
            _sut.CurrentError.CurrentValue.Code.Should().Be(WizardError.Codes.UnhandledException);
            _sut.CurrentError.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Modal);
            _sut.CurrentError.CurrentValue.IsBlocking.Should().BeTrue();
            session.DisposeCallCount.Should().Be(1);

            _navigator.CloseAllImpl = _ => UniTask.CompletedTask;
            _sut.Dispose();
            _sut = null;
        }
    }
}