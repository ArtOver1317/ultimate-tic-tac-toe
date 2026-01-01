using R3;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.UI.MainMenu
{
    public class MainMenuViewModel : BaseViewModel
    {
        private readonly ILocalizationService _localization;
        private readonly ReactiveProperty<string> _title = new();
        private readonly ReactiveProperty<string> _startButtonText = new();
        private readonly ReactiveProperty<string> _exitButtonText = new();
        private readonly ReactiveProperty<bool> _isInteractable = new(true);
        private readonly Subject<Unit> _startGameRequested = new();
        private readonly Subject<Unit> _exitRequested = new();

        private bool _isInitialized;

        public ReadOnlyReactiveProperty<string> Title => _title;
        public ReadOnlyReactiveProperty<string> StartButtonText => _startButtonText;
        public ReadOnlyReactiveProperty<string> ExitButtonText => _exitButtonText;
        public ReadOnlyReactiveProperty<bool> IsInteractable => _isInteractable;
        public Observable<Unit> StartGameRequested => _startGameRequested;
        public Observable<Unit> ExitRequested => _exitRequested;

        public MainMenuViewModel(ILocalizationService localization) => 
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

        public override void Initialize()
        {
            if (_isInitialized)
                return;

            AddDisposable(_localization
                .Observe(TextTableId.UI, new TextKey("MainMenu.Title"))
                .Subscribe(text => _title.Value = text));

            AddDisposable(_localization
                .Observe(TextTableId.UI, new TextKey("MainMenu.StartButton"))
                .Subscribe(text => _startButtonText.Value = text));

            AddDisposable(_localization
                .Observe(TextTableId.UI, new TextKey("MainMenu.ExitButton"))
                .Subscribe(text => _exitButtonText.Value = text));

            _isInitialized = true;
        }

        public void SetInteractable(bool isInteractable) => _isInteractable.Value = isInteractable;

        public void RequestStartGame() => _startGameRequested.OnNext(Unit.Default);

        public void RequestExit() => _exitRequested.OnNext(Unit.Default);

        protected override void OnDispose()
        {
            _startGameRequested?.Dispose();
            _exitRequested?.Dispose();
            _title?.Dispose();
            _startButtonText?.Dispose();
            _exitButtonText?.Dispose();
            _isInteractable?.Dispose();
        }
    }
}