using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameSessionTests
    {
        private GameSession _sut;
        private IGameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = CreateCatalog();
            _sut = new GameSession(_catalog);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        private static IGameCatalog CreateCatalog()
        {
            return new GameCatalog(new IGameStrategy[]
            {
                new TicTacToeStrategy(() => new TicTacToeSettingsViewModel())
            });
        }

        [Test]
        public void WhenCreated_ThenSnapshotIsDefault()
        {
            // Arrange
            using var sut = new GameSession(_catalog);

            // Act
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.SelectedGameId.Should().BeNull();
            snapshot.GameConfig.Should().BeNull();
            snapshot.OpponentType.Should().Be(OpponentType.Bot);
            snapshot.BotDifficultyId.Should().BeNull();
            snapshot.TargetPlayerId.Should().BeNull();
            snapshot.MatchmakingState.Should().Be(MatchmakingState.Idle);
            snapshot.Version.Should().Be(0);
        }

        [Test]
        public void WhenSessionCreatedWithNullInitialSnapshot_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new GameSession(_catalog, initialSnapshot: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSessionCreatedWithInvalidSnapshot_ThenSnapshotIsNormalized()
        {
            // Arrange
            var invalid = GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("1001")
                .WithMatchmakingState(MatchmakingState.Searching)
                .WithBotDifficultyId("Hard");

            // Act
            using var sut = new GameSession(_catalog, invalid);
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.OpponentType.Should().Be(OpponentType.Human);
            snapshot.BotDifficultyId.Should().Be("Hard", "bot difficulty should be preserved when switching to human");
            snapshot.TargetPlayerId.Should().Be("1001", "direct invite requires keeping target player id");
            snapshot.MatchmakingState.Should().Be(MatchmakingState.Idle, "matchmaking state must reset when not in matchmaking kind");
        }

        [Test]
        public void WhenUpdateCalled_ThenNewSnapshotInstanceEmitted()
        {
            // Arrange
            var before = _sut.Snapshot.CurrentValue;

            // Act
            _sut.Update(s => s);

            // Assert
            var after = _sut.Snapshot.CurrentValue;
            ReferenceEquals(before, after).Should().BeFalse();
        }

        [Test]
        public void WhenUpdateCalled_ThenVersionIncrements()
        {
            // Arrange
            var before = _sut.Snapshot.CurrentValue.Version;

            // Act
            _sut.Update(s => s);

            // Assert
            _sut.Snapshot.CurrentValue.Version.Should().Be(before + 1);
        }

        [Test]
        public void WhenUpdateCalledWithNullReducer_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _sut.Update(null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSetModeConfigCalledWithNull_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _sut.SetModeConfig(null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenResetCalled_ThenVersionIncrements()
        {
            // Arrange
            _sut.Update(s => s.WithSelectedGameId(TicTacToeStrategy.DefaultGameId));
            var before = _sut.Snapshot.CurrentValue.Version;

            // Act
            _sut.Reset();

            // Assert
            _sut.Snapshot.CurrentValue.Version.Should().Be(before + 1);
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenTargetPlayerIdIsCleared()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("2001"));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            // Assert
            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenMatchmakingStateResetToIdle()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithMatchmakingState(MatchmakingState.Searching));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Bot).WithMatchmakingState(MatchmakingState.Searching));

            // Assert
            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenHumanKindIsPreserved()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            // Assert
            sut.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.Matchmaking);
        }

        [Test]
        public void WhenOpponentChangedToHuman_ThenBotDifficultyIsPreserved()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId("Hard"));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Human));

            // Assert
            sut.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenHumanKindChangedFromDirectInvite_ThenTargetPlayerIdIsCleared()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("123"));

            // Act
            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Local));

            // Assert
            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenHumanKindChangedFromMatchmaking_ThenMatchmakingStateResetsToIdle()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithMatchmakingState(MatchmakingState.Searching));

            // Act
            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Local));

            // Assert
            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenHumanKindIsMatchmaking_ThenTargetPlayerIdIsCleared()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("123")
                .WithMatchmakingState(MatchmakingState.Idle));

            // Act
            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Assert
            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentIsBot_ThenMatchmakingStateAlwaysIdleEvenIfReducerSetsSearching()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithMatchmakingState(MatchmakingState.Idle));

            // Act
            sut.Update(s => s.WithMatchmakingState(MatchmakingState.Searching));

            // Assert
            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenOpponentIsHumanAndReducerSetsBotDifficulty_ThenBotDifficultyIsPreserved()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            sut.Update(s => s.WithBotDifficultyId("Hard"));

            // Assert
            sut.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenSelectedGameIdChanges_ThenGameConfigIsCleared()
        {
            // Arrange
            var classicConfig = new TicTacToeConfig(boardSize: 3);
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(classicConfig));

            // Act
            sut.Update(s => s.WithSelectedGameId("other-game"));

            // Assert
            sut.Snapshot.CurrentValue.GameConfig.Should().BeNull();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenModeNotSelected_ThenCanStartIsFalse(string gameId)
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameRequired");
        }

        [Test]
        public void WhenModeSelectedButConfigMissing_ThenCanStartIsFalse()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(null)
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCatalogIsMissingAndModeSelected_ThenCanStartIsFalse()
        {
            // Arrange
            using var sut = new GameSession(GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "GameCatalog" && e.MessageKey == "Errors.GameWizard.GameCatalogMissing");
        }

        [Test]
        public void WhenModeIsUnknown_ThenValidationContainsUnknownModeError()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId("unknown")
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            // Act
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameUnknown");
        }

        [Test]
        public void WhenModeConfigDoesNotMatchSelectedMode_ThenValidationContainsModeConfigError()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(Substitute.For<IGameConfig>())
                .WithBotDifficultyId("Easy"));

            // Act
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.TicTacToeConfigInvalid");
        }

        [Test]
        public void WhenOpponentIsBotAndDifficultySet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenOpponentIsBotAndDifficultyMissing_ThenCanStartIsFalse(string difficulty)
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId(difficulty));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "BotDifficultyId" && e.MessageKey == "Errors.GameWizard.DifficultyRequired");
        }

        [Test]
        public void WhenOpponentIsHumanAndDifficultyMissing_ThenNoValidationErrorForDifficulty()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithBotDifficultyId(null));

            // Act
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            errors.Should().NotContain(e => e.Field == "BotDifficultyId");
        }

        [Test]
        public void WhenOpponentIsHumanLocalAndModeSelectedAndConfigSet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            var canStart = sut.CanStart.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenOpponentIsDirectInviteAndSessionIdSet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("AB2CD7"));

            // Act
            var canStart = sut.CanStart.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenOpponentIsDirectInviteAndNoSessionId_ThenCanStartIsFalse(string playerId)
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == WizardFieldNames.InviteSessionId && e.MessageKey == "Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public void WhenOpponentIsDirectInviteAndSessionIdInvalid_ThenCanStartIsFalse()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("invalid-id"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == WizardFieldNames.InviteSessionId && e.MessageKey == "Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public void WhenOpponentIsHumanMatchmaking_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            errors.Should().BeEmpty();
        }

        [Test]
        public void WhenStateTransitionsFromInvalidToValid_ThenValidationErrorsClearedAndCanStartBecomesTrue()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(null));

            sut.CanStart.CurrentValue.Should().BeFalse();
            sut.ValidationErrors.CurrentValue.Should().NotBeEmpty();

            // Act
            sut.Update(s => s.WithTargetPlayerId("AB2CD7"));

            // Assert
            sut.CanStart.CurrentValue.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenValidBotState_ThenBuildReturnsBotConfigWithCorrectData()
        {
            // Arrange
            var gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);
            var difficulty = "Easy";

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId(difficulty));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);

            var opponent = result.Value.OpponentConfig.Should().BeOfType<BotOpponentConfig>().Subject;
            opponent.DifficultyId.Should().Be(difficulty);
        }

        [Test]
        public void WhenValidLocalHumanState_ThenBuildReturnsLocalHumanConfigWithCorrectData()
        {
            // Arrange
            var gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);
            result.Value.OpponentConfig.Should().BeOfType<LocalHumanConfig>();
        }

        [Test]
        public void WhenValidDirectInviteState_ThenBuildReturnsDirectInviteConfigWithCorrectData()
        {
            // Arrange
            var gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);
            var playerId = "AB2CD7";

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);

            var opponent = result.Value.OpponentConfig.Should().BeOfType<DirectInviteConfig>().Subject;
            opponent.SessionId.Should().Be(playerId);
        }

        [Test]
        public void WhenMoveTimeLimitSetInSession_ThenBuildLaunchConfigReturnsSameMoveTimeLimit()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId("Easy")
                .WithMoveTimeLimitSeconds(60));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.MoveTimeLimitSeconds.Should().Be(60);
        }

        [Test]
        public void WhenCanStartIsFalse_ThenBuildReturnsFailure()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default);
            sut.CanStart.CurrentValue.Should().BeFalse();

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameRequired");
            result.Errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenBuildLaunchConfigCalled_ThenFailureErrorsMatchValidationErrors()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default);
            var expected = sut.ValidationErrors.CurrentValue;

            expected.Should().NotBeEmpty("default snapshot is invalid in Phase 1 and must produce validation errors");

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().BeEquivalentTo(expected, options => options.WithoutStrictOrdering());
        }

        [Test]
        public void WhenResetCalled_ThenSnapshotRestoredToDefault()
        {
            // Arrange
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            sut.CanStart.CurrentValue.Should().BeTrue();

            // Act
            sut.Reset();

            // Assert
            var snapshot = sut.Snapshot.CurrentValue;
            snapshot.SelectedGameId.Should().BeNull();
            snapshot.GameConfig.Should().BeNull();
            snapshot.OpponentType.Should().Be(OpponentType.Bot);
            snapshot.BotDifficultyId.Should().BeNull();
            snapshot.HumanOpponentKind.Should().Be(HumanOpponentKind.Local);
            snapshot.TargetPlayerId.Should().BeNull();
            snapshot.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenUpdateReducerReturnsNull_ThenThrowsInvalidOperationException()
        {
            // Arrange
            Action act = () => _sut.Update(_ => null);

            // Act / Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenUpdateCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _sut.Update(s => s);

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenBuildLaunchConfigCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _ = _sut.BuildLaunchConfig();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenResetCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _sut.Reset();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenDoesNotThrow()
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _sut.Dispose();

            // Assert
            act.Should().NotThrow();
        }
    }
}
