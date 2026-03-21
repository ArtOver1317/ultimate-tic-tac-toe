#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Session;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard.UI.MatchSetup
{
    public partial class MatchSetupViewEditModeTests
    {
        [Test]
        public void WhenIsHumanSettingsVisibleFalse_ThenHumanSettingsSectionIsHidden()
        {
            var section = _view.RootForTests.Q<VisualElement>("HumanSettingsSection");
            _viewModel.SetOpponentType(OpponentType.Human);

            _viewModel.SetOpponentType(OpponentType.Bot);

            section.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenIsHumanSettingsVisibleTrue_ThenHumanSettingsSectionIsVisible()
        {
            var section = _view.RootForTests.Q<VisualElement>("HumanSettingsSection");
            _viewModel.SetOpponentType(OpponentType.Bot);
            section.style.display.value.Should().Be(DisplayStyle.None);

            _viewModel.SetOpponentType(OpponentType.Human);

            section.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenSessionHasLocalHumanKind_ThenSectionVisibleAndLocalSelected()
        {
            var section = _view.RootForTests.Q<VisualElement>("HumanSettingsSection");
            var radio = _view.RootForTests.Q<HumanKindRadio>("HumanKindRadio");

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            section.style.display.value.Should().Be(DisplayStyle.Flex);
            radio.SelectedKind.Should().Be(HumanOpponentKind.Local);
        }

        [Test]
        public void WhenSessionHasDirectInviteHumanKindAtBindTime_ThenSectionVisibleAndDirectInviteSelected()
        {
            _viewModel.SetOpponentType(OpponentType.Bot);
            var section = _view.RootForTests.Q<VisualElement>("HumanSettingsSection");
            section.style.display.value.Should().Be(DisplayStyle.None);

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            _view.ClearViewModel();
            _viewModel.Dispose();
            _viewModel = CreateViewModel();
            _view.RebindUxmlForTests();
            _view.SetViewModel(_viewModel);

            section = _view.RootForTests.Q<VisualElement>("HumanSettingsSection");
            var radio = _view.RootForTests.Q<HumanKindRadio>("HumanKindRadio");

            section.style.display.value.Should().Be(DisplayStyle.Flex);
            radio.SelectedKind.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenSessionHasDirectInviteHumanKind_ThenPlayerIdInputIsVisible()
        {
            var input = _view.RootForTests.Q<PlayerIdInput>("PlayerIdInput");

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            input.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenSessionHasLocalHumanKind_ThenPlayerIdInputIsHidden()
        {
            var input = _view.RootForTests.Q<PlayerIdInput>("PlayerIdInput");

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            input.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenValidationErrorTargetsPlayerId_ThenPlayerIdErrorLabelUpdates()
        {
            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            var input = _view.RootForTests.Q<PlayerIdInput>("PlayerIdInput");
            var errorLabel = input.Q<Label>("ErrorLabel");

            _session.EmitValidationErrors(new List<ValidationError>
            {
                new(WizardFieldNames.TargetPlayerId, "Errors.GameWizard.PlayerIdInvalid"),
            });

            errorLabel.text.Should().Be("Errors.GameWizard.PlayerIdInvalid");
            errorLabel.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenSessionSwitchesFromLocalToDirectInviteAfterBind_ThenSelectionUpdatesAndDoesNotThrow()
        {
            var radio = _view.RootForTests.Q<HumanKindRadio>("HumanKindRadio");

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            radio.SelectedKind.Should().Be(HumanOpponentKind.Local);

            Action act = () => _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(2));

            act.Should().NotThrow();
            radio.SelectedKind.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenIsBusyChanges_ThenHumanKindRadioEnabledStateUpdates()
        {
            var radio = _view.RootForTests.Q<HumanKindRadio>("HumanKindRadio");

            _isTransitioning.Value = true;

            radio.enabledSelf.Should().BeFalse();

            _isTransitioning.Value = false;

            radio.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenIsBusyTrue_ThenPlayerIdInputIsDisabled()
        {
            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            var playerIdInput = _view.RootForTests.Q<PlayerIdInput>("PlayerIdInput");

            _isTransitioning.Value = true;

            playerIdInput.enabledSelf.Should().BeFalse();
        }

        [Test]
        public void WhenSessionTargetPlayerIdChanges_ThenPlayerIdInputValueUpdates()
        {
            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("777")
                .WithVersion(1));

            var playerIdInput = _view.RootForTests.Q<PlayerIdInput>("PlayerIdInput");
            playerIdInput.Value.Should().Be("777");

            _session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("12345")
                .WithVersion(2));

            playerIdInput.Value.Should().Be("12345");
        }
    }
}