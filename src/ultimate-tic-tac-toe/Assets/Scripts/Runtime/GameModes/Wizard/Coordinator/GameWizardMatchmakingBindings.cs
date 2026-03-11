#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardMatchmakingBindings
    {
        private readonly GameWizardMatchmakingFlow _owner;

        private MatchmakingViewModel? _viewModel;
        private CompositeDisposable? _subscriptions;
        private GameSessionSnapshot? _snapshot;
        private int _terminalModalPendingAck;

        internal GameWizardMatchmakingBindings(GameWizardMatchmakingFlow owner) => 
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        internal bool HasActiveViewModel => _viewModel != null;

        internal MatchmakingViewModel ViewModel =>
            _viewModel ?? throw new InvalidOperationException("Matchmaking ViewModel is not available.");

        internal void NotifySessionStartFailed() =>
            _viewModel?.NotifySessionStartFailed();

        internal void Bind(MatchmakingViewModel viewModel, GameSessionSnapshot snapshot)
        {
            Cleanup();

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _subscriptions = new CompositeDisposable();

            _viewModel.BackRequested
                .Subscribe(_ => HandleBackRequested())
                .AddTo(_subscriptions);

            _viewModel.RetryRequested
                .Subscribe(_ => RetrySearchAsync().Forget(GameWizardMatchmakingFlow.LogForgetException))
                .AddTo(_subscriptions);

            _viewModel.State
                .Subscribe(state => _owner.HandleMatchmakingStateChanged(state, CancellationToken.None)
                    .Forget(GameWizardMatchmakingFlow.LogForgetException))
                .AddTo(_subscriptions);

            _viewModel.Result
                .Subscribe(_owner.UpdateMatchmakingResult)
                .AddTo(_subscriptions);

            _owner.UpdateMatchmakingResult(null);
        }

        internal void Cleanup()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _viewModel = null;
            _snapshot = null;
            Interlocked.Exchange(ref _terminalModalPendingAck, 0);
            _owner.UpdateMatchmakingResult(null);
        }

        internal void MarkTerminalModalPending() =>
            Interlocked.Exchange(ref _terminalModalPendingAck, 1);

        internal string GetTerminalFailureMessageKey(string defaultKey) =>
            _viewModel?.Failure.CurrentValue?.MessageKey ?? defaultKey;

        internal void TryHandleTerminalModalAcknowledge()
        {
            if (Interlocked.Exchange(ref _terminalModalPendingAck, 0) == 0)
                return;

            _viewModel?.AcknowledgeTerminalModal();
            _owner.CloseMatchmakingToSetupAsync(CancellationToken.None).Forget(GameWizardMatchmakingFlow.LogForgetException);
        }

        private void HandleBackRequested()
        {
            if (_viewModel == null)
                return;

            var currentState = _viewModel.State.CurrentValue;
            
            if (currentState is MatchmakingState.Searching or MatchmakingState.CancelPending)
                return;

            _owner.CloseMatchmakingToSetupAsync(CancellationToken.None).Forget(GameWizardMatchmakingFlow.LogForgetException);
        }

        private async UniTask RetrySearchAsync()
        {
            if (_snapshot == null)
                return;

            await _owner.RestartMatchmakingAsync(_snapshot, CancellationToken.None);
        }
    }
}