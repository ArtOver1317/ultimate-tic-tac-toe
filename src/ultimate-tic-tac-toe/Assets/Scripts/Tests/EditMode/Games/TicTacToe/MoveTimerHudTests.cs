using System;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MoveTimerHudTests
    {
        private sealed class FakeMoveTimerService : IMoveTimerService
        {
            private readonly ReactiveProperty<float> _remainingSeconds = new(0f);
            private readonly ReactiveProperty<bool> _isActive = new(false);

            public ReadOnlyReactiveProperty<float> RemainingSeconds => _remainingSeconds;
            public ReadOnlyReactiveProperty<bool> IsActive => _isActive;

            public void StartOrResetForPlayer(int playerSlot) { }
            public void Stop() => _isActive.Value = false;
            public void Freeze() { }
            public void Unfreeze() { }
            public void Dispose()
            {
                _remainingSeconds.Dispose();
                _isActive.Dispose();
            }

            public void SetState(bool isActive, float remainingSeconds)
            {
                _isActive.Value = isActive;
                _remainingSeconds.Value = remainingSeconds;
            }
        }

        private sealed class FakeGameplayFieldUiAdapter : IGameplayFieldUiAdapter
        {
            private readonly Subject<CellId> _cellClicks = new();

            public Observable<CellId> CellClicks => _cellClicks;
            public Label CurrentPlayerLabel { get; } = new();
            public VisualElement FieldContainer { get; } = new();
            public VisualElement Player1Panel { get; } = new();
            public VisualElement Player2Panel { get; } = new();
            public Label Player1ScoreLabel { get; } = new();
            public Label Player2ScoreLabel { get; } = new();
            public Label DrawsScoreLabel { get; } = new();
            public Label MoveTimerLabel { get; } = new();

            public bool TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
            {
                cellRoot = null;
                markLabel = null;
                return false;
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                cellRoot = null;
                return false;
            }

            public bool TryGetMark(CellId id, out VisualElement mark)
            {
                mark = null;
                return false;
            }
        }

        [Test]
        public void WhenTimerActiveAndUnderMinute_ThenFormatsAsSecondsAndWarningEnabled()
        {
            using var timerService = new FakeMoveTimerService();
            using var viewModel = new MoveTimerHudViewModel(timerService);

            timerService.SetState(isActive: true, remainingSeconds: 9.2f);

            viewModel.IsVisible.CurrentValue.Should().BeTrue();
            viewModel.FormattedTime.CurrentValue.Should().Be("10");
            viewModel.IsWarning.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenTimerActiveAndOverMinute_ThenFormatsAsMinuteSecond()
        {
            using var timerService = new FakeMoveTimerService();
            using var viewModel = new MoveTimerHudViewModel(timerService);

            timerService.SetState(isActive: true, remainingSeconds: 65.1f);

            viewModel.FormattedTime.CurrentValue.Should().Be("01:06");
            viewModel.IsWarning.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenBinderBoundAndVmChanges_ThenUpdatesLabelVisibilityTextAndWarningClass()
        {
            using var timerService = new FakeMoveTimerService();
            using var viewModel = new MoveTimerHudViewModel(timerService);

            var ui = new FakeGameplayFieldUiAdapter();
            using var binder = new MoveTimerHudBinder(ui, viewModel);

            binder.Bind();

            timerService.SetState(isActive: true, remainingSeconds: 4f);
            ui.MoveTimerLabel.style.display.value.Should().Be(DisplayStyle.Flex);
            ui.MoveTimerLabel.text.Should().Be("04");
            ui.MoveTimerLabel.ClassListContains("move-timer-label--warning").Should().BeTrue();

            timerService.SetState(isActive: false, remainingSeconds: 40f);
            ui.MoveTimerLabel.style.display.value.Should().Be(DisplayStyle.None);
            ui.MoveTimerLabel.ClassListContains("move-timer-label--warning").Should().BeFalse();
        }

        [Test]
        public void WhenTimerInactiveAndRemainingSecondsLow_ThenIsWarningFalse()
        {
            using var timerService = new FakeMoveTimerService();
            using var viewModel = new MoveTimerHudViewModel(timerService);

            timerService.SetState(isActive: false, remainingSeconds: 5f);

            viewModel.IsWarning.CurrentValue.Should().BeFalse();
            viewModel.IsVisible.CurrentValue.Should().BeFalse();
        }
    }
}
