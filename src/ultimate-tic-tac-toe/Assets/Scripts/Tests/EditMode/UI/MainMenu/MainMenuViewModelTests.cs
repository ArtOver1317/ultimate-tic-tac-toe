using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.UI.MainMenu;

namespace Tests.EditMode.UI.MainMenu
{
    [TestFixture]
    public class MainMenuViewModelTests
    {
        private ILocalizationService _localizationMock;

        [SetUp]
        public void SetUp()
        {
            _localizationMock = Substitute.For<ILocalizationService>();
            
            // Setup mock to return correct values based on key
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.Title"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Ultimate Tic-Tac-Toe"));
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.StartButton"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Start Game"));
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.Settings"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Settings"));

            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.ExitButton"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Exit"));
        }

        [Test]
        public void WhenInitialized_ThenRequestsKeysFromMainMenuTable()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);

            // Act
            sut.Initialize();

            // Assert
            _localizationMock.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "MainMenu"),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.Title"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localizationMock.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "MainMenu"),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.StartButton"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localizationMock.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "MainMenu"),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.Settings"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localizationMock.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "MainMenu"),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.ExitButton"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            sut.Dispose();
        }

        [Test]
        public void WhenDisposedMultipleTimes_ThenDoesNotThrow()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();

            // Act
            sut.Dispose();
            Action act = () => sut.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenLocalizationObservableEmitsNewValue_ThenStartButtonTextUpdates()
        {
            // Arrange
            var localeSubject = new Subject<string>();
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.StartButton"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(localeSubject);

            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();

            string startButton = null;
            using var d = sut.StartButtonText.Subscribe(text => startButton = text);

            // Act
            localeSubject.OnNext("Начать игру");

            // Assert
            startButton.Should().Be("Начать игру");
            
            localeSubject.Dispose();
        }

    }
}