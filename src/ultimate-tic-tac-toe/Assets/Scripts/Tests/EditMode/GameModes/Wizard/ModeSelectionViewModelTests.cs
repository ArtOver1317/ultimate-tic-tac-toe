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
using Runtime.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class ModeSelectionViewModelTests
    {
        private IGameModeCatalog _catalog;
        private IGameModeWizardCoordinator _coordinator;
        private FakeGameModeSession _session;

        [SetUp]
        public void SetUp()
        {
            _catalog = Substitute.For<IGameModeCatalog>();
            _coordinator = Substitute.For<IGameModeWizardCoordinator>();
            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
            Action act = () => _ = new ModeSelectionViewModel(null, _coordinator);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructorCalledWithNullCoordinator_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new ModeSelectionViewModel(_catalog, null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructorCalledWithCatalogReturningNullMetadata_ThenThrowsArgumentException()
        {
            // Arrange
            _catalog.Metadata.Returns((IReadOnlyList<GameModeMetadata>)null);

            // Act
            Action act = () => _ = new ModeSelectionViewModel(_catalog, _coordinator);

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
            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);

            // Assert
            sut.AvailableModes.CurrentValue.Should().HaveCount(3);
            sut.SelectedModeId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenInitializeCalled_ThenEnsuresWiringWithoutDoubleSubscription()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);

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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();
            sut.SelectMode("classic");
            _session.UpdateCallCount.Should().Be(1);

            // Act
            sut.Reset();
            sut.Initialize();
            sut.SelectMode("ultimate");

            // Assert
            sut.SelectedModeId.Value.Should().Be("ultimate");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(2);
        }

        [Test]
        public void WhenDisposeCalled_ThenDisposesReactivePropertiesAndCompletesSubscriptions()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            sut.Dispose();

            // Assert
            Action subscribeAvailableModes = () => sut.AvailableModes.Subscribe(_ => { });
            Action subscribeSelectedMode = () => sut.SelectedModeId.Subscribe(_ => { });
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

            var sut = new ModeSelectionViewModel(_catalog, _coordinator);

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
        public void WhenSelectModeCalledWithNullOrWhitespace_ThenSetsSelectedModeIdToNullAndCanContinueFalse(string modeId)
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            sut.SelectMode(modeId);

            // Assert
            sut.SelectedModeId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithValidModeId_ThenSetsSelectedModeIdAndCanContinueTrue()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            sut.SelectedModeId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenSelectModeCalledMultipleTimes_ThenUpdatesSelectionAndCanContinueReflectsLastState()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            sut.SelectMode("classic");
            sut.SelectedModeId.Value.Should().Be("classic");

            sut.SelectMode("ultimate");
            sut.SelectedModeId.Value.Should().Be("ultimate");

            sut.SelectMode(null);

            // Assert
            sut.SelectedModeId.Value.Should().BeNull();
            sut.CanContinue.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSelectModeCalledWithSameValueTwice_ThenDoesNotEmitDuplicateUpdates()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            var emitCount = 0;
            using var sub = sut.SelectedModeId.Subscribe(_ => emitCount++);
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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(false);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);

            // Act
            sut.Initialize();
            sut.SelectMode("classic");

            // Assert
            sut.SelectedModeId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenCoordinatorNotReadyAndCanContinueTrue_ThenRequestContinueStillPublishesIntent()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(false);
            _coordinator.TryPublishIntent(WizardIntent.Continue).Returns(true);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithSelectedModeId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);

            // Act
            sut.Initialize();

            // Assert
            sut.SelectedModeId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenSelectModeCalledAfterInitialize_ThenWritesThroughToSession()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            sut.SelectMode("classic");

            // Assert
            _session.UpdateCallCount.Should().Be(1);
            _session.Snapshot.CurrentValue.SelectedModeId.Should().Be("classic");
        }

        [Test]
        public void WhenSessionSnapshotChangesSelectedModeId_ThenVMUpdatesWithoutWritingBack()
        {
            // Arrange
            var modes = CreateModes("classic", "ultimate");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedModeId("ultimate"));

            // Assert
            sut.SelectedModeId.Value.Should().Be("ultimate");
            _session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionSnapshotChangesSelectedModeIdToSameValue_ThenVMDoesNotUpdate()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithSelectedModeId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            var emitCount = 0;
            using var sub = sut.SelectedModeId.Subscribe(_ => emitCount++);
            emitCount = 0;

            // Act
            _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedModeId("classic"));

            // Assert
            emitCount.Should().Be(0);
        }

        [Test]
        public void WhenVMChangesSelectedModeIdAndSessionAlreadyHasSameValue_ThenSessionUpdateIsNoOp()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);

            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithSelectedModeId("classic"));
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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
                _session.EmitSnapshot(_session.Snapshot.CurrentValue.WithSelectedModeId("ultimate"));
            });

            // Act
            startGate.Set();
            Task.WaitAll(new[] { selectTask, sessionTask }, TimeSpan.FromSeconds(2)).Should().BeTrue();

            // Assert
            sut.SelectedModeId.Value.Should().BeOneOf("classic", "ultimate");
            _session.UpdateCallCount.Should().BeLessOrEqualTo(1);
        }

        [Test]
        public void WhenSessionDisposedWhileVMIsSubscribed_ThenVMGracefullyHandlesCompletionOrError()
        {
            // Arrange
            var modes = CreateModes("classic");
            _catalog.Metadata.Returns(modes);
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
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

            _session = new FakeGameModeSession(null);
            SetupCoordinatorWithSession(_session);

            using var sut = new ModeSelectionViewModel(_catalog, _coordinator);
            sut.Initialize();

            // Act
            Action act = () => sut.SelectMode("classic");

            // Assert
            act.Should().NotThrow();
            sut.SelectedModeId.Value.Should().Be("classic");
            sut.CanContinue.CurrentValue.Should().BeTrue();
            _session.UpdateCallCount.Should().Be(1);
        }

        private void SetupCoordinatorWithSession(FakeGameModeSession session) =>
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>())
                .Returns(callInfo =>
                {
                    callInfo[0] = session;
                    return true;
                });

        private static List<GameModeMetadata> CreateModes(params string[] ids) =>
            ids.Select((id, index) => new GameModeMetadata(
                    id,
                    $"mode.{id}",
                    $"desc.{id}",
                    $"icon.{id}",
                    index,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true))
                .ToList();


        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart = new(false);
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors =
                new(Array.Empty<ValidationError>());

            private bool _isDisposed;
            private bool _isSnapshotDisposed;

            public FakeGameModeSession(GameModeSessionSnapshot initial) =>
                _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(initial);

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public int UpdateCallCount { get; private set; }

            public void EmitSnapshot(GameModeSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer)
            {
                UpdateCallCount++;

                var current = _snapshot.Value ?? GameModeSessionSnapshot.Default;
                _snapshot.Value = reducer(current);
            }

            public void SetModeConfig(IGameModeConfig config) => throw new NotSupportedException();

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

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
