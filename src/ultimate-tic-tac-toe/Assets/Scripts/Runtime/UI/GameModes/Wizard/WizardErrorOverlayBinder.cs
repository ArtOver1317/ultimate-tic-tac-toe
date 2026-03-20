#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;

namespace Runtime.UI.GameModes.Wizard
{
    public static class WizardErrorOverlayBinder
    {
        public static CompositeDisposable Bind(
            WizardErrorOverlay overlay,
            ILocalizationService localization,
            ReadOnlyReactiveProperty<WizardError?> errorSource,
            Action? acknowledgeError)
        {
            if (overlay == null)
                throw new ArgumentNullException(nameof(overlay));
            
            if (localization == null)
                throw new ArgumentNullException(nameof(localization));
           
            if (errorSource == null)
                throw new ArgumentNullException(nameof(errorSource));

            var subscriptions = new CompositeDisposable();
            var binderCts = new CancellationTokenSource();
            var binderToken = binderCts.Token;
            var disposeHandle = new CompositeDisposable();

            disposeHandle.Add(Disposable.Create(() => DisposeBinding(overlay, binderCts, subscriptions)));

            var okTextStream = localization.Observe(new TextTableId("Common"), new TextKey("Common.Ok"));
            okTextStream.Subscribe(overlay.SetModalButtonText).AddTo(subscriptions);

            errorSource.Subscribe(error =>
            {
                ApplyErrorPresentation(overlay, localization, error);

                if (error is { DisplayType: ErrorDisplayType.Toast })
                    AcknowledgeToastAfterDelayAsync(error).Forget();
            }).AddTo(subscriptions);

            void OnModalDismissed() => SafeInvoke(acknowledgeError);
            overlay.ModalDismissed += OnModalDismissed;
            subscriptions.Add(Disposable.Create(() => overlay.ModalDismissed -= OnModalDismissed));

            return disposeHandle;

            async UniTaskVoid AcknowledgeToastAfterDelayAsync(WizardError expectedError)
            {
                try
                {
                    await UniTask.Delay(WizardErrorOverlayDefaults.ToastDuration, cancellationToken: binderToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!ReferenceEquals(errorSource.CurrentValue, expectedError))
                    return;

                SafeInvoke(acknowledgeError);
            }
        }

        private static void DisposeBinding(
            WizardErrorOverlay overlay,
            CancellationTokenSource binderCts,
            CompositeDisposable subscriptions)
        {
            TryCancel(binderCts);
            TryDispose(subscriptions);

            if (PlayerLoopHelper.IsMainThread)
                overlay.ResetState();

            TryDispose(binderCts);
        }

        private static void ApplyErrorPresentation(
            WizardErrorOverlay overlay,
            ILocalizationService localization,
            WizardError? error)
        {
            if (error == null || error.DisplayType == ErrorDisplayType.Inline)
            {
                overlay.ResetState();
                return;
            }

            var message = WizardErrorMessageResolver.Resolve(localization, error.MessageKey);
            
            var presentation = new UIErrorPresentation(
                error.Code,
                message,
                error.IsBlocking,
                MapDisplayType(error.DisplayType));

            overlay.Present(presentation);
        }

        private static UIErrorDisplayType MapDisplayType(ErrorDisplayType displayType) => displayType switch
        {
            ErrorDisplayType.Inline => UIErrorDisplayType.Inline,
            ErrorDisplayType.Toast => UIErrorDisplayType.Toast,
            ErrorDisplayType.Modal => UIErrorDisplayType.Modal,
            _ => UIErrorDisplayType.Inline,
        };

        private static void SafeInvoke(Action? action)
        {
            if (action == null)
                return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private static void TryCancel(CancellationTokenSource binderCts)
        {
            try
            {
                binderCts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        private static void TryDispose(IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
    }
}