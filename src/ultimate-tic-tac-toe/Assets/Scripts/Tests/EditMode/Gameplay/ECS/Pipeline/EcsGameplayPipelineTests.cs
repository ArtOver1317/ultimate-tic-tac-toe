#nullable enable

using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.ECS;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS.Pipeline
{
    /// <summary>
    /// Full-pipeline ECS gameplay tests for TicTacToe (Classic 3×3).
    /// Uses <see cref="SynchronousEventScheduler"/> for deterministic inline event delivery.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public partial class EcsGameplayPipelineTests
    {
        private CommandQueue _commandQueue = null!;
        private MatchEcsLifecycleService _lifecycle = null!;
        private MatchStateProvider _stateProvider = null!;

        // Collects all events in delivery order for deterministic assertions
        private List<object> _events = null!;
        private CompositeDisposable _subscriptions = null!;

        [SetUp]
        public void SetUp()
        {
            var scheduler = new SynchronousEventScheduler();
            _commandQueue = new CommandQueue();
            var eventSystem = new EventPublishSystem(scheduler);
            var rulesEngine = new Runtime.Games.TicTacToe.Rules.ClassicRulesEngine();
            var registrar = new TicTacToeEcsRegistrar(rulesEngine);
            
            _lifecycle = new MatchEcsLifecycleService(
                new[] { registrar }, _commandQueue, eventSystem);
            
            _stateProvider = new MatchStateProvider(
                _commandQueue, _lifecycle, eventSystem);

            _events = new List<object>();
            _subscriptions = new CompositeDisposable();

            _stateProvider.CellChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.LastMoveChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.CurrentPlayerChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.CommandRejected.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.RoundFinished.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
        }

        [TearDown]
        public void TearDown()
        {
            _subscriptions.Dispose();
            _stateProvider.Dispose();
            _lifecycle.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────

        private void StartMatch()
        {
            var config = new TicTacToeConfig(boardSize: 3);
            var opponent = Substitute.For<IOpponentConfig>();
            var launch = new GameLaunchConfig(TicTacToeEcsRegistrar.TicTacToeGameId, config, opponent);
            _lifecycle.StartMatch(launch);
        }

        private void PlayMove(int major, int minor) =>
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(major, minor)));

        private void ClearEvents() => _events.Clear();

        private List<T> EventsOf<T>() => _events.OfType<T>().ToList();
    }
}
