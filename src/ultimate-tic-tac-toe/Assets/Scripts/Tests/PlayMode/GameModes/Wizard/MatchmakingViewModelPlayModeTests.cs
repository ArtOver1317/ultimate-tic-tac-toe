using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchmakingViewModelPlayModeTests
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
            // Arrange
            // Act
            _viewModel.Initialize();
            await UniTask.Yield();

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.PlayersWithDifferentParams.CurrentValue.Should().Be(0);
            _viewModel.ErrorMessage.CurrentValue.Should().BeNullOrEmpty();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithoutInitialize_ThenWorksCorrectly() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(200));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalled_ThenStartsSearchAndStateTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            _viewModel.Initialize();
            await UniTask.Yield();

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Searching);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledDuringSearch_ThenCancelsSearchAndResetsState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            _viewModel.Reset();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.ErrorMessage.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledAndThenBeginSearchCalledAgain_ThenStartsNewSearchSuccessfully() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _service.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.Reset();

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithSuccessfulService_ThenStateTransitionsToSearchingThenFound() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            var states = new List<MatchmakingState>();
            using var subscription = _viewModel.State.Subscribe(state => states.Add(state));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            // Assert
            states.Should().Contain(MatchmakingState.Searching);
            states.Should().Contain(MatchmakingState.Found);
            states.IndexOf(MatchmakingState.Searching).Should().BeLessThan(states.IndexOf(MatchmakingState.Found));
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledWithFailingService_ThenStateTransitionsToFailed() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new InvalidOperationException("boom"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Failed);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndCancelled_ThenStateTransitionsToCancelled() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchStarted_ThenTimerStartsAndElapsedTimeIncreases() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            var initial = _viewModel.ElapsedTime.CurrentValue;
            await WaitUntilAsync(() => _viewModel.ElapsedTime.CurrentValue > initial, 2000);

            // Assert
            _viewModel.ElapsedTime.CurrentValue.Should().BeGreaterThan(initial);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCompletes_ThenTimerStopsAndElapsedTimeResets() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);
            await WaitUntilAsync(() => _viewModel.ElapsedTime.CurrentValue == TimeSpan.Zero, 2000);

            // Assert
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCancelled_ThenTimerStopsAndElapsedTimeResets() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetCalledWhileTimerRunning_ThenTimerStopsAndDoesNotContinueUpdating() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
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

            // Assert
            _viewModel.ElapsedTime.CurrentValue.Should().Be(TimeSpan.Zero);
            _viewModel.ElapsedTime.CurrentValue.Should().Be(elapsedAfterReset);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceThrowsException_ThenStateChangesToFailedAndErrorMessageIsSet() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _localization.SetText(LocaleId.EnglishUs, "Errors.GameModeWizard.MatchmakingFailed", "Failed EN");
            _service.EnqueueException(new InvalidOperationException("boom"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            // Assert
            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed EN");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenNewSearchStartedAfterFailure_ThenErrorMessageIsCleared() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new InvalidOperationException("boom"));
            _service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(800));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Assert
            _viewModel.ErrorMessage.CurrentValue.Should().BeNullOrWhiteSpace();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenLocaleChangesWhileErrorMessageKeyIsSet_ThenErrorMessageReResolves() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _localization.SetText(LocaleId.EnglishUs, "Errors.GameModeWizard.MatchmakingFailed", "Failed EN");
            _localization.SetText(LocaleId.Russian, "Errors.GameModeWizard.MatchmakingFailed", "Failed RU");
            _service.EnqueueException(new InvalidOperationException("boom"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);

            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed EN");

            await _localization.SetLocaleAsync(LocaleId.Russian, CancellationToken.None);
            await UniTask.Yield();

            // Assert
            _viewModel.ErrorMessage.CurrentValue.Should().Be("Failed RU");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledDuringSearching_ThenPublishesCancelRequestedEventAndCancelsSearch() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            var cancelCount = 0;
            using var subscription = _viewModel.CancelRequested.Subscribe(_ => cancelCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            cancelCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledWhileNotSearching_ThenPublishesEventButDoesNotChangeState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cancelCount = 0;
            using var subscription = _viewModel.CancelRequested.Subscribe(_ => cancelCount++);

            // Act
            _viewModel.RequestCancel();
            await UniTask.Yield();

            // Assert
            cancelCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledDuringSearching_ThenCancelsSearchAndPublishesEvent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            var backCount = 0;
            using var subscription = _viewModel.BackRequested.Subscribe(_ => backCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            _viewModel.RequestBack();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            backCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledWhileNotSearching_ThenDoesNotCancelAndOnlyPublishesBackRequested() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var backCount = 0;
            using var subscription = _viewModel.BackRequested.Subscribe(_ => backCount++);

            // Act
            _viewModel.RequestBack();
            await UniTask.Yield();

            // Assert
            backCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestRetryCalled_ThenPublishesRetryRequestedEvent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            // Act
            _viewModel.RequestRetry();
            await UniTask.Yield();

            // Assert
            retryCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndCancelRequestedImmediately_ThenSearchDoesNotProceedOrEndsCancelled() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1000));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue != MatchmakingState.Searching, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().BeOneOf(MatchmakingState.Cancelled, MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndResetHappensBeforeSearchEntersSearching_ThenDoesNotEndInFailed() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1000));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            _viewModel.Reset();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().NotBe(MatchmakingState.Failed);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledTwiceQuickly_ThenOnlyLatestSearchAffectsState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var service = new DeterministicMatchmakingService();
            var vm = new MatchmakingViewModel(_localization, service);
            var request = CreateValidRequest();

            // Act
            vm.BeginSearch(request, CancellationToken.None);
            await service.FirstStarted.Task;
            vm.BeginSearch(request, CancellationToken.None);

            await WaitUntilAsync(() => vm.State.CurrentValue == MatchmakingState.Found, 2000);
            service.AllowFirstComplete.TrySetResult(true);
            for (var i = 0; i < 3; i++)
            {
                await UniTask.Yield();
                vm.State.CurrentValue.Should().Be(MatchmakingState.Found);
            }

            // Assert
            vm.State.CurrentValue.Should().Be(MatchmakingState.Found);
            vm.Dispose();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThread_ThenStateUpdatedSafelyWithoutCrash() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            await Task.Run(() => _viewModel.RequestCancel());
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedDuringSearch_ThenSearchStopsAndNoFurtherUpdates() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var blockingService = new BlockingMatchmakingService();
            var vm = new MatchmakingViewModel(_localization, blockingService);
            vm.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => vm.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            vm.Dispose();
            await WaitUntilAsync(() => blockingService.CancellationObserved, 1000);

            // Assert
            blockingService.CancellationObserved.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedMultipleTimes_ThenIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            // Act
            Action act = () =>
            {
                _viewModel.Dispose();
                _viewModel.Dispose();
            };

            // Assert
            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAfterDispose_ThenIsNoOpAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Dispose();

            // Act
            Action act = () => _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);

            // Assert
            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Dispose();

            // Act
            Action act = () => _viewModel.RequestCancel();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Dispose();

            // Act
            Action act = () => _viewModel.RequestBack();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestRetryCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Dispose();

            // Act
            Action act = () => _viewModel.RequestRetry();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        private static MatchmakingRequest CreateValidRequest() =>
            new MatchmakingRequest("classic", new ClassicModeConfig(3));

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private sealed class FakeMatchmakingService : IMatchmakingService
        {
            private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();

            public void EnqueueResult(MatchmakingResult result) =>
                _responses.Enqueue((_, __) => UniTask.FromResult(result));

            public void EnqueueDelayedResult(MatchmakingResult result, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    return result;
                });

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue((_, __) => UniTask.FromException<MatchmakingResult>(exception));

            public void EnqueueDelayedException(Exception exception, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    throw exception;
                });

            public UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                if (_responses.Count == 0)
                    return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."));

                return _responses.Dequeue().Invoke(request, ct);
            }
        }

        private sealed class DeterministicMatchmakingService : IMatchmakingService
        {
            public UniTaskCompletionSource<bool> FirstStarted { get; } = new();
            public UniTaskCompletionSource<bool> AllowFirstComplete { get; } = new();
            private int _callIndex;

            public async UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (_callIndex == 0)
                {
                    _callIndex++;
                    FirstStarted.TrySetResult(true);
                    await AllowFirstComplete.Task.AttachExternalCancellation(ct);
                    throw new InvalidOperationException("late fail");
                }

                return new MatchmakingResult("match-2", "opponent-2");
            }
        }

        private sealed class BlockingMatchmakingService : IMatchmakingService
        {
            public bool CancellationObserved { get; private set; }

            public async UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                ct.Register(() => CancellationObserved = true);
                await UniTask.WaitUntil(() => false, cancellationToken: ct);
                return null;
            }
        }

        private sealed class TestLocalizationService : ILocalizationService, IDisposable
        {
            private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
            private readonly ReactiveProperty<bool> _isBusy = new(false);
            private readonly Subject<LocalizationError> _errors = new();
            private readonly Dictionary<(LocaleId, string), ReactiveProperty<string>> _texts = new();

            public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _currentLocale;
            public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
            public Observable<LocalizationError> Errors => _errors;

            public UniTask InitializeAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
            {
                _currentLocale.Value = locale;
                return UniTask.CompletedTask;
            }

            public UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
                GetOrCreate(_currentLocale.CurrentValue, key.Value).Value;

            public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
                GetOrCreate(_currentLocale.CurrentValue, key.Value);

            public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null) =>
                GetOrCreate(_currentLocale.CurrentValue, key.Value);

            public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs, LocaleId.Russian };

            public void SetText(LocaleId locale, string key, string value)
            {
                GetOrCreate(locale, key).Value = value ?? string.Empty;
            }

            public void Dispose()
            {
                foreach (var entry in _texts.Values)
                    entry.Dispose();

                _errors.Dispose();
                _isBusy.Dispose();
                _currentLocale.Dispose();
                _texts.Clear();
            }

            private ReactiveProperty<string> GetOrCreate(LocaleId locale, string key)
            {
                var id = (locale, key ?? string.Empty);
                if (!_texts.TryGetValue(id, out var value))
                {
                    value = new ReactiveProperty<string>(key ?? string.Empty);
                    _texts.Add(id, value);
                }

                return value;
            }
        }
    }
}