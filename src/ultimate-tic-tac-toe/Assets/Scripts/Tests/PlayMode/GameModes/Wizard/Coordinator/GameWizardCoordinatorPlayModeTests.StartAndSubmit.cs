using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Coordinator
{
    public partial class GameWizardCoordinatorPlayModeTests
    {
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenTryPublishIntentCalledDuringSubmit_ThenRejectsNonCancelIntent() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            var backAccepted = _sut.TryPublishIntent(WizardIntent.Back);

            backAccepted.Should().BeFalse();

            _sut.CompleteStartAttempt(false, new WizardError("wizard.start_failed", "Errors.GameWizard.UnhandledException", true, ErrorDisplayType.Modal));
            await WaitUntilAsync(() => !_sut.IsSubmitting.CurrentValue);
            await _sut.TryAbortBestEffortAsync();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStartIntentProcessedInMatchSetup_ThenSetsSubmittingTrueAndAbortsOnlyAfterCompletion() => UniTask.ToCoroutine(async () =>
        {
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
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

                _sut.CompleteStartAttempt(true);
                await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);

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
        public IEnumerator WhenStartIntentProcessedInMatchSetup_ThenIsSubmittingResetsToFalseAndNoFurtherNavigationOccurs() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            _sut.IsSubmitting.CurrentValue.Should().BeFalse();

            var callsAfterAbort = _navigator.TotalCalls;
            _sut.TryPublishIntent(WizardIntent.Continue).Should().BeFalse("wizard must not accept intents after GameStarted abort");
            _navigator.TotalCalls.Should().Be(callsAfterAbort);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenOpponentIsBotButHumanKindIsMatchmaking_ThenStartDoesNotOpenMatchmaking() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();
            
            session.Update(s => s
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Bot)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => session.DisposeCallCount == 1);

            _navigator.ReplaceMatchSetupWithMatchmakingCalls.Should().Be(0);
            _navigator.ReplaceMatchmakingWithMatchSetupCalls.Should().Be(0);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenBuildLaunchConfigFailsOnStart_ThenDoesNotAbortAndSetsCurrentError() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var session = _sessionFactory.CreatedSessions.Single();
            session.ReturnFailureOnBuildLaunchConfig = true;

            var gameLaunchCount = 0;
            var subscription = _sut.GameLaunchRequested.Subscribe(_ => gameLaunchCount++);

            try
            {
                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => _sut.CurrentError.CurrentValue != null);

                _sut.IsActive.Should().BeTrue("wizard must remain active when launch config validation fails");
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
        public IEnumerator WhenStartIntentPublishedDuringSubmitInProgress_ThenIgnoresSecondStart() => UniTask.ToCoroutine(async () =>
        {
            await _sut.StartWizardAsync(CancellationToken.None);
            await MoveToMatchSetupAsync();

            var first = _sut.TryPublishIntent(WizardIntent.Start);
            await WaitUntilAsync(() => _sut.IsSubmitting.CurrentValue);
            var second = _sut.TryPublishIntent(WizardIntent.Start);

            first.Should().BeTrue();
            second.Should().BeFalse();

            _sut.CompleteStartAttempt(true);
            await WaitUntilAsync(() => _sessionFactory.CreatedSessions.Single().DisposeCallCount == 1);
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenFullFlowModeSelectionToMatchSetupToStart_ThenPublishesGameLaunchRequestedAndAbortsAfterCompletion() => UniTask.ToCoroutine(async () =>
        {
            var launchConfigs = new List<GameLaunchConfig>();
            AbortReason? abortReason = null;

            var launchSub = _sut.GameLaunchRequested.Subscribe(c => launchConfigs.Add(c));
            var abortSub = _sut.WizardAborted.Subscribe(r => abortReason = r);

            try
            {
                await _sut.StartWizardAsync(CancellationToken.None);
                await WaitUntilAsync(() => _navigator.OpenModeSelectionCalls == 1);

                _sut.TryPublishIntent(WizardIntent.Continue).Should().BeTrue();
                await WaitUntilAsync(() => _navigator.ReplaceModeSelectionWithMatchSetupCalls == 1);

                _sut.TryPublishIntent(WizardIntent.Start).Should().BeTrue();
                await WaitUntilAsync(() => launchConfigs.Count == 1);

                _sut.CompleteStartAttempt(true);
                await WaitUntilAsync(() => abortReason != null);

                launchConfigs.Should().HaveCount(1);
                abortReason.Should().Be(AbortReason.GameStarted);
            }
            finally
            {
                launchSub.Dispose();
                abortSub.Dispose();
            }
        });
    }
}
