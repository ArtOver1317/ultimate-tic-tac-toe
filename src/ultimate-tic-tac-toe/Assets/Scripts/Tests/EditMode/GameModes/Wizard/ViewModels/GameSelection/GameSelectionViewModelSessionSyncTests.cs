using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.GameSelection
{
    public partial class GameSelectionViewModelTests
    {
        [Test]
        public void WhenInitializeCalledAndSessionAlreadyHasSelectedModeId_ThenVMRestoresSelectionAndCanContinueBecomesTrue()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameSession(GameSessionSnapshot.Default.WithSelectedGameId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            // Assert
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenSelectModeCalledAfterInitialize_ThenWritesThroughToSession()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            _session.UpdateCallCount.Should().Be(1);
            _session.Snapshot.CurrentValue.SelectedGameId.Should().Be("classic");
        }

        [Test]
        public void WhenUltimateModeSelected_ThenSessionSwitchesToHumanWithoutOverridingHumanKind()
        {
            // Arrange
            var modes = CreateModes(TicTacToeStrategy.DefaultGameId, UltimateTicTacToeStrategy.DefaultGameId);
            _catalog.Metadata.Returns(modes);
           
            _session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithHumanOpponentKind(HumanOpponentKind.Matchmaking)
                .WithBotDifficultyId("normal"));
            
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode(UltimateTicTacToeStrategy.DefaultGameId);

            // Assert
            _session.Snapshot.CurrentValue.SelectedGameId.Should().Be(UltimateTicTacToeStrategy.DefaultGameId);
            _session.Snapshot.CurrentValue.OpponentType.Should().Be(OpponentType.Human);
            _session.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.Matchmaking);
        }

        [Test]
        public void WhenSessionSnapshotChangesSelectedModeId_ThenVMUpdatesWithoutWritingBack()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedGameId("ultimate"));

            // Assert
            sut.SelectedGameId.Value.Should().Be("ultimate");
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionSnapshotChangesSelectedModeIdToSameValue_ThenVMDoesNotUpdate()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameSession(GameSessionSnapshot.Default.WithSelectedGameId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            var emitCount = 0;
            using var sub = sut.SelectedGameId.Subscribe(_ => emitCount++);
            emitCount = 0;

            // Act
            _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedGameId("classic"));

            // Assert
            emitCount.Should().Be(0);
        }

        [Test]
        public void WhenVMChangesSelectedModeIdAndSessionAlreadyHasSameValue_ThenSessionUpdateIsNoOp()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameSession(GameSessionSnapshot.Default.WithSelectedGameId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenConcurrentSessionUpdateAndVMSelect_ThenNoInfiniteLoopOccurs()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            using var startGate = new ManualResetEventSlim(false);

            var selectTask = Task.Run(() =>
            {
                startGate.Wait();
                sut.SelectMode("classic");
            });

            var sessionTask = Task.Run(() =>
            {
                startGate.Wait();
                _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedGameId("ultimate"));
            });

            // Act
            startGate.Set();
            Task.WaitAll(new[] { selectTask, sessionTask }, TimeSpan.FromSeconds(2)).Should().BeTrue();

            // Assert
            sut.SelectedGameId.Value.Should().BeOneOf("classic", "ultimate");
            _session.UpdateCallCount.Should().BeLessOrEqualTo(1);
        }

        [Test]
        public void WhenSessionDisposedWhileVMIsSubscribed_ThenVMGracefullyHandlesCompletionOrError()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            LogAssert.Expect(LogType.Exception, new Regex(@"ObjectDisposedException"));
            _session.DisposeSnapshot();
            Action act = () => sut.SelectMode("classic");

            // Assert
            act.Should().NotThrow();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WhenSessionSnapshotCurrentValueIsNull_ThenSelectingModeDoesNotThrowAndCanContinueUpdates()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameSession(null);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            Action act = () => sut.SelectMode("classic");

            // Assert
            act.Should().NotThrow();
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(1);
        }
    }
}