using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameSelectionViewModelTests
    {
        private IGameCatalog _catalog;
        private IGameWizardCoordinator _coordinator;
        private FakeGameSession _session;
        private ILocalizationService _localization;

        [SetUp]
        public void SetUp()
        {
            _catalog = Substitute.For<IGameCatalog>();
            _coordinator = Substitute.For<IGameWizardCoordinator>();
            _session = new FakeGameSession(GameSessionSnapshot.Default);
            _localization = Substitute.For<ILocalizationService>();

            _coordinator.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            _coordinator.IsSubmitting.Returns(new ReactiveProperty<bool>(false));

            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
        }

        [TearDown]
        public void TearDown()
        {
            _session?.Dispose();
            _session = null;
        }

        [Test]
        public void WhenConstructorCalledWithNullCatalog_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new GameSelectionViewModel(null, _coordinator, _localization);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructorCalledWithNullCoordinator_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new GameSelectionViewModel(_catalog, null, _localization);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructorCalledWithCatalogReturningNullMetadata_ThenThrowsArgumentException()
        {
            // Arrange
            _catalog.Metadata.Returns((IReadOnlyList<GameMetadata>)null);

            // Act
            Action act = () => _ = new GameSelectionViewModel(_catalog, _coordinator, _localization);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Catalog returned null Metadata.*");
        }

        [Test]
        public void WhenConstructed_ThenAvailableModesReflectsCatalogMetadataAndCanContinueIsFalse()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate", "blitz");
            _catalog.Metadata.Returns(modes);

            // Act
            using var sut = CreateSut();

            // Assert
            sut.AvailableModes.CurrentValue.Should().HaveCount(3);
            sut.SelectedGameId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenInitializeCalled_ThenEnsuresWiringWithoutDoubleSubscription()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sut.Initialize();
            sut.SelectMode("classic");

            // Assert
            _session.UpdateCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalled_ThenClearsStateAndUnwires()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");
            _session.UpdateCallCount.Should().Be(1);

            // Act
            sut.Reset();
            sut.Initialize();
            sut.SelectMode("ultimate");

            // Assert
            sut.SelectedGameId.Value.Should().Be("ultimate");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(2);
        }

        [Test]
        public void WhenDisposeCalled_ThenDisposesReactivePropertiesAndCompletesSubscriptions()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.Dispose();

            // Assert
            Action subscribeAvailableModes = () => sut.AvailableModes.Subscribe(_ => { });
            Action subscribeSelectedMode = () => sut.SelectedGameId.Subscribe(_ => { });
            Action subscribeCanContinue = () => sut.CanContinue.Subscribe(_ => { });

            subscribeAvailableModes.Should().Throw<ObjectDisposedException>();
            subscribeSelectedMode.Should().Throw<ObjectDisposedException>();
            subscribeCanContinue.Should().Throw<ObjectDisposedException>();

            Action selectAfterDispose = () => sut.SelectMode("classic");
            selectAfterDispose.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotentAndDoesNotThrow()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            var sut = CreateSut();

            // Act
            Action act = () =>
            {
                sut.Dispose();
                sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WhenSelectModeCalledWithNullOrWhitespace_ThenSetsSelectedModeIdToNullAndCanContinueFalse(string gameId)
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode(gameId);

            // Assert
            sut.SelectedGameId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithValidModeId_ThenSetsSelectedModeIdAndCanContinueTrue()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenSelectModeCalledMultipleTimes_ThenUpdatesSelectionAndCanContinueReflectsLastState()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SelectMode("classic");
            sut.SelectedGameId.Value.Should().Be("classic");

            sut.SelectMode("ultimate");
            sut.SelectedGameId.Value.Should().Be("ultimate");

            sut.SelectMode(null);

            // Assert
            sut.SelectedGameId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithSameValueTwice_ThenDoesNotEmitDuplicateUpdates()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            var emitCount = 0;
            using var sub = sut.SelectedGameId.Subscribe(_ => emitCount++);
            emitCount = 0;

            // Act
            sut.SelectMode("classic");
            sut.SelectMode("classic");

            // Assert
            emitCount.Should().Be(1);
        }

        [Test]
        public void WhenRequestContinueCalledWithNoSelection_ThenDoesNotPublishIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.DidNotReceive().TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestContinueCalledWithValidSelection_ThenPublishesContinueIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestContinueCalledAndCoordinatorRejectsIntent_ThenDoesNotThrow()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            Action act = sut.RequestContinue;

            // Assert
            act.Should().NotThrow();
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [Test]
        public void WhenRequestCancelCalled_ThenPublishesCancelIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryPublishIntent(WizardIntent.Cancel).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestCancel();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
        }

        [Test]
        public void WhenInitializeCalledAndCoordinatorReturnsNoSession_ThenVMWorksWithoutSessionSync()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sut.SelectMode("classic");

            // Assert
            sut.SelectedGameId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenCoordinatorNotReadyAndCanContinueTrue_ThenRequestContinueStillPublishesIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(true);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SelectMode("classic");

            // Act
            sut.RequestContinue();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

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

        private void SetupCoordinatorWithSession(FakeGameSession session) =>
            _coordinator.TryGetSession(out Arg.Any<IGameSession>())
                .Returns(callInfo =>
                {
                    callInfo[0] = session;
                    return true;
                });

        private GameSelectionViewModel CreateSut() =>
            new GameSelectionViewModel(_catalog, _coordinator, _localization);

        private static List<GameMetadata> CreateModes(params string[] ids) =>
            ids.Select((id, index) => new GameMetadata(
                    id,
                    $"mode.{id}",
                    $"desc.{id}",
                    $"icon.{id}",
                    index,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true))
                .ToList();


        private sealed class FakeGameSession : IGameSession
        {
            private readonly ReactiveProperty<GameSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart = new(false);
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors =
                new(Array.Empty<ValidationError>());

            private bool _isDisposed;
            private bool _isSnapshotDisposed;

            public FakeGameSession(GameSessionSnapshot initial) =>
                _snapshot = new ReactiveProperty<GameSessionSnapshot>(initial);

            public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public int UpdateCallCount { get; private set; }

            public void EmitSnapshot(GameSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
            {
                UpdateCallCount++;

                var current = _snapshot.Value ?? GameSessionSnapshot.Default;
                _snapshot.Value = reducer(current);
            }

            public void SetModeConfig(IGameConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

            public void DisposeSnapshot()
            {
                if (_isSnapshotDisposed)
                    return;

                _isSnapshotDisposed = true;
                _snapshot.Dispose();
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;

                if (!_isSnapshotDisposed)
                    _snapshot.Dispose();

                _canStart.Dispose();
                _validationErrors.Dispose();
            }
        }
    }
}