using R3;
using UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.MainMenu
{
    public class MainMenuView : UIView<MainMenuViewModel>
    {
        [Core.UxmlElement("Title")] 
        private Label _titleLabel;
        [Core.UxmlElement("StartButton")] 
        private Button _startButton;
        [Core.UxmlElement("ExitButton")] 
        private Button _exitButton;

        protected override void BindViewModel()
        {
            BindText(ViewModel.Title, _titleLabel);
            BindText(ViewModel.StartButtonText, _startButton);
            BindText(ViewModel.ExitButtonText, _exitButton);

            BindEnabled(ViewModel.IsInteractable, _startButton);
            BindEnabled(ViewModel.IsInteractable, _exitButton);

            _startButton.clicked += OnStartButtonClicked;
            _exitButton.clicked += OnExitButtonClicked;
        }

        private void OnStartButtonClicked()
        {
            Debug.Log("[MainMenuView] Start button clicked");
            ViewModel.OnStartGameClicked.OnNext(Unit.Default);
        }

        private void OnExitButtonClicked()
        {
            Debug.Log("[MainMenuView] Exit button clicked");
            ViewModel.OnExitClicked.OnNext(Unit.Default);
        }

        protected override void OnDestroy()
        {
            if (_startButton != null)
                _startButton.clicked -= OnStartButtonClicked;
            
            if (_exitButton != null)
                _exitButton.clicked -= OnExitButtonClicked;

            base.OnDestroy();
        }
    }
}

