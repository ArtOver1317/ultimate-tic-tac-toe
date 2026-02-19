#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Extensions;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Services.UI.Assets;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;
using VContainer;

namespace Runtime.UI.GameModes.Wizard
{
    public sealed class MatchSetupView : UIView<MatchSetupViewModel>
    {
        [Runtime.UI.Core.UxmlElementAttribute("BackButton")]
        private Button? _backButton;

        [Runtime.UI.Core.UxmlElementAttribute("TitleLabel")]
        private Label? _titleLabel;

        [Runtime.UI.Core.UxmlElementAttribute("ModeOptionsTitle")]
        private Label? _modeOptionsTitle;

        [Runtime.UI.Core.UxmlElementAttribute("ModeOptionsHost")]
        private ModeOptionsHost? _modeOptionsHost;

        [Runtime.UI.Core.UxmlElementAttribute("OpponentTitle")]
        private Label? _opponentTitle;

        [Runtime.UI.Core.UxmlElementAttribute("OpponentToggle")]
        private SegmentedToggle? _opponentToggle;

        [Runtime.UI.Core.UxmlElementAttribute("BotSettingsSection")]
        private VisualElement? _botSettingsSection;

        [Runtime.UI.Core.UxmlElementAttribute("BotSettingsTitle")]
        private Label? _botSettingsTitle;

        [Runtime.UI.Core.UxmlElementAttribute("DifficultyChips")]
        private DifficultyChips? _difficultyChips;

        [Runtime.UI.Core.UxmlElementAttribute("HumanSettingsSection")]
        private VisualElement? _humanSettingsSection;

        [Runtime.UI.Core.UxmlElementAttribute("HumanSettingsTitle")]
        private Label? _humanSettingsTitle;

        [Runtime.UI.Core.UxmlElementAttribute("HumanKindRadio")]
        private HumanKindRadio? _humanKindRadio;

        [Runtime.UI.Core.UxmlElementAttribute("PlayerIdInput")]
        private PlayerIdInput? _playerIdInput;

        [Runtime.UI.Core.UxmlElementAttribute("OnlinePanel")]
        private VisualElement? _onlinePanel;

        [Runtime.UI.Core.UxmlElementAttribute("SessionIdLabel")]
        private Label? _sessionIdLabel;

        [Runtime.UI.Core.UxmlElementAttribute("SessionIdValue")]
        private Label? _sessionIdValue;

        [Runtime.UI.Core.UxmlElementAttribute("CopySessionIdButton")]
        private Button? _copySessionIdButton;

        [Runtime.UI.Core.UxmlElementAttribute("BecomeHostButton")]
        private Button? _becomeHostButton;

        [Runtime.UI.Core.UxmlElementAttribute("OnlineStatusLabel")]
        private Label? _onlineStatusLabel;

        [Runtime.UI.Core.UxmlElementAttribute("OnlineCountdownLabel")]
        private Label? _onlineCountdownLabel;

        [Runtime.UI.Core.UxmlElementAttribute("CancelButton")]
        private Button? _cancelButton;

        [Runtime.UI.Core.UxmlElementAttribute("StartButton")]
        private Button? _startButton;

        [Runtime.UI.Core.UxmlElementAttribute("ErrorLabel", isOptional: true)]
        private Label? _errorLabel;

        [Runtime.UI.Core.UxmlElementAttribute("ErrorOverlay", isOptional: true)]
        private WizardErrorOverlay? _errorOverlay;

        private IViewAssetProvider _assetProvider = null!;
        private IGameSettingsBinder[] _binders = System.Array.Empty<IGameSettingsBinder>();
        private ILocalizationService? _localization;

        private CancellationTokenSource? _loadCts;
        private IAssetLease<VisualTreeAsset>? _currentLease;
        private IDisposable? _subBinding;
        private int _loadVersion;

        private string _botLabel = string.Empty;
        private string _humanLabel = string.Empty;
        private string _humanLocalLabel = string.Empty;
        private string _humanDirectInviteLabel = string.Empty;
        private string _humanMatchmakingLabel = string.Empty;

        [Inject]
        public void Construct(IViewAssetProvider assetProvider, IEnumerable<IGameSettingsBinder> binders, ILocalizationService localization)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _binders = binders != null ? new List<IGameSettingsBinder>(binders).ToArray() : System.Array.Empty<IGameSettingsBinder>();
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        protected override void BindViewModel()
        {
            var backButton = _backButton ?? throw new InvalidOperationException("BackButton element is missing in UXML.");
            var titleLabel = _titleLabel ?? throw new InvalidOperationException("TitleLabel element is missing in UXML.");
            var modeOptionsTitle = _modeOptionsTitle ?? throw new InvalidOperationException("ModeOptionsTitle element is missing in UXML.");
            if (_modeOptionsHost == null)
                throw new InvalidOperationException("ModeOptionsHost element is missing in UXML.");
            var opponentTitle = _opponentTitle ?? throw new InvalidOperationException("OpponentTitle element is missing in UXML.");
            var opponentToggle = _opponentToggle ?? throw new InvalidOperationException("OpponentToggle element is missing in UXML.");
            var botSettingsSection = _botSettingsSection ?? throw new InvalidOperationException("BotSettingsSection element is missing in UXML.");
            var botSettingsTitle = _botSettingsTitle ?? throw new InvalidOperationException("BotSettingsTitle element is missing in UXML.");
            var difficultyChips = _difficultyChips ?? throw new InvalidOperationException("DifficultyChips element is missing in UXML.");
            var humanSettingsSection = _humanSettingsSection ?? throw new InvalidOperationException("HumanSettingsSection element is missing in UXML.");
            var humanSettingsTitle = _humanSettingsTitle ?? throw new InvalidOperationException("HumanSettingsTitle element is missing in UXML.");
            var humanKindRadio = _humanKindRadio ?? throw new InvalidOperationException("HumanKindRadio element is missing in UXML.");
            var playerIdInput = _playerIdInput ?? throw new InvalidOperationException("PlayerIdInput element is missing in UXML.");
            var onlinePanel = _onlinePanel ?? throw new InvalidOperationException("OnlinePanel element is missing in UXML.");
            var sessionIdLabel = _sessionIdLabel ?? throw new InvalidOperationException("SessionIdLabel element is missing in UXML.");
            var sessionIdValue = _sessionIdValue ?? throw new InvalidOperationException("SessionIdValue element is missing in UXML.");
            var copySessionIdButton = _copySessionIdButton ?? throw new InvalidOperationException("CopySessionIdButton element is missing in UXML.");
            var becomeHostButton = _becomeHostButton ?? throw new InvalidOperationException("BecomeHostButton element is missing in UXML.");
            var onlineStatusLabel = _onlineStatusLabel ?? throw new InvalidOperationException("OnlineStatusLabel element is missing in UXML.");
            var onlineCountdownLabel = _onlineCountdownLabel ?? throw new InvalidOperationException("OnlineCountdownLabel element is missing in UXML.");
            var cancelButton = _cancelButton ?? throw new InvalidOperationException("CancelButton element is missing in UXML.");
            var startButton = _startButton ?? throw new InvalidOperationException("StartButton element is missing in UXML.");

            AddDisposable(ViewModel.ModeOptionsTitle.Subscribe(text => modeOptionsTitle.text = text));
            AddDisposable(ViewModel.OpponentSectionTitle.Subscribe(text => opponentTitle.text = text));
            AddDisposable(ViewModel.BotDifficultyTitle.Subscribe(text => botSettingsTitle.text = text));
            AddDisposable(ViewModel.HumanSettingsTitle.Subscribe(text => humanSettingsTitle.text = text));

            AddDisposable(ViewModel.BackButtonText.Subscribe(text => backButton.text = text));
            AddDisposable(ViewModel.CancelButtonText.Subscribe(text => cancelButton.text = text));
            AddDisposable(ViewModel.StartButtonText.Subscribe(text => startButton.text = text));

            AddDisposable(ViewModel.OpponentBotText.Subscribe(text =>
            {
                _botLabel = text ?? string.Empty;
                opponentToggle.SetLabels(_botLabel, _humanLabel);
            }));

            AddDisposable(ViewModel.OpponentHumanText.Subscribe(text =>
            {
                _humanLabel = text ?? string.Empty;
                opponentToggle.SetLabels(_botLabel, _humanLabel);
            }));

            SyncOpponentToggle(ViewModel.OpponentType.CurrentValue);

            AddDisposable(ViewModel.OpponentType.Subscribe(SyncOpponentToggle));

            void OnOpponentToggleChanged(int index)
            {
                var type = index == 0 ? OpponentType.Bot : OpponentType.Human;
                ViewModel.SetOpponentType(type);
            }

            opponentToggle.SelectedIndexChanged += OnOpponentToggleChanged;
            AddDisposable(Disposable.Create(() => opponentToggle.SelectedIndexChanged -= OnOpponentToggleChanged));

            BindText(ViewModel.ModeTitleText, titleLabel);

            var isBlocking = ViewModel.Error.Select(static error => error != null && error.IsBlocking);
            var canStart = Observable.CombineLatest(
                ViewModel.CanStart,
                ViewModel.IsBusy,
                isBlocking,
                static (isAllowed, isBusy, blocked) => isAllowed && !isBusy && !blocked);

            BindEnabled(canStart, startButton);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), backButton);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), opponentToggle);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), difficultyChips);
            BindEnabled(ViewModel.IsBusy.Select(static isBusy => !isBusy), humanKindRadio);
            BindEnabled(Observable.CombineLatest(
                    ViewModel.IsBusy,
                    ViewModel.IsModeOptionsEnabled,
                    static (isBusy, isModeOptionsEnabled) => !isBusy && isModeOptionsEnabled),
                playerIdInput);
            BindEnabled(Observable.CombineLatest(
                    ViewModel.IsBusy,
                    ViewModel.IsModeOptionsEnabled,
                    static (isBusy, isModeOptionsEnabled) => !isBusy && isModeOptionsEnabled),
                _modeOptionsHost);

            BindVisibility(ViewModel.IsBotSettingsVisible, botSettingsSection);
            BindVisibility(ViewModel.IsHumanSettingsVisible, humanSettingsSection);
            BindVisibility(ViewModel.IsPlayerIdInputVisible, playerIdInput);

            AddDisposable(backButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestBack()));
            AddDisposable(startButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestStart()));
            AddDisposable(cancelButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCancel()));

            if (_errorLabel != null)
            {
                AddDisposable(ViewModel.InlineErrorText.Subscribe(text =>
                {
                    _errorLabel.text = text ?? string.Empty;
                    _errorLabel.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
                }));
            }

            BindErrorOverlay();

            AddDisposable(ViewModel.DifficultyItems
                .Subscribe(items =>
                {
                    difficultyChips.SetItems(items);
                    difficultyChips.SetSelectedIdWithoutNotify(ViewModel.SelectedDifficultyId.CurrentValue);
                }));

            AddDisposable(ViewModel.SelectedDifficultyId
                .Subscribe(id => difficultyChips.SetSelectedIdWithoutNotify(id)));

            void OnDifficultySelected(string id) => ViewModel.SetBotDifficultyId(id);

            difficultyChips.SelectedIdChanged += OnDifficultySelected;
            AddDisposable(Disposable.Create(() => difficultyChips.SelectedIdChanged -= OnDifficultySelected));

            AddDisposable(ViewModel.HumanLocalText.Subscribe(text =>
            {
                _humanLocalLabel = text ?? string.Empty;
                UpdateHumanKindOptions();
            }));

            AddDisposable(ViewModel.HumanDirectInviteText.Subscribe(text =>
            {
                _humanDirectInviteLabel = text ?? string.Empty;
                UpdateHumanKindOptions();
            }));

            AddDisposable(ViewModel.HumanMatchmakingText.Subscribe(text =>
            {
                _humanMatchmakingLabel = text ?? string.Empty;
                UpdateHumanKindOptions();
            }));

            UpdateHumanKindOptions();
            humanKindRadio.SetSelectedKindWithoutNotify(ViewModel.HumanOpponentKind.CurrentValue);

            AddDisposable(ViewModel.HumanOpponentKind
                .Subscribe(kind => humanKindRadio.SetSelectedKindWithoutNotify(kind)));

            void OnHumanKindSelected(HumanOpponentKind kind) => ViewModel.SetHumanOpponentKind(kind);

            humanKindRadio.SelectedKindChanged += OnHumanKindSelected;
            AddDisposable(Disposable.Create(() => humanKindRadio.SelectedKindChanged -= OnHumanKindSelected));

            AddDisposable(ViewModel.PlayerIdLabelText.Subscribe(text => playerIdInput.SetLabel(text)));
            AddDisposable(ViewModel.TargetPlayerId.Subscribe(id => playerIdInput.SetValueWithoutNotify(id)));
            AddDisposable(ViewModel.PlayerIdErrorText.Subscribe(error => playerIdInput.SetError(error)));

            AddDisposable(ViewModel.OnlinePanelVisible.Subscribe(isVisible =>
                onlinePanel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None));
            AddDisposable(ViewModel.SessionIdLabelText.Subscribe(text => sessionIdLabel.text = text));
            AddDisposable(ViewModel.VisibleSessionId.Subscribe(text => sessionIdValue.text = text));
            AddDisposable(ViewModel.CopySessionIdButtonText.Subscribe(text => copySessionIdButton.text = text));
            AddDisposable(ViewModel.BecomeHostButtonText.Subscribe(text => becomeHostButton.text = text));
            AddDisposable(ViewModel.OnlineStatusText.Subscribe(text =>
            {
                onlineStatusLabel.text = text ?? string.Empty;
                onlineStatusLabel.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }));
            AddDisposable(ViewModel.OnlineCountdownText.Subscribe(text =>
            {
                onlineCountdownLabel.text = text ?? string.Empty;
                onlineCountdownLabel.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }));
            BindEnabled(ViewModel.CanCopySessionId, copySessionIdButton);
            BindEnabled(ViewModel.CanBecomeHost, becomeHostButton);

            void OnPlayerIdChanged(string value) => ViewModel.SetTargetPlayerId(value);

            playerIdInput.ValueChanged += OnPlayerIdChanged;
            AddDisposable(Disposable.Create(() => playerIdInput.ValueChanged -= OnPlayerIdChanged));

            AddDisposable(copySessionIdButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestCopySessionId()));
            AddDisposable(becomeHostButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestBecomeHost()));

            AddDisposable(ViewModel.ActiveSettings
                .Subscribe(presentation => LoadSettingsSafeAsync(presentation).Forget(ex => GameLog.Exception(ex))));
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

        protected override void OnResetForPool()
        {
            CleanupLoadedSettings();
            base.OnResetForPool();
        }

        protected override void OnDestroy()
        {
            CleanupLoadedSettings();
            base.OnDestroy();
        }

        private void SyncOpponentToggle(OpponentType opponentType)
        {
            var toggle = _opponentToggle;
            if (toggle == null)
                return;

            var index = opponentType == OpponentType.Bot ? 0 : 1;
            toggle.SetSelectedIndexWithoutNotify(index);
        }

        private void UpdateHumanKindOptions()
        {
            var humanKindRadio = _humanKindRadio;
            if (humanKindRadio == null)
                return;

            var items = new[]
            {
                new HumanKindRadioItem(HumanOpponentKind.Local, _humanLocalLabel),
                new HumanKindRadioItem(HumanOpponentKind.DirectInvite, _humanDirectInviteLabel),
                new HumanKindRadioItem(HumanOpponentKind.Matchmaking, _humanMatchmakingLabel, isEnabled: true)
            };

            humanKindRadio.SetItems(items);
            humanKindRadio.SetSelectedKindWithoutNotify(ViewModel.HumanOpponentKind.CurrentValue);
        }

        private async UniTask LoadSettingsSafeAsync(GameSettingsPresentation? presentation)
        {
            CancelPendingLoad();
            CleanupCurrentSettings();

            var version = Interlocked.Increment(ref _loadVersion);

            if (presentation == null)
                return;

            if (_assetProvider == null)
            {
                GameLog.Error("[MatchSetupView] IViewAssetProvider is not available.");
                return;
            }

            var cts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _loadCts, cts);
            if (previousCts != null)
            {
                try
                {
                    previousCts.Cancel();
                }
                finally
                {
                    previousCts.Dispose();
                }
            }

            try
            {
                var lease = await _assetProvider.LoadVisualTreeAsync(presentation.UxmlAssetKey, cts.Token);

                if (version != Volatile.Read(ref _loadVersion))
                {
                    lease.Dispose();
                    return;
                }

                _currentLease = lease;

                var instance = _currentLease.Asset.CloneTree();
                _modeOptionsHost?.Add(instance);

                _subBinding = BindSubViewModel(instance, presentation.ViewModel);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
            finally
            {
                cts.Dispose();
                Interlocked.CompareExchange(ref _loadCts, null, cts);
            }
        }

        private IDisposable BindSubViewModel(VisualElement root, IGameSettingsViewModel viewModel)
        {
            var disposables = new CompositeDisposable();

            var bound = false;
            for (var i = 0; i < _binders.Length; i++)
            {
                var binder = _binders[i];
                if (binder == null || !binder.CanBind(viewModel))
                    continue;

                binder.Bind(root, viewModel, disposables);
                bound = true;
                break;
            }

            if (!bound)
                GameLog.Warning($"[MatchSetupView] No binder registered for settings VM type {viewModel.GetType().Name}.");

            return disposables;
        }

        private void CancelPendingLoad()
        {
            if (_loadCts == null)
                return;

            var cts = Interlocked.Exchange(ref _loadCts, null);
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void CleanupCurrentSettings()
        {
            _subBinding?.Dispose();
            _subBinding = null;

            if (_currentLease != null)
            {
                _currentLease.Dispose();
                _currentLease = null;
            }

            _modeOptionsHost?.Clear();
        }

        private void CleanupLoadedSettings()
        {
            CancelPendingLoad();
            CleanupCurrentSettings();
        }
    }
}

#nullable restore
