using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingViewModelPlayModeTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedDuringSearch_ThenSearchStopsAndNoFurtherUpdates() => UniTask.ToCoroutine(async () =>
        {
            var blockingService = new BlockingMatchmakingService();
            var vm = new MatchmakingViewModel(_localization, blockingService);
            vm.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => vm.State.CurrentValue == MatchmakingState.Searching, 1000);

            vm.Dispose();
            await WaitUntilAsync(() => blockingService.CancellationObserved, 1000);

            blockingService.CancellationObserved.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedMultipleTimes_ThenIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            Action act = () =>
            {
                _viewModel.Dispose();
                _viewModel.Dispose();
            };

            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAfterDispose_ThenIsNoOpAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Dispose();

            Action act = () => _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);

            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Dispose();

            Action act = () => _viewModel.RequestCancel();

            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Dispose();

            Action act = () => _viewModel.RequestBack();

            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestRetryCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Dispose();

            Action act = () => _viewModel.RequestRetry();

            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });
    }
}