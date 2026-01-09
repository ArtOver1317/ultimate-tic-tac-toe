using R3;
using Runtime.Extensions;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.Settings
{
    public sealed class SettingsView : UIView<SettingsViewModel>
    {
        [Core.UxmlElementAttribute("Title")]
        private Label _titleLabel;

        [Core.UxmlElementAttribute("LanguageButton")] 
        private Button _languageButton;
        
        [Core.UxmlElementAttribute("BackButton")] 
        private Button _backButton;

        protected override void BindViewModel()
        {
            BindText(ViewModel.TitleText, _titleLabel);
            BindText(ViewModel.LanguageButtonText, _languageButton);
            BindText(ViewModel.BackButtonText, _backButton);

            AddDisposable(_languageButton.OnClickAsObservable().Subscribe(_ => ViewModel.OpenLanguageSelection()));
            AddDisposable(_backButton.OnClickAsObservable().Subscribe(_ => ViewModel.Close()));
        }
    }
}
