using Cysharp.Threading.Tasks;
using Runtime.Localization;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveServiceLocaleStorage : ILocaleStorage
    {
        private const string Section = "locale";

        private readonly ISaveService _saveService;

        public SaveServiceLocaleStorage(ISaveService saveService)
            => _saveService = saveService;

        public UniTask<LocaleId?> LoadAsync()
        {
            var localeCode = _saveService.Load(Section, string.Empty);

            if (string.IsNullOrWhiteSpace(localeCode))
                return UniTask.FromResult<LocaleId?>(null);

            return UniTask.FromResult<LocaleId?>(new LocaleId(localeCode));
        }

        public UniTask SaveAsync(LocaleId locale)
        {
            _saveService.Save(Section, locale.Code);
            return UniTask.CompletedTask;
        }
    }
}