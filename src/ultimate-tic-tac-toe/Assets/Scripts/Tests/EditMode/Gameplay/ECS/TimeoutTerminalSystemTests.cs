#nullable enable

using System;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.Shared;
using Scellecs.Morpeh;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Gameplay.ECS
{
    [TestFixture]
    [Category("Unit")]
    public sealed class TimeoutTerminalSystemTests
    {
        private World _world = null!;
        private SystemsGroup _systemsGroup = null!;
        private TimeoutTerminalSystem _sut = null!;
        private Stash<MatchTag> _matchTagStash = null!;
        private Stash<TimeoutRequest> _timeoutRequestStash = null!;
        private Stash<MatchStatusComponent> _statusStash = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<RoundFinishedOneShot> _roundFinishedStash = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _world.UpdateByUnity = false;

            _sut = new TimeoutTerminalSystem
            {
                World = _world,
            };

            _systemsGroup = _world.CreateSystemsGroup();
            _systemsGroup.AddSystem(_sut);
            _world.AddSystemsGroup(0, _systemsGroup);

            _matchTagStash = _world.GetStash<MatchTag>();
            _timeoutRequestStash = _world.GetStash<TimeoutRequest>();
            _statusStash = _world.GetStash<MatchStatusComponent>();
            _playersStash = _world.GetStash<PlayersComponent>();
            _roundFinishedStash = _world.GetStash<RoundFinishedOneShot>();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void WhenTimeoutRequestSubmitted_ThenMatchStatusTimeoutAndWinnerSlotCorrect(int loserSlot, int expectedWinnerSlot)
        {
            // Arrange
            var matchEntity = CreateMatchEntity(
                status: EcsGameStatus.InProgress,
                playerSlots: new[] { 0, 1 },
                playerCount: 2,
                loserSlot: loserSlot);
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            var status = _statusStash.Get(matchEntity);
            status.Status.Should().Be(EcsGameStatus.Timeout);
            status.WinnerSlot.Should().Be(expectedWinnerSlot);
            status.WinLine.Should().BeNull();

            _roundFinishedStash.Has(matchEntity).Should().BeTrue();
            var roundFinished = _roundFinishedStash.Get(matchEntity);
            roundFinished.Status.Should().Be(EcsGameStatus.Timeout);
            roundFinished.WinnerSlot.Should().Be(expectedWinnerSlot);
            roundFinished.WinLine.Should().BeNull();

            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
        }

        [Test]
        public void WhenMatchAlreadyFinished_ThenTimeoutRequestIgnoredAndStatusUnchanged()
        {
            // Arrange
            var matchEntity = CreateMatchEntity(
                status: EcsGameStatus.Win,
                playerSlots: new[] { 0, 1 },
                playerCount: 2,
                loserSlot: 0);
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            _statusStash.Get(matchEntity).Status.Should().Be(EcsGameStatus.Win);
            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
            _roundFinishedStash.Has(matchEntity).Should().BeFalse();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void WhenMatchEntityMissingStatusOrPlayersComponent_ThenTimeoutRequestRemovedAndNoCrash(
            bool withStatus,
            bool withPlayers)
        {
            // Arrange
            var matchEntity = _world.CreateEntity();
            _matchTagStash.Set(matchEntity);
            if (withStatus)
            {
                _statusStash.Set(matchEntity, new MatchStatusComponent
                {
                    Status = EcsGameStatus.InProgress,
                    WinnerSlot = null,
                    WinLine = null,
                });
            }

            if (withPlayers)
            {
                _playersStash.Set(matchEntity, new PlayersComponent
                {
                    PlayerCount = 2,
                    PlayerSlots = new[] { 0, 1 },
                    ActivePlayerSlot = 0,
                });
            }

            _timeoutRequestStash.Set(matchEntity, new TimeoutRequest { LoserSlot = 0 });
            _world.Commit();

            // Act
            Action act = () => _world.Update(0f);
            act.Should().NotThrow();

            // Assert
            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
        }

        [Test]
        public void WhenPlayersLayoutUnsupported_ThenTimeoutIgnoredAndRequestRemoved()
        {
            // Arrange
            LogAssert.Expect(LogType.Error,
                new Regex("Unsupported players layout for timeout resolution"));

            var matchEntity = CreateMatchEntity(
                status: EcsGameStatus.InProgress,
                playerSlots: new[] { 0, 1, 2 },
                playerCount: 3,
                loserSlot: 0);
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            _statusStash.Get(matchEntity).Status.Should().Be(EcsGameStatus.InProgress);
            _roundFinishedStash.Has(matchEntity).Should().BeFalse();
            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
        }

        [Test]
        public void WhenPlayerSlotsContainOnlyLoser_ThenTimeoutIgnoredAndRequestRemoved()
        {
            // Arrange
            LogAssert.Expect(LogType.Error,
                new Regex("Winner slot could not be resolved"));

            var matchEntity = CreateMatchEntity(
                status: EcsGameStatus.InProgress,
                playerSlots: new[] { 0, 0 },
                playerCount: 2,
                loserSlot: 0);
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            _statusStash.Get(matchEntity).Status.Should().Be(EcsGameStatus.InProgress);
            _roundFinishedStash.Has(matchEntity).Should().BeFalse();
            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
        }

        [Test]
        public void WhenLoserSlotNotInPlayers_ThenTimeoutIgnoredAndStatusRemainsInProgress()
        {
            // Arrange
            LogAssert.Expect(LogType.Error,
                new Regex("Invalid LoserSlot=99"));

            var matchEntity = CreateMatchEntity(
                status: EcsGameStatus.InProgress,
                playerSlots: new[] { 0, 1 },
                playerCount: 2,
                loserSlot: 99);
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            _statusStash.Get(matchEntity).Status.Should().Be(EcsGameStatus.InProgress);
            _roundFinishedStash.Has(matchEntity).Should().BeFalse();
            _timeoutRequestStash.Has(matchEntity).Should().BeFalse();
        }

        private Entity CreateMatchEntity(EcsGameStatus status, int[]? playerSlots, int playerCount, int loserSlot)
        {
            var entity = _world.CreateEntity();
            _matchTagStash.Set(entity);
            _statusStash.Set(entity, new MatchStatusComponent
            {
                Status = status,
                WinnerSlot = null,
                WinLine = null,
            });
            _playersStash.Set(entity, new PlayersComponent
            {
                PlayerCount = playerCount,
                PlayerSlots = playerSlots,
                ActivePlayerSlot = 0,
            });
            _timeoutRequestStash.Set(entity, new TimeoutRequest
            {
                LoserSlot = loserSlot,
            });

            return entity;
        }
    }
}
