#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Games.Battleship
{
    public readonly struct BattleshipPlacementMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public string LayoutPayload { get; }
        public long ClientTick { get; }

        public BattleshipPlacementMessage(Guid commandId, string senderUserId, string layoutPayload, long clientTick)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (string.IsNullOrWhiteSpace(layoutPayload))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(layoutPayload));

            CommandId = commandId;
            SenderUserId = senderUserId;
            LayoutPayload = layoutPayload;
            ClientTick = clientTick;
        }
    }

    public readonly struct BattleshipPlacementTimeoutMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public int PlayerSlot { get; }
        public int AutoPlaceSeed { get; }
        public long ClientTick { get; }

        public BattleshipPlacementTimeoutMessage(
            Guid commandId,
            string senderUserId,
            int playerSlot,
            int autoPlaceSeed,
            long clientTick)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (playerSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(playerSlot), playerSlot, "Value cannot be negative.");

            CommandId = commandId;
            SenderUserId = senderUserId;
            PlayerSlot = playerSlot;
            AutoPlaceSeed = autoPlaceSeed;
            ClientTick = clientTick;
        }
    }

    public readonly struct BattleshipRecoveryMessage
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public int MatchRoundId { get; }
        public int Phase { get; }
        public int ActivePlayerSlot { get; }
        public long PlacementTimerRemainingMs { get; }
        public long MoveTimerRemainingMs { get; }
        public int Player0ConsecutiveTimeouts { get; }
        public int Player1ConsecutiveTimeouts { get; }
        public int WinnerSlot { get; }
        public int FinishStatus { get; }
        public long ClientTick { get; }
        public string Player0LayoutPayload { get; }
        public string Player1LayoutPayload { get; }
        public string Player0OpponentMarksPayload { get; }
        public string Player1OpponentMarksPayload { get; }

        public BattleshipRecoveryMessage(
            Guid commandId,
            string senderUserId,
            int matchRoundId,
            int phase,
            int activePlayerSlot,
            long placementTimerRemainingMs,
            long moveTimerRemainingMs,
            int player0ConsecutiveTimeouts,
            int player1ConsecutiveTimeouts,
            int winnerSlot,
            int finishStatus,
            long clientTick,
            string player0LayoutPayload,
            string player1LayoutPayload,
            string player0OpponentMarksPayload,
            string player1OpponentMarksPayload)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (matchRoundId < 1)
                throw new ArgumentOutOfRangeException(nameof(matchRoundId), matchRoundId, "Value must be at least 1.");

            CommandId = commandId;
            SenderUserId = senderUserId;
            MatchRoundId = matchRoundId;
            Phase = phase;
            ActivePlayerSlot = activePlayerSlot;
            PlacementTimerRemainingMs = placementTimerRemainingMs;
            MoveTimerRemainingMs = moveTimerRemainingMs;
            Player0ConsecutiveTimeouts = player0ConsecutiveTimeouts;
            Player1ConsecutiveTimeouts = player1ConsecutiveTimeouts;
            WinnerSlot = winnerSlot;
            FinishStatus = finishStatus;
            ClientTick = clientTick;
            Player0LayoutPayload = player0LayoutPayload ?? string.Empty;
            Player1LayoutPayload = player1LayoutPayload ?? string.Empty;
            Player0OpponentMarksPayload = player0OpponentMarksPayload ?? string.Empty;
            Player1OpponentMarksPayload = player1OpponentMarksPayload ?? string.Empty;
        }
    }

    public interface IBattleshipLayoutSerializer
    {
        string Serialize(FleetLayout layout);
        bool TryDeserialize(string payload, out FleetLayout layout);
    }

    public interface IBattleshipNetworkBridge : IDisposable
    {
        Observable<BattleshipPlacementMessage> IncomingPlacements { get; }
        Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts { get; }
        Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots { get; }

        UniTask BindAsync(string localUserId, bool isHost);
        UniTask UnbindAsync();

        UniTask SubmitPlacementAsync(BattleshipPlacementMessage message);
        UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message);
        UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message);
    }

    public sealed class BattleshipLayoutSerializer : IBattleshipLayoutSerializer
    {
        private const string VersionPrefix = "v1:";
        private const int BoardSize = 10;

        private static readonly ShipSize[] FleetOrder =
        {
            ShipSize.Four,
            ShipSize.Three,
            ShipSize.Three,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.Two,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
            ShipSize.One,
        };

        public string Serialize(FleetLayout layout)
        {
            if (!layout.IsInitialized || layout.Ships == null)
                throw new ArgumentException("Fleet layout is not initialized.", nameof(layout));

            var orderedShips = BuildCanonicalOrder(layout.Ships);
            var builder = new StringBuilder(capacity: 96);
            builder.Append(VersionPrefix);

            for (var i = 0; i < orderedShips.Count; i++)
            {
                if (i > 0)
                    builder.Append(';');

                var ship = orderedShips[i];
                if (!TryGetStartIndex(ship.StartCell, out var startCellIndex))
                    throw new ArgumentException("Ship start cell is out of bounds.", nameof(layout));

                builder.Append(((int)ship.Size).ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(ship.Orientation == ShipOrientation.Horizontal ? 'H' : 'V');
                builder.Append(',');
                builder.Append(startCellIndex.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        public bool TryDeserialize(string payload, out FleetLayout layout)
        {
            layout = default;

            if (string.IsNullOrWhiteSpace(payload) || !payload.StartsWith(VersionPrefix, StringComparison.Ordinal))
                return false;

            var body = payload.Substring(VersionPrefix.Length);
            var shipsRaw = body.Split(';');
            if (shipsRaw.Length != FleetLayout.ExpectedShipCount)
                return false;

            var ships = new ShipPlacement[FleetLayout.ExpectedShipCount];
            for (var i = 0; i < shipsRaw.Length; i++)
            {
                var parts = shipsRaw[i].Split(',');
                if (parts.Length != 3)
                    return false;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sizeValue))
                    return false;

                if (sizeValue != (int)FleetOrder[i])
                    return false;

                if (!TryParseOrientation(parts[1], out var orientation))
                    return false;

                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startCellIndex))
                    return false;

                if (!TryGetStartCell(startCellIndex, out var startCell))
                    return false;

                ships[i] = new ShipPlacement(FleetOrder[i], orientation, startCell);
            }

            try
            {
                layout = new FleetLayout(Array.AsReadOnly(ships));
                return true;
            }
            catch
            {
                layout = default;
                return false;
            }
        }

        private static IReadOnlyList<ShipPlacement> BuildCanonicalOrder(IReadOnlyList<ShipPlacement> source)
        {
            if (source.Count != FleetLayout.ExpectedShipCount)
                throw new ArgumentException($"Fleet must contain exactly {FleetLayout.ExpectedShipCount} ships.", nameof(source));

            var grouped = new List<ShipPlacement>[5];
            for (var i = 0; i < grouped.Length; i++)
                grouped[i] = new List<ShipPlacement>();

            for (var i = 0; i < source.Count; i++)
            {
                var ship = source[i];
                var size = (int)ship.Size;
                if (size < (int)ShipSize.One || size > (int)ShipSize.Four)
                    throw new ArgumentException("Fleet contains unsupported ship size.", nameof(source));

                grouped[size].Add(ship);
            }

            for (var i = (int)ShipSize.One; i <= (int)ShipSize.Four; i++)
            {
                grouped[i].Sort((left, right) =>
                {
                    var leftIndex = left.StartCell.Major * BoardSize + left.StartCell.Minor;
                    var rightIndex = right.StartCell.Major * BoardSize + right.StartCell.Minor;
                    var byIndex = leftIndex.CompareTo(rightIndex);
                    if (byIndex != 0)
                        return byIndex;

                    return left.Orientation.CompareTo(right.Orientation);
                });
            }

            var result = new List<ShipPlacement>(FleetLayout.ExpectedShipCount);
            for (var i = 0; i < FleetOrder.Length; i++)
            {
                var size = (int)FleetOrder[i];
                var bucket = grouped[size];
                if (bucket.Count == 0)
                    throw new ArgumentException("Fleet composition does not match expected order.", nameof(source));

                result.Add(bucket[0]);
                bucket.RemoveAt(0);
            }

            return result;
        }

        private static bool TryParseOrientation(string raw, out ShipOrientation orientation)
        {
            orientation = ShipOrientation.Horizontal;

            if (string.Equals(raw, "H", StringComparison.Ordinal))
            {
                orientation = ShipOrientation.Horizontal;
                return true;
            }

            if (string.Equals(raw, "V", StringComparison.Ordinal))
            {
                orientation = ShipOrientation.Vertical;
                return true;
            }

            return false;
        }

        private static bool TryGetStartIndex(in CellId cellId, out int index)
        {
            index = cellId.Major * BoardSize + cellId.Minor;
            return cellId.Major >= 0 && cellId.Major < BoardSize && cellId.Minor >= 0 && cellId.Minor < BoardSize;
        }

        private static bool TryGetStartCell(int index, out CellId cellId)
        {
            cellId = default;
            if (index < 0 || index >= BoardSize * BoardSize)
                return false;

            var major = index / BoardSize;
            var minor = index % BoardSize;
            cellId = new CellId(major, minor);
            return true;
        }
    }

    public sealed class NoOpBattleshipNetworkBridge : IBattleshipNetworkBridge
    {
        private readonly Subject<BattleshipPlacementMessage> _incomingPlacements = new();
        private readonly Subject<BattleshipPlacementTimeoutMessage> _incomingPlacementTimeouts = new();
        private readonly Subject<BattleshipRecoveryMessage> _incomingRecoverySnapshots = new();

        public static readonly NoOpBattleshipNetworkBridge Instance = new();

        public Observable<BattleshipPlacementMessage> IncomingPlacements => _incomingPlacements;
        public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => _incomingPlacementTimeouts;
        public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => _incomingRecoverySnapshots;

        public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
        public UniTask UnbindAsync() => UniTask.CompletedTask;
        public UniTask SubmitPlacementAsync(BattleshipPlacementMessage message) => UniTask.CompletedTask;
        public UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message) => UniTask.CompletedTask;
        public UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message) => UniTask.CompletedTask;

        public void Dispose()
        {
            _incomingPlacements.Dispose();
            _incomingPlacementTimeouts.Dispose();
            _incomingRecoverySnapshots.Dispose();
        }
    }

    public sealed class FileBattleshipNetworkBridge : IBattleshipNetworkBridge
    {
        private readonly IOnlineGameplaySessionContextStore _contextStore;
        private readonly IPhotonSessionTransport _transport;
        private readonly Subject<BattleshipPlacementMessage> _incomingPlacements = new();
        private readonly Subject<BattleshipPlacementTimeoutMessage> _incomingPlacementTimeouts = new();
        private readonly Subject<BattleshipRecoveryMessage> _incomingRecoverySnapshots = new();

        private string? _localUserId;
        private bool _isBound;
        private bool _isDisposed;

        public FileBattleshipNetworkBridge(IOnlineGameplaySessionContextStore contextStore, IPhotonSessionTransport transport)
        {
            _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public Observable<BattleshipPlacementMessage> IncomingPlacements => _incomingPlacements;
        public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => _incomingPlacementTimeouts;
        public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => _incomingRecoverySnapshots;

        public async UniTask BindAsync(string localUserId, bool isHost)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileBattleshipNetworkBridge));

            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            await UnbindAsync();

            var session = _contextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.SessionId))
                return;

            _localUserId = localUserId;
            _isBound = true;
            _transport.ReliableDataReceived += OnReliableDataReceived;
        }

        public async UniTask UnbindAsync()
        {
            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;

            _isBound = false;
            _localUserId = null;

            await UniTask.CompletedTask;
        }

        public async UniTask SubmitPlacementAsync(BattleshipPlacementMessage message)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(SerializePlacement(message));
        }

        public async UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(SerializePlacementTimeout(message));
        }

        public async UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FileBattleshipNetworkBridge));

            if (!_isBound)
                return;

            await _transport.SendReliableDataAsync(SerializeRecovery(message));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_isBound)
                _transport.ReliableDataReceived -= OnReliableDataReceived;

            _isBound = false;
            _localUserId = null;

            _incomingPlacements.Dispose();
            _incomingPlacementTimeouts.Dispose();
            _incomingRecoverySnapshots.Dispose();
        }

        private void OnReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (!_isBound || evt.Payload == null || evt.Payload.Length == 0)
                return;

            if (TryDeserializePlacement(evt.Payload, out var placementMessage))
            {
                if (string.Equals(placementMessage.SenderUserId, _localUserId, StringComparison.Ordinal))
                    return;

                _incomingPlacements.OnNext(placementMessage);
                return;
            }

            if (!TryDeserializePlacementTimeout(evt.Payload, out var timeoutMessage))
            {
                if (!TryDeserializeRecovery(evt.Payload, out var recoveryMessage))
                    return;

                if (string.Equals(recoveryMessage.SenderUserId, _localUserId, StringComparison.Ordinal))
                    return;

                _incomingRecoverySnapshots.OnNext(recoveryMessage);
                return;
            }

            if (string.Equals(timeoutMessage.SenderUserId, _localUserId, StringComparison.Ordinal))
                return;

            _incomingPlacementTimeouts.OnNext(timeoutMessage);
        }

        private static byte[] SerializePlacement(BattleshipPlacementMessage message)
        {
            var line = string.Concat(
                "BP|",
                message.CommandId.ToString("N"), "|",
                message.SenderUserId.Replace("|", string.Empty), "|",
                message.LayoutPayload.Replace("|", string.Empty), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture));

            return Encoding.UTF8.GetBytes(line);
        }

        private static byte[] SerializePlacementTimeout(BattleshipPlacementTimeoutMessage message)
        {
            var line = string.Concat(
                "BT|",
                message.CommandId.ToString("N"), "|",
                message.SenderUserId.Replace("|", string.Empty), "|",
                message.PlayerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.AutoPlaceSeed.ToString(CultureInfo.InvariantCulture), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture));

            return Encoding.UTF8.GetBytes(line);
        }

        private static bool TryDeserializePlacement(byte[] payload, out BattleshipPlacementMessage message)
        {
            message = default;

            var line = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 5 || parts[0] != "BP")
                return false;

            if (!Guid.TryParse(parts[1], out var commandId) || commandId == Guid.Empty)
                return false;

            var senderUserId = parts[2];
            if (string.IsNullOrWhiteSpace(senderUserId))
                return false;

            var layoutPayload = parts[3];
            if (string.IsNullOrWhiteSpace(layoutPayload))
                return false;

            if (!long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientTick))
                clientTick = 0;

            message = new BattleshipPlacementMessage(commandId, senderUserId, layoutPayload, clientTick);
            return true;
        }

        private static bool TryDeserializePlacementTimeout(byte[] payload, out BattleshipPlacementTimeoutMessage message)
        {
            message = default;

            var line = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 6 || parts[0] != "BT")
                return false;

            if (!Guid.TryParse(parts[1], out var commandId) || commandId == Guid.Empty)
                return false;

            var senderUserId = parts[2];
            if (string.IsNullOrWhiteSpace(senderUserId))
                return false;

            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerSlot) || playerSlot < 0)
                return false;

            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var autoPlaceSeed))
                return false;

            if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientTick))
                clientTick = 0;

            message = new BattleshipPlacementTimeoutMessage(commandId, senderUserId, playerSlot, autoPlaceSeed, clientTick);
            return true;
        }

        private static byte[] SerializeRecovery(BattleshipRecoveryMessage message)
        {
            var line = string.Concat(
                "BR|",
                message.CommandId.ToString("N"), "|",
                Sanitize(message.SenderUserId), "|",
                message.MatchRoundId.ToString(CultureInfo.InvariantCulture), "|",
                message.Phase.ToString(CultureInfo.InvariantCulture), "|",
                message.ActivePlayerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.PlacementTimerRemainingMs.ToString(CultureInfo.InvariantCulture), "|",
                message.MoveTimerRemainingMs.ToString(CultureInfo.InvariantCulture), "|",
                message.Player0ConsecutiveTimeouts.ToString(CultureInfo.InvariantCulture), "|",
                message.Player1ConsecutiveTimeouts.ToString(CultureInfo.InvariantCulture), "|",
                message.WinnerSlot.ToString(CultureInfo.InvariantCulture), "|",
                message.FinishStatus.ToString(CultureInfo.InvariantCulture), "|",
                message.ClientTick.ToString(CultureInfo.InvariantCulture), "|",
                EncodePayload(message.Player0LayoutPayload), "|",
                EncodePayload(message.Player1LayoutPayload), "|",
                EncodePayload(message.Player0OpponentMarksPayload), "|",
                EncodePayload(message.Player1OpponentMarksPayload));

            return Encoding.UTF8.GetBytes(line);
        }

        private static bool TryDeserializeRecovery(byte[] payload, out BattleshipRecoveryMessage message)
        {
            message = default;

            var line = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('|');
            if (parts.Length != 17 || parts[0] != "BR")
                return false;

            if (!Guid.TryParse(parts[1], out var commandId) || commandId == Guid.Empty)
                return false;

            var senderUserId = parts[2];
            if (string.IsNullOrWhiteSpace(senderUserId))
                return false;

            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var matchRoundId) || matchRoundId < 1)
                return false;

            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var phase))
                return false;

            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var activePlayerSlot))
                activePlayerSlot = -1;

            if (!long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var placementTimerRemainingMs))
                placementTimerRemainingMs = 0;

            if (!long.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var moveTimerRemainingMs))
                moveTimerRemainingMs = 0;

            if (!int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var player0Timeouts))
                player0Timeouts = 0;

            if (!int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var player1Timeouts))
                player1Timeouts = 0;

            if (!int.TryParse(parts[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var winnerSlot))
                winnerSlot = -1;

            if (!int.TryParse(parts[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var finishStatus))
                finishStatus = 0;

            if (!long.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientTick))
                clientTick = 0;

            if (!TryDecodePayload(parts[13], out var player0Layout)
                || !TryDecodePayload(parts[14], out var player1Layout)
                || !TryDecodePayload(parts[15], out var player0Marks)
                || !TryDecodePayload(parts[16], out var player1Marks))
            {
                return false;
            }

            message = new BattleshipRecoveryMessage(
                commandId,
                senderUserId,
                matchRoundId,
                phase,
                activePlayerSlot,
                placementTimerRemainingMs,
                moveTimerRemainingMs,
                player0Timeouts,
                player1Timeouts,
                winnerSlot,
                finishStatus,
                clientTick,
                player0Layout,
                player1Layout,
                player0Marks,
                player1Marks);

            return true;
        }

        private static string Sanitize(string value) => value.Replace("|", string.Empty);

        private static string EncodePayload(string payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            return Convert.ToBase64String(bytes);
        }

        private static bool TryDecodePayload(string payload, out string decoded)
        {
            decoded = string.Empty;

            if (payload == null)
                return false;

            try
            {
                var bytes = Convert.FromBase64String(payload);
                decoded = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class BattleshipOnlineCommandSink : IGameplayCommandSink, IDisposable
    {
        private const int DedupWindowSize = 512;

        private readonly IMatchStateProvider _localCommandSink;
        private readonly IGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayNetworkBridge _networkBridge;
        private readonly IBattleshipNetworkBridge _battleshipNetworkBridge;
        private readonly IBattleshipLayoutSerializer _layoutSerializer;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly CompositeDisposable _subscriptions = new();
        private readonly HashSet<Guid> _seenPlacementCommands = new();
        private readonly Queue<Guid> _seenPlacementCommandOrder = new();

        private bool _disposed;

        public BattleshipOnlineCommandSink(
            IMatchStateProvider localCommandSink,
            IGameplaySnapshotProvider snapshotProvider,
            IGameplayNetworkBridge networkBridge,
            IBattleshipNetworkBridge battleshipNetworkBridge,
            IBattleshipLayoutSerializer layoutSerializer,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _localCommandSink = localCommandSink ?? throw new ArgumentNullException(nameof(localCommandSink));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));
            _battleshipNetworkBridge = battleshipNetworkBridge ?? throw new ArgumentNullException(nameof(battleshipNetworkBridge));
            _layoutSerializer = layoutSerializer ?? throw new ArgumentNullException(nameof(layoutSerializer));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));

            _battleshipNetworkBridge.IncomingPlacements
                .Subscribe(OnIncomingPlacement)
                .AddTo(_subscriptions);

            _battleshipNetworkBridge.IncomingPlacementTimeouts
                .Subscribe(OnIncomingPlacementTimeout)
                .AddTo(_subscriptions);
        }

        public void SubmitCommand(IGameplayCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.LocalUserId))
            {
                _localCommandSink.SubmitCommand(command);
                return;
            }

            if (command is SubmitPlacementCommand submitPlacement)
            {
                _localCommandSink.SubmitCommand(command);

                string payload;
                try
                {
                    payload = _layoutSerializer.Serialize(submitPlacement.Layout);
                }
                catch (Exception ex)
                {
                    Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to serialize placement: {ex.Message}");
                    return;
                }

                SubmitPlacementAsync(new BattleshipPlacementMessage(
                    Guid.NewGuid(),
                    session.LocalUserId,
                    payload,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
                return;
            }

            if (command is PlacementTimeoutCommand placementTimeout)
            {
                if (!session.IsHost)
                    return;

                _localCommandSink.SubmitCommand(command);

                SubmitPlacementTimeoutAsync(new BattleshipPlacementTimeoutMessage(
                    Guid.NewGuid(),
                    session.LocalUserId,
                    placementTimeout.PlayerSlot,
                    placementTimeout.AutoPlaceSeed,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
                return;
            }

            if (command is TimeoutCommand timeout)
            {
                if (!session.IsHost)
                    return;

                _localCommandSink.SubmitCommand(command);
                SubmitOnlineTimeoutAsync(new OnlineTimeoutSignal(
                    session.LocalUserId,
                    timeout.LoserSlot,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Forget();
                return;
            }

            if (command is not MakeMoveCommand move)
            {
                _localCommandSink.SubmitCommand(command);
                return;
            }

            var localPlayerSlot = session.IsHost ? PlayerSlotMapping.SlotX : PlayerSlotMapping.SlotO;
            if (_localCommandSink.ActivePlayerSlot != localPlayerSlot)
                return;

            if (session.IsHost)
                _localCommandSink.SubmitCommand(command);

            var cells = _snapshotProvider.GetAllCells();
            var minorCount = ResolveMinorCount(cells);

            MoveCommand onlineMove;
            try
            {
                var cellIndex = OnlineMoveIndexCodec.ToCellIndex(move.CellId, minorCount);
                var sequence = ResolveNextShotSequence();
                onlineMove = new MoveCommand(Guid.NewGuid(), session.LocalUserId, cellIndex, sequence);
            }
            catch
            {
                Log.Error(LogTags.Infrastructure, "[BattleshipOnlineCommandSink] Failed to encode online move.");
                return;
            }

            SubmitOnlineMoveAsync(onlineMove).Forget();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscriptions.Dispose();
            _seenPlacementCommands.Clear();
            _seenPlacementCommandOrder.Clear();
        }

        private void OnIncomingPlacement(BattleshipPlacementMessage message)
        {
            if (!RememberPlacementCommand(message.CommandId))
                return;

            if (!_layoutSerializer.TryDeserialize(message.LayoutPayload, out var layout))
            {
                Log.Warning(LogTags.Infrastructure,
                    $"[BattleshipOnlineCommandSink] Ignored placement payload with unsupported format. Sender={message.SenderUserId}");
                return;
            }

            if (!TryResolvePlayerSlot(message.SenderUserId, out var playerSlot))
                return;

            _localCommandSink.SubmitCommand(new SubmitPlacementCommand(playerSlot, layout));
        }

        private void OnIncomingPlacementTimeout(BattleshipPlacementTimeoutMessage message)
        {
            if (!RememberPlacementCommand(message.CommandId))
                return;

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || session.IsHost)
                return;

            if (!TryResolvePlayerSlot(message.SenderUserId, out var senderSlot)
                || senderSlot != PlayerSlotMapping.SlotX)
            {
                return;
            }

            _localCommandSink.SubmitCommand(new PlacementTimeoutCommand(message.PlayerSlot, message.AutoPlaceSeed));
        }

        private bool RememberPlacementCommand(Guid commandId)
        {
            if (!_seenPlacementCommands.Add(commandId))
                return false;

            _seenPlacementCommandOrder.Enqueue(commandId);
            while (_seenPlacementCommandOrder.Count > DedupWindowSize)
            {
                var oldest = _seenPlacementCommandOrder.Dequeue();
                _seenPlacementCommands.Remove(oldest);
            }

            return true;
        }

        private bool TryResolvePlayerSlot(string senderUserId, out int playerSlot)
        {
            playerSlot = -1;

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite || string.IsNullOrWhiteSpace(session.LocalUserId))
                return false;

            var localSlot = session.IsHost ? PlayerSlotMapping.SlotX : PlayerSlotMapping.SlotO;
            var remoteSlot = localSlot == PlayerSlotMapping.SlotX ? PlayerSlotMapping.SlotO : PlayerSlotMapping.SlotX;

            playerSlot = string.Equals(senderUserId, session.LocalUserId, StringComparison.Ordinal)
                ? localSlot
                : remoteSlot;

            return true;
        }

        private static int ResolveMinorCount(IReadOnlyList<CellSnapshot> cells)
        {
            if (cells == null || cells.Count == 0)
                return 10;

            var maxMinor = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var minor = cells[i].CellId.Minor;
                if (minor > maxMinor)
                    maxMinor = minor;
            }

            return maxMinor + 1;
        }

        private long ResolveNextShotSequence()
        {
            var snapshot = _networkBridge.Snapshot.CurrentValue;
            var currentSequence = snapshot?.ShotSequence ?? 0;
            if (currentSequence < 0)
                currentSequence = 0;

            return currentSequence + 1;
        }

        private async UniTaskVoid SubmitOnlineMoveAsync(MoveCommand move)
        {
            try
            {
                await _networkBridge.SubmitMoveAsync(move);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit online move: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitOnlineTimeoutAsync(OnlineTimeoutSignal signal)
        {
            try
            {
                await _networkBridge.SubmitTimeoutAsync(signal);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit online timeout: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitPlacementAsync(BattleshipPlacementMessage message)
        {
            try
            {
                await _battleshipNetworkBridge.SubmitPlacementAsync(message);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit placement: {ex.Message}");
            }
        }

        private async UniTaskVoid SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message)
        {
            try
            {
                await _battleshipNetworkBridge.SubmitPlacementTimeoutAsync(message);
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.Infrastructure, $"[BattleshipOnlineCommandSink] Failed to submit placement timeout: {ex.Message}");
            }
        }
    }
}