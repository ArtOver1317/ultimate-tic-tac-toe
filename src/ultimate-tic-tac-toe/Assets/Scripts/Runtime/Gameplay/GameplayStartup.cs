using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Infrastructure.Logging;
using Runtime.Gameplay.Moves;
using StripLog;

namespace Runtime.Gameplay
{
    public sealed class GameplayStartup : IGameplayStartup
    {
        private readonly IGameLaunchConfigStore _configStore;
        private readonly IGameService _gameService;
        private readonly IGameplayFieldPresenter _fieldPresenter;
        private readonly ILocalMovesService _localMoves;
        private readonly GameplayMovesBinder _movesBinder;
        private readonly IGameStateMachine _stateMachine;

        public GameplayStartup(
            IGameLaunchConfigStore configStore,
            IGameService gameService,
            IGameplayFieldPresenter fieldPresenter,
            ILocalMovesService localMoves,
            GameplayMovesBinder movesBinder,
            IGameStateMachine stateMachine)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _fieldPresenter = fieldPresenter ?? throw new ArgumentNullException(nameof(fieldPresenter));
            _localMoves = localMoves ?? throw new ArgumentNullException(nameof(localMoves));
            _movesBinder = movesBinder ?? throw new ArgumentNullException(nameof(movesBinder));
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

                var movesConfig = LocalMovesConfigMapper.FromLaunchConfig(config, session.FieldRenderSpec);
                _localMoves.Start(movesConfig);
                _movesBinder.Bind();
            }
            catch (OperationCanceledException)
            {
                _movesBinder.Unbind();
                _localMoves.Stop();
                _fieldPresenter.Unbind();
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
            _movesBinder.Unbind();
            _localMoves.Stop();
            _fieldPresenter.Unbind();

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
