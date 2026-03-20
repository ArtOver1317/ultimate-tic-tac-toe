#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Infrastructure.Logging;
using Runtime.Services.UI.Assets;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    internal sealed class MatchSetupModeSettingsLoader : IDisposable
    {
        private readonly IViewAssetProvider _assetProvider;
        private readonly Func<ModeOptionsHost?> _modeOptionsHostAccessor;
        private readonly IGameSettingsBinder[] _binders;

        private CancellationTokenSource? _loadCts;
        private IAssetLease<VisualTreeAsset>? _currentLease;
        private IDisposable? _subBinding;
        private int _loadVersion;
        private bool _isDisposed;

        public MatchSetupModeSettingsLoader(
            IViewAssetProvider assetProvider,
            Func<ModeOptionsHost?> modeOptionsHostAccessor,
            IGameSettingsBinder[] binders)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _modeOptionsHostAccessor = modeOptionsHostAccessor ?? throw new ArgumentNullException(nameof(modeOptionsHostAccessor));
            _binders = binders;
        }

        public async UniTask LoadAsync(GameSettingsPresentation? presentation)
        {
            if (_isDisposed)
                return;

            CancelPendingLoad();
            CleanupCurrentSettings();

            var version = ++_loadVersion;

            if (presentation == null)
                return;

            var cts = ReplaceLoadCancellation();

            try
            {
                await LoadAndApplySettingsAsync(presentation, version, cts);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
            finally
            {
                if (_loadCts == cts)
                {
                    _loadCts = null;
                    cts.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            CancelPendingLoad();
            CleanupCurrentSettings();
        }

        private CancellationTokenSource ReplaceLoadCancellation()
        {
            var cts = new CancellationTokenSource();
            var previousCts = _loadCts;
            _loadCts = cts;

            if (previousCts != null)
            {
                try
                {
                    previousCts.Cancel();
                }
                finally
                {
                    previousCts.Dispose();
                }
            }

            return cts;
        }

        private async UniTask LoadAndApplySettingsAsync(
            GameSettingsPresentation presentation,
            int version,
            CancellationTokenSource cts)
        {
            var lease = await _assetProvider.LoadVisualTreeAsync(presentation.UxmlAssetKey, cts.Token);

            if (version != _loadVersion)
            {
                lease.Dispose();
                return;
            }

            ApplyLoadedSettings(lease, presentation.ViewModel);
        }

        private void ApplyLoadedSettings(IAssetLease<VisualTreeAsset> lease, IGameSettingsViewModel viewModel)
        {
            var modeOptionsHost = _modeOptionsHostAccessor();

            if (modeOptionsHost == null)
            {
                lease.Dispose();
                return;
            }

            _currentLease = lease;

            var instance = lease.Asset.CloneTree();
            modeOptionsHost.Add(instance);
            _subBinding = BindSubViewModel(instance, viewModel);
        }

        private IDisposable BindSubViewModel(VisualElement root, IGameSettingsViewModel viewModel)
        {
            var disposables = new CompositeDisposable();
            var bound = false;

            for (var i = 0; i < _binders.Length; i++)
            {
                var binder = _binders[i];

                if (!binder.CanBind(viewModel))
                    continue;

                binder.Bind(root, viewModel, disposables);
                bound = true;
                break;
            }

            if (!bound)
                GameLog.Warning($"[MatchSetupModeSettingsLoader] No binder registered for settings VM type {viewModel.GetType().Name}.");

            return disposables;
        }

        private void CancelPendingLoad()
        {
            if (_loadCts == null)
                return;

            var cts = _loadCts;
            _loadCts = null;

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void CleanupCurrentSettings()
        {
            _subBinding?.Dispose();
            _subBinding = null;

            if (_currentLease != null)
            {
                _currentLease.Dispose();
                _currentLease = null;
            }

            _modeOptionsHostAccessor()?.Clear();
        }
    }
}
