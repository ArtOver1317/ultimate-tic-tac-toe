using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelDirectInviteTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenSetTargetPlayerIdCalled_ThenWritesThroughToSessionSnapshot()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("12345");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("12345");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithSameValue_ThenDoesNotUpdateSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("12345"));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var updatesBefore = session.UpdateCallCount;

            sut.SetTargetPlayerId("12345");

            session.UpdateCallCount.Should().Be(updatesBefore);
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithWhitespace_ThenNormalizesInSnapshot()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("  123  ");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("123");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithInvalidString_ThenWritesNormalizedValueToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("invalid");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("INVALID");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithInvalidStringWithWhitespace_ThenWritesNormalizedValueToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("  abc  ");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("ABC");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();

            var before = (
                sut.OpponentType.CurrentValue,
                sut.HumanOpponentKind.CurrentValue,
                sut.TargetPlayerId.CurrentValue,
                sut.PlayerIdErrorText.CurrentValue);

            Action act = () => sut.SetTargetPlayerId("123");

            act.Should().NotThrow();

            (
                    sut.OpponentType.CurrentValue,
                    sut.HumanOpponentKind.CurrentValue,
                    sut.TargetPlayerId.CurrentValue,
                    sut.PlayerIdErrorText.CurrentValue)
                .Should().Be(before);
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndOpponentTypeIsBot_ThenNormalizesToNullInSnapshot()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithOpponentType(OpponentType.Bot));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("123");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndHumanKindIsLocal_ThenNormalizesToNullInSnapshot()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetTargetPlayerId("123");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenLatePlayerIdChangeArrivesAfterSwitchToLocal_ThenDoesNotReintroduceTargetPlayerId()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetHumanOpponentKind(HumanOpponentKind.Local);
            sut.SetTargetPlayerId("123");

            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenSessionTargetPlayerIdChanges_ThenVMUpdatesWithoutWriteBack()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var updatesBefore = session.UpdateCallCount;

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("456")
                .WithVersion(1));

            sut.TargetPlayerId.CurrentValue.Should().Be("456");
            session.UpdateCallCount.Should().Be(updatesBefore);
        }

        [Test]
        public void WhenSessionTargetPlayerIdIsNull_ThenVMTargetPlayerIdIsEmptyString()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(null));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            sut.Initialize();

            sut.TargetPlayerId.CurrentValue.Should().Be("");
        }

        [Test]
        public void WhenOpponentTypeIsHumanAndKindIsDirectInvite_ThenIsPlayerIdInputVisibleIsTrue()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            sut.Initialize();

            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenOpponentTypeIsBot_ThenIsPlayerIdInputVisibleIsFalse()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithOpponentType(OpponentType.Bot));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            sut.Initialize();

            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenHumanKindIsLocal_ThenIsPlayerIdInputVisibleIsFalse()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            sut.Initialize();

            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenResetCalled_ThenTargetPlayerIdIsCleared()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("12345"));
          
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.Reset();

            sut.TargetPlayerId.CurrentValue.Should().Be("");
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowIdleWithoutCandidate_ThenGeneratesSessionIdAndEnablesCopy()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
           
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(Catalog, Coordinator, Localization, DifficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "ABCDEF", StringComparison.Ordinal));

            onlineFlow.EnterHumanSetupCalls.Should().Be(1);
            sut.VisibleSessionId.CurrentValue.Should().Be("ABCDEF");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowTerminatedWithoutCandidate_ThenGeneratesSessionIdAndEnablesCopy()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Terminated,
                    previousStableState: OnlineFlowState.InGame,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 2,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.OpponentLeft,
                    errorLocalizationKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.OpponentLeft),
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(Catalog, Coordinator, Localization, DifficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "ABCDEF", StringComparison.Ordinal));

            onlineFlow.EnterHumanSetupCalls.Should().Be(1);
            sut.VisibleSessionId.CurrentValue.Should().Be("ABCDEF");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
            sut.CanBecomeHost.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowIdleWithCandidateAndStaleActive_ThenVisibleSessionIdUsesCandidate()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: "NEW123",
                    activeSessionId: "OLD999",
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(Catalog, Coordinator, Localization, DifficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "NEW123", StringComparison.Ordinal));

            sut.VisibleSessionId.CurrentValue.Should().Be("NEW123");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
        }
    }
}