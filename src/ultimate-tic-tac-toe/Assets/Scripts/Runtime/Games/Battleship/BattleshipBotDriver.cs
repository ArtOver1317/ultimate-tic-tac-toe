#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.Battleship
{
    public interface IBattleshipBotDriver : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsThinking { get; }
        bool IsStarted { get; }
        int BotSlot { get; }

        UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            CancellationToken ct);
    }

    public sealed class BattleshipBotDriver : IBattleshipBotDriver
    {
        private readonly IBattleshipGameplaySnapshotProvider _battleshipSnapshotProvider;
        private readonly IBattleshipGameplayEventStream _battleshipEventStream;
        private readonly IGameplayEventStream _gameplayEventStream;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IBattleshipAutoPlacer _autoPlacer;
        private readonly IOnlineGameplaySessionContextStore _onlineSessionContextStore;
        private readonly BattleshipGameplaySettingsData _settings;

        private readonly ReactiveProperty<bool> _isThinking = new(false);
        private readonly List<int> _unknownTargetIndices = new(100);
        private readonly List<int> _finishTargetIndices = new(16);
        private readonly HashSet<int> _finishTargetIndexSet = new();

        private CompositeDisposable? _subscriptions;
        private CancellationTokenSource? _lifetimeCts;
        private CancellationTokenSource? _turnLoopCts;
        private Random? _random;
        private bool _turnLoopRunning;
        private bool _disposed;

        public BattleshipBotDriver(
            IBattleshipGameplaySnapshotProvider battleshipSnapshotProvider,
            IBattleshipGameplayEventStream battleshipEventStream,
            IGameplayEventStream gameplayEventStream,
            IGameplayCommandSink commandSink,
            IBattleshipAutoPlacer autoPlacer,
            IOnlineGameplaySessionContextStore onlineSessionContextStore,
            BattleshipGameplaySettings battleshipGameplaySettings)
        {
            _battleshipSnapshotProvider = battleshipSnapshotProvider ?? throw new ArgumentNullException(nameof(battleshipSnapshotProvider));
            _battleshipEventStream = battleshipEventStream ?? throw new ArgumentNullException(nameof(battleshipEventStream));
            _gameplayEventStream = gameplayEventStream ?? throw new ArgumentNullException(nameof(gameplayEventStream));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _autoPlacer = autoPlacer ?? throw new ArgumentNullException(nameof(autoPlacer));
            _onlineSessionContextStore = onlineSessionContextStore ?? throw new ArgumentNullException(nameof(onlineSessionContextStore));
            _settings = battleshipGameplaySettings != null
                ? battleshipGameplaySettings.ToValidatedData()
                : BattleshipGameplaySettingsData.Default;
        }

        public ReadOnlyReactiveProperty<bool> IsThinking => _isThinking;
        public bool IsStarted { get; private set; }
        public int BotSlot { get; private set; } = PlayerSlotMapping.SlotO;

        public UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            CancellationToken ct)
        {
            if (_disposed)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Driver disposed."));

            if (IsStarted)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.Failed, "Already started."));

            if (config.GameConfig is not BattleshipConfig)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.UnsupportedConfig, "Not a Battleship config."));

            if (config.OpponentConfig is not BotOpponentConfig)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.NotEnabled));

            if (_onlineSessionContextStore.Snapshot.IsOnlineDirectInvite)
                return UniTask.FromResult(new BotStartResult(BotStartStatus.UnsupportedConfig, "Online direct invite does not use local bot driver."));

            BotSlot = botSlot;
            _random = new Random(unchecked(Environment.TickCount + botSlot * 7919));
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            IsStarted = true;

            Subscribe();
            TrySubmitPlacementIfNeeded();
            TryStartTurnLoop();

            return UniTask.FromResult(new BotStartResult(BotStartStatus.Started));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            CancelTurnLoop();
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;

            _subscriptions?.Dispose();
            _subscriptions = null;

            _unknownTargetIndices.Clear();
            _isThinking.Dispose();
            IsStarted = false;
            _random = null;
        }

        private void Subscribe()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            _battleshipEventStream.PhaseChanged
                .Subscribe(_ =>
                {
                    TrySubmitPlacementIfNeeded();
                    TryStartTurnLoop();
                })
                .AddTo(_subscriptions);

            _gameplayEventStream.CurrentPlayerChanged
                .Subscribe(_ => TryStartTurnLoop())
                .AddTo(_subscriptions);

            _gameplayEventStream.RoundFinished
                .Subscribe(_ => CancelTurnLoop())
                .AddTo(_subscriptions);
        }

        private void TrySubmitPlacementIfNeeded()
        {
            if (!IsStarted || _disposed)
                return;

            var phase = _battleshipSnapshotProvider.Phase;
            if (phase != BattleshipPhase.Placement && phase != BattleshipPhase.Waiting)
                return;

            if (_battleshipSnapshotProvider.IsPlacementConfirmed(BotSlot))
                return;

            var seed = _random?.Next() ?? Environment.TickCount;
            var layout = _autoPlacer.Generate(seed);
            _commandSink.SubmitCommand(new SubmitPlacementCommand(BotSlot, layout));
        }

        private void TryStartTurnLoop()
        {
            if (!IsStarted || _disposed)
                return;

            if (_turnLoopRunning)
                return;

            if (_battleshipSnapshotProvider.Phase != BattleshipPhase.Battle)
                return;

            if (_battleshipSnapshotProvider.CurrentStatus != GameStatus.InProgress)
                return;

            if (_battleshipSnapshotProvider.ActivePlayerSlot != BotSlot)
                return;

            _turnLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts!.Token);
            RunTurnLoopAsync(_turnLoopCts.Token).Forget();
        }

        private async UniTaskVoid RunTurnLoopAsync(CancellationToken ct)
        {
            _turnLoopRunning = true;
            SetThinkingSafe(true);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!IsStarted || _disposed)
                        break;

                    if (_battleshipSnapshotProvider.Phase != BattleshipPhase.Battle)
                        break;

                    if (_battleshipSnapshotProvider.CurrentStatus != GameStatus.InProgress)
                        break;

                    if (_battleshipSnapshotProvider.ActivePlayerSlot != BotSlot)
                        break;

                    if (!TryChooseTarget(out var targetCell))
                        break;

                    _commandSink.SubmitCommand(new MakeMoveCommand(targetCell));

                    if (_settings.BotShotDelaySeconds > 0f)
                    {
                        await UniTask.Delay(
                            TimeSpan.FromSeconds(_settings.BotShotDelaySeconds),
                            DelayType.UnscaledDeltaTime,
                            PlayerLoopTiming.Update,
                            ct);
                    }
                    else
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                SetThinkingSafe(false);
                _turnLoopRunning = false;
                CancelTurnLoop();
            }
        }

        private void SetThinkingSafe(bool value)
        {
            if (_disposed)
                return;

            try
            {
                _isThinking.Value = value;
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool TryChooseTarget(out CellId cellId)
        {
            cellId = default;

            var marks = _battleshipSnapshotProvider.GetOpponentMarks(BotSlot);
            if (marks == null || marks.Count == 0)
                return false;

            var boardSize = (int)Math.Sqrt(marks.Count);
            if (boardSize <= 0)
                boardSize = 10;

            if (TryChooseFinishTarget(marks, boardSize, out var finishPick))
            {
                cellId = ToCellId(finishPick, boardSize);
                return true;
            }

            _unknownTargetIndices.Clear();
            for (var i = 0; i < marks.Count; i++)
            {
                if (marks[i] == BattleshipCellMark.Unknown)
                    _unknownTargetIndices.Add(i);
            }

            if (_unknownTargetIndices.Count == 0)
                return false;

            var pick = _unknownTargetIndices[(_random?.Next(0, _unknownTargetIndices.Count)).GetValueOrDefault()];
            cellId = ToCellId(pick, boardSize);
            return true;
        }

        private bool TryChooseFinishTarget(IReadOnlyList<BattleshipCellMark> marks, int boardSize, out int targetIndex)
        {
            targetIndex = -1;

            _finishTargetIndices.Clear();
            _finishTargetIndexSet.Clear();

            for (var index = 0; index < marks.Count; index++)
            {
                if (marks[index] != BattleshipCellMark.Hit)
                    continue;

                var row = index / boardSize;
                var col = index % boardSize;

                var hasHorizontalHit = IsHitAt(marks, boardSize, row, col - 1) || IsHitAt(marks, boardSize, row, col + 1);
                if (hasHorizontalHit)
                {
                    TryAddFinishCandidate(marks, boardSize, row, col - 1);
                    TryAddFinishCandidate(marks, boardSize, row, col + 1);
                }

                var hasVerticalHit = IsHitAt(marks, boardSize, row - 1, col) || IsHitAt(marks, boardSize, row + 1, col);
                if (hasVerticalHit)
                {
                    TryAddFinishCandidate(marks, boardSize, row - 1, col);
                    TryAddFinishCandidate(marks, boardSize, row + 1, col);
                }
            }

            if (_finishTargetIndices.Count == 0)
            {
                for (var index = 0; index < marks.Count; index++)
                {
                    if (marks[index] != BattleshipCellMark.Hit)
                        continue;

                    var row = index / boardSize;
                    var col = index % boardSize;

                    TryAddFinishCandidate(marks, boardSize, row - 1, col);
                    TryAddFinishCandidate(marks, boardSize, row + 1, col);
                    TryAddFinishCandidate(marks, boardSize, row, col - 1);
                    TryAddFinishCandidate(marks, boardSize, row, col + 1);
                }
            }

            if (_finishTargetIndices.Count == 0)
                return false;

            var pick = (_random?.Next(0, _finishTargetIndices.Count)).GetValueOrDefault();
            targetIndex = _finishTargetIndices[pick];
            return true;
        }

        private void TryAddFinishCandidate(IReadOnlyList<BattleshipCellMark> marks, int boardSize, int row, int col)
        {
            if (row < 0 || col < 0 || row >= boardSize || col >= boardSize)
                return;

            var index = (row * boardSize) + col;
            if (index < 0 || index >= marks.Count)
                return;

            if (marks[index] != BattleshipCellMark.Unknown)
                return;

            if (!_finishTargetIndexSet.Add(index))
                return;

            _finishTargetIndices.Add(index);
        }

        private static bool IsHitAt(IReadOnlyList<BattleshipCellMark> marks, int boardSize, int row, int col)
        {
            if (row < 0 || col < 0 || row >= boardSize || col >= boardSize)
                return false;

            var index = (row * boardSize) + col;
            return index >= 0
                   && index < marks.Count
                   && marks[index] == BattleshipCellMark.Hit;
        }

        private static CellId ToCellId(int index, int boardSize)
        {
            var row = index / boardSize;
            var col = index % boardSize;
            return new CellId(row, col);
        }

        private void CancelTurnLoop()
        {
            _turnLoopCts?.Cancel();
            _turnLoopCts?.Dispose();
            _turnLoopCts = null;
        }
    }
}
