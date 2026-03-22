using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    [TestFixture]
    [Category("Integration")]
    public partial class MatchmakingViewModelPlayModeTests
    {
        private FakeMatchmakingService _service;
        private TestLocalizationService _localization;
        private MatchmakingViewModel _viewModel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _service = new FakeMatchmakingService();
            _localization = new TestLocalizationService();
            _viewModel = new MatchmakingViewModel(_localization, _service);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel?.Dispose();
            _localization?.Dispose();
            _service = null;
            _viewModel = null;

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenInitialized_ThenStateIsIdleAndPropertiesAreDefault() => UniTask.ToCoroutine(async () =>
        {
            _viewModel.Initialize();
            await UniTask.Yield();

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.PlayersWithDifferentParams.CurrentValue.Should().Be(0);
            _viewModel.ErrorMessage.CurrentValue.Should().BeNullOrEmpty();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithoutInitialize_ThenWorksCorrectly() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(200));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalled_ThenStartsSearchAndStateTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            _viewModel.Initialize();
            await UniTask.Yield();

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Searching);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledDuringSearch_ThenCancelsSearchAndResetsState() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _viewModel.Reset();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.ErrorMessage.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledAndThenBeginSearchCalledAgain_ThenStartsNewSearchSuccessfully() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _service.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.Reset();

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Found);
        });

        private static MatchmakingRequest CreateValidRequest() => new("classic", new TicTacToeConfig(3));

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }
    }
}