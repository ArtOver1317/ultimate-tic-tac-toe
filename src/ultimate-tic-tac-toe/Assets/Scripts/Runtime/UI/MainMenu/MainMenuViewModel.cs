using R3;
using Runtime.UI.Core;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Runtime.UI.MainMenu
{
    public sealed class MainMenuViewModel : BaseViewModel
    {
        private const string _mainMenuTableName = "MainMenu";

        private readonly ILocalizationService _localization;
        private readonly ReactiveProperty<bool> _isInteractable = new(true);
        private readonly Subject<Unit> _startGameRequested = new();
        private readonly Subject<Unit> _statisticsRequested = new();
        private readonly Subject<Unit> _exitRequested = new();
        private readonly Subject<Unit> _settingsRequested = new();

        public Observable<string> Title { get; }
        public Observable<string> StartButtonText { get; }
        public Observable<string> StatisticsButtonText { get; }
        public Observable<string> SettingsButtonText { get; }
        public Observable<string> ExitButtonText { get; }
        public ReadOnlyReactiveProperty<bool> IsInteractable => _isInteractable;
        public Observable<Unit> StartGameRequested => _startGameRequested;
        public Observable<Unit> StatisticsRequested => _statisticsRequested;
        public Observable<Unit> ExitRequested => _exitRequested;
        public Observable<Unit> SettingsRequested => _settingsRequested;

        public MainMenuViewModel(ILocalizationService localization)
        {
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

            var table = new TextTableId(_mainMenuTableName);
            Title = localization.Observe(table, new TextKey("MainMenu.Title"));
            StartButtonText = localization.Observe(table, new TextKey("MainMenu.StartButton"));
            StatisticsButtonText = localization.Observe(table, new TextKey("MainMenu.Statistics"));
            SettingsButtonText = localization.Observe(table, new TextKey("MainMenu.Settings"));
            ExitButtonText = localization.Observe(table, new TextKey("MainMenu.ExitButton"));
        }

        public UniTask PreloadOnOpenAsync(CancellationToken cancellationToken) =>
            _localization.PreloadCurrentLocaleAsync(new TextTableId(_mainMenuTableName), cancellationToken);

        public void SetInteractable(bool isInteractable) => _isInteractable.Value = isInteractable;

        public void RequestStartGame() => _startGameRequested.OnNext(Unit.Default);

        public void RequestStatistics() => _statisticsRequested.OnNext(Unit.Default);

        public void RequestExit() => _exitRequested.OnNext(Unit.Default);

        public void RequestSettings() => _settingsRequested.OnNext(Unit.Default);

        protected override void OnDispose()
        {
            _startGameRequested?.Dispose();
            _statisticsRequested?.Dispose();
            _exitRequested?.Dispose();
            _settingsRequested?.Dispose();
            _isInteractable?.Dispose();
        }
    }
}