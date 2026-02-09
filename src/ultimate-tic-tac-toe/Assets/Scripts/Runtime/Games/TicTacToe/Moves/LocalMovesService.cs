using System;
using System.Collections.Generic;
using R3;
using Runtime.Infrastructure.Logging;

using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe.Moves
{
    public sealed class LocalMovesService : ILocalMovesService
    {
        private enum FailSafeLogCode
        {
            TryApplyNotStarted = 0,
            TryApplyInvalidCellId = 1,
            GetCellValueNotStarted = 2,
            GetCellValueInvalidCellId = 3,
            GetAllCellsNotStarted = 4,
            InvalidStartingPlayer = 5,
        }

        private const double _releaseLogIntervalSeconds = 5.0;

        private readonly ReactiveProperty<bool> _isStarted = new(false);
        private readonly ReactiveProperty<PlayerMark> _currentPlayer = new(PlayerMark.None);
        private readonly Subject<CellChangedEvent> _cellChanged = new();
        private readonly Subject<LastMoveChangedEvent> _lastMoveChanged = new();
        private readonly Subject<ClickRejectedEvent> _clickRejected = new();

        private FieldRenderSpec _fieldSpec;
        private PlayerMark[] _cells;
        private int _majorCount;
        private int _minorCount;
        private CellId? _lastMove;

        private bool _disposed;

        private long _lastReleaseWarnTicks;
        private int _releaseWarnSuppressed;

        public ReadOnlyReactiveProperty<bool> IsStarted => _isStarted;
        public ReadOnlyReactiveProperty<PlayerMark> CurrentPlayer => _currentPlayer;

        public Observable<CellChangedEvent> CellChanged => _cellChanged;
        public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
        public Observable<ClickRejectedEvent> ClickRejected => _clickRejected;

        public void Start(LocalMovesConfig config)
        {
            ThrowIfDisposed();

            if (config.Field == null)
                throw new ArgumentException("Config field spec is required.", nameof(config));

            var oldSpec = _fieldSpec;
            var oldCells = _cells;
            var oldMajorCount = _majorCount;
            var oldMinorCount = _minorCount;

            // Restart (Start called while already started) publishes events in this order:
            // 1) CellChanged(..., None) for previously occupied cells (cold path delta)
            // 2) LastMoveChanged(previous, null)
            // 3) CurrentPlayer (and IsStarted stays true)
            // This order is intentionally different from a successful move, which is:
            // CellChanged -> LastMoveChanged -> CurrentPlayer.
            if (_isStarted.Value && oldSpec != null && oldCells != null)
                PublishClearedCellsIfNeeded(oldSpec, oldCells, oldMajorCount, oldMinorCount);

            _fieldSpec = config.Field;

            var startingPlayer = NormalizeStartingPlayer(config.StartingPlayer);
            EnsureCellsAllocated(_fieldSpec);
            Array.Clear(_cells, 0, _cells.Length);

            var previousLastMove = _lastMove;
            _lastMove = null;
            
            if (previousLastMove != null)
                _lastMoveChanged.OnNext(new LastMoveChangedEvent(previousLastMove, null));

            _currentPlayer.Value = startingPlayer;
            _isStarted.Value = true;
        }

        private void PublishClearedCellsIfNeeded(FieldRenderSpec spec, PlayerMark[] cells, int majorCount, int minorCount)
        {
            if (spec == null)
                return;
            
            if (cells == null)
                return;
            
            if (majorCount <= 0 || minorCount <= 0)
                return;

            // Cold path: publish delta events for cells that become empty after restart.
            if (spec.Kind == FieldKind.Classic)
            {
                var size = spec.OuterSize;
                
                for (var x = 0; x < size; x++)
                {
                    for (var y = 0; y < size; y++)
                    {
                        var id = new CellId(x, y);
                        var index = ToIndex(id, minorCount);
                        
                        if ((uint)index >= (uint)cells.Length)
                            continue;

                        if (cells[index] != PlayerMark.None)
                            _cellChanged.OnNext(new CellChangedEvent(id, PlayerMark.None));
                    }
                }

                return;
            }

            for (var major = 0; major < majorCount; major++)
            {
                for (var minor = 0; minor < minorCount; minor++)
                {
                    var index = checked(major * minorCount + minor);
                    
                    if ((uint)index >= (uint)cells.Length)
                        continue;

                    if (cells[index] == PlayerMark.None)
                        continue;

                    var id = new CellId(major, minor);
                    _cellChanged.OnNext(new CellChangedEvent(id, PlayerMark.None));
                }
            }
        }

        public void Stop()
        {
            ThrowIfDisposed();

            var previousLastMove = _lastMove;
            _lastMove = null;
            
            if (previousLastMove != null)
                _lastMoveChanged.OnNext(new LastMoveChangedEvent(previousLastMove, null));

            _isStarted.Value = false;
            _currentPlayer.Value = PlayerMark.None;
        }

        public ApplyClickResult TryApplyLocalClick(CellId cellId)
        {
            ThrowIfDisposed();

            if (!_isStarted.Value)
            {
                Warn(FailSafeLogCode.TryApplyNotStarted);
                return Reject(cellId, ApplyClickResult.NotStarted);
            }

            if (!_fieldSpec.IsValidCellId(cellId))
            {
                Warn(FailSafeLogCode.TryApplyInvalidCellId, cellId: cellId);
                return Reject(cellId, ApplyClickResult.InvalidCellId);
            }

            var index = ToIndex(cellId);
            
            if (_cells[index] != PlayerMark.None)
                return Reject(cellId, ApplyClickResult.CellOccupied);

            var mark = _currentPlayer.Value;
            
            if (mark != PlayerMark.X && mark != PlayerMark.O)
                mark = PlayerMark.X;

            _cells[index] = mark;

            // Strict order for successful move:
            // 1) CellChanged
            // 2) LastMoveChanged
            // 3) CurrentPlayer
            _cellChanged.OnNext(new CellChangedEvent(cellId, mark));

            var previousLastMove = _lastMove;
            _lastMove = cellId;
            _lastMoveChanged.OnNext(new LastMoveChangedEvent(previousLastMove, cellId));

            _currentPlayer.Value = mark == PlayerMark.X ? PlayerMark.O : PlayerMark.X;
            return ApplyClickResult.Applied;
        }

        public PlayerMark GetCellValue(CellId cellId)
        {
            ThrowIfDisposed();

            if (!_isStarted.Value)
            {
                Warn(FailSafeLogCode.GetCellValueNotStarted, cellId: cellId);
                return PlayerMark.None;
            }

            if (!_fieldSpec.IsValidCellId(cellId))
            {
                Warn(FailSafeLogCode.GetCellValueInvalidCellId, cellId: cellId);
                return PlayerMark.None;
            }

            return _cells[ToIndex(cellId)];
        }

        public IReadOnlyList<CellValue> GetAllCells()
        {
            ThrowIfDisposed();

            if (!_isStarted.Value)
            {
                Warn(FailSafeLogCode.GetAllCellsNotStarted);
                return Array.Empty<CellValue>();
            }

            var result = new List<CellValue>(_cells.Length);

            if (_fieldSpec.Kind == FieldKind.Classic)
            {
                var size = _fieldSpec.OuterSize;
                
                for (var x = 0; x < size; x++)
                {
                    for (var y = 0; y < size; y++)
                    {
                        var id = new CellId(x, y);
                        result.Add(new CellValue(id, _cells[ToIndex(id)]));
                    }
                }

                return result;
            }

            for (var major = 0; major < _majorCount; major++)
            {
                for (var minor = 0; minor < _minorCount; minor++)
                {
                    var id = new CellId(major, minor);
                    result.Add(new CellValue(id, _cells[ToIndex(id)]));
                }
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cellChanged.OnCompleted();
            _lastMoveChanged.OnCompleted();
            _clickRejected.OnCompleted();

            _cellChanged.Dispose();
            _lastMoveChanged.Dispose();
            _clickRejected.Dispose();

            _isStarted.Dispose();
            _currentPlayer.Dispose();

            _cells = null;
            _fieldSpec = null;
        }

        private void EnsureCellsAllocated(FieldRenderSpec spec)
        {
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            _majorCount = spec.Kind switch
            {
                FieldKind.Classic => spec.OuterSize,
                FieldKind.Ultimate => checked(spec.OuterSize * spec.OuterSize),
                _ => 0,
            };

            _minorCount = spec.Kind switch
            {
                FieldKind.Classic => spec.OuterSize,
                FieldKind.Ultimate => checked(spec.InnerSize * spec.InnerSize),
                _ => 0,
            };

            var cellCount = checked(_majorCount * _minorCount);
            
            if (_cells == null || _cells.Length != cellCount)
                _cells = new PlayerMark[cellCount];
        }

        private static int ToIndex(CellId id, int minorCount) => checked(id.Major * minorCount + id.Minor);

        private int ToIndex(CellId id) => ToIndex(id, _minorCount);

        private PlayerMark NormalizeStartingPlayer(PlayerMark startingPlayer)
        {
            if (startingPlayer == PlayerMark.X || startingPlayer == PlayerMark.O)
                return startingPlayer;

            Warn(FailSafeLogCode.InvalidStartingPlayer, playerMark: startingPlayer);
            return PlayerMark.X;
        }

        private ApplyClickResult Reject(CellId cellId, ApplyClickResult reason)
        {
            _clickRejected.OnNext(new ClickRejectedEvent(cellId, reason));
            return reason;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalMovesService));
        }

        private void Warn(
            FailSafeLogCode code,
            CellId? cellId = null,
            PlayerMark? playerMark = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GameLog.Warning(BuildFailSafeMessage(code, cellId, playerMark, suppressed: 0));
#else
            if (!TryBeginReleaseWarn(out var suppressed))
            {
                _releaseWarnSuppressed++;
                return;
            }

            GameLog.Warning(BuildFailSafeMessage(code, cellId, playerMark, suppressed));
#endif
        }

        private static string BuildFailSafeMessage(
            FailSafeLogCode code,
            CellId? cellId,
            PlayerMark? playerMark,
            int suppressed)
        {
            var message = code switch
            {
                FailSafeLogCode.TryApplyNotStarted => "[LocalMovesService] TryApplyLocalClick rejected: NotStarted.",
                FailSafeLogCode.TryApplyInvalidCellId => $"[LocalMovesService] TryApplyLocalClick rejected: InvalidCellId. CellId={cellId}",
                FailSafeLogCode.GetCellValueNotStarted => $"[LocalMovesService] GetCellValue ignored: not started. CellId={cellId}",
                FailSafeLogCode.GetCellValueInvalidCellId => $"[LocalMovesService] GetCellValue ignored: invalid cell id. CellId={cellId}",
                FailSafeLogCode.GetAllCellsNotStarted => "[LocalMovesService] GetAllCells ignored: not started.",
                FailSafeLogCode.InvalidStartingPlayer => $"[LocalMovesService] Invalid StartingPlayer '{playerMark}', defaulting to X.",
                _ => "[LocalMovesService] Fail-safe warning.",
            };

            return suppressed > 0
                ? $"{message} (suppressed {suppressed} warnings)"
                : message;
        }

        private bool TryBeginReleaseWarn(out int suppressed)
        {
            suppressed = 0;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedSeconds = (nowTicks - _lastReleaseWarnTicks) / (double)Stopwatch.Frequency;
            if (_lastReleaseWarnTicks != 0 && elapsedSeconds < _releaseLogIntervalSeconds)
                return false;

            suppressed = _releaseWarnSuppressed;
            _releaseWarnSuppressed = 0;
            _lastReleaseWarnTicks = nowTicks;
            return true;
#endif
        }
    }
}
