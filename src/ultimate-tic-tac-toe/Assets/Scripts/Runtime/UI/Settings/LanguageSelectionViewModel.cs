using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization;
using Runtime.UI.Core;
using StripLog;
using Runtime.Infrastructure.Logging;

namespace Runtime.UI.Settings
{
    public sealed class LanguageSelectionViewModel : BaseViewModel
    {
        private readonly ILocalizationService _localization;
        private CancellationTokenSource _localeChangeCts;
        
        public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _localization.CurrentLocale;
        public IReadOnlyList<LocaleId> AvailableLocales { get; private set; }
        
        // Localized strings
        public Observable<string> TitleText { get; }
        public Observable<string> BackButtonText { get; }

        public LanguageSelectionViewModel(ILocalizationService localization)
        {
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));
            
            TitleText = _localization.Observe("Settings", "Settings.SelectLanguage");
            BackButtonText = _localization.Observe("Settings", "Settings.Back");
        }

        public override void Initialize()
        {
            base.Initialize();
            AvailableLocales = _localization.GetSupportedLocales();
        }

        public override void Reset()
        {
            CancelLocaleChange();
            base.Reset();
        }
        
        protected override void OnDispose()
        {
            CancelLocaleChange();
            _localeChangeCts?.Dispose();
            base.OnDispose();
        }

        public void SelectLocale(LocaleId locale)
        {
             CancelLocaleChange();
             _localeChangeCts = new CancellationTokenSource();
             
             SetLocaleAsync(locale, _localeChangeCts.Token).Forget();
        }

        private void CancelLocaleChange()
        {
            if (_localeChangeCts != null && !_localeChangeCts.IsCancellationRequested)
            {
                _localeChangeCts.Cancel();
                _localeChangeCts.Dispose();
                _localeChangeCts = null;
            }
        }

        private async UniTaskVoid SetLocaleAsync(LocaleId locale, CancellationToken ct)
        {
            try 
            {
                await _localization.SetLocaleAsync(locale, ct);
            } 
            catch (System.OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (System.Exception ex) 
            {
                Log.Error(LogTags.UI, $"Failed to set locale: {ex}");
            }
        }

        public void Close() => RequestClose();

        public string GetNativeName(LocaleId locale) =>
            // Metadata mapping. Ideally, this could come from a config or the ILocalizationCatalog.
            locale.Code.ToLowerInvariant() switch
            {
                "en-us" => "English",
                "ru-ru" => "Русский",
                "ja-jp" => "日本語",
                _ => locale.Code,
            };
    }
}
