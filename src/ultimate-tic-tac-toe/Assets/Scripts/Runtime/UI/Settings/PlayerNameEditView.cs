using R3;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.Settings
{
    public sealed class PlayerNameEditView : UIView<PlayerNameEditViewModel>
    {
        private readonly CompositeDisposable _bindDisposables = new();

        [Core.UxmlElementAttribute("Title")]
        private Label _titleLabel;

        [Core.UxmlElementAttribute("NameInput")]
        private TextField _nameInput;

        [Core.UxmlElementAttribute("ConfirmButton")]
        private Button _confirmButton;

        [Core.UxmlElementAttribute("CancelButton")]
        private Button _cancelButton;

        [Core.UxmlElementAttribute("ErrorOverlay")]
        private WizardErrorOverlay _errorOverlay;

        private ILocalizationService _localization;

        public void Construct(ILocalizationService localization) => _localization = localization;

        protected override void BindViewModel()
        {
            _bindDisposables.Clear();

            ViewModel.OnOpen();

            PlayerNameEditBinder.Bind(
                    ViewModel,
                    _localization,
                    _titleLabel,
                    _nameInput,
                    _confirmButton,
                    _cancelButton,
                    _errorOverlay)
                .AddTo(_bindDisposables);
        }

        protected override void OnDestroy()
        {
            _bindDisposables.Dispose();
            base.OnDestroy();
        }

        protected override void OnResetForPool()
        {
            _bindDisposables.Clear();
            base.OnResetForPool();
        }
    }
}