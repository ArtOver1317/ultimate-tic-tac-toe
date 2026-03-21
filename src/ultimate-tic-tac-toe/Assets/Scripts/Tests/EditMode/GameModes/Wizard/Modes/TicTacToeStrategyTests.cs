using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard.Modes
{
    [TestFixture]
    [Category("Unit")]
    public class TicTacToeStrategyTests
    {
        private TicTacToeStrategy _sut;

        [TearDown]
        public void TearDown() => _sut = null;

        [Test]
        public void WhenTicTacToeStrategyCreatedWithNullFactory_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new TicTacToeStrategy(createSettingsViewModel: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenTicTacToeStrategyCreatedWithInvalidBounds_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Func<TicTacToeSettingsViewModel> factory = () => new TicTacToeSettingsViewModel();

            // Act
            Action minNotPositive = () => _ = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: factory,
                minBoardSize: 0,
                maxBoardSize: 10,
                defaultBoardSize: 3);

            Action maxLessThanMin = () => _ = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: factory,
                minBoardSize: 3,
                maxBoardSize: 2,
                defaultBoardSize: 3);

            Action defaultOutOfBounds = () => _ = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: factory,
                minBoardSize: 3,
                maxBoardSize: 10,
                defaultBoardSize: 11);

            // Assert
            minNotPositive.Should().Throw<ArgumentOutOfRangeException>();
            maxLessThanMin.Should().Throw<ArgumentOutOfRangeException>();
            defaultOutOfBounds.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenCreatePresentationCalledAndFactoryReturnsNull_ThenThrowsInvalidOperationException()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => null);

            // Act
            Action act = () => _ = _sut.CreatePresentation();

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenCreatePresentationCalledMultipleTimes_ThenCreatesNewViewModelInstanceEachTime()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var p1 = _sut.CreatePresentation();
            var p2 = _sut.CreatePresentation();

            try
            {
                // Assert
                ReferenceEquals(p1.ViewModel, p2.ViewModel).Should().BeFalse();
            }
            finally
            {
                p1.ViewModel.Dispose();
                p2.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenReturnsNonNullPresentationWithExpectedUxmlKey()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                presentation.UxmlAssetKey.Should().Be("ui/mode-settings/tic-tac-toe");
                presentation.ViewModel.Should().NotBeNull();
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenViewModelIsConfiguredBeforeReturningPresentation()
        {
            // Arrange
            _sut = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: () => new TicTacToeSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 4,
                defaultBoardSize: 4);

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                var vm = presentation.ViewModel.Should().BeOfType<TicTacToeSettingsViewModel>().Subject;
                vm.BoardSize.CurrentValue.Should().Be(4);

                var config = vm.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
                config.BoardSize.Should().Be(4);
                vm.IsValid.CurrentValue.Should().BeTrue();
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenViewModelHasDefaultBoardSizeAndConfigMatches()
        {
            // Arrange
            _sut = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: () => new TicTacToeSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 10,
                defaultBoardSize: 7);

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                var vm = (TicTacToeSettingsViewModel)presentation.ViewModel;
                vm.BoardSize.CurrentValue.Should().Be(7);

                var config = vm.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
                config.BoardSize.Should().Be(7);
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenValidateConfigCalledWithNull_ThenReturnsConfigRequiredError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(null).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidateConfigCalledWithWrongConfigType_ThenReturnsTicTacToeConfigInvalidError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(Substitute.For<IGameConfig>()).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.TicTacToeConfigInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeOutOfBounds_ThenReturnsBoardSizeInvalidError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: () => new TicTacToeSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 3);

            // Act
            var tooSmallError = _sut.ValidateConfig(new TicTacToeConfig(2)).Should().ContainSingle().Which;
            var tooLargeError = _sut.ValidateConfig(new TicTacToeConfig(6)).Should().ContainSingle().Which;

            // Assert
            tooSmallError.Field.Should().Be("BoardSize");
            tooSmallError.MessageKey.Should().Be("Errors.GameWizard.TicTacToeBoardSizeInvalid");

            tooLargeError.Field.Should().Be("BoardSize");
            tooLargeError.MessageKey.Should().Be("Errors.GameWizard.TicTacToeBoardSizeInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeEqualMinOrMax_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: () => new TicTacToeSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 3);

            // Act
            var min = _sut.ValidateConfig(new TicTacToeConfig(3));
            var max = _sut.ValidateConfig(new TicTacToeConfig(5));

            // Assert
            min.Should().BeEmpty();
            max.Should().BeEmpty();
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeInBounds_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new TicTacToeStrategy(
                gameId: "classic",
                createSettingsViewModel: () => new TicTacToeSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 4);

            // Act
            var errors = _sut.ValidateConfig(new TicTacToeConfig(4));

            // Assert
            errors.Should().BeEmpty();
        }

        [Test]
        public void WhenValidateConfigCalledWithLegacyUltimateFlag_ThenReturnsConfigInvalidError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(new TicTacToeConfig(3, isUltimate: true)).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.TicTacToeConfigInvalid");
        }
    }
}
