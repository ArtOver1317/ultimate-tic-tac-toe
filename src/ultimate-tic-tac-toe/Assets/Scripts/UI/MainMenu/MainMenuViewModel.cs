using R3;
using UI.Core;

namespace UI.MainMenu
{
    public class MainMenuViewModel : BaseViewModel
    {
        private readonly ReactiveProperty<string> _title = new("Ultimate Tic-Tac-Toe");
        private readonly ReactiveProperty<string> _startButtonText = new("Start Game");
        private readonly ReactiveProperty<string> _exitButtonText = new("Exit");
        private readonly ReactiveProperty<bool> _isInteractable = new(true);

        public Observable<string> Title => _title;
        public Observable<string> StartButtonText => _startButtonText;
        public Observable<string> ExitButtonText => _exitButtonText;
        public Observable<bool> IsInteractable => _isInteractable;
        public Subject<Unit> OnStartGameClicked { get; } = new();
        public Subject<Unit> OnExitClicked { get; } = new();

        public void SetInteractable(bool isInteractable) => _isInteractable.Value = isInteractable;

        protected override void OnDispose()
        {
            OnStartGameClicked?.Dispose();
            OnExitClicked?.Dispose();
            _title?.Dispose();
            _startButtonText?.Dispose();
            _exitButtonText?.Dispose();
            _isInteractable?.Dispose();
        }
    }
}
