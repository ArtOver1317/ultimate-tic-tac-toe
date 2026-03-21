#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.ECS;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    public partial class EcsGameplayPipelineTests
    {
        [Test]
        public void WhenSameMovesRepeated100Times_ThenIdenticalEventsAndState()
        {
            var moveSequence = new[]
            {
                new CellId(0, 0),
                new CellId(1, 0),
                new CellId(0, 1),
                new CellId(1, 1),
                new CellId(0, 2),
            };

            List<object>? referenceEvents = null;

            for (var i = 0; i < 100; i++)
            {
                StartMatch();
                ClearEvents();

                foreach (var cellId in moveSequence)
                {
                    PlayMove(cellId.Major, cellId.Minor);
                }

                if (referenceEvents == null)
                    referenceEvents = new List<object>(_events);
                else
                {
                    _events.Should().HaveCount(referenceEvents.Count,
                        $"run {i} should produce same event count");

                    for (var j = 0; j < _events.Count; j++)
                    {
                        _events[j].Should().BeEquivalentTo(referenceEvents[j],
                            $"event[{j}] payload mismatch in run {i}");
                    }

                    var cells = _stateProvider.GetAllCells();
                    cells.First(c => c.CellId == new CellId(0, 0)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
                    cells.First(c => c.CellId == new CellId(0, 2)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
                }

                _lifecycle.StopMatch();
            }
        }
    }
}