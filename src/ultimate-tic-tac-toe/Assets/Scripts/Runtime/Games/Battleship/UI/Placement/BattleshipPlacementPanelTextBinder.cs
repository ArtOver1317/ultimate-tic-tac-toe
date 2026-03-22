#nullable enable

using Runtime.GameModes.Wizard;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Localization.Contracts;

namespace Runtime.Games.Battleship.UI.Placement
{
    /// <summary>
    /// Resolves and applies localized text to placement panel buttons and status label.
    /// </summary>
    internal sealed class BattleshipPlacementPanelTextBinder
    {
        private const string _autoButtonKey = "Game.Battleship.Placement.AutoButton";
        private const string _rotateButtonKey = "Game.Battleship.Placement.RotateButton";
        private const string _removeButtonKey = "Game.Battleship.Placement.RemoveButton";
        private const string _readyButtonKey = "Game.Battleship.Placement.ReadyButton";
        private const string _waitingStatusKey = "Game.Battleship.Placement.Status.WaitingOpponent";
        private const string _unavailableStatusKey = "Game.Battleship.Placement.Status.Unavailable";
        private const string _placeAllShipsKey = "Game.Battleship.Placement.Status.PlaceAllShips";
        private const string _confirmReadyKey = "Game.Battleship.Placement.Status.ConfirmReady";
        private const string _selectShipKey = "Game.Battleship.Placement.Status.SelectShip";
        private const string _selectShipHintKey = "Game.Battleship.Placement.SelectShipHint";

        private const string _autoButtonFallback = "Auto place";
        private const string _rotateButtonFallback = "Rotate";
        private const string _removeButtonFallback = "Remove";
        private const string _readyButtonFallback = "Ready";
        private const string _waitingStatusFallback = "Placement submitted. Waiting for opponent.";
        private const string _unavailableStatusFallback = "Placement is unavailable.";
        private const string _placeAllShipsFallback = "Place all ships.";
        private const string _confirmReadyFallback = "Press Ready to confirm placement.";
        private const string _selectShipFallback = "Select a ship or click a cell.";
        private const string _selectShipHintFallback = "Select a ship first";

        private readonly BattleshipPlacementPanelView _panelView;
        private readonly BattleshipPlacementService _placementService;
        private readonly ILocalizationService? _localization;

        internal BattleshipPlacementPanelTextBinder(
            BattleshipPlacementPanelView panelView,
            BattleshipPlacementService placementService,
            ILocalizationService? localization)
        {
            _panelView = panelView;
            _placementService = placementService;
            _localization = localization;
        }

        internal void RefreshControlTexts()
        {
            if (_panelView.AutoButton != null)
                _panelView.AutoButton.text = Resolve(_autoButtonKey, _autoButtonFallback);

            if (_panelView.RotateButton != null)
                _panelView.RotateButton.text = Resolve(_rotateButtonKey, _rotateButtonFallback);

            if (_panelView.RemoveButton != null)
                _panelView.RemoveButton.text = Resolve(_removeButtonKey, _removeButtonFallback);

            if (_panelView.ReadyButton != null)
                _panelView.ReadyButton.text = Resolve(_readyButtonKey, _readyButtonFallback);
        }

        internal void RefreshStatusLabel(BattleshipPhase phase, bool hasSelection)
        {
            if (_panelView.StatusLabel != null)
                _panelView.StatusLabel.text = ResolveStatusText(phase, hasSelection);
        }

        internal void SetTooltips()
        {
            var hint = Resolve(_selectShipHintKey, _selectShipHintFallback);

            if (_panelView.RotateButton != null)
                _panelView.RotateButton.tooltip = hint;

            if (_panelView.RemoveButton != null)
                _panelView.RemoveButton.tooltip = hint;
        }

        private string ResolveStatusText(BattleshipPhase phase, bool hasSelection)
        {
            if (!_placementService.CanEdit)
            {
                return phase == BattleshipPhase.Waiting
                    ? Resolve(_waitingStatusKey, _waitingStatusFallback)
                    : Resolve(_unavailableStatusKey, _unavailableStatusFallback);
            }

            if (!string.IsNullOrWhiteSpace(_placementService.LastErrorKey))
                return Resolve(_placementService.LastErrorKey!, _placementService.LastErrorKey!);

            if (!_placementService.IsReadyToConfirm)
            {
                var placeAll = Resolve(_placeAllShipsKey, _placeAllShipsFallback);

                return hasSelection
                    ? placeAll
                    : placeAll + " " + Resolve(_selectShipKey, _selectShipFallback);
            }

            return Resolve(_confirmReadyKey, _confirmReadyFallback);
        }

        private string Resolve(string key, string fallback)
        {
            if (_localization == null)
                return fallback;

            var resolved = WizardErrorMessageResolver.Resolve(_localization, key);

            if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, key, System.StringComparison.Ordinal))
                return fallback;

            return resolved;
        }
    }
}
