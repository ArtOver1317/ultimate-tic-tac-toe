using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterLayoutController
    {
        private readonly GameplayFieldPresenterState _state;
        private readonly GameplayFieldPresenterCellLayoutSizer _cellLayoutSizer;
        private readonly GameplayFieldPresenterStyleTokenResolver _styleTokenResolver;

        public GameplayFieldPresenterLayoutController(GameplayFieldPresenterState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));

            _cellLayoutSizer = new GameplayFieldPresenterCellLayoutSizer(_state);
            
            _styleTokenResolver = new GameplayFieldPresenterStyleTokenResolver(_state, RefreshFieldLayout);
        }

        internal void AttachToFieldRoot(VisualElement fieldRoot)
        {
            if (fieldRoot == null)
                return;

            if (!ReferenceEquals(_state.CustomStyleCallbackElement, fieldRoot))
            {
                _state.CustomStyleCallbackElement?.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                _state.CustomStyleCallbackElement = fieldRoot;
                _state.CustomStyleCallbackElement.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            }

            _styleTokenResolver.Apply(fieldRoot.customStyle, validate: false);
        }

        internal void AttachToFieldContainer(VisualElement fieldContainer)
        {
            if (fieldContainer == null)
                return;

            fieldContainer.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            fieldContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        internal void CleanupBindings()
        {
            _state.FieldContainer?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            if (_state.CustomStyleCallbackElement != null)
            {
                _state.CustomStyleCallbackElement.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                _state.CustomStyleCallbackElement = null;
            }
        }

        internal void UpdateCellSizes(Rect rect) =>
            _cellLayoutSizer.UpdateCellSizes(rect);

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _cellLayoutSizer.UpdateCellSizes(evt.newRect);
            _cellLayoutSizer.RefreshMiniBoardCenters();
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt) =>
            _styleTokenResolver.Apply(evt.customStyle, validate: true);

        private void RefreshFieldLayout()
        {
            if (_state.FieldContainer == null)
                return;

            _cellLayoutSizer.UpdateCellSizes(_state.FieldContainer.contentRect);
        }
    }
}