using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Config;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.PlayerStatistics;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerStatisticsMatchReporterTests
    {
        [Test]
        public void WhenConfigNotInStore_ThenConstructorThrows()
        {
            var configStore = new GameLaunchConfigStore();
            var eventStream = CreateEventStream(out _);
            var resolver = Substitute.For<IMatchOutcomeResolver>();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = CreateContextStore(OnlineGameplaySessionSnapshot.Empty());
            var keyMapper = new MatchKeyMapper();

            Action act = () => _ = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                keyMapper);

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenMatchmakingConfigAndNoSessionContext_ThenRoundFinishedDoesNotRecordMatch()
        {
            var configStore = CreateConfigStore(new MatchmakingConfig("match-1", "enemy-1"));
            var eventStream = CreateEventStream(out var roundFinished);
            var resolver = new CountingResolver(returnValue: true, outcome: MatchOutcome.Win);
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            
            var contextStore = CreateContextStore(new OnlineGameplaySessionSnapshot(
                isOnlineDirectInvite: false,
                sessionId: null,
                localUserId: null,
                isHost: false,
                matchConfig: null));

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));

            resolver.Calls.Should().Be(0);
            statisticsService.DidNotReceiveWithAnyArgs().RecordMatch(default!, default);
        }

        [Test]
        public void WhenRoundFinishedEventFired_ThenCallsRecordMatchWithCorrectOutcome()
        {
            var configStore = CreateConfigStore(new LocalHumanConfig());
            var eventStream = CreateEventStream(out var roundFinished);
           
            var resolver = new StubOutcomeResolver((RoundFinishedEvent evt, StatisticsOpponentType opponentType, bool isLocalPlayerHost, out MatchOutcome outcome) =>
            {
                outcome = MatchOutcome.Win;
                return true;
            });
          
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = CreateContextStore(OnlineGameplaySessionSnapshot.Empty());

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));

            statisticsService.Received(1).RecordMatch(
                Arg.Is<MatchKey>(x => x.GameId == "ttt" && x.OpponentType == StatisticsOpponentType.HotSeat),
                MatchOutcome.Win);
        }

        [Test]
        public void WhenDisposed_ThenSubsequentRoundFinishedDoesNotTriggerRecord()
        {
            var configStore = CreateConfigStore(new LocalHumanConfig());
            var eventStream = CreateEventStream(out var roundFinished);
           
            var resolver = new StubOutcomeResolver((RoundFinishedEvent evt, StatisticsOpponentType opponentType, bool isLocalPlayerHost, out MatchOutcome outcome) =>
            {
                outcome = MatchOutcome.Win;
                return true;
            });
           
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = CreateContextStore(OnlineGameplaySessionSnapshot.Empty());

            var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));
            reporter.Dispose();
            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));

            statisticsService.Received(1).RecordMatch(
                Arg.Is<MatchKey>(x => x.GameId == "ttt"),
                MatchOutcome.Win);
        }

        [Test]
        public void WhenUnsupportedOpponentConfig_ThenReporterDisablesAndDoesNotRecord()
        {
            var configStore = CreateConfigStore(new UnknownOpponentConfig());
            var eventStream = CreateEventStream(out var roundFinished);
            var resolver = new CountingResolver(returnValue: true, outcome: MatchOutcome.Win);
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = CreateContextStore(OnlineGameplaySessionSnapshot.Empty());

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));

            resolver.Calls.Should().Be(0);
            statisticsService.DidNotReceiveWithAnyArgs().RecordMatch(default!, default);
        }

        [Test]
        public void WhenOnlineDirectInviteAndIsHostFalse_ThenRecordsMatchWithGuestSlot()
        {
            var configStore = CreateConfigStore(new DirectInviteConfig("AB2CD7"));
            var eventStream = CreateEventStream(out var roundFinished);
            var resolver = new RecordingResolver();
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
           
            var contextStore = CreateContextStore(new OnlineGameplaySessionSnapshot(
                isOnlineDirectInvite: true,
                sessionId: "AB2CD7",
                localUserId: "guest-1",
                isHost: false,
                matchConfig: null));

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 1, winLine: null));

            resolver.LastIsLocalPlayerHost.Should().BeFalse();
            
            statisticsService.Received(1).RecordMatch(
                Arg.Is<MatchKey>(x => x.OpponentType == StatisticsOpponentType.Online),
                MatchOutcome.Win);
        }

        [Test]
        public void WhenOutcomeResolverReturnsFalse_ThenReporterDoesNotRecordMatch()
        {
            var configStore = CreateConfigStore(new LocalHumanConfig());
            var eventStream = CreateEventStream(out var roundFinished);
            var resolver = new CountingResolver(returnValue: false, outcome: MatchOutcome.Win);
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            var contextStore = CreateContextStore(OnlineGameplaySessionSnapshot.Empty());

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null));

            resolver.Calls.Should().Be(1);
            statisticsService.DidNotReceiveWithAnyArgs().RecordMatch(default!, default);
        }

        [Test]
        public void WhenDirectInviteConfigButSnapshotIsNotDirectInvite_ThenReportingIsDisabled()
        {
            var configStore = CreateConfigStore(new DirectInviteConfig("AB2CD7"));
            var eventStream = CreateEventStream(out var roundFinished);
            var resolver = new CountingResolver(returnValue: true, outcome: MatchOutcome.Win);
            var statisticsService = Substitute.For<IPlayerStatisticsService>();
            
            var contextStore = CreateContextStore(new OnlineGameplaySessionSnapshot(
                isOnlineDirectInvite: false,
                sessionId: "AB2CD7",
                localUserId: "user-1",
                isHost: false,
                matchConfig: null));

            using var reporter = new PlayerStatisticsMatchReporter(
                configStore,
                eventStream,
                resolver,
                statisticsService,
                contextStore,
                new MatchKeyMapper());

            roundFinished.OnNext(new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 1, winLine: null));

            resolver.Calls.Should().Be(0);
            statisticsService.DidNotReceiveWithAnyArgs().RecordMatch(default!, default);
        }

        private static IGameplayEventStream CreateEventStream(out Subject<RoundFinishedEvent> roundFinished)
        {
            var eventStream = Substitute.For<IGameplayEventStream>();
            roundFinished = new Subject<RoundFinishedEvent>();
            eventStream.RoundFinished.Returns(roundFinished);
            return eventStream;
        }

        private static IOnlineGameplaySessionContextStore CreateContextStore(OnlineGameplaySessionSnapshot snapshot)
        {
            var contextStore = Substitute.For<IOnlineGameplaySessionContextStore>();
            contextStore.Snapshot.Returns(snapshot);
            return contextStore;
        }

        private static IGameLaunchConfigStore CreateConfigStore(IOpponentConfig opponentConfig)
        {
            var store = new GameLaunchConfigStore();
            store.Set(new GameLaunchConfig("ttt", new TicTacToeConfig(3), opponentConfig));
            return store;
        }

        private sealed class UnknownOpponentConfig : IOpponentConfig { }

        private sealed class StubOutcomeResolver : IMatchOutcomeResolver
        {
            private readonly TryResolveDelegate _handler;

            public StubOutcomeResolver(TryResolveDelegate handler) => _handler = handler;

            public bool TryResolveOutcome(
                RoundFinishedEvent evt,
                StatisticsOpponentType opponentType,
                bool isLocalPlayerHost,
                out MatchOutcome outcome) =>
                _handler(evt, opponentType, isLocalPlayerHost, out outcome);

            public delegate bool TryResolveDelegate(
                RoundFinishedEvent evt,
                StatisticsOpponentType opponentType,
                bool isLocalPlayerHost,
                out MatchOutcome outcome);
        }

        private sealed class RecordingResolver : IMatchOutcomeResolver
        {
            public bool LastIsLocalPlayerHost { get; private set; }

            public bool TryResolveOutcome(
                RoundFinishedEvent evt,
                StatisticsOpponentType opponentType,
                bool isLocalPlayerHost,
                out MatchOutcome outcome)
            {
                LastIsLocalPlayerHost = isLocalPlayerHost;
                outcome = isLocalPlayerHost ? MatchOutcome.Loss : MatchOutcome.Win;
                return true;
            }
        }

        private sealed class CountingResolver : IMatchOutcomeResolver
        {
            private readonly bool _returnValue;
            private readonly MatchOutcome _outcome;

            public int Calls { get; private set; }

            public CountingResolver(bool returnValue, MatchOutcome outcome)
            {
                _returnValue = returnValue;
                _outcome = outcome;
            }

            public bool TryResolveOutcome(
                RoundFinishedEvent evt,
                StatisticsOpponentType opponentType,
                bool isLocalPlayerHost,
                out MatchOutcome outcome)
            {
                Calls++;
                outcome = _outcome;
                return _returnValue;
            }
        }
    }
}
