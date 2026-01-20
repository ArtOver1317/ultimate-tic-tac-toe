using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class ClassicModeStrategyTests
    {
        private ClassicModeStrategy _sut;

        [TearDown]
        public void TearDown() => _sut = null;

        [Test]
        public void WhenClassicModeStrategyCreatedWithNullFactory_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new ClassicModeStrategy(createSettingsViewModel: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenClassicModeStrategyCreatedWithInvalidBounds_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Func<ClassicSettingsViewModel> factory = () => new ClassicSettingsViewModel();

            // Act
            Action minNotPositive = () => _ = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: factory,
                minBoardSize: 0,
                maxBoardSize: 10,
                defaultBoardSize: 3);

            Action maxLessThanMin = () => _ = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: factory,
                minBoardSize: 3,
                maxBoardSize: 2,
                defaultBoardSize: 3);

            Action defaultOutOfBounds = () => _ = new ClassicModeStrategy(
                modeId: "classic",
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
            _sut = new ClassicModeStrategy(createSettingsViewModel: () => null);

            // Act
            Action act = () => _ = _sut.CreatePresentation();

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenCreatePresentationCalledMultipleTimes_ThenCreatesNewViewModelInstanceEachTime()
        {
            // Arrange
            _sut = new ClassicModeStrategy(createSettingsViewModel: () => new ClassicSettingsViewModel());

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
            _sut = new ClassicModeStrategy(createSettingsViewModel: () => new ClassicSettingsViewModel());

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                presentation.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
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
            _sut = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: () => new ClassicSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 4,
                defaultBoardSize: 4);

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                var vm = presentation.ViewModel.Should().BeOfType<ClassicSettingsViewModel>().Subject;
                vm.BoardSize.CurrentValue.Should().Be(4);

                var config = vm.Config.CurrentValue.Should().BeOfType<ClassicModeConfig>().Subject;
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
            _sut = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: () => new ClassicSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 10,
                defaultBoardSize: 7);

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                var vm = (ClassicSettingsViewModel)presentation.ViewModel;
                vm.BoardSize.CurrentValue.Should().Be(7);

                var config = vm.Config.CurrentValue.Should().BeOfType<ClassicModeConfig>().Subject;
                config.BoardSize.Should().Be(7);
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenValidateConfigCalledWithNull_ThenReturnsModeConfigRequiredError()
        {
            // Arrange
            _sut = new ClassicModeStrategy(createSettingsViewModel: () => new ClassicSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(null).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("ModeConfig");
            error.MessageKey.Should().Be("Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenValidateConfigCalledWithWrongConfigType_ThenReturnsClassicConfigInvalidError()
        {
            // Arrange
            _sut = new ClassicModeStrategy(createSettingsViewModel: () => new ClassicSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(new UltimateModeConfig()).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("ModeConfig");
            error.MessageKey.Should().Be("Errors.GameModeWizard.ClassicConfigInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeOutOfBounds_ThenReturnsBoardSizeInvalidError()
        {
            // Arrange
            _sut = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: () => new ClassicSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 3);

            // Act
            var tooSmallError = _sut.ValidateConfig(new ClassicModeConfig(2)).Should().ContainSingle().Which;
            var tooLargeError = _sut.ValidateConfig(new ClassicModeConfig(6)).Should().ContainSingle().Which;

            // Assert
            tooSmallError.Field.Should().Be("BoardSize");
            tooSmallError.MessageKey.Should().Be("Errors.GameModeWizard.ClassicBoardSizeInvalid");

            tooLargeError.Field.Should().Be("BoardSize");
            tooLargeError.MessageKey.Should().Be("Errors.GameModeWizard.ClassicBoardSizeInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeEqualMinOrMax_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: () => new ClassicSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 3);

            // Act
            var min = _sut.ValidateConfig(new ClassicModeConfig(3));
            var max = _sut.ValidateConfig(new ClassicModeConfig(5));

            // Assert
            min.Should().BeEmpty();
            max.Should().BeEmpty();
        }

        [Test]
        public void WhenValidateConfigCalledWithBoardSizeInBounds_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new ClassicModeStrategy(
                modeId: "classic",
                createSettingsViewModel: () => new ClassicSettingsViewModel(),
                minBoardSize: 3,
                maxBoardSize: 5,
                defaultBoardSize: 4);

            // Act
            var errors = _sut.ValidateConfig(new ClassicModeConfig(4));

            // Assert
            errors.Should().BeEmpty();
        }
    }
}
