#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardLaunchConfigResolver
    {
        internal bool TryBuild(IGameSession? session, out GameLaunchConfig? launchConfig, out WizardError? error)
        {
            launchConfig = null;
            error = null;

            if (session == null)
            {
                error = new WizardError(
                    code: WizardError.Codes.SessionMissing,
                    messageKey: "Errors.GameWizard.UnhandledException",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal);

                return false;
            }

            Result<GameLaunchConfig> result;

            try
            {
                result = session.BuildLaunchConfig();
            }
            catch (Exception ex)
            {
                error = WizardError.FromException(ex);
                return false;
            }

            if (result.IsFailure)
            {
                error = CreateWizardErrorFromValidation(result.Errors);
                return false;
            }

            launchConfig = result.Value;
            return true;
        }

        private static WizardError CreateWizardErrorFromValidation(IReadOnlyList<ValidationError>? errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return new WizardError(
                    code: WizardError.Codes.ValidationFailed,
                    messageKey: "Errors.GameWizard.UnhandledException",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal);
            }

            return CreateWizardErrorFromValidation(errors[0]);
        }

        private static WizardError CreateWizardErrorFromValidation(ValidationError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            var displayType = error.Field switch
            {
                WizardFieldNames.Matchmaking => ErrorDisplayType.Modal,
                WizardFieldNames.GameCatalog => ErrorDisplayType.Modal,
                _ => ErrorDisplayType.Inline,
            };

            return new WizardError(
                code: $"wizard.field.{error.Field}",
                messageKey: error.MessageKey,
                isBlocking: displayType == ErrorDisplayType.Modal,
                displayType: displayType);
        }
    }
}