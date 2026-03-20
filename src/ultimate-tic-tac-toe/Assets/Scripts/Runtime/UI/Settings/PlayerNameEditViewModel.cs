using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.PlayerProfile;
using Runtime.UI.Core;

namespace Runtime.UI.Settings
{
    public sealed class PlayerNameEditViewModel : BaseViewModel
    {
        private readonly IPlayerNameService _playerNameService;
        private readonly ILocalizationService _localization;
        private readonly ReactiveProperty<string> _inputText = new(string.Empty);
        private readonly ReactiveProperty<bool> _isBusy = new(false);
        private readonly ReactiveProperty<WizardError> _error = new(null);

        private string _initialShownValue = string.Empty;
        private bool _isOpened;

        public ReadOnlyReactiveProperty<string> InputText => _inputText;
        public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
        public ReadOnlyReactiveProperty<WizardError> Error => _error;

        public Observable<string> TitleText { get; }
        public Observable<string> ConfirmButtonText { get; }
        public Observable<string> CancelButtonText { get; }

        public PlayerNameEditViewModel(IPlayerNameService playerNameService, ILocalizationService localization)
        {
            _playerNameService = playerNameService ?? throw new ArgumentNullException(nameof(playerNameService));
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));

            TitleText = _localization.Observe(new TextTableId("Settings"), new TextKey("Settings.EditPlayerName"));
            ConfirmButtonText = _localization.Observe(new TextTableId("Common"), new TextKey("Common.Ok"));
            CancelButtonText = _localization.Observe(new TextTableId("Settings"), new TextKey("Settings.Back"));
        }

        public void OnOpen()
        {
            if (_isOpened)
                return;

            _isOpened = true;
            _error.Value = null;
            _isBusy.Value = false;

            _initialShownValue = _playerNameService.Snapshot.CurrentValue.DisplayName;
            _inputText.Value = _initialShownValue;
        }

        public void SetInput(string value)
        {
            _inputText.Value = value ?? string.Empty;
        }

        public void CloseWithoutConfirm() => RequestClose();

        public async UniTask ConfirmAsync(CancellationToken ct)
        {
            if (_isBusy.Value)
                return;

            if (!_isOpened)
                OnOpen();

            if (string.Equals(_inputText.Value, _initialShownValue, StringComparison.Ordinal))
            {
                RequestClose();
                return;
            }

            _isBusy.Value = true;

            try
            {
                ct.ThrowIfCancellationRequested();

                var result = await _playerNameService.TryChangeNameAsync(_inputText.Value, ct);

                if (result.IsSuccess)
                {
                    RequestClose();
                    return;
                }

                var messageKey = string.IsNullOrWhiteSpace(result.ErrorMessageKey)
                    ? "Errors.PlayerProfile.NameInvalidChars"
                    : result.ErrorMessageKey;

                _error.Value = new WizardError(
                    code: "player_profile.name_edit_failed",
                    messageKey: messageKey,
                    isBlocking: false,
                    displayType: ErrorDisplayType.Toast);
            }
            finally
            {
                _isBusy.Value = false;
            }
        }

        public void AcknowledgeError() => _error.Value = null;

        protected override void OnReset()
        {
            _initialShownValue = string.Empty;
            _isOpened = false;
            _inputText.Value = string.Empty;
            _isBusy.Value = false;
            _error.Value = null;
        }

        protected override void OnDispose()
        {
            _inputText.Dispose();
            _isBusy.Dispose();
            _error.Dispose();
        }
    }
}