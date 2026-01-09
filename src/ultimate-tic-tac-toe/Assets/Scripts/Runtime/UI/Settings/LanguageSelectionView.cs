using R3;
using Runtime.Extensions;
using Runtime.Localization;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.Settings
{
    public sealed class LanguageSelectionView : UIView<LanguageSelectionViewModel>
    {
        [Core.UxmlElementAttribute("Title")]
        private Label _titleLabel;
        
        [Core.UxmlElementAttribute("BackButton")] 
        private Button _backButton;
        
        [Core.UxmlElementAttribute("Container")] 
        private ScrollView _container;

        private const string _languageButtonClass = "language-button";

        protected override void BindViewModel()
        {
            BindText(ViewModel.TitleText, _titleLabel);
            BindText(ViewModel.BackButtonText, _backButton);

            AddDisposable(_backButton.OnClickAsObservable().Subscribe(_ => ViewModel.Close()));
            
            RenderLanguageList();
        }

        private void RenderLanguageList()
        {
            _container.Clear();

            foreach (var locale in ViewModel.AvailableLocales)
            {
                var button = new Button
                {
                    text = ViewModel.GetNativeName(locale),
                };

                button.AddToClassList(_languageButtonClass);
                button.clicked += () => ViewModel.SelectLocale(locale);
                _container.Add(button);
            }
        }
    }
}
