#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Session;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard.UI.MatchSetup
{
    public partial class MatchSetupViewEditModeTests
    {
        [Test]
        public void WhenCanStartFalseOrIsBusyTrue_ThenStartButtonIsDisabled()
        {
            var startButton = _view.RootForTests.Q<Button>("StartButton");

            _session.EmitCanStart(false);
            _isTransitioning.Value = false;
            _isSubmitting.Value = false;

            startButton.enabledSelf.Should().BeFalse();

            _session.EmitCanStart(true);
            _isTransitioning.Value = true;

            startButton.enabledSelf.Should().BeFalse();
        }

        [Test]
        public void WhenCanStartTrueAndIsBusyFalse_ThenStartButtonIsEnabled()
        {
            var startButton = _view.RootForTests.Q<Button>("StartButton");

            _session.EmitCanStart(true);
            _isTransitioning.Value = false;
            _isSubmitting.Value = false;

            startButton.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenIsBusyTrue_ThenBackButtonAndOpponentToggleAreDisabled()
        {
            var backButton = _view.RootForTests.Q<Button>("BackButton");
            var opponentToggle = _view.RootForTests.Q<SegmentedToggle>("OpponentToggle");

            _isTransitioning.Value = true;

            backButton.enabledSelf.Should().BeFalse();
            opponentToggle.enabledSelf.Should().BeFalse();

            _isTransitioning.Value = false;

            backButton.enabledSelf.Should().BeTrue();
            opponentToggle.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenIsBotSettingsVisibleFalse_ThenBotSettingsSectionIsHidden()
        {
            var section = _view.RootForTests.Q<VisualElement>("BotSettingsSection");

            _viewModel.SetOpponentType(OpponentType.Human);

            section.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenIsBotSettingsVisibleTrue_ThenBotSettingsSectionIsVisible()
        {
            var section = _view.RootForTests.Q<VisualElement>("BotSettingsSection");
            _viewModel.SetOpponentType(OpponentType.Human);

            _viewModel.SetOpponentType(OpponentType.Bot);

            section.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenDifficultyItemsChanges_ThenDifficultyChipsUpdates()
        {
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            
            var items = Array.AsReadOnly(new[]
            {
                new DifficultyChipItem("Easy", "Easy"),
                new DifficultyChipItem("Hard", "Hard"),
            });

            _viewModel.SetDifficultyItemsForTests(items);

            chips.childCount.Should().Be(2);
            chips.Q<Button>("Easy").text.Should().Be("Easy");
            chips.Q<Button>("Hard").text.Should().Be("Hard");
        }

        [Test]
        public void WhenSelectedDifficultyIdChanges_ThenDifficultyChipsSelectionUpdates()
        {
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            
            _viewModel.SetDifficultyItemsForTests(Array.AsReadOnly(new[]
            {
                new DifficultyChipItem("Easy", "Easy"),
                new DifficultyChipItem("Hard", "Hard"),
            }));

            _viewModel.SetBotDifficultyId("Hard");

            chips.SelectedId.Should().Be("Hard");
        }

        [Test]
        public void WhenIsBusyTrue_ThenDifficultyChipsIsDisabled()
        {
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");

            _isTransitioning.Value = true;

            chips.enabledSelf.Should().BeFalse();
        }

        [Test]
        public void WhenIsBusyFalse_ThenDifficultyChipsIsEnabled()
        {
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            _isTransitioning.Value = true;

            _isTransitioning.Value = false;

            chips.enabledSelf.Should().BeTrue();
        }
    }
}