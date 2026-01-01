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
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.ExitButton"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Exit"));
        }

        [Test]
        public void WhenInitialized_ThenHasCorrectDefaults()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();

            // Assert
            sut.Title.CurrentValue.Should().Be("Ultimate Tic-Tac-Toe");
            sut.StartButtonText.CurrentValue.Should().Be("Start Game");
            sut.ExitButtonText.CurrentValue.Should().Be("Exit");
            sut.IsInteractable.CurrentValue.Should().BeTrue();
            sut.StartGameRequested.Should().NotBeNull();
            sut.ExitRequested.Should().NotBeNull();
        }

        [Test]
        public void WhenSetInteractable_ThenUpdatesIsInteractable()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();

            // Act
            sut.SetInteractable(false);

            // Assert
            sut.IsInteractable.CurrentValue.Should().BeFalse();

            // Act again
            sut.SetInteractable(true);

            // Assert again
            sut.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenDisposed_ThenObservablesThrowObjectDisposedException()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();
            var valueEmitted = false;
            sut.StartGameRequested.Subscribe(_ => valueEmitted = true);

            sut.RequestStartGame();
            valueEmitted.Should().BeTrue("Subject should work before dispose");

            // Act
            sut.Dispose();

            // Assert
            Action actSubject = () => sut.RequestStartGame();
            actSubject.Should().Throw<ObjectDisposedException>();

            Action actProperty = () => sut.Title.Subscribe(_ => { });
            actProperty.Should().Throw<ObjectDisposedException>();
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
        public void WhenInitialize_ThenPlayLabelIsLocalized()
        {
            // Arrange
            var sut = new MainMenuViewModel(_localizationMock);

            // Act
            sut.Initialize();

            // Assert
            sut.StartButtonText.CurrentValue.Should().Be("Start Game");
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

            // Act
            localeSubject.OnNext("Начать игру");

            // Assert
            sut.StartButtonText.CurrentValue.Should().Be("Начать игру");
            
            localeSubject.Dispose();
        }

        [Test]
        public void WhenViewModelDisposedAndLocaleChanges_ThenNoUpdatesAndNoExceptions()
        {
            // Arrange
            var localeSubject = new Subject<string>();
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Is<TextKey>(k => k.Value == "MainMenu.StartButton"), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(localeSubject);

            var sut = new MainMenuViewModel(_localizationMock);
            sut.Initialize();
            var initialValue = sut.StartButtonText.CurrentValue;

            // Act
            sut.Dispose();
            Action act = () => localeSubject.OnNext("New Value");

            // Assert
            act.Should().NotThrow();
            sut.StartButtonText.CurrentValue.Should().Be(initialValue);
            
            localeSubject.Dispose();
        }

        [Test]
        public void WhenMainMenuViewModelInitializeCalledTwice_ThenDoesNotDuplicateSubscriptions()
        {
            // Arrange
            var localeSubject = new Subject<string>();
            
            _localizationMock.Observe(
                    Arg.Any<TextTableId>(), 
                    Arg.Any<TextKey>(), 
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(localeSubject);

            var sut = new MainMenuViewModel(_localizationMock);

            // Act
            sut.Initialize();
            sut.Initialize();

            // Assert: Each key should be observed exactly once (no duplicate subscriptions)
            _localizationMock.Received(1).Observe(
                Arg.Any<TextTableId>(),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.Title"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
                
            _localizationMock.Received(1).Observe(
                Arg.Any<TextTableId>(),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.StartButton"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
                
            _localizationMock.Received(1).Observe(
                Arg.Any<TextTableId>(),
                Arg.Is<TextKey>(k => k.Value == "MainMenu.ExitButton"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
            
            localeSubject.Dispose();
        }
    }
}