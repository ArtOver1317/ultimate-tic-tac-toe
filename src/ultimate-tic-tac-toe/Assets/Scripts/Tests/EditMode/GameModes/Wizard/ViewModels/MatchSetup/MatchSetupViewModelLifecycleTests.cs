using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization.Types;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelLifecycleTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenInitializeCalledMultipleTimes_ThenDoesNotDuplicateCoordinatorErrorSubscription()
        {
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            sut.Initialize();
            sut.Initialize();

            CurrentError.Value = new WizardError(
                code: "code",
                messageKey: "Errors.GameWizard.Coordinator",
                isBlocking: true,
                displayType: ErrorDisplayType.Inline);

            Localization.Received(1).Resolve(
                Arg.Is<TextTableId>(t => t.Name == "Errors"),
                Arg.Is<TextKey>(k => k.Value == "Errors.GameWizard.Coordinator"),
                Arg.Any<System.Collections.Generic.IReadOnlyDictionary<string, object>>());
        }

        [Test]
        public void WhenInitializeCalledMultipleTimes_ThenDoesNotDuplicateSessionWiring()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();

            sut.Initialize();

            var snapshotGetsAfterFirstInit = session.SnapshotGetCount;
            var canStartGetsAfterFirstInit = session.CanStartGetCount;
            var validationGetsAfterFirstInit = session.ValidationErrorsGetCount;

            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("classic")
                .WithVersion(1));

            Coordinator.Received(1).TryGetSession(out Arg.Any<IGameSession>());
            session.SnapshotGetCount.Should().Be(snapshotGetsAfterFirstInit);
            session.CanStartGetCount.Should().Be(canStartGetsAfterFirstInit);
            session.ValidationErrorsGetCount.Should().Be(validationGetsAfterFirstInit);
            subVm.InitializeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalled_ThenClearsStateAndReleasesActiveSettings()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
          
            Catalog.TryGetStrategy("classic", out Arg.Any<IGameStrategy>())
                .Returns(callInfo =>
                {
                    callInfo[1] = strategy;
                    return true;
                });

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("classic")
                .WithVersion(1));

            sut.Reset();

            sut.ActiveSettings.CurrentValue.Should().BeNull();
            sut.ModeTitleText.CurrentValue.Should().BeEmpty();
            sut.ModeIconKey.CurrentValue.Should().BeEmpty();
            sut.InlineErrorText.CurrentValue.Should().BeNull();
            subVm.DisposeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalledAndInitializeCalledAgain_ThenRewiresToNewSessionAndStopsReactingToOldSession()
        {
            var sessionA = new FakeGameSession(GameSessionSnapshot.Default);
            var sessionB = new FakeGameSession(GameSessionSnapshot.Default);
            var currentSession = sessionA;

            Coordinator.TryGetSession(out Arg.Any<IGameSession>())
                .Returns(callInfo =>
                {
                    callInfo[0] = currentSession;
                    return true;
                });

            using var sut = CreateSut();

            sut.Initialize();
           
            sessionA.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithVersion(1));

            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Human);

            sut.Reset();

            currentSession = sessionB;
            sut.Initialize();

            sessionB.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);

            sessionA.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithVersion(2));

            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenSnapshotAppliesSelectedModeId_ThenCreatesPresentationAndInitializesSubViewModel()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("classic")
                .WithVersion(1));

            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
            sut.ModeIconKey.CurrentValue.Should().Be("icons/classic");
            sut.ModeTitleText.CurrentValue.Should().Be("Mode.Classic");
            subVm.InitializeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenSnapshotAppliesSameSelectedModeId_ThenDoesNotRecreatePresentation()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(2));

            strategy.CreatePresentationCallCount.Should().Be(1);
            subVm.DisposeCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSnapshotAppliesUnknownModeId_ThenClearsPresentationAndCanStartUpdates()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            Catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitCanStart(false);
            
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("unknown")
                .WithVersion(1));

            sut.ActiveSettings.CurrentValue.Should().BeNull();
            sut.ModeTitleText.CurrentValue.Should().BeEmpty();
            sut.ModeIconKey.CurrentValue.Should().BeEmpty();
            sut.CanStart.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSnapshotAppliedWithOlderVersion_ThenIsIgnored()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var classicVm = new TestSettingsViewModel(new TestGameModeConfig("classic"));
            var ultimateVm = new TestSettingsViewModel(new TestGameModeConfig("ultimate"));

            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", classicVm));
            SetupStrategy("ultimate", new TestStrategy("ultimate", "icons/ultimate", "Mode.Ultimate", ultimateVm));

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(10));
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("ultimate").WithVersion(9));

            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
        }

        [Test]
        public void WhenSnapshotAppliedWithSameVersion_ThenSecondSnapshotIsIgnored()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var classicVm = new TestSettingsViewModel(new TestGameModeConfig("classic"));
            var ultimateVm = new TestSettingsViewModel(new TestGameModeConfig("ultimate"));

            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", classicVm));
            SetupStrategy("ultimate", new TestStrategy("ultimate", "icons/ultimate", "Mode.Ultimate", ultimateVm));

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(10));
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("ultimate").WithVersion(10));

            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
            ultimateVm.InitializeCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSubViewModelConfigChanges_ThenSessionModeConfigIsSet()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm));

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));

            var updatedConfig = new TestGameModeConfig("updated");
            subVm.EmitConfig(updatedConfig);

            session.SetModeConfigCallCount.Should().BeGreaterThan(0);
            session.LastModeConfig.Should().BeSameAs(updatedConfig);
        }

        [Test]
        public void WhenSessionIsNotAvailable_ThenVMStillWorksLocallyWithoutThrowing()
        {
            Coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            Action act = () =>
            {
                sut.Initialize();
                sut.SetOpponentType(OpponentType.Human);
            };

            act.Should().NotThrow();
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenSessionIsDisposedWhileViewModelIsAlive_ThenDoesNotThrowAndStopsWritingThrough()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm));

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));

            session.Dispose();

            var updateCallsBefore = session.UpdateCallCount;
            var setConfigCallsBefore = session.SetModeConfigCallCount;

            Action act = () =>
            {
                sut.SetOpponentType(OpponentType.Human);
                subVm.EmitConfig(new TestGameModeConfig("updated"));
            };

            act.Should().NotThrow();
            session.UpdateCallCount.Should().Be(updateCallsBefore);
            session.SetModeConfigCallCount.Should().Be(setConfigCallsBefore);
        }
    }
}