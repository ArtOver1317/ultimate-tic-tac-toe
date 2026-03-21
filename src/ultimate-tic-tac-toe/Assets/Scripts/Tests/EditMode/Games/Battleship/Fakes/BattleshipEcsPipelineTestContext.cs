#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.Recovery;
using Runtime.Games.Battleship.State;

namespace Tests.EditMode.Games.Battleship.Fakes
{
    internal sealed class BattleshipEcsPipelineTestContext : IDisposable
    {
        public BattleshipEcsPipelineTestContext()
        {
            var validator = new BattleshipPlacementValidator();
            AutoPlacer = new BattleshipAutoPlacer(validator);

            CommandQueue = new CommandQueue();
            var scheduler = new SynchronousEventScheduler();
            var eventPublishSystem = new EventPublishSystem(scheduler);
            BattleshipEventStream = new BattleshipGameplayEventStream(scheduler);
            
            Lifecycle = new MatchEcsLifecycleService(
                new IEcsGameplayRegistrar[]
                {
                    new BattleshipEcsRegistrar(CommandQueue, BattleshipEventStream, validator, AutoPlacer),
                },
                CommandQueue,
                eventPublishSystem);
          
            StateProvider = new MatchStateProvider(
                CommandQueue,
                Lifecycle,
                eventPublishSystem);
           
            SnapshotProvider = new BattleshipSnapshotProvider(
                Lifecycle,
                StateProvider);
           
            RecoveryStateApplier = new BattleshipRecoveryStateApplier(
                Lifecycle,
                StateProvider,
                BattleshipEventStream);
        }

        public CommandQueue CommandQueue { get; }
        public BattleshipGameplayEventStream BattleshipEventStream { get; }
        public MatchEcsLifecycleService Lifecycle { get; }
        public MatchStateProvider StateProvider { get; }
        public IBattleshipGameplaySnapshotProvider SnapshotProvider { get; }
        public IBattleshipRecoveryStateApplier RecoveryStateApplier { get; }
        public BattleshipAutoPlacer AutoPlacer { get; }

        public void StartMatch() => Lifecycle.StartMatch(CreateConfig());

        public void Dispose()
        {
            StateProvider.Dispose();
            Lifecycle.Dispose();
        }

        public void AssertTimeoutCounter(int timedOutPlayerSlot, int expectedCount)
        {
            SnapshotProvider.TryGetConsecutiveTimeouts(out var player0Timeouts, out var player1Timeouts).Should().BeTrue();

            if (timedOutPlayerSlot == PlayerSlotMapping.SlotX)
            {
                player0Timeouts.Should().Be(expectedCount);
                player1Timeouts.Should().Be(0);
            }
            else
            {
                player0Timeouts.Should().Be(0);
                player1Timeouts.Should().Be(expectedCount);
            }
        }

        public static BattleshipCellMark[] CreateUnknownMarks() => new BattleshipCellMark[100];

        public static GameLaunchConfig CreateConfig() =>
            new(BattleshipStrategy.DefaultGameId, new BattleshipConfig(90), new LocalHumanConfig());

        public static FleetLayout CreateKnownValidLayout() =>
            new(Array.AsReadOnly(new[]
            {
                new ShipPlacement(ShipSize.Four, ShipOrientation.Horizontal, new CellId(0, 5)),
                new ShipPlacement(ShipSize.Three, ShipOrientation.Horizontal, new CellId(0, 0)),
                new ShipPlacement(ShipSize.Three, ShipOrientation.Vertical, new CellId(2, 0)),
                new ShipPlacement(ShipSize.Two, ShipOrientation.Horizontal, new CellId(2, 3)),
                new ShipPlacement(ShipSize.Two, ShipOrientation.Vertical, new CellId(3, 7)),
                new ShipPlacement(ShipSize.Two, ShipOrientation.Horizontal, new CellId(6, 0)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(6, 4)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(6, 7)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(8, 0)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(8, 3)),
            }));

        public static FleetLayout GetTargetLayout(int shooterSlot, FleetLayout xLayout, FleetLayout oLayout) =>
            shooterSlot == PlayerSlotMapping.SlotX ? oLayout : xLayout;

        public static int GetOtherPlayerSlot(int playerSlot) =>
            playerSlot == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;

        public static string SerializeLayout(FleetLayout layout) => new BattleshipLayoutSerializer().Serialize(layout);

        public static CellId FindFirstShipCell(FleetLayout layout)
        {
            foreach (var cell in FindShipCells(layout))
            {
                return cell;
            }

            throw new AssertionException("Expected at least one occupied ship cell on board.");
        }

        public static IReadOnlyList<CellId> FindShipCells(FleetLayout layout)
        {
            var cells = new List<CellId>(20);
            var ships = layout.Ships!;

            for (var shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                var ship = ships[shipIndex];
                var deckCount = (int)ship.Size;
               
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    cells.Add(new CellId(major, minor));
                }
            }

            return cells;
        }

        public static CellId FindFirstWaterCell(FleetLayout layout)
        {
            var occupied = BuildOccupiedMap(layout);

            for (var major = 0; major < 10; major++)
            {
                for (var minor = 0; minor < 10; minor++)
                {
                    if (!occupied[major * 10 + minor])
                        return new CellId(major, minor);
                }
            }

            throw new AssertionException("Expected at least one water cell on board.");
        }

        public static IReadOnlyList<CellId> FindWaterCells(FleetLayout layout, int count)
        {
            var occupied = BuildOccupiedMap(layout);
            var result = new List<CellId>(count);

            for (var major = 0; major < 10 && result.Count < count; major++)
            {
                for (var minor = 0; minor < 10 && result.Count < count; minor++)
                {
                    if (!occupied[major * 10 + minor])
                        result.Add(new CellId(major, minor));
                }
            }

            return result.Count < count 
                ? throw new AssertionException("Expected enough water cells on board.") 
                : result;
        }

        public static CellId FindSingleDeckShipCell(FleetLayout layout)
        {
            var ships = layout.Ships!;
           
            for (var i = 0; i < ships.Count; i++)
            {
                if (ships[i].Size == ShipSize.One)
                    return ships[i].StartCell;
            }

            throw new AssertionException("Expected at least one single-deck ship in fleet.");
        }

        public static IReadOnlyList<int> FindWaterNeighborIndexes(FleetLayout layout, ShipPlacement ship)
        {
            var occupied = BuildOccupiedMap(layout);
            var indexes = new List<int>(16);
            var visited = new HashSet<int>();
            var deckCount = (int)ship.Size;

            for (var deck = 0; deck < deckCount; deck++)
            {
                var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);

                for (var neighborMajor = major - 1; neighborMajor <= major + 1; neighborMajor++)
                {
                    for (var neighborMinor = minor - 1; neighborMinor <= minor + 1; neighborMinor++)
                    {
                        if (neighborMajor < 0 || neighborMajor >= 10 || neighborMinor < 0 || neighborMinor >= 10)
                            continue;

                        var index = neighborMajor * 10 + neighborMinor;
                        
                        if (occupied[index] || !visited.Add(index))
                            continue;

                        indexes.Add(index);
                    }
                }
            }

            return indexes;
        }

        public static IReadOnlyList<int> FindWaterNeighborIndexes(FleetLayout layout, CellId center)
        {
            var occupied = BuildOccupiedMap(layout);
            var neighbors = new List<int>(8);

            for (var major = center.Major - 1; major <= center.Major + 1; major++)
            {
                for (var minor = center.Minor - 1; minor <= center.Minor + 1; minor++)
                {
                    if (major < 0 || major >= 10 || minor < 0 || minor >= 10)
                        continue;

                    if (major == center.Major && minor == center.Minor)
                        continue;

                    var index = major * 10 + minor;
                   
                    if (occupied[index])
                        continue;

                    neighbors.Add(index);
                }
            }

            return neighbors;
        }

        public static bool[] BuildOccupiedMap(FleetLayout layout)
        {
            var occupied = new bool[100];
            var ships = layout.Ships!;

            for (var shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                var ship = ships[shipIndex];
                var deckCount = (int)ship.Size;
               
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    occupied[major * 10 + minor] = true;
                }
            }

            return occupied;
        }
    }
}