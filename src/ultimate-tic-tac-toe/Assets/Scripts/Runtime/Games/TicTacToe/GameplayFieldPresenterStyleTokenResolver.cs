using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal sealed class GameplayFieldPresenterStyleTokenResolver
    {
        private static readonly CustomStyleProperty<float> _gridGapHalfProperty = new("--grid-gap-half");
        private static readonly CustomStyleProperty<float> _gridGapProperty = new("--grid-gap");
        private static readonly CustomStyleProperty<float> _miniBoardGapHalfProperty = new("--mini-board-gap-half");
        private static readonly CustomStyleProperty<float> _miniBoardBorderProperty = new("--mini-board-border-width");
        private static readonly CustomStyleProperty<float> _miniBoardPaddingProperty = new("--mini-board-padding");
        private static readonly CustomStyleProperty<float> _markFontScaleProperty = new("--mark-font-scale");

        private readonly GameplayFieldPresenterState _state;
        private readonly Action _refreshLayout;

        public GameplayFieldPresenterStyleTokenResolver(
            GameplayFieldPresenterState state,
            Action refreshLayout)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _refreshLayout = refreshLayout ?? throw new ArgumentNullException(nameof(refreshLayout));
        }

        internal void Apply(ICustomStyle customStyle, bool validate)
        {
            if (customStyle == null)
                return;

            var changed = TryApplyGridGap(customStyle);
           
            changed |= TryApplyStyleToken(customStyle, _miniBoardGapHalfProperty, value =>
            {
                _state.MiniBoardGapHalf = value;
                _state.HasMiniBoardGapHalf = true;
            });
            
            changed |= TryApplyStyleToken(customStyle, _miniBoardBorderProperty, value =>
            {
                _state.MiniBoardBorder = value;
                _state.HasMiniBoardBorder = true;
            });
            
            changed |= TryApplyStyleToken(customStyle, _miniBoardPaddingProperty, value =>
            {
                _state.MiniBoardPadding = value;
                _state.HasMiniBoardPadding = true;
            });
            
            changed |= TryApplyStyleToken(customStyle, _markFontScaleProperty, value =>
            {
                _state.MarkFontScale = value;
                _state.HasMarkFontScale = true;
            });

            if (validate)
                ValidateRequiredStyleTokensIfPossible();

            if (!changed)
                return;

            _state.LastCellSize = 0;
            _refreshLayout();
        }

        private bool TryApplyGridGap(ICustomStyle customStyle)
        {
            if (customStyle.TryGetValue(_gridGapHalfProperty, out var gridGapHalf))
            {
                _state.GridGapHalf = gridGapHalf;
                _state.HasGridGapHalf = true;
                return true;
            }

            if (!customStyle.TryGetValue(_gridGapProperty, out var gridGap))
                return false;

            _state.GridGapHalf = gridGap / 2f;
            _state.HasGridGapHalf = true;
            return true;
        }

        private static bool TryApplyStyleToken(
            ICustomStyle customStyle,
            CustomStyleProperty<float> property,
            Action<float> apply)
        {
            if (!customStyle.TryGetValue(property, out var value))
                return false;

            apply(value);
            return true;
        }

        private void ValidateRequiredStyleTokensIfPossible()
        {
            if (_state.FieldRoot?.panel == null || _state.Spec == null)
                return;

            var missing = CollectMissingStyleTokens();

            if (missing.Count == 0)
                return;

            var message = $"[GameplayFieldPresenter] Missing required USS custom properties: {string.Join(", ", missing)}";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            throw new InvalidOperationException(message);
#else
            Log.Error(LogTags.UI, message);
#endif
        }

        private List<string> CollectMissingStyleTokens()
        {
            var missing = new List<string>(5);
            AddMissingTokenName(missing, _state.HasGridGapHalf, _gridGapHalfProperty.name);
            AddMissingTokenName(missing, _state.HasMarkFontScale, _markFontScaleProperty.name);

            if (_state.Spec?.Kind != FieldKind.Ultimate)
                return missing;

            AddMissingTokenName(missing, _state.HasMiniBoardGapHalf, _miniBoardGapHalfProperty.name);
            AddMissingTokenName(missing, _state.HasMiniBoardBorder, _miniBoardBorderProperty.name);
            AddMissingTokenName(missing, _state.HasMiniBoardPadding, _miniBoardPaddingProperty.name);
            return missing;
        }

        private static void AddMissingTokenName(List<string> missing, bool hasValue, string tokenName)
        {
            if (!hasValue)
                missing.Add(tokenName);
        }
    }
}