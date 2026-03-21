using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Extensions;
using Runtime.Infrastructure.Logging;
using Runtime.Localization.Contracts;
using Runtime.PlayerProfile;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using StripLog;
using UnityEngine.UIElements;

namespace Runtime.UI.Settings
{
    public static class PlayerNameEditBinder
    {
        public static CompositeDisposable Bind(
            PlayerNameEditViewModel viewModel,
            ILocalizationService localization,
            Label title,
            TextField nameInput,
            Button confirmButton,
            Button cancelButton,
            WizardErrorOverlay errorOverlay)
        {
            ValidateArguments(viewModel, title, nameInput, confirmButton, cancelButton, errorOverlay);

            var disposables = new CompositeDisposable();
            var binderCts = CreateLifetimeLinkedCts(disposables);

            BindTexts(viewModel, title, confirmButton, cancelButton, disposables);
            BindNameInput(viewModel, nameInput, confirmButton, disposables);
            BindActions(viewModel, confirmButton, cancelButton, binderCts.Token, disposables);
            BindErrorOverlay(viewModel, localization, errorOverlay, disposables);

            return disposables;
        }

        private static void ValidateArguments(
            PlayerNameEditViewModel viewModel,
            Label title,
            TextField nameInput,
            Button confirmButton,
            Button cancelButton,
            WizardErrorOverlay errorOverlay)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));

            if (title == null)
                throw new ArgumentNullException(nameof(title));

            if (nameInput == null)
                throw new ArgumentNullException(nameof(nameInput));

            if (confirmButton == null)
                throw new ArgumentNullException(nameof(confirmButton));

            if (cancelButton == null)
                throw new ArgumentNullException(nameof(cancelButton));

            if (errorOverlay == null)
                throw new ArgumentNullException(nameof(errorOverlay));
        }

        private static CancellationTokenSource CreateLifetimeLinkedCts(CompositeDisposable disposables)
        {
            var binderCts = new CancellationTokenSource();

            disposables.Add(Disposable.Create(() =>
            {
                try
                {
                    binderCts.Cancel();
                }
                catch (ObjectDisposedException) { }

                binderCts.Dispose();
            }));

            return binderCts;
        }

        private static void BindTexts(
            PlayerNameEditViewModel viewModel,
            Label title,
            Button confirmButton,
            Button cancelButton,
            CompositeDisposable disposables)
        {
            viewModel.TitleText
                .Subscribe(text => title.text = text ?? string.Empty)
                .AddTo(disposables);

            viewModel.ConfirmButtonText
                .Subscribe(text => confirmButton.text = text ?? string.Empty)
                .AddTo(disposables);

            viewModel.CancelButtonText
                .Subscribe(text => cancelButton.text = text ?? string.Empty)
                .AddTo(disposables);
        }

        private static void BindNameInput(
            PlayerNameEditViewModel viewModel,
            TextField nameInput,
            Button confirmButton,
            CompositeDisposable disposables)
        {
            nameInput.maxLength = PlayerNameValidator.MaxLength;

            viewModel.InputText
                .Subscribe(value => nameInput.SetValueWithoutNotify(value ?? string.Empty))
                .AddTo(disposables);

            viewModel.IsBusy
                .Subscribe(isBusy => confirmButton.SetEnabled(!isBusy))
                .AddTo(disposables);

            EventCallback<ChangeEvent<string>> onInputChanged = evt => viewModel.SetInput(evt.newValue);
            nameInput.RegisterValueChangedCallback(onInputChanged);
            disposables.Add(Disposable.Create(() => nameInput.UnregisterValueChangedCallback(onInputChanged)));
        }

        private static void BindActions(
            PlayerNameEditViewModel viewModel,
            Button confirmButton,
            Button cancelButton,
            CancellationToken binderToken,
            CompositeDisposable disposables)
        {
            confirmButton.OnClickAsObservable()
                .Subscribe(_ => viewModel.ConfirmAsync(binderToken).Forget(OnConfirmFailed))
                .AddTo(disposables);

            cancelButton.OnClickAsObservable()
                .Subscribe(_ => viewModel.CloseWithoutConfirm())
                .AddTo(disposables);
        }

        private static void BindErrorOverlay(
            PlayerNameEditViewModel viewModel,
            ILocalizationService localization,
            WizardErrorOverlay errorOverlay,
            CompositeDisposable disposables)
        {
            if (localization == null)
                return;

            WizardErrorOverlayBinder.Bind(
                    overlay: errorOverlay,
                    localization: localization,
                    errorSource: viewModel.Error,
                    acknowledgeError: viewModel.AcknowledgeError)
                .AddTo(disposables);
        }

        private static void OnConfirmFailed(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            Log.Exception(ex, LogTags.UI);
        }
    }
}