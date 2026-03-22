#nullable enable

using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Runtime.UI.Components;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenStopsSpinnerAndUnbindsViewModel() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), System.Threading.CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);
            var rotated = await WaitForSpinnerRotationAsync(GetSpinnerAngle, 3000);
            rotated.Should().BeTrue();

            _view.ResetForPool();
            await UniTask.Yield();

            _view.GetViewModel().Should().BeNull();
            GetSpinnerAngle().Should().Be(0f);
            
            for (var i = 0; i < 5; i++)
            {
                await UniTask.Yield();
                GetSpinnerAngle().Should().Be(0f);
            }
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReusedAfterPooling_ThenBindsNewViewModelAndUIWorksCorrectly() => UniTask.ToCoroutine(async () =>
        {
            _view.ResetForPool();
            var service = new FakeMatchmakingService();
            var localization = new TestLocalizationService();
            localization.SetText("GameWizard.Matchmaking.Cancel", "Cancel");
            localization.SetText("GameWizard.Matchmaking.Back", "Back");
            localization.SetText("GameWizard.Matchmaking.Retry", "Retry");
            var newViewModel = new MatchmakingViewModel(localization, service, _coordinator);

            _view.SetViewModel(newViewModel);
            _view.Show();
            service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(1500));
            newViewModel.BeginSearch(CreateValidRequest(), System.Threading.CancellationToken.None);
            await WaitUntilAsync(() => newViewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _view.GetViewModel().Should().Be(newViewModel);
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.Flex);

            newViewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCoordinatorBecomesBusy_ThenViewRootIsDisabledUntilBusyEnds() => UniTask.ToCoroutine(async () =>
        {
            Root.enabledInHierarchy.Should().BeTrue();

            _isTransitioning.Value = true;
            await UniTask.Yield();

            Root.enabledInHierarchy.Should().BeFalse();

            _isTransitioning.Value = false;
            await UniTask.Yield();

            Root.enabledInHierarchy.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCoordinatorPublishesWizardError_ThenOverlayReflectsItThroughViewModel() => UniTask.ToCoroutine(async () =>
        {
            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.MatchmakingModal",
                true,
                ErrorDisplayType.Modal);
            
            await UniTask.Yield();

            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        [Category("Integration")]
        public IEnumerator WhenRetryButtonClickedAfterFailure_ThenViewModelReceivesRetryRequest() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            _viewModel.BeginSearch(CreateValidRequest(), System.Threading.CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);
            await WaitUntilAsync(() => GetRetryButton().style.display.value == DisplayStyle.Flex, 2000);
            await UniTask.Yield();

            SimulateClick(GetRetryButton());
            await WaitUntilAsync(() => retryCount == 1, 2000);

            retryCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        [Category("Integration")]
        [Category("UIWiring")]
        [Explicit]
        public IEnumerator WhenUserTriggersRetryAfterFailure_ThenViewModelReceivesRetryRequest() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            _viewModel.BeginSearch(CreateValidRequest(), System.Threading.CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);
            await WaitUntilAsync(() => GetRetryButton().style.display.value == DisplayStyle.Flex, 2000);
            await UniTask.Yield();

            var button = GetRetryButton();
            button.Should().NotBeNull("RetryButton должен существовать в UXML");

            if (button.panel == null)
                Assert.Inconclusive("UI Toolkit panel не прикреплён: click pipeline может не доставлять события в PlayMode. Это допустимо для [Explicit] smoke.");

            if (button.clickable == null)
                Assert.Inconclusive("У Button отсутствует clickable: невозможно надёжно симулировать клик в этом окружении. Это допустимо для [Explicit] smoke.");

            SimulateClick(button);
            await WaitUntilAsync(() => retryCount == 1, 2000);

            retryCount.Should().Be(1, "это smoke-тест wiring-а: один пользовательский клик должен привести к одному событию RetryRequested");
           
            for (var i = 0; i < 3; i++)
            {
                await UniTask.Yield();
                retryCount.Should().Be(1, "клик не должен триггерить событие повторно");
            }
        });
    }
}