using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
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
    }
}