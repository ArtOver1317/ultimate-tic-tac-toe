using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization.Types;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingViewModelPlayModeTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithSuccessfulService_ThenStateTransitionsToSearchingThenFound() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            var states = new List<MatchmakingState>();
            using var subscription = _viewModel.State.Subscribe(state => states.Add(state));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            states.Should().Contain(MatchmakingState.Searching);
            states.Should().Contain(MatchmakingState.Found);
            states.IndexOf(MatchmakingState.Searching).Should().BeLessThan(states.IndexOf(MatchmakingState.Found));
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithFailingService_ThenStateTransitionsToFailed() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Failed);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndCancelled_ThenStateTransitionsToCancelled() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchStarted_ThenTimerStartsAndElapsedTimeIncreases() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            var initial = _viewModel.ElapsedTime.CurrentValue;
            await WaitUntilAsync(() => _viewModel.ElapsedTime.CurrentValue > initial, 2000);

            _viewModel.ElapsedTime.CurrentValue.Should().BeGreaterThan(initial);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCompletes_ThenTimerStopsAndElapsedTimeResets() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
            await WaitUntilAsync(() => _viewModel.ElapsedTime.CurrentValue == TimeSpan.Zero, 2000);

            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCancelled_ThenTimerStopsAndElapsedTimeResets() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledWhileTimerRunning_ThenTimerStopsAndDoesNotContinueUpdating() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            await WaitUntilAsync(() => _viewModel.ElapsedTime.CurrentValue > TimeSpan.Zero, 2000);

            _viewModel.Reset();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);
            var elapsedAfterReset = _viewModel.ElapsedTime.CurrentValue;
            
            for (var i = 0; i < 5; i++)
            {
                await UniTask.Yield();
                _viewModel.ElapsedTime.CurrentValue.Should().Be(elapsedAfterReset);
            }

            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(elapsedAfterReset);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceThrowsException_ThenStateChangesToFailedAndErrorMessageIsSet() => UniTask.ToCoroutine(async () =>
        {
            _localization.SetText(LocaleId.EnglishUs, "Errors.GameWizard.MatchmakingFailed", "Failed EN");
            _service.EnqueueException(new InvalidOperationException("boom"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed EN");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenNewSearchStartedAfterFailure_ThenErrorMessageIsCleared() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));
            _service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(800));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
           
            await WaitUntilAsync(() =>
                _viewModel.State.CurrentValue is MatchmakingState.Searching or MatchmakingState.Found, 1000);

            _viewModel.ErrorMessage.CurrentValue.Should().BeNullOrWhiteSpace();

            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenLocaleChangesWhileErrorMessageKeyIsSet_ThenErrorMessageReResolves() => UniTask.ToCoroutine(async () =>
        {
            _localization.SetText(LocaleId.EnglishUs, "Errors.GameWizard.MatchmakingFailed", "Failed EN");
            _localization.SetText(LocaleId.Russian, "Errors.GameWizard.MatchmakingFailed", "Failed RU");
            _service.EnqueueException(new InvalidOperationException("boom"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed EN");

            await _localization.SetLocaleAsync(LocaleId.Russian, CancellationToken.None);
            await UniTask.Yield();

            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed RU");
        });
    }
}