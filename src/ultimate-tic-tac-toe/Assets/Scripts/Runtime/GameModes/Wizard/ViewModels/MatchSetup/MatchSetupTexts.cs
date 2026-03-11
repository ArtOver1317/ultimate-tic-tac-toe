#nullable enable

using System;
using R3;
using Runtime.Localization;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupTexts
    {
        private static readonly TextTableId _wizardTable = new("GameWizard");

        private readonly ILocalizationService _localization;

        public MatchSetupTexts(ILocalizationService localization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            BackButtonText = ObserveWizardText("GameWizard.MatchSetup.Back");
            CancelButtonText = ObserveWizardText("GameWizard.MatchSetup.Cancel");
            StartButtonText = ObserveWizardText("GameWizard.MatchSetup.Start");
            OpponentBotText = ObserveWizardText("GameWizard.MatchSetup.Opponent.Bot");
            OpponentHumanText = ObserveWizardText("GameWizard.MatchSetup.Opponent.Human");
            OpponentSectionTitle = ObserveWizardText("GameWizard.MatchSetup.Opponent.Title");
            ModeOptionsTitle = ObserveWizardText("GameWizard.MatchSetup.ModeOptions.Title");
            BotDifficultyTitle = ObserveWizardText("GameWizard.MatchSetup.BotDifficulty.Title");
            HumanSettingsTitle = ObserveWizardText("GameWizard.MatchSetup.HumanSettings.Title");
            HumanLocalText = ObserveWizardText("GameWizard.MatchSetup.HumanSettings.Local");
            HumanDirectInviteText = ObserveWizardText("GameWizard.MatchSetup.HumanSettings.DirectInvite");
            HumanMatchmakingText = ObserveWizardText("GameWizard.MatchSetup.HumanSettings.Matchmaking");
            PlayerIdLabelText = ObserveWizardText("GameWizard.MatchSetup.PlayerId.Label");
            SessionIdLabelText = ObserveWizardText("GameWizard.MatchSetup.SessionId.Label");
            CopySessionIdButtonText = ObserveWizardText("GameWizard.MatchSetup.SessionId.Copy");
            BecomeHostButtonText = ObserveWizardText("GameWizard.MatchSetup.Host.Become");
        }

        public Observable<string> BackButtonText { get; }
        public Observable<string> CancelButtonText { get; }
        public Observable<string> StartButtonText { get; }
        public Observable<string> OpponentBotText { get; }
        public Observable<string> OpponentHumanText { get; }
        public Observable<string> OpponentSectionTitle { get; }
        public Observable<string> ModeOptionsTitle { get; }
        public Observable<string> BotDifficultyTitle { get; }
        public Observable<string> HumanSettingsTitle { get; }
        public Observable<string> HumanLocalText { get; }
        public Observable<string> HumanDirectInviteText { get; }
        public Observable<string> HumanMatchmakingText { get; }
        public Observable<string> PlayerIdLabelText { get; }
        public Observable<string> SessionIdLabelText { get; }
        public Observable<string> CopySessionIdButtonText { get; }
        public Observable<string> BecomeHostButtonText { get; }

        public string ResolveMessageKey(string messageKey)
        {
            if (string.IsNullOrWhiteSpace(messageKey))
                return string.Empty;

            var dotIndex = messageKey.IndexOf('.', StringComparison.Ordinal);

            if (dotIndex <= 0)
                return messageKey;

            var tableName = messageKey[..dotIndex];
            return _localization.Resolve(new TextTableId(tableName), new TextKey(messageKey));
        }

        private Observable<string> ObserveWizardText(string key) =>
            _localization.Observe(_wizardTable, new TextKey(key));
    }
}