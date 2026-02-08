using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    /// <summary>
    /// Cell sizing, custom USS style resolution, back navigation.
    /// </summary>
    public sealed partial class GameplayFieldPresenter
    {
        private void OnGeometryChanged(GeometryChangedEvent evt) => UpdateCellSizes(evt.newRect);

        private void UpdateCellSizes(Rect rect)
        {
            if (_spec == null || _fieldContainer == null)
                return;

            if (rect.width <= 0f || rect.height <= 0f)
                return;

            var columns = _spec.Kind == FieldKind.Classic
                ? _spec.OuterSize
                : _spec.OuterSize * _spec.InnerSize;

            if (columns <= 0)
                return;

            var cellSize = _spec.Kind == FieldKind.Classic
                ? CalculateClassicCellSize(rect, _spec.OuterSize)
                : CalculateUltimateCellSize(rect, _spec.OuterSize, _spec.InnerSize);
            
            if (cellSize <= 0)
                return;

            if (cellSize == _lastCellSize)
                return;

            _lastCellSize = cellSize;

            foreach (var cell in _cells)
            {
                cell.style.width = cellSize;
                cell.style.height = cellSize;
            }

            UpdateMarkFontSizes(cellSize);

            if (_spec.Kind == FieldKind.Ultimate)
            {
                foreach (var mini in _miniBoards)
                {
                    var miniSize = cellSize * _spec.InnerSize
                                   + _spec.InnerSize * (_gridGapHalf * 2f)
                                   + _miniBoardPadding * 2f
                                   + _miniBoardBorder * 2f;
                    
                    mini.style.width = miniSize;
                    mini.style.height = miniSize;
                }
            }
        }

        private void UpdateMarkFontSizes(int cellSize)
        {
            if (cellSize <= 0)
                return;

            var scale = _markFontScale;
            
            if (scale <= 0f)
                scale = 0.62f;

            var fontSize = Mathf.Max(1f, cellSize * scale);

            foreach (var label in _markLabelById.Values)
            {
                if (label == null)
                    continue;

                label.style.fontSize = fontSize;
            }
        }

        private void OnBackClicked()
        {
            if (_backInProgress)
                return;

            _backInProgress = true;
            // Important: do NOT tie back navigation to the presenter's bind CTS.
            // Scene transitions and MainMenu UI initialization can outlive this presenter and must not be cancelled.
            BackToModeSelectionAsync(CancellationToken.None).Forget();
        }

        private async UniTask BackToModeSelectionAsync(CancellationToken ct)
        {
            try
            {
                await _backHandler.HandleBackAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Expected when scene is unloading or presenter is unbound.
            }
            catch (Exception ex)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Failed to return to ModeSelection: {ex}");
            }
            finally
            {
                _backInProgress = false;
            }
        }

        private int CalculateClassicCellSize(Rect rect, int size)
        {
            var totalMarginX = size * (_gridGapHalf * 2f);
            var totalMarginY = size * (_gridGapHalf * 2f);
            var availableWidth = rect.width - totalMarginX;
            var availableHeight = rect.height - totalMarginY;

            if (availableWidth <= 0f || availableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(availableWidth / size, availableHeight / size));
        }

        private int CalculateUltimateCellSize(Rect rect, int outer, int inner)
        {
            var totalMiniMarginX = outer * (_miniBoardGapHalf * 2f);
            var totalMiniMarginY = outer * (_miniBoardGapHalf * 2f);
            var availableWidth = rect.width - totalMiniMarginX;
            var availableHeight = rect.height - totalMiniMarginY;

            if (availableWidth <= 0f || availableHeight <= 0f)
                return 0;

            var miniAvailableWidth = availableWidth / outer;
            var miniAvailableHeight = availableHeight / outer;

            var innerMargins = inner * (_gridGapHalf * 2f);
            var fixedExtras = _miniBoardPadding * 2f + _miniBoardBorder * 2f + innerMargins;

            var cellAvailableWidth = miniAvailableWidth - fixedExtras;
            var cellAvailableHeight = miniAvailableHeight - fixedExtras;

            if (cellAvailableWidth <= 0f || cellAvailableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(cellAvailableWidth / inner, cellAvailableHeight / inner));
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt) =>
            UpdateSpacingFromCustomStyle(evt.customStyle, validate: true);

        private void UpdateSpacingFromCustomStyle(ICustomStyle customStyle, bool validate)
        {
            if (customStyle == null)
                return;

            var changed = false;

            var gridGapHalfSet = false;
            
            if (customStyle.TryGetValue(_gridGapHalfProperty, out var gridGapHalf))
            {
                _gridGapHalf = gridGapHalf;
                gridGapHalfSet = true;
                _hasGridGapHalf = true;
                changed = true;
            }

            if (customStyle.TryGetValue(_gridGapProperty, out var gridGap))
            {
                if (!gridGapHalfSet)
                {
                    _gridGapHalf = gridGap / 2f;
                    _hasGridGapHalf = true;
                    changed = true;
                }
            }

            if (customStyle.TryGetValue(_miniBoardGapHalfProperty, out var miniGapHalf))
            {
                _miniBoardGapHalf = miniGapHalf;
                _hasMiniBoardGapHalf = true;
                changed = true;
            }

            if (customStyle.TryGetValue(_miniBoardBorderProperty, out var miniBorder))
            {
                _miniBoardBorder = miniBorder;
                _hasMiniBoardBorder = true;
                changed = true;
            }

            if (customStyle.TryGetValue(_miniBoardPaddingProperty, out var miniPadding))
            {
                _miniBoardPadding = miniPadding;
                _hasMiniBoardPadding = true;
                changed = true;
            }

            if (customStyle.TryGetValue(_markFontScaleProperty, out var markFontScale))
            {
                _markFontScale = markFontScale;
                _hasMarkFontScale = true;
                changed = true;
            }

            if (validate)
                ValidateRequiredStyleTokensIfPossible();

            if (changed)
            {
                _lastCellSize = 0;
                
                if (_fieldContainer != null)
                    UpdateCellSizes(_fieldContainer.contentRect);
            }
        }

        private void ValidateRequiredStyleTokensIfPossible()
        {
            // In EditMode tests (no panel) customStyle isn't reliably resolved.
            if (_fieldRoot?.panel == null)
                return;

            if (_spec == null)
                return;

            // Classic needs only grid spacing; Ultimate needs full set.
            var requireUltimateTokens = _spec.Kind == FieldKind.Ultimate;

            if (_hasGridGapHalf
                && _hasMarkFontScale
                && (!requireUltimateTokens || (_hasMiniBoardGapHalf && _hasMiniBoardBorder && _hasMiniBoardPadding)))
                return;

            var missing = new List<string>(4);
            
            if (!_hasGridGapHalf)
                missing.Add(_gridGapHalfProperty.name);
            
            if (!_hasMarkFontScale)
                missing.Add(_markFontScaleProperty.name);
            
            if (requireUltimateTokens && !_hasMiniBoardGapHalf)
                missing.Add(_miniBoardGapHalfProperty.name);
            
            if (requireUltimateTokens && !_hasMiniBoardBorder)
                missing.Add(_miniBoardBorderProperty.name);
            
            if (requireUltimateTokens && !_hasMiniBoardPadding)
                missing.Add(_miniBoardPaddingProperty.name);

            var message = $"[GameplayFieldPresenter] Missing required USS custom properties: {string.Join(", ", missing)}";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            throw new InvalidOperationException(message);
#else
            Log.Error(LogTags.UI, message);
#endif
        }
    }
}
