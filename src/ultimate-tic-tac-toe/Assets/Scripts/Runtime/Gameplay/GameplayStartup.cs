using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay
{
    public sealed class GameplayStartup : IGameplayStartup
    {
        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly IGameStateMachine _stateMachine;

        public GameplayStartup(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            IGameStateMachine stateMachine)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!_configStore.TryConsume(out var config) || config == null)
            {
                await HandleErrorAsync(GameplayError.InvalidConfig("Launch config not found."), ct);
                return;
            }

            try
            {
                var session = await _gameService.StartMatchAsync(config, ct);
                await _fieldPresenter.BindAsync(session.FieldRenderSpec, ct);
            }
            catch (OperationCanceledException)
            {
                _fieldPresenter.Unbind();
                _gameService.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                var error = MapError(ex);
                Log.Error(LogTags.Infrastructure, $"[GameplayStartup] Failed to start gameplay: {ex}");
                await HandleErrorAsync(error, ct);
            }
        }

        private async UniTask HandleErrorAsync(GameplayError error, CancellationToken ct)
        {
            _fieldPresenter.Unbind();
            _gameService.Dispose();

            Log.Error(LogTags.Infrastructure, $"[GameplayStartup] {error.Code}: {error.Details}");
            await _stateMachine.EnterAsync<LoadMainMenuState>(ct);
        }

        private static GameplayError MapError(Exception ex)
        {
            if (ex is ArgumentException or InvalidOperationException)
                return GameplayError.InvalidConfig(ex.Message);

            return GameplayError.BuildFailed(ex.Message);
        }
    }
}
