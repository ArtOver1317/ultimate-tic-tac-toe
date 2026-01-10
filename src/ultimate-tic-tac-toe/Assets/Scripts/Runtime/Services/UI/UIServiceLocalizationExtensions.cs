using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.Services.UI
{
    public static class UIServiceLocalizationExtensions
    {
        public static async UniTask<TWindow> OpenWithLocalizationPreloadAsync<TWindow, TViewModel>(
            this IUIService uiService,
            ILocalizationService localization,
            IReadOnlyList<TextTableId> tables,
            CancellationToken cancellationToken,
            Action<TViewModel> configureViewModel = null)
            where TWindow : class, IUIView<TViewModel>
            where TViewModel : BaseViewModel
        {
            if (uiService == null)
                throw new ArgumentNullException(nameof(uiService));

            if (localization == null)
                throw new ArgumentNullException(nameof(localization));

            if (tables == null)
                throw new ArgumentNullException(nameof(tables));

            if (tables.Count > 0)
            {
                await localization.PreloadAsync(
                    localization.CurrentLocale.CurrentValue,
                    tables,
                    cancellationToken);
            }

            return configureViewModel == null
                ? uiService.Open<TWindow, TViewModel>()
                : uiService.Open<TWindow, TViewModel>(configureViewModel);
        }

        public static UniTask<TWindow> OpenWithLocalizationPreloadAsync<TWindow, TViewModel>(
            this IUIService uiService,
            ILocalizationService localization,
            CancellationToken cancellationToken,
            params TextTableId[] tables)
            where TWindow : class, IUIView<TViewModel>
            where TViewModel : BaseViewModel
            => OpenWithLocalizationPreloadAsync<TWindow, TViewModel>(uiService, localization, tables, cancellationToken);
    }
}
