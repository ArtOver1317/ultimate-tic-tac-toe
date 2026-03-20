#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Services.UI.Assets;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class MatchSetupView : UIView<MatchSetupViewModel>
    {
        [Core.UxmlElementAttribute("BackButton")]
        private Button? _backButton;

        [Core.UxmlElementAttribute("TitleLabel")]
        private Label? _titleLabel;

        [Core.UxmlElementAttribute("ModeOptionsTitle")]
        private Label? _modeOptionsTitle;

        [Core.UxmlElementAttribute("ModeOptionsHost")]
        private ModeOptionsHost? _modeOptionsHost;

        [Core.UxmlElementAttribute("OpponentTitle")]
        private Label? _opponentTitle;

        [Core.UxmlElementAttribute("OpponentToggle")]
        private SegmentedToggle? _opponentToggle;

        [Core.UxmlElementAttribute("BotSettingsSection")]
        private VisualElement? _botSettingsSection;

        [Core.UxmlElementAttribute("BotSettingsTitle")]
        private Label? _botSettingsTitle;

        [Core.UxmlElementAttribute("DifficultyChips")]
        private DifficultyChips? _difficultyChips;

        [Core.UxmlElementAttribute("HumanSettingsSection")]
        private VisualElement? _humanSettingsSection;

        [Core.UxmlElementAttribute("HumanSettingsTitle")]
        private Label? _humanSettingsTitle;

        [Core.UxmlElementAttribute("HumanKindRadio")]
        private HumanKindRadio? _humanKindRadio;

        [Core.UxmlElementAttribute("PlayerIdInput")]
        private PlayerIdInput? _playerIdInput;

        [Core.UxmlElementAttribute("OnlinePanel")]
        private VisualElement? _onlinePanel;

        [Core.UxmlElementAttribute("SessionIdLabel")]
        private Label? _sessionIdLabel;

        [Core.UxmlElementAttribute("SessionIdValue")]
        private Label? _sessionIdValue;

        [Core.UxmlElementAttribute("CopySessionIdButton")]
        private Button? _copySessionIdButton;

        [Core.UxmlElementAttribute("BecomeHostButton")]
        private Button? _becomeHostButton;

        [Core.UxmlElementAttribute("OnlineStatusLabel")]
        private Label? _onlineStatusLabel;

        [Core.UxmlElementAttribute("OnlineCountdownLabel")]
        private Label? _onlineCountdownLabel;

        [Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Core.UxmlElementAttribute("StartButton")]
        private Button? _startButton;

        [Core.UxmlElementAttribute("ErrorLabel", isOptional: true)]
        private Label? _errorLabel;

        [Core.UxmlElementAttribute("ErrorOverlay", isOptional: true)]
        private WizardErrorOverlay? _errorOverlay;

        private IViewAssetProvider? _assetProvider;
        private IGameSettingsBinder[] _binders = Array.Empty<IGameSettingsBinder>();
        private readonly MoveTimerSettingsBinder _moveTimerBinder = new();
        private ILocalizationService? _localization;

        [Inject]
        public void Construct(IViewAssetProvider assetProvider, IEnumerable<IGameSettingsBinder> binders, ILocalizationService localization)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _binders = new List<IGameSettingsBinder>(binders).ToArray();
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        protected override void BindViewModel()
        {
            var backButton = GetRequired(_backButton, "BackButton");
            var titleLabel = GetRequired(_titleLabel, "TitleLabel");
            var modeOptionsTitle = GetRequired(_modeOptionsTitle, "ModeOptionsTitle");
            var modeOptionsHost = GetRequired(_modeOptionsHost, "ModeOptionsHost");
            var opponentTitle = GetRequired(_opponentTitle, "OpponentTitle");
            var opponentToggle = GetRequired(_opponentToggle, "OpponentToggle");
            var botSettingsSection = GetRequired(_botSettingsSection, "BotSettingsSection");
            var botSettingsTitle = GetRequired(_botSettingsTitle, "BotSettingsTitle");
            var difficultyChips = GetRequired(_difficultyChips, "DifficultyChips");
            var humanSettingsSection = GetRequired(_humanSettingsSection, "HumanSettingsSection");
            var humanSettingsTitle = GetRequired(_humanSettingsTitle, "HumanSettingsTitle");
            var humanKindRadio = GetRequired(_humanKindRadio, "HumanKindRadio");
            var playerIdInput = GetRequired(_playerIdInput, "PlayerIdInput");
            var onlinePanel = GetRequired(_onlinePanel, "OnlinePanel");
            var sessionIdLabel = GetRequired(_sessionIdLabel, "SessionIdLabel");
            var sessionIdValue = GetRequired(_sessionIdValue, "SessionIdValue");
            var copySessionIdButton = GetRequired(_copySessionIdButton, "CopySessionIdButton");
            var becomeHostButton = GetRequired(_becomeHostButton, "BecomeHostButton");
            var onlineStatusLabel = GetRequired(_onlineStatusLabel, "OnlineStatusLabel");
            var onlineCountdownLabel = GetRequired(_onlineCountdownLabel, "OnlineCountdownLabel");
            var cancelButton = GetRequired(_cancelButton, "CancelButton");
            var startButton = GetRequired(_startButton, "StartButton");

            BindSectionTexts(modeOptionsTitle, opponentTitle, botSettingsTitle, humanSettingsTitle);
            BindHeader(backButton, titleLabel, cancelButton, startButton);
            
            BindParticipantSection(
                opponentToggle,
                botSettingsSection,
                humanSettingsSection,
                difficultyChips,
                humanKindRadio,
                playerIdInput);
            
            BindOnlinePanel(
                onlinePanel,
                sessionIdLabel,
                sessionIdValue,
                copySessionIdButton,
                becomeHostButton,
                onlineStatusLabel,
                onlineCountdownLabel);
            
            BindInteractiveStates(
                backButton,
                opponentToggle,
                difficultyChips,
                humanKindRadio,
                playerIdInput,
                modeOptionsHost,
                startButton,
                copySessionIdButton,
                becomeHostButton);
            
            BindInlineError();
            BindErrorOverlay();
            BindModeSettings(modeOptionsHost);
        }

        private void BindParticipantSection(
            SegmentedToggle opponentToggle,
            VisualElement botSettingsSection,
            VisualElement humanSettingsSection,
            DifficultyChips difficultyChips,
            HumanKindRadio humanKindRadio,
            PlayerIdInput playerIdInput)
        {
            var binder = new MatchSetupParticipantSectionBinder(
                ViewModel,
                opponentToggle,
                botSettingsSection,
                humanSettingsSection,
                difficultyChips,
                humanKindRadio,
                playerIdInput);

            binder.Bind();
            AddDisposable(binder);
        }

        private void BindModeSettings(ModeOptionsHost modeOptionsHost)
        {
            var assetProvider = GetRequired(_assetProvider, nameof(IViewAssetProvider));
            var settingsLoader = new MatchSetupModeSettingsLoader(assetProvider, () => _modeOptionsHost, _binders);

            AddDisposable(settingsLoader);

            var moveTimerDisposables = new CompositeDisposable();
            _moveTimerBinder.Bind(Root, ViewModel.MoveTimerSettings, moveTimerDisposables);
            AddDisposable(moveTimerDisposables);

            AddDisposable(ViewModel.ActiveSettings
                .Subscribe(presentation => settingsLoader.LoadAsync(presentation).Forget(ex => GameLog.Exception(ex))));
        }

        private void BindErrorOverlay()
        {
            var overlay = _errorOverlay;

            if (overlay == null)
                return;

            if (_localization == null)
                throw new InvalidOperationException("Localization service is not available for error overlay binding.");

            AddDisposable(WizardErrorOverlayBinder.Bind(overlay, _localization, ViewModel.Error, ViewModel.AcknowledgeError));
        }

        private void BindSectionTexts(Label modeOptionsTitle, Label opponentTitle, Label botSettingsTitle, Label humanSettingsTitle)
        {
            BindText(ViewModel.ModeOptionsTitle, modeOptionsTitle);
            BindText(ViewModel.OpponentSectionTitle, opponentTitle);
            BindText(ViewModel.BotDifficultyTitle, botSettingsTitle);
            BindText(ViewModel.HumanSettingsTitle, humanSettingsTitle);
        }

        private void BindHeader(Button backButton, Label titleLabel, Button cancelButton, Button startButton)
        {
            BindText(ViewModel.ModeTitleText, titleLabel);
            BindText(ViewModel.BackButtonText, backButton);
            BindText(ViewModel.CancelButtonText, cancelButton);
            BindText(ViewModel.StartButtonText, startButton);

            AddDisposable(backButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestBack()));
            AddDisposable(startButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestStart()));
            AddDisposable(cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));
        }

        private void BindOnlinePanel(
            VisualElement onlinePanel,
            Label sessionIdLabel,
            Label sessionIdValue,
            Button copySessionIdButton,
            Button becomeHostButton,
            Label onlineStatusLabel,
            Label onlineCountdownLabel)
        {
            BindVisibility(ViewModel.OnlinePanelVisible, onlinePanel);
            BindText(ViewModel.SessionIdLabelText, sessionIdLabel);
            BindText(ViewModel.VisibleSessionId, sessionIdValue);
            BindText(ViewModel.CopySessionIdButtonText, copySessionIdButton);
            BindText(ViewModel.BecomeHostButtonText, becomeHostButton);
            BindTextWithAutoVisibility(ViewModel.OnlineStatusText, onlineStatusLabel);
            BindTextWithAutoVisibility(ViewModel.OnlineCountdownText, onlineCountdownLabel);

            AddDisposable(copySessionIdButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCopySessionId()));
            AddDisposable(becomeHostButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestBecomeHost()));
        }

        private void BindInteractiveStates(
            Button backButton,
            SegmentedToggle opponentToggle,
            DifficultyChips difficultyChips,
            HumanKindRadio humanKindRadio,
            PlayerIdInput playerIdInput,
            VisualElement modeOptionsHost,
            Button startButton,
            Button copySessionIdButton,
            Button becomeHostButton)
        {
            var isBlocking = ViewModel.Error.Select(static error => error is { IsBlocking: true });
            
            var canStart = ViewModel.CanStart.CombineLatest(
                ViewModel.IsBusy,
                isBlocking,
                static (isAllowed, isBusy, blocked) => isAllowed && !isBusy && !blocked);
            
            var isNotBusy = ViewModel.IsBusy.Select(static isBusy => !isBusy);
           
            var isModeOptionsInteractive = ViewModel.IsBusy.CombineLatest(
                ViewModel.IsModeOptionsEnabled,
                static (isBusy, isModeOptionsEnabled) => !isBusy && isModeOptionsEnabled);

            BindEnabled(canStart, startButton);
            BindEnabled(isNotBusy, backButton);
            BindEnabled(isNotBusy, opponentToggle);
            BindEnabled(isNotBusy, difficultyChips);
            BindEnabled(isNotBusy, humanKindRadio);
            BindEnabled(isModeOptionsInteractive, playerIdInput);
            BindEnabled(isModeOptionsInteractive, modeOptionsHost);
            BindEnabled(ViewModel.CanCopySessionId, copySessionIdButton);
            BindEnabled(ViewModel.CanBecomeHost, becomeHostButton);
        }

        private void BindInlineError()
        {
            if (_errorLabel == null)
                return;

            BindTextWithAutoVisibility(ViewModel.InlineErrorText, _errorLabel);
        }

        private void BindTextWithAutoVisibility(Observable<string?> source, Label label) =>
            AddDisposable(source.Subscribe(text =>
            {
                label.text = text ?? string.Empty;
                label.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }));

        private static T GetRequired<T>(T? element, string elementName)
            where T : class =>
            element ?? throw new InvalidOperationException($"{elementName} element is missing in UXML.");
    }
}