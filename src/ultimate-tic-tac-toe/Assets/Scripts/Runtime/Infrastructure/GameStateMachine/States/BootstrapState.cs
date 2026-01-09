using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using StripLog;

namespace Runtime.Infrastructure.GameStateMachine.States
{
    public class BootstrapState : IState
    {
        private readonly IGameStateMachine _stateMachine;
        private readonly ILocalizationService _localization;
        private bool _isInitialized;

        public BootstrapState(IGameStateMachine stateMachine, ILocalizationService localization)
        {
            _stateMachine = stateMachine;
            _localization = localization;
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!_isInitialized)
            {
                Log.Info(LogTags.Infrastructure, "[BootstrapState] Initializing game systems...");
                await InitializeLocalizationAsync(cancellationToken);
                _isInitialized = true; // Set AFTER successful initialization
            }

            await _stateMachine.EnterAsync<LoadMainMenuState>(cancellationToken);
        }

        public void Exit() => Log.Debug(LogTags.Infrastructure, "[BootstrapState] Exiting...");

        private async UniTask InitializeLocalizationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _localization.InitializeAsync(cancellationToken);
                Log.Info(LogTags.Infrastructure, $"[BootstrapState] Localization initialized. Active locale: {_localization.CurrentLocale.CurrentValue}");
            }
            catch (System.OperationCanceledException)
            {
                Log.Debug(LogTags.Infrastructure, "[BootstrapState] Localization initialization cancelled");
                throw;
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BootstrapState] Failed to initialize localization: {ex}");
                throw;
            }
        }
    }
}