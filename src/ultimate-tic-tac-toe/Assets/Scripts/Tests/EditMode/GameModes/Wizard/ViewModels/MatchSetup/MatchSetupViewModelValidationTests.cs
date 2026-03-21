using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelValidationTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenValidationErrorsChange_ThenInlineErrorTextShowsHighestPriorityError()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            var errors = new List<ValidationError>
            {
                new("TargetPlayerId", "Errors.Online.InvalidSessionIdFormat"),
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            };

            session.EmitValidationErrors(errors);

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidationErrorsContainUnknownFieldAndGameCatalog_ThenInlineErrorShowsGameCatalog()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("UnknownField", "Errors.GameWizard.Unknown"),
                new(WizardFieldNames.GameCatalog, "Errors.GameWizard.GameCatalogMissing"),
            });

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.GameCatalogMissing");
        }

        [Test]
        public void WhenValidationErrorsContainGameCatalogAndGameConfig_ThenInlineErrorShowsGameConfig()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new(WizardFieldNames.GameCatalog, "Errors.GameWizard.GameCatalogMissing"),
                new(WizardFieldNames.GameConfig, "Errors.GameWizard.ConfigRequired"),
            });

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsInline_ThenInlineErrorPrefersCoordinatorOverValidation()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            CurrentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Inline);

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Coordinator");
        }

        [Test]
        public void WhenCoordinatorCurrentErrorIsNotInline_ThenInlineErrorFallsBackToValidation()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            CurrentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Modal);

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenCoordinatorInlineErrorClears_ThenInlineErrorFallsBackToValidationAgain()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            CurrentError.Value = new WizardError("code", "Errors.GameWizard.Coordinator", true, ErrorDisplayType.Inline);
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Coordinator");

            CurrentError.Value = null;

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidationErrorsContainUnknownField_ThenInlineErrorShowsResolvedUnknownMessage()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            Action act = () => session.EmitValidationErrors(new List<ValidationError>
            {
                new("UnknownField", "Errors.GameWizard.Unknown"),
            });

            act.Should().NotThrow();
            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.Unknown");
        }

        [Test]
        public void WhenMessageKeyHasNoDot_ThenResolveMessageKeyReturnsRawKey()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "SomeKey"),
            });

            sut.InlineErrorText.CurrentValue.Should().Be("SomeKey");
        }

        [Test]
        public void WhenValidationErrorsContainBotDifficultyId_ThenInlineErrorShowsIt()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("BotDifficultyId", "Errors.GameWizard.DifficultyRequired"),
            });

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.DifficultyRequired");
        }

        [Test]
        public void WhenBotDifficultyIdErrorHasLowerPriorityThanModeConfig_ThenModeConfigErrorShown()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
                new("BotDifficultyId", "Errors.GameWizard.DifficultyRequired"),
            });

            sut.InlineErrorText.CurrentValue.Should().Be("resolved:Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidationErrorTargetsPlayerId_ThenPlayerIdErrorTextShowsError()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
         
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            session.EmitValidationErrors(new[]
            {
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"),
            });

            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorsContainPlayerIdAndOtherFields_ThenPlayerIdErrorTextPicksTargetPlayerIdOnly()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
         
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            session.EmitValidationErrors(new[]
            {
                new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ModeConfigInvalid"),
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"),
                new ValidationError(WizardFieldNames.BotDifficultyId, "Errors.GameWizard.DifficultyRequired"),
            });

            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorsContainPlayerIdAndOtherFieldsAndTargetPlayerIdErrorIsNotFirst_ThenPlayerIdErrorTextStillPicksTargetPlayerId()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
           
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            const string expected = "resolved:Errors.Online.InvalidSessionIdFormat";

            session.EmitValidationErrors(new[]
            {
                new ValidationError(WizardFieldNames.GameConfig, "Errors.GameWizard.ModeConfigInvalid"),
                new ValidationError(WizardFieldNames.BotDifficultyId, "Errors.GameWizard.DifficultyRequired"),
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"),
            });

            sut.PlayerIdErrorText.CurrentValue.Should().Be(expected);
        }

        [Test]
        public void WhenValidationErrorTargetsPlayerIdButNotInDirectInviteMode_ThenPlayerIdErrorTextIsNull()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local));
          
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new[]
            {
                new ValidationError(WizardFieldNames.InviteSessionId, "Errors.Online.InvalidSessionIdFormat"),
            });

            sut.PlayerIdErrorText.CurrentValue.Should().BeNull();
        }

        [Test]
        public void WhenHumanOpponentKindChangesFromDirectInvite_ThenPlayerIdErrorTextClears()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.DirectInvite));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitValidationErrors(new[]
            {
                new ValidationError(WizardFieldNames.TargetPlayerId, "Errors.Online.InvalidSessionIdFormat"),
            });

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithOpponentType(OpponentType.Human)
                .WithHumanOpponentKind(HumanOpponentKind.Local)
                .WithVersion(1));

            sut.PlayerIdErrorText.CurrentValue.Should().BeNull();
        }
    }
}