using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class LocalMovesServiceTests
    {
        private LocalMovesService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new LocalMovesService();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
        }

        [Test]
        public void WhenStartCalledWithNullFieldSpec_ThenThrowsArgumentException()
        {
            // Arrange
            var config = new LocalMovesConfig(null, PlayerMark.X);

            // Act
            Action act = () => _service.Start(config);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenTryApplyClickWithInvalidCellId_ThenReturnsInvalidCellIdAndDoesNotChangeState()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);

            // Act
            var result1 = _service.TryApplyLocalClick(new CellId(-1, 0));
            var result2 = _service.TryApplyLocalClick(new CellId(0, -1));
            var result3 = _service.TryApplyLocalClick(new CellId(999, 0));

            // Assert
            result1.Should().Be(ApplyClickResult.InvalidCellId);
            result2.Should().Be(ApplyClickResult.InvalidCellId);
            result3.Should().Be(ApplyClickResult.InvalidCellId);
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenGetCellValueWithNotStarted_ThenReturnsNone()
        {
            // Arrange
            // Start не вызываем

            // Act
            var value = _service.GetCellValue(new CellId(0, 0));

            // Assert
            value.Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenGetCellValueWithInvalidCellId_ThenReturnsNone()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            // Act
            var value = _service.GetCellValue(new CellId(-1, 0));

            // Assert
            value.Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenGetAllCellsWithNotStarted_ThenReturnsEmptyList()
        {
            // Arrange
            // Start не вызываем

            // Act
            var cells = _service.GetAllCells();

            // Assert
            cells.Should().BeEmpty();
        }

        [Test]
        public void WhenStopCalledBeforeStart_ThenIsIdempotentAndDoesNotThrow()
        {
            // Arrange
            // Start не вызываем

            // Act
            Action act = () => _service.Stop();

            // Assert
            act.Should().NotThrow();
            _service.IsStarted.CurrentValue.Should().BeFalse();
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenStopCalledTwice_ThenIsIdempotent()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.Stop();

            // Act
            Action act = () => _service.Stop();

            // Assert
            act.Should().NotThrow();
            _service.IsStarted.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenStopCalled_ThenTryApplyReturnsNotStarted()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);

            // Act
            _service.Stop();

            // Assert
            _service.IsStarted.CurrentValue.Should().BeFalse();
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.None);
            _service.TryApplyLocalClick(new CellId(1, 0)).Should().Be(ApplyClickResult.NotStarted);
        }

        [Test]
        public void WhenStopCalledAfterMove_ThenPublishesLastMoveChangedToNull()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));

            var lastMoveEvents = new List<LastMoveChangedEvent>();
            using var disposables = new CompositeDisposable();
            _service.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e)).AddTo(disposables);

            // Act
            _service.Stop();

            // Assert
            lastMoveEvents.Should().ContainSingle();
            lastMoveEvents[0].Previous.Should().Be(new CellId(0, 0));
            lastMoveEvents[0].Current.Should().BeNull();
        }

        [Test]
        public void WhenStopCalledAfterStartAndNoMoves_ThenDoesNotPublishLastMoveChanged()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var lastMoveEvents = new List<LastMoveChangedEvent>();
            using var disposables = new CompositeDisposable();
            _service.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e)).AddTo(disposables);

            // Act
            _service.Stop();

            // Assert
            lastMoveEvents.Should().BeEmpty();
        }

        [Test]
        public void WhenStartCalledAfterMove_ThenPublishesLastMoveChangedToNull()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(1, 1));

            var lastMoveEvents = new List<LastMoveChangedEvent>();
            using var disposables = new CompositeDisposable();
            _service.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e)).AddTo(disposables);

            // Act
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.O));

            // Assert
            lastMoveEvents.Should().ContainSingle();
            lastMoveEvents[0].Previous.Should().Be(new CellId(1, 1));
            lastMoveEvents[0].Current.Should().BeNull();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            var service = new LocalMovesService();
            service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            // Act
            service.Dispose();
            service.Dispose();
            service.Dispose();

            // Assert
            Action act = () => service.TryApplyLocalClick(new CellId(0, 0));
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenStartClassicThenStartUltimate_ThenReallocatesAndClearsState()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));
            _service.TryApplyLocalClick(new CellId(1, 1));

            // Act
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Ultimate(), PlayerMark.X));

            // Assert
            _service.IsStarted.CurrentValue.Should().BeTrue();
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.None);
            _service.GetCellValue(new CellId(1, 1)).Should().Be(PlayerMark.None);

            var cells = _service.GetAllCells();
            cells.Should().HaveCount(81);
            cells.Should().OnlyContain(cell => cell.Value == PlayerMark.None);
        }

        [Test]
        public void WhenStartUltimateThenStartClassic_ThenReallocatesAndClearsState()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Ultimate(), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));
            _service.TryApplyLocalClick(new CellId(8, 8));

            // Act
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.O));

            // Assert
            _service.IsStarted.CurrentValue.Should().BeTrue();
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.None);
            _service.GetCellValue(new CellId(2, 2)).Should().Be(PlayerMark.None);

            var cells = _service.GetAllCells();
            cells.Should().HaveCount(9);
            cells.Should().OnlyContain(cell => cell.Value == PlayerMark.None);
        }

        [Test]
        public void WhenMultipleMovesApplied_ThenLastMoveUpdatesCorrectly()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var lastMoveEvents = new List<LastMoveChangedEvent>();
            using var disposables = new CompositeDisposable();
            _service.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e)).AddTo(disposables);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0));
            _service.TryApplyLocalClick(new CellId(1, 1));
            _service.TryApplyLocalClick(new CellId(2, 2));

            // Assert
            lastMoveEvents.Should().HaveCount(3);

            lastMoveEvents[0].Previous.Should().BeNull();
            lastMoveEvents[0].Current.Should().Be(new CellId(0, 0));

            lastMoveEvents[1].Previous.Should().Be(new CellId(0, 0));
            lastMoveEvents[1].Current.Should().Be(new CellId(1, 1));

            lastMoveEvents[2].Previous.Should().Be(new CellId(1, 1));
            lastMoveEvents[2].Current.Should().Be(new CellId(2, 2));
        }

        [Test]
        public void WhenStartWithInvalidStartingPlayer_ThenDefaultsToX()
        {
            // Arrange
            // service создан в SetUp

            // Act
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.None));

            // Assert
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.X);
        }

        [Test]
        public void WhenClickOccupiedCell_ThenDoesNotPublishCellChangedOrLastMoveChangedOrCurrentPlayer()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));

            var cellEvents = new List<CellChangedEvent>();
            var lastMoveEvents = new List<LastMoveChangedEvent>();
            var playerEvents = new List<PlayerMark>();
            using var disposables = new CompositeDisposable();

            _service.CellChanged.Subscribe(e => cellEvents.Add(e)).AddTo(disposables);
            _service.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e)).AddTo(disposables);
            _service.CurrentPlayer.Skip(1).Subscribe(p => playerEvents.Add(p)).AddTo(disposables);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.CellOccupied);

            // Assert
            cellEvents.Should().BeEmpty();
            lastMoveEvents.Should().BeEmpty();
            playerEvents.Should().BeEmpty();
        }

        [Test]
        public void WhenRejectHappens_ThenPublishesClickRejectedWithReason()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));

            var rejectedEvents = new List<ClickRejectedEvent>();
            using var disposables = new CompositeDisposable();
            _service.ClickRejected.Subscribe(e => rejectedEvents.Add(e)).AddTo(disposables);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0));
            _service.TryApplyLocalClick(new CellId(-1, 0));
            _service.Stop();
            _service.TryApplyLocalClick(new CellId(1, 0));

            // Assert
            rejectedEvents.Should().HaveCount(3);
            rejectedEvents[0].CellId.Should().Be(new CellId(0, 0));
            rejectedEvents[0].Reason.Should().Be(ApplyClickResult.CellOccupied);
            rejectedEvents[1].CellId.Should().Be(new CellId(-1, 0));
            rejectedEvents[1].Reason.Should().Be(ApplyClickResult.InvalidCellId);
            rejectedEvents[2].CellId.Should().Be(new CellId(1, 0));
            rejectedEvents[2].Reason.Should().Be(ApplyClickResult.NotStarted);
        }

        [Test]
        public void WhenAppliedMove_ThenDoesNotPublishClickRejected()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var rejectedEvents = new List<ClickRejectedEvent>();
            using var disposables = new CompositeDisposable();
            _service.ClickRejected.Subscribe(e => rejectedEvents.Add(e)).AddTo(disposables);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            _service.TryApplyLocalClick(new CellId(1, 1)).Should().Be(ApplyClickResult.Applied);

            // Assert
            rejectedEvents.Should().BeEmpty();
        }

        [Test]
        public void WhenGetAllCellsAfterMoves_ThenReturnsCorrectSnapshot()
        {
            // Arrange
            const int size = 3;
            var spec = FieldRenderSpec.Classic(size);
            _service.Start(new LocalMovesConfig(spec, PlayerMark.X));
            _service.TryApplyLocalClick(new CellId(0, 0));
            _service.TryApplyLocalClick(new CellId(1, 1));

            // Act
            var snapshot = _service.GetAllCells();

            // Assert
            snapshot.Should().HaveCount(9);

            var expectedIds = new HashSet<CellId>();
            for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
                expectedIds.Add(new CellId(x, y));

            var seenIds = new HashSet<CellId>();

            foreach (var cell in snapshot)
            {
                expectedIds.Contains(cell.CellId).Should().BeTrue("snapshot contains unexpected CellId");
                seenIds.Add(cell.CellId).Should().BeTrue("snapshot contains duplicate CellId");

                var expectedValue =
                    cell.CellId == new CellId(0, 0) ? PlayerMark.X :
                    cell.CellId == new CellId(1, 1) ? PlayerMark.O :
                    PlayerMark.None;

                cell.Value.Should().Be(expectedValue);
            }

            seenIds.SetEquals(expectedIds).Should().BeTrue("snapshot must contain every CellId exactly once");
        }

        [Test]
        public void WhenUltimateFieldUsed_ThenGetAllCellsReturns81AndMovesAddressCorrectly()
        {
            // Arrange
            var spec = FieldRenderSpec.Ultimate();
            _service.Start(new LocalMovesConfig(spec, PlayerMark.X));

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            _service.TryApplyLocalClick(new CellId(8, 8)).Should().Be(ApplyClickResult.Applied);
            var snapshot = _service.GetAllCells();

            // Assert
            snapshot.Should().HaveCount(81);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);
            _service.GetCellValue(new CellId(8, 8)).Should().Be(PlayerMark.O);

            var expectedMajorCount = spec.OuterSize * spec.OuterSize;
            var expectedMinorCount = spec.InnerSize * spec.InnerSize;

            var expectedIds = new HashSet<CellId>();
            for (var major = 0; major < expectedMajorCount; major++)
            for (var minor = 0; minor < expectedMinorCount; minor++)
                expectedIds.Add(new CellId(major, minor));

            var seenIds = new HashSet<CellId>();

            var nonEmptyCount = 0;
            foreach (var cell in snapshot)
            {
                expectedIds.Contains(cell.CellId).Should().BeTrue("snapshot contains unexpected CellId");
                seenIds.Add(cell.CellId).Should().BeTrue("snapshot contains duplicate CellId");

                var expectedValue =
                    cell.CellId == new CellId(0, 0) ? PlayerMark.X :
                    cell.CellId == new CellId(8, 8) ? PlayerMark.O :
                    PlayerMark.None;

                cell.Value.Should().Be(expectedValue);

                if (cell.Value == PlayerMark.None)
                    continue;

                nonEmptyCount++;
            }

            seenIds.SetEquals(expectedIds).Should().BeTrue("snapshot must contain every CellId exactly once");
            nonEmptyCount.Should().Be(2);
        }

        [Test]
        public void WhenStartAndApplyClicks_ThenAlternatesXAndO()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            // Act
            var initialPlayer = _service.CurrentPlayer.CurrentValue;
            var result1 = _service.TryApplyLocalClick(new CellId(0, 0));
            var value1 = _service.GetCellValue(new CellId(0, 0));
            var playerAfterMove1 = _service.CurrentPlayer.CurrentValue;

            var result2 = _service.TryApplyLocalClick(new CellId(1, 0));
            var value2 = _service.GetCellValue(new CellId(1, 0));
            var playerAfterMove2 = _service.CurrentPlayer.CurrentValue;

            // Assert
            initialPlayer.Should().Be(PlayerMark.X);

            result1.Should().Be(ApplyClickResult.Applied);
            value1.Should().Be(PlayerMark.X);
            playerAfterMove1.Should().Be(PlayerMark.O);

            result2.Should().Be(ApplyClickResult.Applied);
            value2.Should().Be(PlayerMark.O);
            playerAfterMove2.Should().Be(PlayerMark.X);
        }

        [Test]
        public void WhenClickOccupiedCell_ThenReturnsCellOccupiedAndDoesNotSwitchPlayer()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.CellOccupied);

            // Assert
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);
        }

        [Test]
        public void WhenStartCalledAgain_ThenClearsFieldAndResetsCurrentPlayer()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);

            // Act
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.O));

            // Assert
            _service.IsStarted.CurrentValue.Should().BeTrue();
            _service.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O);
            _service.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.None);
        }

        [Test]
        public void WhenNotStartedAndTryApplyClick_ThenReturnsNotStarted()
        {
            // Arrange
            // Start не вызываем

            // Act
            var result = _service.TryApplyLocalClick(new CellId(0, 0));

            // Assert
            result.Should().Be(ApplyClickResult.NotStarted);
        }

        [Test]
        public void WhenMoveApplied_ThenPublishesEventsInStrictOrder()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var order = new List<string>();
            using var disposables = new CompositeDisposable();

            _service.CellChanged.Subscribe(_ => order.Add("CellChanged")).AddTo(disposables);
            _service.LastMoveChanged.Subscribe(_ => order.Add("LastMoveChanged")).AddTo(disposables);
            _service.CurrentPlayer.Skip(1).Subscribe(_ => order.Add("CurrentPlayer")).AddTo(disposables);

            // Act
            _service.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);

            // Assert
            order.Should().Equal("CellChanged", "LastMoveChanged", "CurrentPlayer");
        }

        [Test]
        public void WhenClickRejectedByInvalidCellId_ThenDoesNotPublishAnyEvents()
        {
            // Arrange
            _service.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var eventsPublished = new List<string>();
            using var disposables = new CompositeDisposable();

            _service.CellChanged.Subscribe(_ => eventsPublished.Add("CellChanged")).AddTo(disposables);
            _service.LastMoveChanged.Subscribe(_ => eventsPublished.Add("LastMoveChanged")).AddTo(disposables);
            _service.CurrentPlayer.Skip(1).Subscribe(_ => eventsPublished.Add("CurrentPlayer")).AddTo(disposables);

            // Act
            var result = _service.TryApplyLocalClick(new CellId(99, 99));

            // Assert
            result.Should().Be(ApplyClickResult.InvalidCellId);
            eventsPublished.Should().BeEmpty("InvalidCellId не должен публиковать события");
        }

        [Test]
        public void WhenClickRejectedByNotStarted_ThenDoesNotPublishAnyEvents()
        {
            // Arrange
            // Intentionally do NOT call Start()

            var eventsPublished = new List<string>();
            using var disposables = new CompositeDisposable();

            _service.CellChanged.Subscribe(_ => eventsPublished.Add("CellChanged")).AddTo(disposables);
            _service.LastMoveChanged.Subscribe(_ => eventsPublished.Add("LastMoveChanged")).AddTo(disposables);
            // ReactiveProperty publishes initial value on subscribe; Skip(1) filters it out.
            _service.CurrentPlayer.Skip(1).Subscribe(_ => eventsPublished.Add("CurrentPlayer")).AddTo(disposables);

            // Act
            var result = _service.TryApplyLocalClick(new CellId(0, 0));

            // Assert
            result.Should().Be(ApplyClickResult.NotStarted);
            eventsPublished.Should().BeEmpty("NotStarted не должен публиковать события");
        }
    }
}
