using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameSettingsBinderTests
    {
        private ILocalizationService _localization;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
        }

        [Test]
        public void WhenTicTacToeSettingsBinderBindCalledWithMissingElements_ThenDoesNotThrow()
        {
            // Arrange
            var binder = new TicTacToeSettingsBinder(_localization);
            var root = new VisualElement();
            using var vm = new TicTacToeSettingsViewModel();
            var disposables = new CompositeDisposable();

            LogAssert.Expect(LogType.Error, new Regex("Settings UXML is missing board size elements"));

            // Act
            Action act = () => binder.Bind(root, vm, disposables);

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenTicTacToeSettingsBinderBindCalled_ThenBoardSizeSubscriptionUpdatesLabelText()
        {
            // Arrange
            var binder = new TicTacToeSettingsBinder(_localization);
            var root = new VisualElement();

            var decrementButton = new Button { name = "DecrementButton" };
            var incrementButton = new Button { name = "IncrementButton" };
            var boardSizeValue = new Label { name = "BoardSizeValue" };
            var boardSizeTitle = new Label { name = "BoardSizeTitle" };

            root.Add(decrementButton);
            root.Add(incrementButton);
            root.Add(boardSizeValue);
            root.Add(boardSizeTitle);

            using var vm = new TicTacToeSettingsViewModel();
            vm.Configure(minBoardSize: 3, maxBoardSize: 4, defaultBoardSize: 3);

            var disposables = new CompositeDisposable();

            binder.Bind(root, vm, disposables);

            // Act
            vm.IncrementBoardSize();

            // Assert
            boardSizeValue.text.Should().Be("4");
            disposables.Dispose();
        }

        [Test]
        public void WhenTicTacToeSettingsBinderBindCalledWithMissingInfoLabel_ThenDoesNotThrow()
        {
            // Arrange
            var binder = new TicTacToeSettingsBinder(_localization);
            var root = new VisualElement();
            using var vm = new TicTacToeSettingsViewModel();
            var disposables = new CompositeDisposable();

            LogAssert.Expect(LogType.Error, new Regex("Settings UXML is missing board size elements"));

            // Act
            Action act = () => binder.Bind(root, vm, disposables);

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenMoveTimerSettingsViewModelCreated_ThenDefaultSelectedPresetIsZero()
        {
            // Arrange
            var config = MoveTimerPresetsConfig.CreateRuntimeDefault();

            // Act
            using var vm = new MoveTimerSettingsViewModel(config, _localization);

            // Assert
            vm.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
            vm.SelectedPresetId.CurrentValue.Should().Be("0");
        }

        [Test]
        public void WhenMoveTimerSettingsTryApplyConfigCalledWithInvalidValue_ThenFallsBackToZero()
        {
            // Arrange
            var config = MoveTimerPresetsConfig.CreateRuntimeDefault();
            using var vm = new MoveTimerSettingsViewModel(config, _localization);

            vm.SetSelectedPresetId("30");
            vm.MoveTimeLimitSeconds.CurrentValue.Should().Be(30);

            // Act
            var result = vm.TryApplyConfig(17);

            // Assert
            result.Should().BeFalse();
            vm.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
            vm.SelectedPresetId.CurrentValue.Should().Be("0");
        }

        [Test]
        public void WhenMoveTimerSettingsBinderBindCalled_ThenFormatsNumericPresetsWithSecondsFormat()
        {
            // Arrange
            _localization
                .Observe(
                    Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.Timer.Off"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("No limit"));

            _localization
                .Observe(
                    Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.Timer.SecondsFormat"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("{0} sec"));

            _localization
                .Observe(
                    Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.Timer.Label"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Move time"));

            var root = new VisualElement();
            var title = new Label { name = "MoveTimerTitle" };
            var chips = new DifficultyChips { name = "MoveTimerChips" };
            root.Add(title);
            root.Add(chips);

            var binder = new MoveTimerSettingsBinder();
            using var vm = new MoveTimerSettingsViewModel(MoveTimerPresetsConfig.CreateRuntimeDefault(), _localization);
            var disposables = new CompositeDisposable();

            // Act
            binder.Bind(root, vm, disposables);

            // Assert
            title.text.Should().Be("Move time");
            chips.Q<Button>("0")!.text.Should().Be("No limit");
            chips.Q<Button>("15")!.text.Should().Be("15 sec");
            chips.Q<Button>("90")!.text.Should().Be("90 sec");

            disposables.Dispose();
        }
    }
}