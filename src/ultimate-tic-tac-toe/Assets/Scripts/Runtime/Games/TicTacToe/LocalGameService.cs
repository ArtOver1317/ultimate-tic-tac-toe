using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe
{
    public sealed class LocalGameService : IGameService
    {
        private readonly IGameCatalog _catalog;
        private readonly FieldSpecMapper _mapper;
        private IGameplaySession _activeSession;
        private bool _disposed;

        public LocalGameService(IGameCatalog catalog, FieldSpecMapper mapper)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public UniTask<IGameplaySession> StartMatchAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalGameService));
            
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            
            if (_activeSession != null)
                throw new InvalidOperationException("Match is already started.");

            ct.ThrowIfCancellationRequested();

            var spec = _mapper.Map(config, _catalog);
            ct.ThrowIfCancellationRequested();

            _activeSession = new LocalGameplaySession(spec);
            return UniTask.FromResult(_activeSession);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _activeSession?.Dispose();
            _activeSession = null;
        }

        private sealed class LocalGameplaySession : IGameplaySession
        {
            private bool _disposed;

            public FieldRenderSpec FieldRenderSpec { get; }

            public LocalGameplaySession(FieldRenderSpec spec) => FieldRenderSpec = spec ?? throw new ArgumentNullException(nameof(spec));

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
            }
        }
    }
}
