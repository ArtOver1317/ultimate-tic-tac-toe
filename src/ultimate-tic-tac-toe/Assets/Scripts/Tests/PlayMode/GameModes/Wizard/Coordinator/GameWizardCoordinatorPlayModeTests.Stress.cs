using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Coordinator;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorPlayModeTests
    {
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishIntentCalledTwiceQuickly_ThenSecondRejectedDueToPendingInFlightGate() => UniTask.ToCoroutine(async () =>
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
        public IEnumerator WhenWizardOpenedAndAborted10Times_ThenAllSessionsDisposedAndNoNavigatorLeaks() => UniTask.ToCoroutine(async () =>
        {
            const int cycles = 10;

            for (var i = 0; i < cycles; i++)
            {
                await _sut.StartWizardAsync(CancellationToken.None);
                await _sut.AbortWizardAsync(AbortReason.UserCancel);
            }

            _sessionFactory.CreatedSessions.Should().HaveCount(cycles);
            _sessionFactory.CreatedSessions.All(s => s.DisposeCallCount == 1).Should().BeTrue();
            _navigator.OpenModeSelectionCalls.Should().Be(cycles);
            _navigator.CloseAllCalls.Should().Be(cycles);
        });

        [UnityTest]
        [Explicit]
        [Timeout(60000)]
        public IEnumerator WhenWizardOpenedAndAborted100Times_ThenAllSessionsDisposedAndNoNavigatorLeaks() => UniTask.ToCoroutine(async () =>
        {
            const int cycles = 100;

            for (var i = 0; i < cycles; i++)
            {
                await _sut.StartWizardAsync(CancellationToken.None);
                await _sut.AbortWizardAsync(AbortReason.UserCancel);
            }

            _sessionFactory.CreatedSessions.Should().HaveCount(cycles);
            _sessionFactory.CreatedSessions.All(s => s.DisposeCallCount == 1).Should().BeTrue();
            _navigator.OpenModeSelectionCalls.Should().Be(cycles);
            _navigator.CloseAllCalls.Should().Be(cycles);
        });
    }
}
