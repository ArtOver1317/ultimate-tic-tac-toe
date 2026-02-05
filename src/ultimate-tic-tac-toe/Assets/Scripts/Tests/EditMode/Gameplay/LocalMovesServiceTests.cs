using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.Moves;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class LocalMovesServiceTests
    {
        [Test]
        public void WhenStartAndApplyClicks_ThenAlternatesXAndO()
        {
            using var service = new LocalMovesService();
            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);
            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);
            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);

            service.TryApplyLocalClick(new CellId(1, 0)).Should().Be(ApplyClickResult.Applied);
            service.GetCellValue(new CellId(1, 0)).Should().Be(PlayerMark.O);
            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);
        }

        [Test]
        public void WhenClickOccupiedCell_ThenReturnsCellOccupiedAndDoesNotSwitchPlayer()
        {
            using var service = new LocalMovesService();
            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);

            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.CellOccupied);
            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);
            service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);
        }

        [Test]
        public void WhenStartCalledAgain_ThenClearsFieldAndResetsCurrentPlayer()
        {
            using var service = new LocalMovesService();
            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);

            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.O));
            service.IsStarted.CurrentValue.Should().BeTrue();
            service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);
            service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenNotStartedAndTryApplyClick_ThenReturnsNotStarted()
        {
            using var service = new LocalMovesService();

            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.NotStarted);
        }

        [Test]
        public void WhenApplySuccessfulMove_ThenPublishesEventsInStrictOrder()
        {
            using var service = new LocalMovesService();
            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var order = new List<string>();
            using var disposables = new CompositeDisposable();

            service.CellChanged.Subscribe(_ => order.Add("CellChanged")).AddTo(disposables);
            service.LastMoveChanged.Subscribe(_ => order.Add("LastMoveChanged")).AddTo(disposables);

            var firstCurrentPlayer = true;
            service.CurrentPlayer.Subscribe(_ =>
            {
                if (firstCurrentPlayer)
                {
                    firstCurrentPlayer = false;
                    return;
                }

                order.Add("CurrentPlayer");
            }).AddTo(disposables);

            service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            order.Should().Equal("CellChanged", "LastMoveChanged", "CurrentPlayer");
        }
    }
}
