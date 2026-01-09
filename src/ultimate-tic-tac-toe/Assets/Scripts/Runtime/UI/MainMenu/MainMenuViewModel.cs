using R3;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.UI.MainMenu
{
    public sealed class MainMenuViewModel : BaseViewModel
    {
        private readonly ReactiveProperty<bool> _isInteractable = new(true);
        private readonly Subject<Unit> _startGameRequested = new();
        private readonly Subject<Unit> _exitRequested = new();
        private readonly Subject<Unit> _settingsRequested = new();

        public Observable<string> Title { get; }
        public Observable<string> StartButtonText { get; }
        public Observable<string> SettingsButtonText { get; }
        public Observable<string> ExitButtonText { get; }
        public ReadOnlyReactiveProperty<bool> IsInteractable => _isInteractable;
        public Observable<Unit> StartGameRequested => _startGameRequested;
        public Observable<Unit> ExitRequested => _exitRequested;
        public Observable<Unit> SettingsRequested => _settingsRequested;

        public MainMenuViewModel(ILocalizationService localization)
        {
            if (localization == null)
                throw new System.ArgumentNullException(nameof(localization));

            var table = new TextTableId("MainMenu");
            Title = localization.Observe(table, new TextKey("MainMenu.Title"));
            StartButtonText = localization.Observe(table, new TextKey("MainMenu.StartButton"));
            SettingsButtonText = localization.Observe(table, new TextKey("MainMenu.Settings"));
            ExitButtonText = localization.Observe(table, new TextKey("MainMenu.ExitButton"));
        }

        public void SetInteractable(bool isInteractable) => _isInteractable.Value = isInteractable;

        public void RequestStartGame() => _startGameRequested.OnNext(Unit.Default);

        public void RequestExit() => _exitRequested.OnNext(Unit.Default);

        public void RequestSettings() => _settingsRequested.OnNext(Unit.Default);

        protected override void OnDispose()
        {
            _startGameRequested?.Dispose();
            _exitRequested?.Dispose();
            _settingsRequested?.Dispose();
            _isInteractable?.Dispose();
        }
    }
}