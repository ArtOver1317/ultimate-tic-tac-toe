#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Games.Battleship.Networking
{
    internal sealed class BattleshipOnlinePlacementMessageHandler
    {
        private const int _dedupWindowSize = 512;

        private readonly IMatchStateProvider _localCommandSink;
        private readonly IBattleshipLayoutSerializer _layoutSerializer;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly HashSet<Guid> _seenPlacementCommands = new();
        private readonly Queue<Guid> _seenPlacementCommandOrder = new();

        public BattleshipOnlinePlacementMessageHandler(
            IMatchStateProvider localCommandSink,
            IBattleshipLayoutSerializer layoutSerializer,
            IOnlineGameplaySessionContextStore sessionContextStore)
        {
            _localCommandSink = localCommandSink ?? throw new ArgumentNullException(nameof(localCommandSink));
            _layoutSerializer = layoutSerializer ?? throw new ArgumentNullException(nameof(layoutSerializer));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
        }

        public void ApplyIncomingPlacement(BattleshipPlacementMessage message)
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

        public void ApplyIncomingPlacementTimeout(BattleshipPlacementTimeoutMessage message)
        {
            if (!RememberPlacementCommand(message.CommandId))
                return;

            var session = _sessionContextStore.Snapshot;
            
            if (!session.IsOnlineDirectInvite || session.IsHost)
                return;

            if (!TryResolvePlayerSlot(message.SenderUserId, out var senderSlot)
                || senderSlot != PlayerSlotMapping.SlotX)
                return;

            _localCommandSink.SubmitCommand(new PlacementTimeoutCommand(message.PlayerSlot, message.AutoPlaceSeed));
        }

        public void Reset()
        {
            _seenPlacementCommands.Clear();
            _seenPlacementCommandOrder.Clear();
        }

        private bool RememberPlacementCommand(Guid commandId)
        {
            if (!_seenPlacementCommands.Add(commandId))
                return false;

            _seenPlacementCommandOrder.Enqueue(commandId);
            
            while (_seenPlacementCommandOrder.Count > _dedupWindowSize)
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
    }
}