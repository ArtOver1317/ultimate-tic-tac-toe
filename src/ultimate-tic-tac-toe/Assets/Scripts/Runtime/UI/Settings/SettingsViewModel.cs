using R3;
using Runtime.UI.Core;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

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
            
            LanguageButtonText = _localizationService.Observe(TextTableId.Settings, new TextKey("Settings.Language"));
            EditPlayerNameButtonText = _localizationService.Observe(TextTableId.Settings, new TextKey("Settings.EditPlayerName"));
            BackButtonText = _localizationService.Observe(TextTableId.Settings, new TextKey("Settings.Back"));
            TitleText = _localizationService.Observe(TextTableId.Settings, new TextKey("Settings.Title"));
        }

        public UniTask PreloadOnOpenAsync(CancellationToken cancellationToken) =>
            _localizationService.PreloadCurrentLocaleAsync(TextTableId.Settings, cancellationToken);

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