using System;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Runtime.Games.TicTacToe.Ultimate.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    /// <summary>
    /// Geometry data for positioning the win-line overlay.
    /// Pure value type — testable without the rendering pipeline.
    /// </summary>
    internal struct WinLineGeometry
    {
        public float Left;
        public float Top;
        public float Width;
        public float RotationDeg;
    }

    /// <summary>
    /// Renders a victory line overlay on top of the field grid.
    /// Uses UI Toolkit position:absolute VisualElement inside FieldContainer.
    /// Recalculates on <see cref="GeometryChangedEvent"/> for layout race protection.
    /// </summary>
    public sealed class WinLineRenderer : IDisposable
    {
        private const float LineThickness = 6f;

        /// <summary>
        /// How far the line extends beyond cell centers, as a fraction of cell width.
        /// </summary>
        private const float ExtensionFraction = 0.35f;

        private readonly IGameplayFieldUiAdapter _fieldUiAdapter;

        private VisualElement _lineElement;
        private VisualElement _overlayParent;
        private WinLine? _currentWinLine;
        private UltimateBigBoardWinLine? _currentUltimateWinLine;
        private IUltimateGameplayFieldUiAdapter _ultimateUiAdapter;

        public WinLineRenderer(IGameplayFieldUiAdapter fieldUiAdapter)
        {
            _fieldUiAdapter = fieldUiAdapter ?? throw new ArgumentNullException(nameof(fieldUiAdapter));
        }

        /// <summary>
        /// Shows the win line overlay from <paramref name="winLine"/>.Start to .End.
        /// Safe to call when cells are not found — silently does nothing.
        /// </summary>
        public void Show(WinLine winLine)
        {
            Clear();

            var container = _fieldUiAdapter.FieldContainer;
            if (container == null)
                return;

            if (!_fieldUiAdapter.TryGetCell(winLine.Start, out var startCell) ||
                !_fieldUiAdapter.TryGetCell(winLine.End, out var endCell))
                return;

            _currentWinLine = winLine;
            _currentUltimateWinLine = null;
            _ultimateUiAdapter = null;
            _overlayParent = container;

            _lineElement = new VisualElement { name = "WinLine" };
            _lineElement.AddToClassList("win-line");
            _lineElement.pickingMode = PickingMode.Ignore;

            // Visual layering: ResultOverlay (popup) should always be above win-line.
            // Both elements live under FieldContainer, so we must NOT add win-line as the last child.
            // Instead, insert it right before ResultOverlay when it exists.
            var resultOverlay = _overlayParent.Q<VisualElement>("ResultOverlay");
            if (resultOverlay != null)
            {
                var idx = _overlayParent.IndexOf(resultOverlay);
                if (idx >= 0)
                    _overlayParent.Insert(idx, _lineElement);
                else
                    _overlayParent.Add(_lineElement);
            }
            else
            {
                _overlayParent.Add(_lineElement);
            }
            UpdateLineGeometry(startCell, endCell);

            // Defer class toggle to next frame tick so CSS opacity transition fires.
            _lineElement.schedule.Execute(() => _lineElement?.AddToClassList("win-line--visible"));

            _overlayParent.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public void ShowUltimate(UltimateBigBoardWinLine bigBoardWinLine, IUltimateGameplayFieldUiAdapter ultimateUiAdapter)
        {
            if (ultimateUiAdapter == null)
                throw new ArgumentNullException(nameof(ultimateUiAdapter));

            Clear();

            var container = _fieldUiAdapter.FieldContainer;
            if (container == null)
                return;

            if (!ultimateUiAdapter.TryGetMiniBoardCenter(bigBoardWinLine.Major0, out var startPanelCenter) ||
                !ultimateUiAdapter.TryGetMiniBoardCenter(bigBoardWinLine.Major2, out var endPanelCenter))
            {
                return;
            }

            _currentWinLine = null;
            _currentUltimateWinLine = bigBoardWinLine;
            _ultimateUiAdapter = ultimateUiAdapter;
            _overlayParent = container;

            _lineElement = new VisualElement { name = "WinLine" };
            _lineElement.AddToClassList("win-line");
            _lineElement.pickingMode = PickingMode.Ignore;

            var resultOverlay = _overlayParent.Q<VisualElement>("ResultOverlay");
            if (resultOverlay != null)
            {
                var idx = _overlayParent.IndexOf(resultOverlay);
                if (idx >= 0)
                    _overlayParent.Insert(idx, _lineElement);
                else
                    _overlayParent.Add(_lineElement);
            }
            else
            {
                _overlayParent.Add(_lineElement);
            }

            UpdateUltimateLineGeometry(bigBoardWinLine, startPanelCenter, endPanelCenter, ultimateUiAdapter);

            _lineElement.schedule.Execute(() => _lineElement?.AddToClassList("win-line--visible"));
            _overlayParent.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>
        /// Removes the win line overlay. Safe to call multiple times or before Show.
        /// </summary>
        public void Clear()
        {
            if (_overlayParent != null)
                _overlayParent.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            _lineElement?.RemoveFromHierarchy();
            _lineElement = null;
            _currentWinLine = null;
            _currentUltimateWinLine = null;
            _ultimateUiAdapter = null;
            _overlayParent = null;
        }

        /// <inheritdoc />
        public void Dispose() => Clear();

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_currentWinLine == null || _lineElement == null || _overlayParent == null)
            {
                if (_currentUltimateWinLine == null || _lineElement == null || _overlayParent == null || _ultimateUiAdapter == null)
                    return;

                var ultimateLine = _currentUltimateWinLine.Value;
                if (!_ultimateUiAdapter.TryGetMiniBoardCenter(ultimateLine.Major0, out var startCenter) ||
                    !_ultimateUiAdapter.TryGetMiniBoardCenter(ultimateLine.Major2, out var endCenter))
                {
                    return;
                }

                UpdateUltimateLineGeometry(ultimateLine, startCenter, endCenter, _ultimateUiAdapter);
                return;
            }

            var winLine = _currentWinLine.Value;
            if (!_fieldUiAdapter.TryGetCell(winLine.Start, out var startCell) ||
                !_fieldUiAdapter.TryGetCell(winLine.End, out var endCell))
                return;

            UpdateLineGeometry(startCell, endCell);
        }

        private void UpdateLineGeometry(VisualElement startCell, VisualElement endCell)
        {
            // worldBound requires a rendered panel with completed layout.
            // In EditMode tests or before first layout pass, panel is null.
            if (startCell.panel == null || endCell.panel == null || _overlayParent.panel == null)
                return;

            // Guard: skip until layout is computed (GeometryChangedEvent will retry).
            if (startCell.worldBound.width <= 0 || endCell.worldBound.width <= 0)
                return;

            var startCenter = _overlayParent.WorldToLocal(startCell.worldBound.center);
            var endCenter = _overlayParent.WorldToLocal(endCell.worldBound.center);

            var cellWidth = startCell.worldBound.width;
            var extension = cellWidth > 0f ? cellWidth * ExtensionFraction : 0f;

            var geo = CalculateGeometry(startCenter, endCenter, LineThickness, extension);

            ApplyGeometry(geo);
        }

        private void UpdateUltimateLineGeometry(
            UltimateBigBoardWinLine bigBoardWinLine,
            Vector2 startPanelCenter,
            Vector2 endPanelCenter,
            IUltimateGameplayFieldUiAdapter ultimateUiAdapter)
        {
            if (_overlayParent.panel == null)
                return;

            var startCenter = _overlayParent.WorldToLocal(startPanelCenter);
            var endCenter = _overlayParent.WorldToLocal(endPanelCenter);

            float referenceWidth = 0f;
            if (ultimateUiAdapter.TryGetMiniBoard(bigBoardWinLine.Major0, out var startMini) && startMini != null)
                referenceWidth = startMini.worldBound.width;

            var extension = referenceWidth > 0f ? referenceWidth * ExtensionFraction : 0f;
            var geo = CalculateGeometry(startCenter, endCenter, LineThickness, extension);

            ApplyGeometry(geo);
        }

        private void ApplyGeometry(WinLineGeometry geo)
        {
            if (_lineElement == null)
                return;

            _lineElement.style.position = Position.Absolute;
            _lineElement.style.width = geo.Width;
            _lineElement.style.height = LineThickness;
            _lineElement.style.left = geo.Left;
            _lineElement.style.top = geo.Top;
            _lineElement.style.rotate = new StyleRotate(new Rotate(new Angle(geo.RotationDeg, AngleUnit.Degree)));
        }

        /// <summary>
        /// Pure geometry: computes position, size, and rotation for a line between two centers.
        /// Internal for unit testing.
        /// </summary>
        internal static WinLineGeometry CalculateGeometry(
            Vector2 startCenter, Vector2 endCenter, float thickness, float extension)
        {
            var dx = endCenter.x - startCenter.x;
            var dy = endCenter.y - startCenter.y;
            var distance = Mathf.Sqrt(dx * dx + dy * dy);
            var angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            var totalWidth = distance + extension * 2f;
            var midX = (startCenter.x + endCenter.x) * 0.5f;
            var midY = (startCenter.y + endCenter.y) * 0.5f;

            return new WinLineGeometry
            {
                Left = midX - totalWidth * 0.5f,
                Top = midY - thickness * 0.5f,
                Width = totalWidth,
                RotationDeg = angleDeg,
            };
        }
    }
}
