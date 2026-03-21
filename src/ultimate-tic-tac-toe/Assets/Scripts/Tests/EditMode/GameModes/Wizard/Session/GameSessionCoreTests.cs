using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.Session
{
    public partial class GameSessionTests
    {
        [Test]
        public void WhenCreated_ThenSnapshotIsDefault()
        {
            using var sut = new GameSession(_catalog);

            var snapshot = sut.Snapshot.CurrentValue;

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
            Action act = () => _ = new GameSession(_catalog, initialSnapshot: null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSessionCreatedWithInvalidSnapshot_ThenSnapshotIsNormalized()
        {
            var invalid = GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("1001")
                .WithMatchmakingState(MatchmakingState.Searching)
                .WithBotDifficultyId("Hard");

            using var sut = new GameSession(_catalog, invalid);
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.OpponentType.Should().Be(OpponentType.Human);
            snapshot.BotDifficultyId.Should().Be("Hard", "bot difficulty should be preserved when switching to human");
            snapshot.TargetPlayerId.Should().Be("1001", "direct invite requires keeping target player id");
            snapshot.MatchmakingState.Should().Be(MatchmakingState.Idle, "matchmaking state must reset when not in matchmaking kind");
        }

        [Test]
        public void WhenUpdateCalled_ThenNewSnapshotInstanceEmitted()
        {
            var before = _sut.Snapshot.CurrentValue;

            _sut.Update(s => s);

            var after = _sut.Snapshot.CurrentValue;
            ReferenceEquals(before, after).Should().BeFalse();
        }

        [Test]
        public void WhenUpdateCalled_ThenVersionIncrements()
        {
            var before = _sut.Snapshot.CurrentValue.Version;

            _sut.Update(s => s);

            _sut.Snapshot.CurrentValue.Version.Should().Be(before + 1);
        }

        [Test]
        public void WhenUpdateCalledWithNullReducer_ThenThrowsArgumentNullException()
        {
            Action act = () => _sut.Update(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSetModeConfigCalledWithNull_ThenThrowsArgumentNullException()
        {
            Action act = () => _sut.SetModeConfig(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenResetCalled_ThenVersionIncrements()
        {
            _sut.Update(s => s.WithSelectedGameId(TicTacToeStrategy.DefaultGameId));
            var before = _sut.Snapshot.CurrentValue.Version;

            _sut.Reset();

            _sut.Snapshot.CurrentValue.Version.Should().Be(before + 1);
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenTargetPlayerIdIsCleared()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("2001"));

            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenMatchmakingStateResetToIdle()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithMatchmakingState(MatchmakingState.Searching));

            sut.Update(s => s.WithOpponentType(OpponentType.Bot).WithMatchmakingState(MatchmakingState.Searching));

            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenOpponentChangedToBot_ThenHumanKindIsPreserved()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            sut.Update(s => s.WithOpponentType(OpponentType.Bot));

            sut.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.Matchmaking);
        }

        [Test]
        public void WhenOpponentChangedToHuman_ThenBotDifficultyIsPreserved()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId("Hard"));

            sut.Update(s => s.WithOpponentType(OpponentType.Human));

            sut.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenHumanKindChangedFromDirectInvite_ThenTargetPlayerIdIsCleared()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("123"));

            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Local));

            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenHumanKindChangedFromMatchmaking_ThenMatchmakingStateResetsToIdle()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithMatchmakingState(MatchmakingState.Searching));

            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Local));

            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenHumanKindIsMatchmaking_ThenTargetPlayerIdIsCleared()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("123")
                .WithMatchmakingState(MatchmakingState.Idle));

            sut.Update(s => s.WithHumanOpponentKind(HumanOpponentKind.Matchmaking));

            sut.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentIsBot_ThenMatchmakingStateAlwaysIdleEvenIfReducerSetsSearching()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithMatchmakingState(MatchmakingState.Idle));

            sut.Update(s => s.WithMatchmakingState(MatchmakingState.Searching));

            sut.Snapshot.CurrentValue.MatchmakingState.Should().Be(MatchmakingState.Idle);
        }

        [Test]
        public void WhenOpponentIsHumanAndReducerSetsBotDifficulty_ThenBotDifficultyIsPreserved()
        {
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));

            sut.Update(s => s.WithBotDifficultyId("Hard"));

            sut.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenSelectedGameIdChanges_ThenGameConfigIsCleared()
        {
            var classicConfig = new TicTacToeConfig(boardSize: 3);
          
            using var sut = new GameSession(_catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(TicTacToeStrategy.DefaultGameId)
                .WithGameConfig(classicConfig));

            sut.Update(s => s.WithSelectedGameId("other-game"));

            sut.Snapshot.CurrentValue.GameConfig.Should().BeNull();
        }
    }
}