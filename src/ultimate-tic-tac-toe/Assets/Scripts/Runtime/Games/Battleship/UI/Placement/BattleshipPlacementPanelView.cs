#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.Battleship.Placement;
using UnityEngine.UIElements;

namespace Runtime.Games.Battleship.UI.Placement
{
    internal sealed class BattleshipPlacementPanelView
    {
        private const string _panelName = "BattleshipPlacementPanel";
        private const string _statusLabelName = "BattleshipPlacementStatusLabel";
        private const string _shipButtonPrefix = "ShipButton_";

        private readonly Action _onAutoPlace;
        private readonly Action _onRotate;
        private readonly Action _onRemove;
        private readonly Action _onReady;
        private readonly Action<int> _onShipSelected;
        private readonly Dictionary<int, Button> _shipButtons = new();

        public BattleshipPlacementPanelView(
            Action onAutoPlace,
            Action onRotate,
            Action onRemove,
            Action onReady,
            Action<int> onShipSelected)
        {
            _onAutoPlace = onAutoPlace ?? throw new ArgumentNullException(nameof(onAutoPlace));
            _onRotate = onRotate ?? throw new ArgumentNullException(nameof(onRotate));
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
            _onReady = onReady ?? throw new ArgumentNullException(nameof(onReady));
            _onShipSelected = onShipSelected ?? throw new ArgumentNullException(nameof(onShipSelected));
        }

        public VisualElement? Root { get; private set; }

        public Label? StatusLabel { get; private set; }

        public Button? AutoButton { get; private set; }

        public Button? RotateButton { get; private set; }

        public Button? RemoveButton { get; private set; }

        public Button? ReadyButton { get; private set; }

        public IReadOnlyDictionary<int, Button> ShipButtons => _shipButtons;

        public bool TryAttach(VisualElement? fieldContainer, IReadOnlyList<BattleshipPlacementShipState> ships)
        {
            if (!TryResolveFieldRoot(fieldContainer, out var fieldRoot))
                return false;

            Detach();

            RemoveExistingPanel(fieldRoot);
            Root = CreateRoot(ships);
            InsertRoot(fieldRoot, fieldContainer!, Root);

            return true;
        }

        private static bool TryResolveFieldRoot(VisualElement? fieldContainer, out VisualElement fieldRoot)
        {
            fieldRoot = null!;
            
            if (fieldContainer?.parent == null)
                return false;

            fieldRoot = fieldContainer.parent;
            return true;
        }

        private static void RemoveExistingPanel(VisualElement fieldRoot)
        {
            var existing = fieldRoot.Q<VisualElement>(_panelName);
            existing?.RemoveFromHierarchy();
        }

        private VisualElement CreateRoot(IReadOnlyList<BattleshipPlacementShipState> ships)
        {
            var root = new VisualElement
            {
                name = _panelName,
                style =
                {
                    flexDirection = FlexDirection.Column,
                    marginBottom = 8f,
                },
            };

            root.Add(CreateControlsRow());
            root.Add(CreateShipsRow(ships));
            StatusLabel = new Label { name = _statusLabelName };
            StatusLabel.AddToClassList("placement-status-label");
            root.Add(StatusLabel);
            return root;
        }

        private static void InsertRoot(VisualElement fieldRoot, VisualElement fieldContainer, VisualElement root)
        {
            var insertIndex = fieldRoot.IndexOf(fieldContainer);
            
            if (insertIndex < 0)
                fieldRoot.Add(root);
            else
                fieldRoot.Insert(insertIndex, root);
        }

        public void Detach()
        {
            _shipButtons.Clear();

            if (Root != null)
            {
                Root.RemoveFromHierarchy();
                Root = null;
            }

            StatusLabel = null;
            AutoButton = null;
            RotateButton = null;
            RemoveButton = null;
            ReadyButton = null;
        }

        private VisualElement CreateControlsRow()
        {
            var controlsRow = new VisualElement();
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.marginBottom = 6f;
            controlsRow.style.flexWrap = Wrap.Wrap;

            AutoButton = CreateActionButton(_onAutoPlace);
            RotateButton = CreateActionButton(_onRotate);
            RemoveButton = CreateActionButton(_onRemove);
            ReadyButton = CreateActionButton(_onReady);
            ReadyButton.AddToClassList("placement-ready-button");

            controlsRow.Add(AutoButton);
            controlsRow.Add(RotateButton);
            controlsRow.Add(RemoveButton);
            controlsRow.Add(ReadyButton);
            return controlsRow;
        }

        private VisualElement CreateShipsRow(IReadOnlyList<BattleshipPlacementShipState> ships)
        {
            var shipsRow = new VisualElement
            {
                name = "ShipButtonsRow",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 6f,
                    flexWrap = Wrap.Wrap,
                },
            };

            for (var i = 0; i < ships.Count; i++)
            {
                var shipId = ships[i].ShipId;
                var button = CreateActionButton(() => _onShipSelected(shipId));
                button.name = _shipButtonPrefix + shipId;
                _shipButtons[shipId] = button;
                shipsRow.Add(button);
            }

            return shipsRow;
        }

        private static Button CreateActionButton(Action onClick)
        {
            var button = new Button(onClick)
            {
                style =
                {
                    marginRight = 4f,
                    marginBottom = 4f,
                },
            };

            return button;
        }
    }
}