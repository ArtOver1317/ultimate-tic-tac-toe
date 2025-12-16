using R3;
using Runtime.Extensions;
using Runtime.Infrastructure.Logging;
using Runtime.UI.Core;
using StripLog;
using UnityEngine.UIElements;

namespace Runtime.UI.MainMenu
{
    public class MainMenuView : UIView<MainMenuViewModel>
    {
        [Core.UxmlElementAttribute("Title")] 
        private Label _titleLabel;
        [Core.UxmlElementAttribute("StartButton")] 
        private Button _startButton;
        [Core.UxmlElementAttribute("ExitButton")] 
        private Button _exitButton;

        protected override void BindViewModel()
        {
            BindText(ViewModel.Title, _titleLabel);
            BindText(ViewModel.StartButtonText, _startButton);
            BindText(ViewModel.ExitButtonText, _exitButton);

            BindEnabled(ViewModel.IsInteractable, _startButton);
            BindEnabled(ViewModel.IsInteractable, _exitButton);

            AddDisposable(_startButton.OnClickAsObservable().Subscribe(_ => OnStartButtonClicked()));
            AddDisposable(_exitButton.OnClickAsObservable().Subscribe(_ => OnExitButtonClicked()));
        }

        internal void OnStartButtonClicked()
        {
            Log.Debug(LogTags.UI, "[MainMenuView] Start button clicked", this);
            ViewModel.RequestStartGame();
        }

        internal void OnExitButtonClicked()
        {
            Log.Debug(LogTags.UI, "[MainMenuView] Exit button clicked", this);
            ViewModel.RequestExit();
        }
    }
}