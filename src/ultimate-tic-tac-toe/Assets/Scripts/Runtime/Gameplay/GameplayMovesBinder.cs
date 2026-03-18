using System;
using System.Collections.Generic;
using R3;
using Runtime.Gameplay.Shared;
using Runtime.Infrastructure.Logging;
using Runtime.Localization;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    public sealed class GameplayMovesBinder : IDisposable
    {
        private const string ActivePanelClass = "player-panel--active";

        private readonly IGameplayFieldUiAdapter _ui;
        private readonly IGameplayCommandSink _commandSink;
        private readonly IGameplayEventStream _eventStream;
        private readonly IGameplaySnapshotProvider _snapshotProvider;
        private readonly IGameplayMovesModeBehavior _modeBehavior;
        private readonly GameplayMovesFieldRenderer _fieldRenderer;
        private readonly ILocalizationService _localization;

        private CompositeDisposable _subscriptions;
        private Label _currentPlayerLabel;
        private bool _isBound;
        private bool _disposed;

        public GameplayMovesBinder(
            IGameplayFieldUiAdapter ui,
            IGameplayCommandSink commandSink,
            IGameplayEventStream eventStream,
            IGameplaySnapshotProvider snapshotProvider,
            IGameplayMovesModeBehavior modeBehavior = null,
            ILocalizationService localization = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _modeBehavior = modeBehavior ?? DefaultGameplayMovesModeBehavior.Instance;

            _fieldRenderer = new GameplayMovesFieldRenderer(
                _ui,
                NormalizeVfxSettings(MovesVfxSettings.Default));

            _localization = localization;
        }

        public void Bind()
        {
            ThrowIfDisposed();

            if (_isBound)
            {
                GameLog.Warning("[GameplayMovesBinder] Bind called more than once. Ignored.");
                return;
            }

            _currentPlayerLabel = AcquireCurrentPlayerLabel();
            _subscriptions = new CompositeDisposable();

            try
            {
                SubscribeToEvents();

                var coldPathSnapshot = MapEcsCells(_snapshotProvider.GetAllCells());
                _modeBehavior.Initialize(_fieldRenderer, coldPathSnapshot);
                UpdateCurrentPlayerLabel(PlayerSlotMapping.SlotToMark(_snapshotProvider.ActivePlayerSlot));
                _isBound = true;
            }
            catch
            {
                _subscriptions.Dispose();
                _subscriptions = null;
                _currentPlayerLabel = null;
                _fieldRenderer.Reset();
                throw;
            }
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            _subscriptions?.Dispose();
            _subscriptions = null;
            _currentPlayerLabel = null;
            _fieldRenderer.Reset();
            _isBound = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private Label AcquireCurrentPlayerLabel()
        {
            Label label;

            try
            {
                label = _ui.CurrentPlayerLabel;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                GameLog.Error($"[GameplayMovesBinder] Bind failed: UI is not ready ({ex.GetType().Name}). {ex.Message}");
                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: UI is not ready.", ex);
            }

            if (label == null)
            {
                GameLog.Error("[GameplayMovesBinder] Bind failed: CurrentPlayerLabel is null.");
                throw new InvalidOperationException("GameplayMovesBinder.Bind() failed: CurrentPlayerLabel is null.");
            }

            return label;
        }

        private void SubscribeToEvents()
        {
            _ui.CellClicks
                .Subscribe(OnCellClicked)
                .AddTo(_subscriptions!);

            _eventStream.CellChanged
                .Subscribe(OnEcsCellChanged)
                .AddTo(_subscriptions!);

            _eventStream.LastMoveChanged
                .Subscribe(OnEcsLastMoveChanged)
                .AddTo(_subscriptions!);

            _eventStream.CurrentPlayerChanged
                .Subscribe(evt => UpdateCurrentPlayerLabel(PlayerSlotMapping.SlotToMark(evt.ActivePlayerSlot)))
                .AddTo(_subscriptions!);

            _eventStream.CommandRejected
                .Subscribe(OnCommandRejected)
                .AddTo(_subscriptions!);
        }

        private void OnCellClicked(CellId cellId)
        {
            if (!_isBound || _disposed)
                return;

            if (!_modeBehavior.CanSubmitCellClick())
                return;

            try
            {
                _commandSink.SubmitCommand(new MakeMoveCommand(cellId));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnCommandRejected(CommandRejectedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            GameLog.Warning($"[GameplayMovesBinder] Command rejected: {evt.Rejection.Reason}");
        }

        private void OnEcsCellChanged(CellChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            _modeBehavior.HandleCellChanged(_fieldRenderer, evt);
        }

        private void OnEcsLastMoveChanged(LastMoveChangedEvent evt)
        {
            if (!_isBound || _disposed)
                return;

            _modeBehavior.HandleLastMoveChanged(_fieldRenderer, evt);
        }

        private static IReadOnlyList<CellValue> MapEcsCells(IReadOnlyList<CellSnapshot> ecsCells)
        {
            var result = new CellValue[ecsCells.Count];

            for (var i = 0; i < ecsCells.Count; i++)
            {
                result[i] = new CellValue(ecsCells[i].CellId, PlayerSlotMapping.SlotToMark(ecsCells[i].Slot));
            }

            return result;
        }

        private static MovesVfxSettings NormalizeVfxSettings(MovesVfxSettings settings)
        {
            if (!settings.EnableMarkAppearAnimation || settings.MarkAppearDurationSeconds <= 0f)
                return new MovesVfxSettings(enableMarkAppearAnimation: false, markAppearDurationSeconds: 0f);

            return settings;
        }

        private void UpdateCurrentPlayerLabel(PlayerMark mark)
        {
            if (_currentPlayerLabel == null)
                return;

            _currentPlayerLabel.text = mark.ToTurnIndicatorText(_localization);
            UpdateActivePlayerPanel(mark);
        }

        private void UpdateActivePlayerPanel(PlayerMark mark)
        {
            var p1 = _ui.Player1Panel;
            var p2 = _ui.Player2Panel;

            if (p1 == null || p2 == null)
                return;

            if (mark == PlayerMark.X)
            {
                p1.AddToClassList(ActivePanelClass);
                p2.RemoveFromClassList(ActivePanelClass);
            }
            else if (mark == PlayerMark.O)
            {
                p1.RemoveFromClassList(ActivePanelClass);
                p2.AddToClassList(ActivePanelClass);
            }
            else
            {
                p1.RemoveFromClassList(ActivePanelClass);
                p2.RemoveFromClassList(ActivePanelClass);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameplayMovesBinder));
        }
    }
}