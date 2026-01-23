using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.UI.Core;

#pragma warning disable CS8632

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelTests
    {
        private const int WaitUntilTimeoutMs = 3000;
        private const int WaitUntilPollDelayMs = 10;

        private IGameModeCatalog _catalog;
        private IGameModeWizardCoordinator _coordinator;
        private ILocalizationService _localization;
        private IBotDifficultyCatalog _difficultyCatalog;
        private ReactiveProperty<bool> _isTransitioning;
        private ReactiveProperty<bool> _isSubmitting;
        private ReactiveProperty<WizardError?> _currentError;

        [SetUp]
        public void SetUp()
        {
            _catalog = Substitute.For<IGameModeCatalog>();
            _coordinator = Substitute.For<IGameModeWizardCoordinator>();
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
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sut.Initialize();

            _currentError.Value = new WizardError(
                code: "code",
                messageKey: "Errors.GameModeWizard.Coordinator",
                isBlocking: true,
                displayType: ErrorDisplayType.Inline);

            // Assert
            _localization.Received(1).Resolve(
                Arg.Is<TextTableId>(t => t.Name == "Errors"),
                Arg.Is<TextKey>(k => k.Value == "Errors.GameModeWizard.Coordinator"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
        }

        [Test]
        public void WhenInitializeCalledMultipleTimes_ThenDoesNotDuplicateSessionWiring()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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

            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithSelectedModeId("classic")
                .WithVersion(1));

            // Assert
            _coordinator.Received(1).TryGetSession(out Arg.Any<IGameModeSession>());
            session.SnapshotGetCount.Should().Be(snapshotGetsAfterFirstInit);
            session.CanStartGetCount.Should().Be(canStartGetsAfterFirstInit);
            session.ValidationErrorsGetCount.Should().Be(validationGetsAfterFirstInit);
            subVm.InitializeCallCount.Should().Be(1);
        }

        [Test]
        public void WhenResetCalled_ThenClearsStateAndReleasesActiveSettings()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            _catalog.TryGetStrategy("classic", out Arg.Any<IGameModeStrategy>())
                .Returns(callInfo =>
                {
                    callInfo[1] = strategy;
                    return true;
                });

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithSelectedModeId("classic")
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
            var sessionA = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            var sessionB = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            var currentSession = sessionA;

            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>())
                .Returns(callInfo =>
                {
                    callInfo[0] = currentSession;
                    return true;
                });

            using var sut = CreateSut();

            // Act
            sut.Initialize();
            sessionA.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithVersion(1));

            sut.OpponentType.Value.Should().Be(OpponentType.Human);

            sut.Reset();

            currentSession = sessionB;
            sut.Initialize();

            sessionB.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            // Assert
            sut.OpponentType.Value.Should().Be(OpponentType.Bot);

            sessionA.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithVersion(2));

            sut.OpponentType.Value.Should().Be(OpponentType.Bot);
        }

        [Test]
        public void WhenSnapshotAppliesSelectedModeId_ThenCreatesPresentationAndInitializesSubViewModel()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithSelectedModeId("classic")
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            var strategy = new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm);
            SetupStrategy("classic", strategy);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(2));

            // Assert
            strategy.CreatePresentationCallCount.Should().Be(1);
            subVm.DisposeCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSnapshotAppliesUnknownModeId_ThenClearsPresentationAndCanStartUpdates()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            _catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameModeStrategy>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitCanStart(false);

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithSelectedModeId("unknown")
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var classicVm = new TestSettingsViewModel(new TestGameModeConfig("classic"));
            var ultimateVm = new TestSettingsViewModel(new TestGameModeConfig("ultimate"));

            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", classicVm));
            SetupStrategy("ultimate", new TestStrategy("ultimate", "icons/ultimate", "Mode.Ultimate", ultimateVm));

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(10));
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("ultimate").WithVersion(9));

            // Assert
            sut.ActiveSettings.CurrentValue.Should().NotBeNull();
            sut.ActiveSettings.CurrentValue.UxmlAssetKey.Should().Be("ui/mode-settings/classic");
        }

        [Test]
        public void WhenValidationErrorsChange_ThenInlineErrorTextShowsHighestPriorityError()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var errors = new List<ValidationError>
            {
                new("TargetPlayerId", "Errors.GameModeWizard.PlayerIdRequired"),
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            };

            // Act
            session.EmitValidationErrors(errors);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsInline_ThenInlineErrorPrefersCoordinatorOverValidation()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            });

            // Act
            _currentError.Value = new WizardError("code", "Errors.GameModeWizard.Coordinator", true, ErrorDisplayType.Inline);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.Coordinator");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsNotInline_ThenInlineErrorFallsBackToValidation()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            });

            // Act
            _currentError.Value = new WizardError("code", "Errors.GameModeWizard.Coordinator", true, ErrorDisplayType.Modal);

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenCoordinatorInlineErrorClears_ThenInlineErrorFallsBackToValidationAgain()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            });

            _currentError.Value = new WizardError("code", "Errors.GameModeWizard.Coordinator", true, ErrorDisplayType.Inline);
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.Coordinator");

            // Act
            _currentError.Value = null;

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenValidationErrorsContainUnknownField_ThenInlineErrorShowsResolvedUnknownMessage()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            });

            // Act
            Action act = () => session.EmitValidationErrors(new List<ValidationError>
            {
                new("UnknownField", "Errors.GameModeWizard.Unknown"),
            });

            // Assert
            act.Should().NotThrow();
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.Unknown");
        }

        [Test]
        public void WhenMessageKeyHasNoDot_ThenResolveMessageKeyReturnsRawKey()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "SomeKey"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("SomeKey");
        }

        [Test]
        public void WhenOpponentTypeChangedFromUI_ThenWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithOpponentType(OpponentType.Human).WithVersion(1));

            // Assert
            sut.OpponentType.Value.Should().Be(OpponentType.Human);
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSetBotDifficultyIdCalled_ThenWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            sut.SetBotDifficultyId("Hard");

            // Assert
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
            sut.SelectedDifficultyId.Value.Should().Be("Hard");
        }

        [Test]
        public void WhenSetBotDifficultyIdCalledWithSameValue_ThenDoesNotCallSessionUpdate()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Easy");

            // Act
            sut.SetBotDifficultyId("Unknown");

            // Assert
            sut.SelectedDifficultyId.Value.Should().BeNull();
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenSessionBotDifficultyIdChanges_ThenSelectedDifficultyIdUpdatesWithoutWriteBack()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithBotDifficultyId("Hard")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            // Assert
            sut.SelectedDifficultyId.Value.Should().Be("Hard");
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionBotDifficultyIdIsUnknownAndOpponentIsBot_ThenVMSanitizesSessionByWritingBackNull()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitSnapshot(GameModeSessionSnapshot.Default
                .WithBotDifficultyId("UnknownDifficulty")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            // Assert
            sut.SelectedDifficultyId.Value.Should().BeNull();
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChangeAndSelectedIdIsNoLongerAvailable_ThenSelectionClearsAndWritesThroughToSession()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");
            var callsBefore = session.UpdateCallCount;
            sut.SelectedDifficultyId.Value.Should().Be("Hard");

            // Act
            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameModeWizard.MatchSetup.BotDifficulty.Normal", 1)
            }));

            await WaitUntilAsync(
                () => sut.SelectedDifficultyId.Value == null && session.UpdateCallCount == callsBefore + 1,
                timeoutMs: WaitUntilTimeoutMs);

            // Assert
            sut.SelectedDifficultyId.Value.Should().BeNull();
            session.UpdateCallCount.Should().Be(callsBefore + 1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentTypeTogglesBotToHumanToBot_ThenSelectedDifficultyIdIsPreservedAndUIRestoresSelection()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            // Act
            sut.SetOpponentType(OpponentType.Human);
            sut.SetOpponentType(OpponentType.Bot);

            // Assert
            sut.SelectedDifficultyId.Value.Should().Be("Hard");
        }

        [Test]
        public void WhenOpponentTypeChangesToHuman_ThenIsBotSettingsVisibleBecomesFalse()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            // Act
            sut.Reset();

            // Assert
            sut.SelectedDifficultyId.Value.Should().BeNull();
        }

        [Test]
        public async Task WhenResetCalled_ThenDifficultyLocalizationSubscriptionsAreDisposed()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameModeWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameModeWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0)
            }));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, localization, difficultyCatalog);
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitUntilAsync(
                () => sut.DifficultyItems.CurrentValue.Count == 1
                      && sut.DifficultyItems.CurrentValue[0].Id == "Easy"
                      && sut.DifficultyItems.CurrentValue[0].Label == "Easy",
                timeoutMs: WaitUntilTimeoutMs);

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            // Act
            sut.Reset();

            easySubject.OnNext("Easy+2");

            await WaitUntilAsync(
                () => sut.DifficultyItems.CurrentValue.Count == 0,
                timeoutMs: WaitUntilTimeoutMs);

            // Assert
            sut.DifficultyItems.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChanges_ThenDifficultyItemsAreRebuilt()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("A", "Key.A", 0),
                new BotDifficulty("B", "Key.B", 1)
            }));

            await WaitUntilAsync(
                () => sut.DifficultyItems.CurrentValue.Count == 2,
                timeoutMs: WaitUntilTimeoutMs);

            // Assert
            sut.DifficultyItems.CurrentValue.Should().HaveCount(2);
            sut.DifficultyItems.CurrentValue[0].Id.Should().Be("A");
            sut.DifficultyItems.CurrentValue[1].Id.Should().Be("B");
        }

        [Test]
        public async Task WhenLocalizationEmitsNewLabel_ThenDifficultyItemsAreUpdated()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameModeWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameModeWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0)
            }));

            using var sut = new MatchSetupViewModel(_catalog, _coordinator, localization, difficultyCatalog);
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitUntilAsync(
                () => sut.DifficultyItems.CurrentValue.Count == 1
                      && sut.DifficultyItems.CurrentValue[0].Label == "Easy",
                timeoutMs: WaitUntilTimeoutMs);

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            // Act
            easySubject.OnNext("Лёгкий");

            await WaitUntilAsync(
                () => sut.DifficultyItems.CurrentValue.Count == 1
                      && sut.DifficultyItems.CurrentValue[0].Label == "Лёгкий",
                timeoutMs: WaitUntilTimeoutMs);

            // Assert
            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Лёгкий");
        }

        [Test]
        public void WhenValidationErrorsContainBotDifficultyId_ThenInlineErrorShowsIt()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("BotDifficultyId", "Errors.GameModeWizard.DifficultyRequired"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.DifficultyRequired");
        }

        [Test]
        public void WhenBotDifficultyIdErrorHasLowerPriorityThanModeConfig_ThenModeConfigErrorShown()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            // Act
            session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
                new("BotDifficultyId", "Errors.GameModeWizard.DifficultyRequired"),
            });

            // Assert
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenSubViewModelConfigChanges_ThenSessionModeConfigIsSet()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm));

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));

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
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(false);

            using var sut = CreateSut();

            // Act
            Action act = () =>
            {
                sut.Initialize();
                sut.SetOpponentType(OpponentType.Human);
            };

            // Assert
            act.Should().NotThrow();
            sut.OpponentType.Value.Should().Be(OpponentType.Human);
        }

        [Test]
        public void WhenSessionIsDisposedWhileViewModelIsAlive_ThenDoesNotThrowAndStopsWritingThrough()
        {
            // Arrange
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var subVm = new TestSettingsViewModel(new TestGameModeConfig("initial"));
            SetupStrategy("classic", new TestStrategy("classic", "icons/classic", "Mode.Classic", subVm));

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));

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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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
            var session = new FakeGameModeSession(GameModeSessionSnapshot.Default);
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

        private void SetupCoordinatorWithSession(FakeGameModeSession session) =>
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(callInfo =>
            {
                callInfo[0] = session;
                return true;
            });

        private void SetupStrategy(string modeId, IGameModeStrategy strategy) =>
            _catalog.TryGetStrategy(modeId, out Arg.Any<IGameModeStrategy>()).Returns(callInfo =>
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

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();

            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMs)
                    Assert.Fail($"Condition was not met within {timeoutMs} ms.");

                await Task.Delay(WaitUntilPollDelayMs);
            }
        }

        private MatchSetupViewModel CreateSut() =>
            new MatchSetupViewModel(_catalog, _coordinator, _localization, _difficultyCatalog);

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;
            private bool _isDisposed;

            public FakeGameModeSession(GameModeSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public int SnapshotGetCount { get; private set; }
            public int CanStartGetCount { get; private set; }
            public int ValidationErrorsGetCount { get; private set; }

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot
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
            public IGameModeConfig LastModeConfig { get; private set; }

            public void EmitSnapshot(GameModeSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void EmitCanStart(bool value) => _canStart.Value = value;

            public void EmitValidationErrors(IReadOnlyList<ValidationError> errors) => _validationErrors.Value = errors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer)
            {
                EnsureNotDisposed();
                UpdateCallCount++;
                _snapshot.Value = reducer(_snapshot.Value ?? GameModeSessionSnapshot.Default);
            }

            public void SetModeConfig(IGameModeConfig config)
            {
                EnsureNotDisposed();
                SetModeConfigCallCount++;
                LastModeConfig = config;
            }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

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
                    throw new ObjectDisposedException(nameof(FakeGameModeSession));
            }
        }

        private sealed class TestStrategy : IGameModeStrategy
        {
            private readonly TestSettingsViewModel _viewModel;

            public TestStrategy(string modeId, string iconKey, string displayNameKey, TestSettingsViewModel viewModel)
            {
                ModeId = modeId;
                Metadata = new GameModeMetadata(
                    id: modeId,
                    displayNameKey: displayNameKey,
                    descriptionKey: "desc",
                    iconAssetKey: iconKey,
                    sortOrder: 0,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true);
                _viewModel = viewModel;
            }

            public string ModeId { get; }
            public GameModeMetadata Metadata { get; }
            public int CreatePresentationCallCount { get; private set; }

            public ModeSettingsPresentation CreatePresentation()
            {
                CreatePresentationCallCount++;
                return new ModeSettingsPresentation($"ui/mode-settings/{ModeId}", _viewModel);
            }

            public IReadOnlyList<ValidationError> ValidateConfig(IGameModeConfig? config) => Array.Empty<ValidationError>();
        }

        private sealed class TestSettingsViewModel : BaseViewModel, ISpecificModeSettingsViewModel
        {
            private readonly ReactiveProperty<IGameModeConfig> _config;
            private readonly ReactiveProperty<bool> _isValid = new(true);

            public TestSettingsViewModel(IGameModeConfig config) =>
                _config = new ReactiveProperty<IGameModeConfig>(config);

            public ReadOnlyReactiveProperty<IGameModeConfig> Config => _config;
            public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

            public bool TryApplyConfig(IGameModeConfig config)
            {
                if (config == null)
                    return false;

                _config.Value = config;
                return true;
            }

            public int InitializeCallCount { get; private set; }
            public int DisposeCallCount { get; private set; }

            public void EmitConfig(IGameModeConfig config) => _config.Value = config;

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

        private sealed class TestGameModeConfig : IGameModeConfig
        {
            public TestGameModeConfig(string value) => Value = value;
            public string Value { get; }
        }
    }
}