using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.Session
{
    public partial class GameSessionTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenModeNotSelected_ThenCanStartIsFalse(string gameId)
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameRequired");
        }

        [Test]
        public void WhenModeSelectedButConfigMissing_ThenCanStartIsFalse()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(null)
                .WithBotDifficultyId("Easy"));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCatalogIsMissingAndModeSelected_ThenCanStartIsFalse()
        {
            using var sut = new GameSession(GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "GameCatalog" && e.MessageKey == "Errors.GameWizard.GameCatalogMissing");
        }

        [Test]
        public void WhenModeIsUnknown_ThenValidationContainsUnknownModeError()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId("unknown")
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            var errors = sut.ValidationErrors.CurrentValue;

            errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameUnknown");
        }

        [Test]
        public void WhenModeConfigDoesNotMatchSelectedMode_ThenValidationContainsModeConfigError()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(Substitute.For<IGameConfig>())
                .WithBotDifficultyId("Easy"));

            var errors = sut.ValidationErrors.CurrentValue;

            errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.TicTacToeConfigInvalid");
        }

        [Test]
        public void WhenOpponentIsBotAndDifficultySet_ThenCanStartIsTrue()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            var canStart = sut.CanStart.CurrentValue;

            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenOpponentIsBotAndDifficultyMissing_ThenCanStartIsFalse(string difficulty)
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId(difficulty));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "BotDifficultyId" && e.MessageKey == "Errors.GameWizard.DifficultyRequired");
        }

        [Test]
        public void WhenOpponentIsHumanAndDifficultyMissing_ThenNoValidationErrorForDifficulty()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithBotDifficultyId(null));

            var errors = sut.ValidationErrors.CurrentValue;

            errors.Should().NotContain(e => e.Field == "BotDifficultyId");
        }

        [Test]
        public void WhenOpponentIsHumanLocalAndModeSelectedAndConfigSet_ThenCanStartIsTrue()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            var canStart = sut.CanStart.CurrentValue;

            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenOpponentIsDirectInviteAndSessionIdSet_ThenCanStartIsTrue()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("AB2CD7"));

            var canStart = sut.CanStart.CurrentValue;

            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenOpponentIsDirectInviteAndNoSessionId_ThenCanStartIsFalse(string playerId)
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == WizardFieldNames.InviteSessionId && e.MessageKey == "Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public void WhenOpponentIsDirectInviteAndSessionIdInvalid_ThenCanStartIsFalse()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("invalid-id"));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == WizardFieldNames.InviteSessionId && e.MessageKey == "Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public void WhenOpponentIsHumanMatchmaking_ThenCanStartIsTrue()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            canStart.Should().BeTrue();
            errors.Should().BeEmpty();
        }

        [Test]
        public void WhenStateTransitionsFromInvalidToValid_ThenValidationErrorsClearedAndCanStartBecomesTrue()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(null));

            sut.CanStart.CurrentValue.Should().BeFalse();
            sut.ValidationErrors.CurrentValue.Should().NotBeEmpty();

            sut.Update(s => s.WithTargetPlayerId("AB2CD7"));

            sut.CanStart.CurrentValue.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }
    }
}