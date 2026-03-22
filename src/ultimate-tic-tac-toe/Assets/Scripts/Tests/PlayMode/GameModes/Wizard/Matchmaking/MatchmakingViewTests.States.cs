#nullable enable

using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingViewTests
    {
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStateIsSearching_ThenSearchingElementsAreVisibleAndOthersAreHidden() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            await UniTask.Yield();

            GetSearchingState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetRetryButton().style.display.value.Should().Be(DisplayStyle.None);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsFound_ThenFoundElementsAreVisibleAndOthersAreHidden() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelButton().style.display.value.Should().Be(DisplayStyle.None);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsFailed_ThenFailedElementsAreVisibleAndRetryButtonShown() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);

            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetRetryButton().style.display.value.Should().Be(DisplayStyle.Flex);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsCancelled_ThenCancelledElementsAreVisibleAndBackButtonShown() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetCancelButton().text.Should().Be("Back");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelButtonTextChanges_ThenButtonLabelUpdates() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _localization.SetText("GameWizard.Matchmaking.Cancel", "Cancel-Updated");
            await UniTask.Yield();

            GetCancelButton().text.Should().Be("Cancel-Updated");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStateChangesFromSearchingToFailed_ThenCancelButtonLabelChangesToBack() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Initialize();
            _service.EnqueueDelayedException(new InvalidOperationException("boom"), TimeSpan.FromMilliseconds(300));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            GetCancelButton().text.Should().Be("Cancel");

            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);
            await UniTask.Yield();

            GetCancelButton().text.Should().Be("Back");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenViewModelStateChanges_ThenViewReflectsChangeImmediately() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Initialize();
            _service.EnqueueDelayedException(new InvalidOperationException("boom"), TimeSpan.FromMilliseconds(300));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);
            await UniTask.Yield();

            GetFailedState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
        });
    }
}