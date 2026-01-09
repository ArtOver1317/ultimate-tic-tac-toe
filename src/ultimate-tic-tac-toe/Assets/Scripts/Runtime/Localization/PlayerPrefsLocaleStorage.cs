using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Runtime.Localization
{
    public sealed class PlayerPrefsLocaleStorage : ILocaleStorage
    {
        private const string _localeKey = "Runtime.Localization.Locale";

        public UniTask<LocaleId?> LoadAsync()
        {
            if (!PlayerPrefs.HasKey(_localeKey))
                return UniTask.FromResult<LocaleId?>(null);

            var code = PlayerPrefs.GetString(_localeKey, string.Empty);
            
            return string.IsNullOrWhiteSpace(code) ? UniTask.FromResult<LocaleId?>(null) : UniTask.FromResult<LocaleId?>(new LocaleId(code));
        }

        public UniTask SaveAsync(LocaleId locale)
        {
            PlayerPrefs.SetString(_localeKey, locale.Code);
            PlayerPrefs.Save();
            return UniTask.CompletedTask;
        }
    }
}