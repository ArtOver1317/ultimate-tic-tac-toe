#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Matchmaking flow: open/close matchmaking step, bind ViewModel, handle state transitions.
    /// </summary>
    public sealed partial class GameWizardCoordinator
    {
        private async UniTask OpenMatchmakingAsync(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameWizard.GameConfigRequired",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal));
                
                return;
            }

            await TransitionAsync(
                transition: async token =>
                {
                    var viewModel = await _navigator.ReplaceMatchSetupWithMatchmakingAsync(token);
                    
                    if (viewModel == null)
                        throw new InvalidOperationException("Matchmaking ViewModel is not available.");

                    BindMatchmakingViewModel(viewModel, snapshot, token);
                },
                ct: ct);

            _step = WizardStep.Matchmaking;
        }

        private void BindMatchmakingViewModel(MatchmakingViewModel viewModel, GameSessionSnapshot snapshot, CancellationToken ct)
        {
            CleanupMatchmakingBindings();

            _matchmakingViewModel = viewModel;
            _matchmakingSubscriptions = new CompositeDisposable();

            viewModel.CancelRequested
                .Subscribe(_ => CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.BackRequested
                .Subscribe(_ => CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.RetryRequested
                .Subscribe(_ => TryRestartMatchmaking(snapshot, ct))
                .AddTo(_matchmakingSubscriptions);

            viewModel.State
                .Subscribe(state => HandleMatchmakingStateChanged(state, ct).Forget(LogForgetException))
                .AddTo(_matchmakingSubscriptions);

            viewModel.Result
                .Subscribe(UpdateMatchmakingResult)
                .AddTo(_matchmakingSubscriptions);

            UpdateMatchmakingResult(null);

            if (snapshot is { SelectedGameId: not null, GameConfig: not null }) 
                viewModel.BeginSearch(new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig), ct);
        }

        private void TryRestartMatchmaking(GameSessionSnapshot snapshot, CancellationToken ct)
        {
            if (_matchmakingViewModel == null)
                return;

            if (string.IsNullOrWhiteSpace(snapshot.SelectedGameId) || snapshot.GameConfig == null)
            {
                TrySetCurrentError(new WizardError(
                    code: "wizard.mode_config_required",
                    messageKey: "Errors.GameWizard.GameConfigRequired",
                    isBlocking: true,
                    displayType: ErrorDisplayType.Modal));

                CloseMatchmakingToSetupAsync(ct).Forget(LogForgetException);
                return;
            }

            var request = new MatchmakingRequest(snapshot.SelectedGameId, snapshot.GameConfig);
            UpdateMatchmakingResult(null);
            _matchmakingViewModel.BeginSearch(request, ct);
        }

        private async UniTask HandleMatchmakingStateChanged(MatchmakingState state, CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            switch (state)
            {
                case MatchmakingState.Found:
                    await UniTask.Delay(_matchmakingFoundAutoCloseDelay, cancellationToken: ct);
                    await CloseMatchmakingAndStartAsync(ct);
                    break;

                case MatchmakingState.Cancelled:
                    await CloseMatchmakingToSetupAsync(ct);
                    break;
            }
        }

        private async UniTask CloseMatchmakingToSetupAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                await TransitionAsync(
                    transition: _navigator.ReplaceMatchmakingWithMatchSetupAsync,
                    ct: ct);

                CleanupMatchmakingBindings();
                _step = WizardStep.MatchSetup;
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private async UniTask CloseMatchmakingAndStartAsync(CancellationToken ct)
        {
            if (_step != WizardStep.Matchmaking)
                return;

            if (Interlocked.Exchange(ref _matchmakingCloseInProgress, 1) != 0)
                return;

            try
            {
                if (!TryBuildLaunchConfig(out var launchConfig, out var error))
                {
                    if (error != null)
                        TrySetCurrentError(error);

                    await TransitionAsync(
                        transition: _navigator.ReplaceMatchmakingWithMatchSetupAsync,
                        ct: ct);

                    CleanupMatchmakingBindings();
                    _step = WizardStep.MatchSetup;
                    return;
                }

                await TransitionAsync(
                    transition: _navigator.CloseMatchmakingAsync,
                    ct: ct);

                CleanupMatchmakingBindings();

                SetIsSubmitting(true);

                if (launchConfig != null) 
                    PublishGameLaunchRequested(launchConfig);

                try
                {
                    await AbortWizardCoreAsync(AbortReason.GameStarted, awaitProcessingTask: false);
                }
                finally
                {
                    SetIsSubmitting(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
            }
        }

        private void CleanupMatchmakingBindings()
        {
            _matchmakingSubscriptions?.Dispose();
            _matchmakingSubscriptions = null;
            _matchmakingViewModel = null;
            UpdateMatchmakingResult(null);
            Interlocked.Exchange(ref _matchmakingCloseInProgress, 0);
        }

        private void UpdateMatchmakingResult(MatchmakingResult? result) =>
            _session?.Update(s =>
                s.WithMatchmakingResult(result?.MatchId, result?.OpponentId));

        private static void LogForgetException(Exception ex)
        {
            if (ex is OperationCanceledException)
                return;

            GameLog.Exception(ex);
        }
    }
}
