using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Coordinator;
using Tests.PlayMode.GameModes.Wizard.Fixtures;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    [TestFixture]
    [Category("Integration")]
    public partial class GameWizardCoordinatorPlayModeTests
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
