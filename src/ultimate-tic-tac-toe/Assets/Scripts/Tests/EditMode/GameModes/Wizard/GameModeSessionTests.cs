using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameModeSessionTests
    {
        private GameModeSession _sut;

        [SetUp]
        public void SetUp() => _sut = new GameModeSession();

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [Test]
        public void WhenCreated_ThenSnapshotIsDefault()
        {
            // Arrange
            using var sut = new GameModeSession();

            // Act
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.SelectedModeId.Should().BeNull();
            snapshot.ModeConfig.Should().BeNull();
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
            Action act = () => _ = new GameModeSession(initialSnapshot: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSessionCreatedWithInvalidSnapshot_ThenSnapshotIsNormalized()
        {
            // Arrange
            var invalid = GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("player-1")
                .WithMatchmakingState(MatchmakingState.Searching)
                .WithBotDifficultyId("Hard");

            // Act
            using var sut = new GameModeSession(invalid);
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.OpponentType.Should().Be(OpponentType.Human);
            snapshot.BotDifficultyId.Should().BeNull("bot difficulty must not leak into human opponent state");
            snapshot.TargetPlayerId.Should().Be("player-1", "direct invite requires keeping target player id");
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
            _sut.Update(s => s.WithSelectedModeId("Classic"));
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("user-1"));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            // Assert
            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenMatchmakingStateResetToIdle()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            // Assert
            sut.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.Matchmaking);
        }

        [Test]
        public void WhenOpponentChangedToHuman_ThenBotDifficultyIsCleared()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId("Hard"));

            // Act
            sut.Update(s => s.WithOpponentType(OpponentType.Human));

            // Assert
            sut.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenHumanKindChangedFromDirectInvite_ThenTargetPlayerIdIsCleared()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithMatchmakingState(MatchmakingState.Idle));

            // Act
            sut.Update(s => s.WithMatchmakingState(MatchmakingState.Searching));

            // Assert
            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenOpponentIsHumanAndReducerSetsBotDifficulty_ThenBotDifficultyIsRemovedByNormalize()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            sut.Update(s => s.WithBotDifficultyId("Hard"));

            // Assert
            sut.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenSelectedModeIdChanges_ThenModeConfigIsCleared()
        {
            // Arrange
            var classicConfig = new ClassicModeConfig(boardSize: 3);
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(classicConfig));

            // Act
            sut.Update(s => s.WithSelectedModeId("Ultimate"));

            // Assert
            sut.Snapshot.CurrentValue.ModeConfig.Should().BeNull();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenModeNotSelected_ThenCanStartIsFalse(string modeId)
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId(modeId)
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "SelectedModeId" && e.MessageKey == "error.mode_required");
        }

        [Test]
        public void WhenModeSelectedButConfigMissing_ThenCanStartIsFalse()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(null)
                .WithBotDifficultyId("Easy"));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "ModeConfig" && e.MessageKey == "error.mode_config_required");
        }

        [Test]
        public void WhenOpponentIsBotAndDifficultySet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithBotDifficultyId(difficulty));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "BotDifficultyId" && e.MessageKey == "error.difficulty_required");
        }

        [Test]
        public void WhenOpponentIsHumanLocalAndModeSelectedAndConfigSet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            var canStart = sut.CanStart.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenOpponentIsDirectInviteAndPlayerIdSet_ThenCanStartIsTrue()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("user1"));

            // Act
            var canStart = sut.CanStart.CurrentValue;

            // Assert
            canStart.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenOpponentIsDirectInviteAndNoPlayerId_ThenCanStartIsFalse(string playerId)
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "TargetPlayerId" && e.MessageKey == "error.player_id_required");
        }

        [Test]
        public void WhenOpponentIsHumanMatchmaking_ThenCanStartIsFalse()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            // Act
            var canStart = sut.CanStart.CurrentValue;
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            canStart.Should().BeFalse();
            errors.Should().ContainSingle(e => e.Field == "Matchmaking" && e.MessageKey == "error.matchmaking_config_missing");
        }

        [Test]
        public void WhenStateTransitionsFromInvalidToValid_ThenValidationErrorsClearedAndCanStartBecomesTrue()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithBotDifficultyId(null));

            sut.CanStart.CurrentValue.Should().BeFalse();
            sut.ValidationErrors.CurrentValue.Should().NotBeEmpty();

            // Act
            sut.Update(s => s.WithBotDifficultyId("Easy"));

            // Assert
            sut.CanStart.CurrentValue.Should().BeTrue();
            sut.ValidationErrors.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public void WhenValidBotState_ThenBuildReturnsBotConfigWithCorrectData()
        {
            // Arrange
            var modeId = "Classic";
            var modeConfig = new ClassicModeConfig(boardSize: 3);
            var difficulty = "Easy";

            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId(modeId)
                .WithModeConfig(modeConfig)
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId(difficulty));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameModeId.Should().Be(modeId);
            result.Value.ModeConfig.Should().BeSameAs(modeConfig);

            var opponent = result.Value.OpponentConfig.Should().BeOfType<BotOpponentConfig>().Subject;
            opponent.DifficultyId.Should().Be(difficulty);
        }

        [Test]
        public void WhenValidLocalHumanState_ThenBuildReturnsLocalHumanConfigWithCorrectData()
        {
            // Arrange
            var modeId = "Classic";
            var modeConfig = new ClassicModeConfig(boardSize: 3);

            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId(modeId)
                .WithModeConfig(modeConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameModeId.Should().Be(modeId);
            result.Value.ModeConfig.Should().BeSameAs(modeConfig);
            result.Value.OpponentConfig.Should().BeOfType<LocalHumanConfig>();
        }

        [Test]
        public void WhenValidDirectInviteState_ThenBuildReturnsDirectInviteConfigWithCorrectData()
        {
            // Arrange
            var modeId = "Classic";
            var modeConfig = new ClassicModeConfig(boardSize: 3);
            var playerId = "user1";

            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId(modeId)
                .WithModeConfig(modeConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.GameModeId.Should().Be(modeId);
            result.Value.ModeConfig.Should().BeSameAs(modeConfig);

            var opponent = result.Value.OpponentConfig.Should().BeOfType<DirectInviteConfig>().Subject;
            opponent.PlayerId.Should().Be(playerId);
        }

        [Test]
        public void WhenCanStartIsFalse_ThenBuildReturnsFailure()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default);
            sut.CanStart.CurrentValue.Should().BeFalse();

            // Act
            var result = sut.BuildLaunchConfig();

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Should().ContainSingle(e => e.Field == "SelectedModeId" && e.MessageKey == "error.mode_required");
            result.Errors.Should().ContainSingle(e => e.Field == "ModeConfig" && e.MessageKey == "error.mode_config_required");
            result.Errors.Should().ContainSingle(e => e.Field == "BotDifficultyId" && e.MessageKey == "error.difficulty_required");
        }

        [Test]
        public void WhenBuildLaunchConfigCalled_ThenFailureErrorsMatchValidationErrors()
        {
            // Arrange
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default);
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
            using var sut = new GameModeSession(GameModeSessionSnapshot.Default
                .WithSelectedModeId("Classic")
                .WithModeConfig(new ClassicModeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            sut.CanStart.CurrentValue.Should().BeTrue();

            // Act
            sut.Reset();

            // Assert
            var snapshot = sut.Snapshot.CurrentValue;
            snapshot.SelectedModeId.Should().BeNull();
            snapshot.ModeConfig.Should().BeNull();
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
