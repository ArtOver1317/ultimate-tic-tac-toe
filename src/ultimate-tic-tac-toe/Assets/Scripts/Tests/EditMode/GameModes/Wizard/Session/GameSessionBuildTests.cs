using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.Session
{
    public partial class GameSessionTests
    {
        [Test]
        public void WhenValidBotState_ThenBuildReturnsBotConfigWithCorrectData()
        {
            const string gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);
            const string difficulty = "Easy";

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId(difficulty));

            var result = sut.BuildLaunchConfig();

            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);
            var opponent = result.Value.OpponentConfig.Should().BeOfType<BotOpponentConfig>().Subject;
            opponent.DifficultyId.Should().Be(difficulty);
        }

        [Test]
        public void WhenValidLocalHumanState_ThenBuildReturnsLocalHumanConfigWithCorrectData()
        {
            const string gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            var result = sut.BuildLaunchConfig();

            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);
            result.Value.OpponentConfig.Should().BeOfType<LocalHumanConfig>();
        }

        [Test]
        public void WhenValidDirectInviteState_ThenBuildReturnsDirectInviteConfigWithCorrectData()
        {
            const string gameId = TicTacToeStrategy.DefaultGameId;
            var gameConfig = new TicTacToeConfig(boardSize: 3);
            const string playerId = "AB2CD7";

            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(gameId)
                .WithGameConfig(gameConfig)
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(playerId));

            var result = sut.BuildLaunchConfig();

            result.IsSuccess.Should().BeTrue();
            result.Value.GameId.Should().Be(gameId);
            result.Value.GameConfig.Should().BeSameAs(gameConfig);
            var opponent = result.Value.OpponentConfig.Should().BeOfType<DirectInviteConfig>().Subject;
            opponent.SessionId.Should().Be(playerId);
        }

        [Test]
        public void WhenMoveTimeLimitSetInSession_ThenBuildLaunchConfigReturnsSameMoveTimeLimit()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId("Easy")
                .WithMoveTimeLimitSeconds(60));

            var result = sut.BuildLaunchConfig();

            result.IsSuccess.Should().BeTrue();
            result.Value.MoveTimeLimitSeconds.Should().Be(60);
        }

        [Test]
        public void WhenCanStartIsFalse_ThenBuildReturnsFailure()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default);
            sut.CanStart.CurrentValue.Should().BeFalse();

            var result = sut.BuildLaunchConfig();

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Should().ContainSingle(e => e.Field == "SelectedGameId" && e.MessageKey == "Errors.GameWizard.GameRequired");
            result.Errors.Should().ContainSingle(e => e.Field == "GameConfig" && e.MessageKey == "Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenBuildLaunchConfigCalled_ThenFailureErrorsMatchValidationErrors()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default);
            var expected = sut.ValidationErrors.CurrentValue;

            expected.Should().NotBeEmpty("default snapshot is invalid in Phase 1 and must produce validation errors");

            var result = sut.BuildLaunchConfig();

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().BeEquivalentTo(expected, options => options.WithoutStrictOrdering());
        }

        [Test]
        public void WhenResetCalled_ThenSnapshotRestoredToDefault()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(new TicTacToeConfig(boardSize: 3))
                .WithBotDifficultyId("Easy"));

            sut.CanStart.CurrentValue.Should().BeTrue();

            sut.Reset();

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
            Action act = () => _sut.Update(_ => null);

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenUpdateCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            _sut.Dispose();

            Action act = () => _sut.Update(s => s);

            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenBuildLaunchConfigCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            _sut.Dispose();

            Action act = () => _ = _sut.BuildLaunchConfig();

            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenResetCalledAfterDispose_ThenThrowsObjectDisposedException()
        {
            _sut.Dispose();

            Action act = () => _sut.Reset();

            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenDoesNotThrow()
        {
            _sut.Dispose();

            Action act = () => _sut.Dispose();

            act.Should().NotThrow();
        }
    }
}