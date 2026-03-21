using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelOpponentTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenOpponentTypeChangedFromUI_ThenWritesThroughToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetOpponentType(OpponentType.Human);

            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.OpponentType.Should().Be(OpponentType.Human);
        }

        [Test]
        public void WhenOpponentTypeChangedFromSession_ThenDoesNotWriteBackToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default.WithOpponentType(OpponentType.Human).WithVersion(1));

            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Human);
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalled_ThenWritesThroughToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalledWithSameValue_ThenDoesNotCallSessionUpdate()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionHumanOpponentKindChanges_ThenHumanOpponentKindUpdatesWithoutWriteBack()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            using var sut = CreateSut();
            sut.Initialize();

            Action act = () => sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            act.Should().NotThrow();
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.Local);
        }

        [Test]
        public void WhenSetOpponentTypeCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            using var sut = CreateSut();
            sut.Initialize();

            Action act = () => sut.SetOpponentType(OpponentType.Human);

            act.Should().NotThrow();
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenOpponentTypeChangesToHuman_ThenIsHumanSettingsVisibleBecomesTrue()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetOpponentType(OpponentType.Human);

            sut.IsHumanSettingsVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenOpponentTypeChangesToBot_ThenIsHumanSettingsVisibleBecomesFalse()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetOpponentType(OpponentType.Human);

            sut.SetOpponentType(OpponentType.Bot);

            sut.IsHumanSettingsVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenOpponentTypeTogglesHumanToBotToHuman_ThenHumanOpponentKindIsPreserved()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            sut.SetOpponentType(OpponentType.Bot);
            sut.SetOpponentType(OpponentType.Human);

            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenResetCalled_ThenHumanOpponentKindIsSetToDefault()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            sut.Reset();

            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.Local);
        }

        [Test]
        public void WhenOpponentTypeChangesToHuman_ThenIsBotSettingsVisibleBecomesFalse()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetOpponentType(OpponentType.Human);

            sut.IsBotSettingsVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenOpponentTypeChangesToBot_ThenIsBotSettingsVisibleBecomesTrue()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetOpponentType(OpponentType.Human);

            sut.SetOpponentType(OpponentType.Bot);

            sut.IsBotSettingsVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenBattleshipSelectedAndHumanLocalInSnapshot_ThenHumanKindAutoNormalizedToDirectInvite()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            SetupStrategy(BattleshipStrategy.DefaultGameId, CreateBattleshipStrategy());

            using var sut = CreateSut();
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            sut.IsLocalHumanSupported.CurrentValue.Should().BeFalse();
            session.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.DirectInvite);
        }
    }
}