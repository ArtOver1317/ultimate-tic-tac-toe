using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.Core;

#pragma warning disable CS8632

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelTests
    {
        private const int WaitUntilTimeoutMs = 3000;

        private IGameCatalog _catalog;
        private IGameWizardCoordinator _coordinator;
        private ILocalizationService _localization;
        private IBotDifficultyCatalog _difficultyCatalog;
        private ReactiveProperty<bool> _isTransitioning;
        private ReactiveProperty<bool> _isSubmitting;
        private ReactiveProperty<WizardError?> _currentError;

        [SetUp]
        public void SetUp()
        {
            _catalog = Substitute.For<IGameCatalog>();
            _coordinator = Substitute.For<IGameWizardCoordinator>();
            _localization = Substitute.For<ILocalizationService>();

            _isTransitioning = new ReactiveProperty<bool>(false);
            _isSubmitting = new ReactiveProperty<bool>(false);
            _currentError = new ReactiveProperty<WizardError?>(null);

            _coordinator.IsTransitioning.Returns(_isTransitioning);
            _coordinator.IsSubmitting.Returns(_isSubmitting);
            _coordinator.CurrentError.Returns(_currentError);

            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));

            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => $"resolved:{callInfo.Arg<TextKey>().Value}");

            _difficultyCatalog = new BotDifficultyCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            _isTransitioning?.Dispose();
            _isSubmitting?.Dispose();
            _currentError?.Dispose();
        }

        [Test]
        public void WhenInitializeCalledMultipleTimes_ThenDoesNotDuplicateCoordinatorErrorSubscription()
        {
            // Arrange
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sut.Initialize();

            _currentError.Value = new WizardError(
                code: "code",
                messageKey: "Errors.GameWizard.Coordinator",
                isBlocking: true,
                displayType: ErrorDisplayType.Inline);

            // Assert
            _localization.Received(1).Resolve(
                Arg.Is<TextTableId>(t => t.Name == "Errors"),
                Arg.Is<TextKey>(k => k.Value == "Errors.GameWizard.Coordinator"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
        }

        [Test]
        public void WhenInitializeCalledMultipleTimes_ThenDoesNotDuplicateSessionWiring()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            var snapshotGetsAfterFirstInit = session.SnapshotGetCount;
            var canStartGetsAfterFirstInit = session.CanStartGetCount;
            var validationGetsAfterFirstInit = session.ValidationErrorsGetCount;

            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("classic")
                .WithVersion(1));

            // Assert
            _coordinator.Received(1).TryGetSession(out Arg.Any<IGameSession>());
            session.SnapshotGetCount.Should().Be(snapshotGetsAfterFirstInit);
            session.CanStartGetCount.Should().Be(canStartGetsAfterFirstInit);
            session.ValidationErrorsGetCount.Should().Be(validationGetsAfterFirstInit);
            subVm.InitializeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalled_ThenClearsStateAndReleasesActiveSettings()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            _catalog.TryGetStrategy("classic", out Arg.Any<IGameStrategy>())
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

            // Act
            sut.Reset();

            // Assert
            sut.ActiveSettings.CurrentValue.Should().BeNull();
            sut.ModeTitleText.CurrentValue.Should().BeEmpty();
            sut.ModeIconKey.CurrentValue.Should().BeEmpty();
            sut.InlineErrorText.CurrentValue.Should().BeNull();
            subVm.DisposeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalledAndInitializeCalledAgain_ThenRewiresToNewSessionAndStopsReactingToOldSession()
        {
            // Arrange
            var sessionA = new FakeGameSession(GameSessionSnapshot.Default);
            var sessionB = new FakeGameSession(GameSessionSnapshot.Default);
            var currentSession = sessionA;

            _coordinator.TryGetSession(out Arg.Any<IGameSession>())
                .Returns(callInfo =>
                {
                    callInfo[0] = currentSession;
                    return true;
                });

            using var sut = CreateSut();

            // Act
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

            // Assert
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);

            sessionA.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithVersion(2));

            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenSnapshotAppliesSelectedModeId_ThenCreatesPresentationAndInitializesSubViewModel()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("classic")
                .WithVersion(1));

            // Assert
            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
            sut.ModeIconKey.CurrentValue.Should().Be("icons/classic");
            sut.ModeTitleText.CurrentValue.Should().Be("Mode.Classic");
            subVm.InitializeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenSnapshotAppliesSameSelectedModeId_ThenDoesNotRecreatePresentation()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(2));

            // Assert
            strategy.CreatePresentationCallCount.Should().Be(1);
            subVm.DisposeCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSnapshotAppliesUnknownModeId_ThenClearsPresentationAndCanStartUpdates()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            _catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitCanStart(false);

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId("unknown")
                .WithVersion(1));

            // Assert
            sut.ActiveSettings.CurrentValue.Should().BeNull();
            sut.ModeTitleText.CurrentValue.Should().BeEmpty();
            sut.ModeIconKey.CurrentValue.Should().BeEmpty();
            sut.CanStart.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenSnapshotAppliedWithOlderVersion_ThenIsIgnored()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var classicVm = new TestSettingsViewModel(new TestGameModeConfig("classic"));
            var ultimateVm = new TestSettingsViewModel(new TestGameModeConfig("ultimate"));

            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", classicVm));
            SetupStrategy("ultimate", new TestStrategy("ultimate", "icons/ultimate", "Mode.Ultimate", ultimateVm));

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(10));
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("ultimate").WithVersion(9));

            // Assert
            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
        }

        [Test]
        public void WhenValidationErrorsChange_ThenInlineErrorTextShowsHighestPriorityError()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var errors = new List<ValidationError>
            {
                new("TargetPlayerId", "Errors.Online.InvalidSessionIdFormat"),
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsInline_ThenInlineErrorPrefersCoordinatorOverValidation()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            // Act
            _currentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Inline);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Coordinator");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsNotInline_ThenInlineErrorFallsBackToValidation()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            // Act
            _currentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Modal);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCoordinatorInlineErrorClears_ThenInlineErrorFallsBackToValidationAgain()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            _currentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Inline);
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Coordinator");

            // Act
            _currentError.Value = null;

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidationErrorsContainUnknownField_ThenInlineErrorShowsResolvedUnknownMessage()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            // Act
            Action act = () => session.EmitValidationErrors(new List<ValidationError>
            {
                new("UnknownField", "Errors.GameWizard.Unknown"),
            });

            // Assert
            act.Should().NotThrow();
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Unknown");
        }

        [Test]
        public void WhenMessageKeyHasNoDot_ThenResolveMessageKeyReturnsRawKey()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "SomeKey"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("SomeKey");
        }

        [Test]
        public void WhenOpponentTypeChangedFromUI_ThenWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetOpponentType(OpponentType.Human);

            // Assert
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.OpponentType.Should().Be(OpponentType.Human);
        }

        [Test]
        public void WhenOpponentTypeChangedFromSession_ThenDoesNotWriteBackToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default.WithOpponentType(OpponentType.Human).WithVersion(1));

            // Assert
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Human);
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalled_ThenWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            // Assert
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalledWithSameValue_ThenDoesNotCallSessionUpdate()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            // Assert
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionHumanOpponentKindChanges_ThenHumanOpponentKindUpdatesWithoutWriteBack()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            // Assert
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSetHumanOpponentKindCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            // Arrange
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            using var sut = CreateSut();
            sut.Initialize();

            // Act
            Action act = () => sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            // Assert
            act.Should().NotThrow();
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.Local);
        }

        [Test]
        public void WhenSetOpponentTypeCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            // Arrange
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);
            using var sut = CreateSut();
            sut.Initialize();

            // Act
            Action act = () => sut.SetOpponentType(OpponentType.Human);

            // Assert
            act.Should().NotThrow();
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenOpponentTypeChangesToHuman_ThenIsHumanSettingsVisibleBecomesTrue()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetOpponentType(OpponentType.Human);

            // Assert
            sut.IsHumanSettingsVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenOpponentTypeChangesToBot_ThenIsHumanSettingsVisibleBecomesFalse()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetOpponentType(OpponentType.Human);

            // Act
            sut.SetOpponentType(OpponentType.Bot);

            // Assert
            sut.IsHumanSettingsVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenOpponentTypeTogglesHumanToBotToHuman_ThenHumanOpponentKindIsPreserved()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithVersion(1));

            // Act
            sut.SetOpponentType(OpponentType.Bot);
            sut.SetOpponentType(OpponentType.Human);

            // Assert
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenResetCalled_ThenHumanOpponentKindIsSetToDefault()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetHumanOpponentKind(HumanOpponentKind.DirectInvite);

            // Act
            sut.Reset();

            // Assert
            sut.HumanOpponentKind.CurrentValue.Should().Be(HumanOpponentKind.Local);
        }

        [Test]
        public void WhenSetBotDifficultyIdCalled_ThenWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetBotDifficultyId("Hard");

            // Assert
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
        }

        [Test]
        public void WhenSetBotDifficultyIdCalledWithSameValue_ThenDoesNotCallSessionUpdate()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Easy");
            var callsBefore = session.UpdateCallCount;

            // Act
            sut.SetBotDifficultyId("Easy");

            // Assert
            session.UpdateCallCount.Should().Be(callsBefore);
        }

        [Test]
        public void WhenSetBotDifficultyIdCalledWithUnknownId_ThenNormalizesToNull()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Easy");

            // Act
            sut.SetBotDifficultyId("Unknown");

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenSessionBotDifficultyIdChanges_ThenSelectedDifficultyIdUpdatesWithoutWriteBack()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithBotDifficultyId("Hard")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionBotDifficultyIdIsUnknownAndOpponentIsBot_ThenVMSanitizesSessionByWritingBackNull()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithBotDifficultyId("UnknownDifficulty")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChangeAndSelectedIdIsNoLongerAvailable_ThenSelectionClearsAndWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");
            var callsBefore = session.UpdateCallCount;
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");

            // Act
            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameWizard.MatchSetup.BotDifficulty.Normal", 1)
            }));

            await WaitForSelectedDifficultyAsync(sut, value => value == null);

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.UpdateCallCount.Should().Be(callsBefore + 1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentTypeTogglesBotToHumanToBot_ThenSelectedDifficultyIdIsPreservedAndUIRestoresSelection()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            // Act
            sut.SetOpponentType(OpponentType.Human);
            sut.SetOpponentType(OpponentType.Bot);

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
        }

        [Test]
        public void WhenOpponentTypeChangesToHuman_ThenIsBotSettingsVisibleBecomesFalse()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetOpponentType(OpponentType.Human);

            // Assert
            sut.IsBotSettingsVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenOpponentTypeChangesToBot_ThenIsBotSettingsVisibleBecomesTrue()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetOpponentType(OpponentType.Human);

            // Act
            sut.SetOpponentType(OpponentType.Bot);

            // Assert
            sut.IsBotSettingsVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenResetCalled_ThenSelectedDifficultyIdIsCleared()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            // Act
            sut.Reset();

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
        }

        [Test]
        public void WhenSessionContainsUnsupportedMoveTimeLimit_ThenMoveTimerFallsBackToZero()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithMoveTimeLimitSeconds(17)
                .WithVersion(2));

            // Assert
            sut.MoveTimerSettings.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
            sut.MoveTimerSettings.SelectedPresetId.CurrentValue.Should().Be("0");
        }

        [Test]
        public async Task WhenResetCalled_ThenDifficultyLocalizationSubscriptionsAreDisposed()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var localization = Substitute.For<ILocalizationService>();
            localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            using var easySubject = new Subject<string>();
            localization
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0)
            }));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, localization, difficultyCatalog);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Id == "Easy"
                && items[0].Label == "Easy");

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            // Act
            sut.Reset();

            easySubject.OnNext("Easy+2");

            await WaitForDifficultyItemsAsync(sut, items => items.Count == 0);

            // Assert
            sut.DifficultyItems.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChanges_ThenDifficultyItemsAreRebuilt()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("A", "Key.A", 0),
                new BotDifficulty("B", "Key.B", 1)
            }));

            await WaitForDifficultyItemsAsync(sut, items => items.Count == 2);

            // Assert
            sut.DifficultyItems.CurrentValue.Should().HaveCount(2);
            sut.DifficultyItems.CurrentValue[0].Id.Should().Be("A");
            sut.DifficultyItems.CurrentValue[1].Id.Should().Be("B");
        }

        [Test]
        public async Task WhenLocalizationEmitsNewLabel_ThenDifficultyItemsAreUpdated()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var localization = Substitute.For<ILocalizationService>();
            localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            using var easySubject = new Subject<string>();
            localization
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0)
            }));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, localization, difficultyCatalog);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Label == "Easy");

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            // Act
            easySubject.OnNext("˸����");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Label == "˸����");

            // Assert
            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "˸����");
        }

        [Test]
        public void WhenValidationErrorsContainBotDifficultyId_ThenInlineErrorShowsIt()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("BotDifficultyId", "Errors.GameWizard.DifficultyRequired"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.DifficultyRequired");
        }

        [Test]
        public void WhenBotDifficultyIdErrorHasLowerPriorityThanModeConfig_ThenModeConfigErrorShown()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
                new("BotDifficultyId", "Errors.GameWizard.DifficultyRequired"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenSubViewModelConfigChanges_ThenSessionModeConfigIsSet()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm));

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));

            // Act
            var updatedConfig = new TestGameModeConfig("updated");
            subVm.EmitConfig(updatedConfig);

            // Assert
            session.SetModeConfigCallCount.Should().BeGreaterThan(0);
            session.LastModeConfig.Should().BeSameAs(updatedConfig);
        }

        [Test]
        public void WhenSessionIsNotAvailable_ThenVMStillWorksLocallyWithoutThrowing()
        {
            // Arrange
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            Action act = () =>
            {
                sut.Initialize();
                sut.SetOpponentType(OpponentType.Human);
            };

            // Assert
            act.Should().NotThrow();
            sut.OpponentType.CurrentValue.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenSessionIsDisposedWhileViewModelIsAlive_ThenDoesNotThrowAndStopsWritingThrough()
        {
            // Arrange
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

            // Act
            Action act = () =>
            {
                sut.SetOpponentType(OpponentType.Human);
                subVm.EmitConfig(new TestGameModeConfig("updated"));
            };

            // Assert
            act.Should().NotThrow();
            session.UpdateCallCount.Should().Be(updateCallsBefore);
            session.SetModeConfigCallCount.Should().Be(setConfigCallsBefore);
        }

        [Test]
        public void WhenRequestBackCalled_ThenPublishesBackIntent()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestBack();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Back);
        }

        [Test]
        public void WhenRequestCancelCalled_ThenPublishesCancelIntent()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.RequestCancel();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
        }

        [Test]
        public void WhenRequestStartCalledAndCanStartIsFalse_ThenDoesNotPublishStartIntent()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(false);

            // Act
            sut.RequestStart();

            // Assert
            _coordinator.DidNotReceive().TryPublishIntent(WizardIntent.Start);
        }

        [Test]
        public void WhenRequestStartCalledAndCanStartIsTrue_ThenPublishesStartIntent()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(true);

            // Act
            sut.RequestStart();

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Start);
        }

        [Test]
        public void WhenCoordinatorRejectsIntent_ThenDoesNotThrow()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            _coordinator.TryPublishIntent(Arg.Any<WizardIntent>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(true);

            // Act
            Action act = () =>
            {
                sut.RequestBack();
                sut.RequestStart();
                sut.RequestCancel();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenSetTargetPlayerIdCalled_ThenWritesThroughToSessionSnapshot()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("12345");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("12345");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithSameValue_ThenDoesNotUpdateSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("12345"));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var updatesBefore = session.UpdateCallCount;

            // Act
            sut.SetTargetPlayerId("12345");

            // Assert
            session.UpdateCallCount.Should().Be(updatesBefore);
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithWhitespace_ThenNormalizesInSnapshot()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("  123  ");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("123");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithInvalidString_ThenWritesNormalizedValueToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("invalid");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("INVALID");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledWithInvalidStringWithWhitespace_ThenWritesNormalizedValueToSession()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("  abc  ");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().Be("ABC");
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndSessionIsNull_ThenDoesNotThrowAndDoesNotChangeState()
        {
            // Arrange
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();

            var before = (
                sut.OpponentType.CurrentValue,
                sut.HumanOpponentKind.CurrentValue,
                sut.TargetPlayerId.CurrentValue,
                sut.PlayerIdErrorText.CurrentValue);

            // Act
            Action act = () => sut.SetTargetPlayerId("123");

            // Assert
            act.Should().NotThrow();
            (
                sut.OpponentType.CurrentValue,
                sut.HumanOpponentKind.CurrentValue,
                sut.TargetPlayerId.CurrentValue,
                sut.PlayerIdErrorText.CurrentValue)
                .Should().Be(before);
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndOpponentTypeIsBot_ThenNormalizesToNullInSnapshot()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("123");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenSetTargetPlayerIdCalledAndHumanKindIsLocal_ThenNormalizesToNullInSnapshot()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetTargetPlayerId("123");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenLatePlayerIdChangeArrivesAfterSwitchToLocal_ThenDoesNotReintroduceTargetPlayerId()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetHumanOpponentKind(HumanOpponentKind.Local);
            sut.SetTargetPlayerId("123");

            // Assert
            session.Snapshot.CurrentValue.TargetPlayerId.Should().BeNull();
        }

        [Test]
        public void WhenSessionTargetPlayerIdChanges_ThenVMUpdatesWithoutWriteBack()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var updatesBefore = session.UpdateCallCount;

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("456")
                .WithVersion(1));

            // Assert
            sut.TargetPlayerId.CurrentValue.Should().Be("456");
            session.UpdateCallCount.Should().Be(updatesBefore);
        }

        [Test]
        public void WhenSessionTargetPlayerIdIsNull_ThenVMTargetPlayerIdIsEmptyString()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId(null));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            // Assert
            sut.TargetPlayerId.CurrentValue.Should().Be("");
        }

        [Test]
        public void WhenValidationErrorTargetsPlayerId_ThenPlayerIdErrorTextShowsError()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            var errors = new[]
            {
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat")
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorsContainPlayerIdAndOtherFields_ThenPlayerIdErrorTextPicksTargetPlayerIdOnly()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            var errors = new[]
            {
                new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ModeConfigInvalid"),
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"),
                new ValidationError(WizardFieldNames.BotDifficultyId, "Errors.GameWizard.DifficultyRequired")
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorsContainPlayerIdAndOtherFieldsAndTargetPlayerIdErrorIsNotFirst_ThenPlayerIdErrorTextStillPicksTargetPlayerId()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            var errors = new[]
            {
                new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ModeConfigInvalid"),
                new ValidationError(WizardFieldNames.BotDifficultyId, "Errors.GameWizard.DifficultyRequired"),
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat")
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorTargetsPlayerIdButNotInDirectInviteMode_ThenPlayerIdErrorTextIsNull()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var errors = new[]
            {
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat")
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.PlayerIdErrorText.CurrentValue.Should().BeNull();
        }

        [Test]
        public void WhenHumanOpponentKindChangesFromDirectInvite_ThenPlayerIdErrorTextClears()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var errors = new[]
            {
                new ValidationError(WizardFieldNames.TargetPlayerId, "Errors.Online.InvalidSessionIdFormat")
            };
            session.EmitValidationErrors(errors);

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            // Assert
            sut.PlayerIdErrorText.CurrentValue.Should().BeNull();
        }

        [Test]
        public void WhenOpponentTypeIsHumanAndKindIsDirectInvite_ThenIsPlayerIdInputVisibleIsTrue()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            // Assert
            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenOpponentTypeIsBot_ThenIsPlayerIdInputVisibleIsFalse()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            // Assert
            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenHumanKindIsLocal_ThenIsPlayerIdInputVisibleIsFalse()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            // Act
            sut.Initialize();

            // Assert
            sut.IsPlayerIdInputVisible.CurrentValue.Should().BeFalse();
        }

        [Test]
        public void WhenResetCalled_ThenTargetPlayerIdIsCleared()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite)
                .WithTargetPlayerId("12345"));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.Reset();

            // Assert
            sut.TargetPlayerId.CurrentValue.Should().Be("");
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowIdleWithoutCandidate_ThenGeneratesSessionIdAndEnablesCopy()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, _localization, _difficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "ABCDEF", StringComparison.Ordinal));

            onlineFlow.EnterHumanSetupCalls.Should().Be(1);
            sut.VisibleSessionId.CurrentValue.Should().Be("ABCDEF");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowTerminatedWithoutCandidate_ThenGeneratesSessionIdAndEnablesCopy()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Terminated,
                    previousStableState: OnlineFlowState.InGame,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: 2,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.OpponentLeft,
                    errorLocalizationKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.OpponentLeft),
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, _localization, _difficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "ABCDEF", StringComparison.Ordinal));

            onlineFlow.EnterHumanSetupCalls.Should().Be(1);
            sut.VisibleSessionId.CurrentValue.Should().Be("ABCDEF");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
            sut.CanBecomeHost.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenDirectInviteSelectedAndFlowIdleWithCandidateAndStaleActive_ThenVisibleSessionIdUsesCandidate()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            SetupCoordinatorWithSession(session);

            using var onlineFlow = new SpyMatchSetupOnlineFlow(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: "NEW123",
                    activeSessionId: "OLD999",
                    flowEpoch: 1,
                    region: "eu",
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, _localization, _difficultyCatalog, onlineFlow);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            await WaitUntilAsync(() =>
                sut.CanCopySessionId.CurrentValue &&
                string.Equals(sut.VisibleSessionId.CurrentValue, "NEW123", StringComparison.Ordinal));

            sut.VisibleSessionId.CurrentValue.Should().Be("NEW123");
            sut.CanCopySessionId.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenBattleshipSelectedAndHumanLocalInSnapshot_ThenHumanKindAutoNormalizedToDirectInvite()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            SetupStrategy(BattleshipStrategy.DefaultGameId, CreateBattleshipStrategy());

            using var sut = CreateSut();
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            // Assert
            sut.IsLocalHumanSupported.CurrentValue.Should().BeFalse();
            session.Snapshot.CurrentValue.HumanOpponentKind.Should().Be(HumanOpponentKind.DirectInvite);
        }

        [Test]
        public void WhenBattleshipSelectedInBotModeAndDifficultyMissing_ThenSelectsDefaultDifficultyAndHidesBotDifficultySection()
        {
            // Arrange
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            SetupStrategy(BattleshipStrategy.DefaultGameId, CreateBattleshipStrategy());

            using var sut = CreateSut();
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId(null)
                .WithVersion(1));

            // Assert
            sut.SelectedDifficultyId.CurrentValue.Should().Be(BattleshipStrategy.DefaultBotDifficultyId);
            sut.IsBotSettingsVisible.CurrentValue.Should().BeFalse();
        }

        private void SetupCoordinatorWithSession(FakeGameSession session) =>
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(callInfo =>
            {
                callInfo[0] = session;
                return true;
            });

        private BattleshipStrategy CreateBattleshipStrategy() =>
            new(() => new BattleshipSettingsViewModel(MoveTimerPresetsConfig.CreateRuntimeDefault(), _localization));

        private void SetupStrategy(string gameId, IGameStrategy strategy) =>
            _catalog.TryGetStrategy(gameId, out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = strategy;
                return true;
            });

        private static void SetAvailableDifficulties(MatchSetupViewModel sut, IReadOnlyList<BotDifficulty> difficulties)
        {
            var property = sut.AvailableDifficulties as ReactiveProperty<IReadOnlyList<BotDifficulty>>;
            property.Should().NotBeNull();
            property!.Value = difficulties;
        }

        private static async Task WaitForDifficultyItemsAsync(
            MatchSetupViewModel sut,
            Func<IReadOnlyList<DifficultyChipItem>, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = sut.DifficultyItems.Subscribe(items =>
            {
                if (predicate(items))
                    tcs.TrySetResult(true);
            });

            if (predicate(sut.DifficultyItems.CurrentValue))
                return;

            using var cts = new CancellationTokenSource(WaitUntilTimeoutMs);
            using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException(
                       $"Condition was not met within {WaitUntilTimeoutMs} ms."))))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        private static async Task WaitForSelectedDifficultyAsync(
            MatchSetupViewModel sut,
            Func<string?, bool> predicate)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = sut.SelectedDifficultyId.Subscribe(value =>
            {
                if (predicate(value))
                    tcs.TrySetResult(true);
            });

            if (predicate(sut.SelectedDifficultyId.CurrentValue))
                return;

            using var cts = new CancellationTokenSource(WaitUntilTimeoutMs);
            using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException(
                       $"Condition was not met within {WaitUntilTimeoutMs} ms."))))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int maxFrames = 120)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                if (predicate())
                    return;

                await UniTask.DelayFrame(1);
            }

            Assert.Fail($"Condition was not met within {maxFrames} frames.");
        }

        private MatchSetupViewModel CreateSut() =>
            CreateSutWithDefaults();

        private MatchSetupViewModel CreateSutWithDefaults()
        {
            var sut = new MatchSetupViewModel(_catalog, _coordinator, _localization, _difficultyCatalog);
            sut.DisablePlayerLoopForTests();
            return sut;
        }

        private sealed class SpyMatchSetupOnlineFlow : IOnlineSessionFlowService
        {
            private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot;

            public SpyMatchSetupOnlineFlow(OnlineFlowSnapshot initialSnapshot)
            {
                _snapshot = new ReactiveProperty<OnlineFlowSnapshot>(initialSnapshot);
            }

            public int EnterHumanSetupCalls { get; private set; }

            public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

            public UniTask EnterHumanSetupAsync(string region, string currentUserId)
            {
                EnterHumanSetupCalls++;

                var current = _snapshot.Value;
                if ((current.State == OnlineFlowState.Idle ||
                     current.State == OnlineFlowState.Terminated ||
                     current.State == OnlineFlowState.Failed) &&
                    string.IsNullOrWhiteSpace(current.CandidateSessionId))
                {
                    _snapshot.Value = new OnlineFlowSnapshot(
                        state: OnlineFlowState.Idle,
                        previousStableState: null,
                        candidateSessionId: "ABCDEF",
                        activeSessionId: null,
                        flowEpoch: current.FlowEpoch + 1,
                        region: current.Region,
                        canStart: false,
                        isBusy: false,
                        errorCode: OnlineErrorCode.None,
                        errorLocalizationKey: null,
                        statusLocalizationKey: null,
                        countdownRemainingSeconds: null,
                        graceDeadlineUtc: null);
                }

                return UniTask.CompletedTask;
            }

            public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
            public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
            public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
            public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
            public UniTask BackAsync() => UniTask.CompletedTask;
            public UniTask ExitAsync() => UniTask.CompletedTask;
            public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
            public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
            public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
            public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
            public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
            public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
            public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
            public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
            public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
            public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
            public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

            public void Dispose() => _snapshot.Dispose();
        }

        private sealed class FakeGameSession : IGameSession
        {
            private readonly ReactiveProperty<GameSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;
            private bool _isDisposed;

            public FakeGameSession(GameSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public int SnapshotGetCount { get; private set; }
            public int CanStartGetCount { get; private set; }
            public int ValidationErrorsGetCount { get; private set; }

            public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot
            {
                get
                {
                    SnapshotGetCount++;
                    return _snapshot;
                }
            }

            public ReadOnlyReactiveProperty<bool> CanStart
            {
                get
                {
                    CanStartGetCount++;
                    return _canStart;
                }
            }

            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors
            {
                get
                {
                    ValidationErrorsGetCount++;
                    return _validationErrors;
                }
            }

            public int UpdateCallCount { get; private set; }
            public int SetModeConfigCallCount { get; private set; }
            public IGameConfig LastModeConfig { get; private set; }

            public void EmitSnapshot(GameSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void EmitCanStart(bool value) => _canStart.Value = value;

            public void EmitValidationErrors(IReadOnlyList<ValidationError> errors) => _validationErrors.Value = errors;

            public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
            {
                EnsureNotDisposed();
                UpdateCallCount++;
                var current = _snapshot.Value ?? GameSessionSnapshot.Default;
                var updated = reducer(current) ?? GameSessionSnapshot.Default;
                var nextVersion = current.Version + 1;
                if (updated.Version < nextVersion)
                    updated = updated.WithVersion(nextVersion);
                _snapshot.Value = updated;
            }

            public void SetModeConfig(IGameConfig config)
            {
                EnsureNotDisposed();
                SetModeConfigCallCount++;
                LastModeConfig = config;
            }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

            public void Dispose()
            {
                _isDisposed = true;
                _snapshot.Dispose();
                _canStart.Dispose();
                _validationErrors.Dispose();
            }

            private void EnsureNotDisposed()
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FakeGameSession));
            }
        }

        private sealed class TestStrategy : IGameStrategy
        {
            private readonly TestSettingsViewModel _viewModel;

            public TestStrategy(string gameId, string iconKey, string displayNameKey, TestSettingsViewModel viewModel)
            {
                GameId = gameId;
                Metadata = new GameMetadata(
                    id: gameId,
                    displayNameKey: displayNameKey,
                    descriptionKey: "desc",
                    iconAssetKey: iconKey,
                    sortOrder: 0,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true);
                _viewModel = viewModel;
            }

            public string GameId { get; }
            public GameMetadata Metadata { get; }
            public int CreatePresentationCallCount { get; private set; }

            public GameSettingsPresentation CreatePresentation()
            {
                CreatePresentationCallCount++;
                return new GameSettingsPresentation($"ui/mode-settings/{GameId}", _viewModel);
            }

            public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config) => Array.Empty<ValidationError>();

            public IEnumerable<string> GetSupportedBotDifficultyIds() => Array.Empty<string>();
        }

        private sealed class TestSettingsViewModel : BaseViewModel, IGameSettingsViewModel
        {
            private readonly ReactiveProperty<IGameConfig> _config;
            private readonly ReactiveProperty<bool> _isValid = new(true);

            public TestSettingsViewModel(IGameConfig config) =>
                _config = new ReactiveProperty<IGameConfig>(config);

            public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
            public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

            public bool TryApplyConfig(IGameConfig config)
            {
                if (config == null)
                    return false;

                _config.Value = config;
                return true;
            }

            public int InitializeCallCount { get; private set; }
            public int DisposeCallCount { get; private set; }

            public void EmitConfig(IGameConfig config) => _config.Value = config;

            public override void Initialize()
            {
                base.Initialize();
                InitializeCallCount++;
            }

            protected override void OnDispose()
            {
                DisposeCallCount++;
                _config.Dispose();
                _isValid.Dispose();
                base.OnDispose();
            }
        }

        private sealed class TestGameModeConfig : IGameConfig
        {
            public TestGameModeConfig(string value) => Value = value;
            public string Value { get; }

            public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, string>> GetMatchmakingParams() =>
                System.Array.Empty<System.Collections.Generic.KeyValuePair<string, string>>();
        }
    }
}