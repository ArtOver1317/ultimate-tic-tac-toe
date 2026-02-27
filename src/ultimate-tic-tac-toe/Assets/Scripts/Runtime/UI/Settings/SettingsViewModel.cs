using R3;
using Runtime.Localization;
using Runtime.UI.Core;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Runtime.UI.Settings
{
    public sealed class SettingsViewModel : BaseViewModel
    {
        private readonly ILocalizationService _localizationService;
        private readonly Subject<Unit> _languageRequest = new();
        private readonly Subject<Unit> _playerNameEditRequest = new();

        public Observable<Unit> LanguageRequest => _languageRequest;
        public Observable<Unit> PlayerNameEditRequest => _playerNameEditRequest;
        
        // Reactive properties for localized strings
        public Observable<string> LanguageButtonText { get; }
        public Observable<string> EditPlayerNameButtonText { get; }
        public Observable<string> BackButtonText { get; }
        public Observable<string> TitleText { get; }

        public SettingsViewModel(ILocalizationService localizationService)
        {
            _localizationService = localizationService;
            
            LanguageButtonText = _localizationService.Observe("Settings", "Settings.Language");
            EditPlayerNameButtonText = _localizationService.Observe("Settings", "Settings.EditPlayerName");
            BackButtonText = _localizationService.Observe("Settings", "Settings.Back");
            TitleText = _localizationService.Observe("Settings", "Settings.Title");
        }

        public UniTask PreloadOnOpenAsync(CancellationToken cancellationToken) =>
            _localizationService.PreloadCurrentLocaleAsync(new TextTableId("Settings"), cancellationToken);

        public override void Initialize()
        {
            base.Initialize();
            // _languageRequest is now readonly and persistent
        }

        public override void Reset()
        {
            // Do NOT dispose _languageRequest here as it's readonly
            base.Reset();
        }

        public void OpenLanguageSelection() => _languageRequest.OnNext(Unit.Default);

        public void OpenPlayerNameEdit() => _playerNameEditRequest.OnNext(Unit.Default);

        public void Close() => RequestClose();

        protected override void OnDispose()
        {
            _languageRequest.OnCompleted();
            _languageRequest.Dispose();
            _playerNameEditRequest.OnCompleted();
            _playerNameEditRequest.Dispose();
        }
    }
}