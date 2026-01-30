#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
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

            var disposables = new CompositeDisposable();
            // Ownership: disposed explicitly in teardown to preserve order
            // (cancel token → dispose subscriptions → reset overlay → dispose CTS).
            var subscriptions = new CompositeDisposable();

            var binderCts = new CancellationTokenSource();
            var binderToken = binderCts.Token;

            disposables.Add(Disposable.Create(() =>
            {
                try
                {
                    binderCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                try
                {
                    subscriptions.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }

                if (PlayerLoopHelper.IsMainThread)
                    overlay.ResetState();

                try
                {
                    binderCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }));

            var okTextStream = localization.Observe(new TextTableId("Common"), new TextKey("Common.Ok"));
            okTextStream.Subscribe(text =>
            {
                MainThreadInvoker.Run(() => overlay.SetModalButtonText(text), binderToken);
            }).AddTo(subscriptions);

            errorSource.Subscribe(error =>
            {
                MainThreadInvoker.Run(() =>
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
                }, binderToken);

                if (error != null && error.DisplayType == ErrorDisplayType.Toast)
                    AcknowledgeToastAfterDelayAsync(error).Forget();
            }).AddTo(subscriptions);

            void OnModalDismissed() => SafeInvoke(acknowledgeError);
            overlay.ModalDismissed += OnModalDismissed;
            subscriptions.Add(Disposable.Create(() => overlay.ModalDismissed -= OnModalDismissed));

            return disposables;

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

        private static UIErrorDisplayType MapDisplayType(ErrorDisplayType displayType) => displayType switch
        {
            ErrorDisplayType.Inline => UIErrorDisplayType.Inline,
            ErrorDisplayType.Toast => UIErrorDisplayType.Toast,
            ErrorDisplayType.Modal => UIErrorDisplayType.Modal,
            _ => UIErrorDisplayType.Inline
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

        private static class MainThreadInvoker
        {
            public static void Run(Action action, CancellationToken ct)
            {
                if (action == null)
                    return;

                if (ct.IsCancellationRequested)
                    return;

                if (PlayerLoopHelper.IsMainThread)
                {
                    action();
                    return;
                }

                RunAsync(action, ct).Forget();
            }

            private static async UniTaskVoid RunAsync(Action action, CancellationToken ct)
            {
                try
                {
                    await UniTask.SwitchToMainThread(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (ct.IsCancellationRequested)
                    return;

                action();
            }
        }
    }
}
