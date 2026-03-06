using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.Localization;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class BattleshipStrategyTests
    {
        private BattleshipStrategy _sut;

        [TearDown]
        public void TearDown() => _sut = null;

        [Test]
        public void WhenCreatePresentationCalled_ThenReturnsExpectedUxmlKey()
        {
            // Arrange
            _sut = CreateStrategy();

            // Act
            var presentation = _sut.CreatePresentation();

            // Assert
            presentation.UxmlAssetKey.Should().Be("ui/mode-settings/battleship");
            presentation.ViewModel.Should().BeOfType<BattleshipSettingsViewModel>();

            presentation.ViewModel.Dispose();
        }

        [Test]
        public void WhenGetSupportedBotDifficultyIdsCalled_ThenReturnsSingleDefaultId()
        {
            // Arrange
            _sut = CreateStrategy();

            // Act
            var ids = new List<string>(_sut.GetSupportedBotDifficultyIds());

            // Assert
            ids.Should().Equal(BattleshipStrategy.DefaultBotDifficultyId);
        }

        [Test]
        public void WhenValidateConfigCalledWithWrongType_ThenReturnsInvalidConfigError()
        {
            // Arrange
            _sut = CreateStrategy();

            // Act
            var error = _sut.ValidateConfig(Substitute.For<IGameConfig>()).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be(WizardFieldNames.GameConfig);
            error.MessageKey.Should().Be("Errors.GameWizard.BattleshipConfigInvalid");
        }

        [Test]
        public void WhenValidateForStartAndHumanLocalSelected_ThenReturnsLocalUnsupportedError()
        {
            // Arrange
            _sut = CreateStrategy();
            var snapshot = GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithMoveTimeLimitSeconds(30);

            // Act
            var error = _sut.ValidateForStart(snapshot).Should().ContainSingle().Which;

            // Assert
            error.MessageKey.Should().Be("Errors.GameWizard.BattleshipLocalHumanUnsupported");
        }

        [Test]
        public void WhenValidateForStartAndOnlineMoveTimerIsZero_ThenReturnsMoveTimerRequiredError()
        {
            // Arrange
            _sut = CreateStrategy();
            var snapshot = GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithMoveTimeLimitSeconds(0);

            // Act
            var error = _sut.ValidateForStart(snapshot).Should().ContainSingle().Which;

            // Assert
            error.MessageKey.Should().Be("Errors.GameWizard.BattleshipMoveTimerRequired");
        }

        [Test]
        public void WhenValidateForStartAndPlacementTimerIsZero_ThenReturnsPlacementTimerRequiredError()
        {
            // Arrange
            _sut = CreateStrategy();
            var snapshot = GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(0))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithMoveTimeLimitSeconds(30);

            // Act
            var error = _sut.ValidateForStart(snapshot).Should().ContainSingle().Which;

            // Assert
            error.MessageKey.Should().Be("Errors.GameWizard.BattleshipPlacementTimerRequired");
        }

        private static BattleshipStrategy CreateStrategy() =>
            new(() => new BattleshipSettingsViewModel(
                MoveTimerPresetsConfig.CreateRuntimeDefault(),
                CreateLocalization()));

        private static ILocalizationService CreateLocalization()
        {
            var localization = Substitute.For<ILocalizationService>();
            localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => R3.Observable.Return(callInfo.Arg<TextKey>().Value));
            return localization;
        }
    }
}
