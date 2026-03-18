using Cysharp.Threading.Tasks;
using Runtime.Localization;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveServiceLocaleStorage : ILocaleStorage
    {
        private const string _section = "locale";

        private readonly ISaveService _saveService;

        public SaveServiceLocaleStorage(ISaveService saveService)
            => _saveService = saveService;

        public UniTask<LocaleId?> LoadAsync()
        {
            var localeCode = _saveService.Load(_section, string.Empty);

            return string.IsNullOrWhiteSpace(localeCode) 
                ? UniTask.FromResult<LocaleId?>(null) 
                : UniTask.FromResult<LocaleId?>(new LocaleId(localeCode));
        }

        public UniTask SaveAsync(LocaleId locale)
        {
            _saveService.Save(_section, locale.Code);
            return UniTask.CompletedTask;
        }
    }
}