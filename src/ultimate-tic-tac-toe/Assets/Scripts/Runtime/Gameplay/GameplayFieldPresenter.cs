using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    public sealed class GameplayFieldPresenter : IGameplayFieldPresenter
    {
        private readonly UIDocument _uiDocument;
        private readonly IGameplayBackHandler _backHandler;
        private readonly List<VisualElement> _cells = new();
        private readonly List<VisualElement> _miniBoards = new();
        private VisualElement _root;
        private VisualElement _fieldRoot;
        private VisualElement _fieldContainer;
        private Button _backButton;
        private FieldRenderSpec _spec;
        private bool _isBound;
        private bool _disposed;
        private bool _backInProgress;
        private int _lastCellSize;
        private bool _customStyleRegistered;
        private CancellationTokenSource _bindCts;

        private float _gridGapHalf = 3f;
        private float _miniBoardGapHalf = 6f;
        private float _miniBoardBorder = 2f;
        private float _miniBoardPadding = 5f;

        private static readonly CustomStyleProperty<float> GridGapHalfProperty = new("--grid-gap-half");
        private static readonly CustomStyleProperty<float> GridGapProperty = new("--grid-gap");
        private static readonly CustomStyleProperty<float> MiniBoardGapHalfProperty = new("--mini-board-gap-half");
        private static readonly CustomStyleProperty<float> MiniBoardBorderProperty = new("--mini-board-border-width");
        private static readonly CustomStyleProperty<float> MiniBoardPaddingProperty = new("--mini-board-padding");

        public GameplayFieldPresenter(
            UIDocument uiDocument,
            IGameplayBackHandler backHandler)
        {
            _uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
        }

        public UniTask BindAsync(FieldRenderSpec spec, CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameplayFieldPresenter));
            if (spec == null)
                throw new ArgumentNullException(nameof(spec));

            ct.ThrowIfCancellationRequested();

            if (_isBound)
                Unbind();

            _spec = spec;

            try
            {
                BuildVisualTree();
                _isBound = true;
                _bindCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                UpdateCellSizes(_fieldContainer.contentRect);
            }
            catch
            {
                CleanupBindings();
                _bindCts?.Cancel();
                _bindCts?.Dispose();
                _bindCts = null;
                _spec = null;
                _isBound = false;
                throw;
            }

            return UniTask.CompletedTask;
        }

        public void Unbind()
        {
            if (!_isBound)
                return;

            CleanupBindings();

            _cells.Clear();
            _miniBoards.Clear();
            _fieldContainer?.Clear();
            _backButton = null;
            _spec = null;
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;
            _isBound = false;
            _lastCellSize = 0;
        }

        private void CleanupBindings()
        {
            if (_fieldContainer != null)
                _fieldContainer.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            if (_fieldRoot != null && _customStyleRegistered)
            {
                _fieldRoot.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                _customStyleRegistered = false;
            }

            if (_backButton != null)
                _backButton.clicked -= OnBackClicked;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unbind();
        }

        private void BuildVisualTree()
        {
            var stopwatch = Stopwatch.StartNew();

            _root = _uiDocument.rootVisualElement ?? throw new InvalidOperationException("UIDocument root is null.");
            var fieldRoot = _root.Q<VisualElement>("GameplayFieldRoot");

            if (fieldRoot == null)
            {
                fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
                fieldRoot.AddToClassList("gameplay-field-root");
                _root.Add(fieldRoot);
            }

            _fieldRoot = fieldRoot;
            if (!_customStyleRegistered)
            {
                _fieldRoot.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                _customStyleRegistered = true;
            }

            UpdateSpacingFromCustomStyle(_fieldRoot.customStyle);

            _fieldContainer = _fieldRoot.Q<VisualElement>("FieldContainer");
            if (_fieldContainer == null)
            {
                _fieldContainer = new VisualElement { name = "FieldContainer" };
                _fieldContainer.AddToClassList("field-container");
                _fieldRoot.Add(_fieldContainer);
            }

            _backButton = _fieldRoot.Q<Button>("BackButton");
            if (_backButton != null)
                _backButton.clicked += OnBackClicked;
            else
                Log.Error(LogTags.UI, "[GameplayFieldPresenter] BackButton not found.");

            _fieldContainer.Clear();
            _fieldContainer.RemoveFromClassList("field-container--classic");
            _fieldContainer.RemoveFromClassList("field-container--ultimate");

            if (_spec.Kind == FieldKind.Classic)
            {
                _fieldContainer.AddToClassList("field-container--classic");
                BuildClassic(_spec);
            }
            else
            {
                _fieldContainer.AddToClassList("field-container--ultimate");
                BuildUltimate(_spec);
            }

            _fieldContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            stopwatch.Stop();
            Log.Info(LogTags.UI, $"[GameplayFieldPresenter] Field build: {_spec.Kind}, {stopwatch.ElapsedMilliseconds} ms");
        }

        private void BuildClassic(FieldRenderSpec spec)
        {
            var size = spec.OuterSize;
            for (var y = 0; y < size; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("field-row");

                for (var x = 0; x < size; x++)
                {
                    var cell = CreateCell(x, y);
                    row.Add(cell);
                    _cells.Add(cell);
                }

                _fieldContainer.Add(row);
            }
        }

        private void BuildUltimate(FieldRenderSpec spec)
        {
            var outer = spec.OuterSize;
            var inner = spec.InnerSize;

            for (var miniY = 0; miniY < outer; miniY++)
            {
                var miniRow = new VisualElement();
                miniRow.AddToClassList("mini-row");

                for (var miniX = 0; miniX < outer; miniX++)
                {
                    var mini = new VisualElement { name = $"Mini_{miniX}_{miniY}" };
                    mini.AddToClassList("mini-board");

                    for (var y = 0; y < inner; y++)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("mini-row-inner");

                        for (var x = 0; x < inner; x++)
                        {
                            var cell = CreateCell(x, y);
                            row.Add(cell);
                            _cells.Add(cell);
                        }

                        mini.Add(row);
                    }

                    miniRow.Add(mini);
                    _miniBoards.Add(mini);
                }

                _fieldContainer.Add(miniRow);
            }
        }

        private static VisualElement CreateCell(int x, int y)
        {
            var cell = new VisualElement { name = $"Cell_{x}_{y}" };
            cell.AddToClassList("cell");

            var mark = new VisualElement { name = "Mark" };
            mark.AddToClassList("cell-mark");
            cell.Add(mark);

            return cell;
        }

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
            var rows = columns;

            if (columns <= 0 || rows <= 0)
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

            if (_spec.Kind == FieldKind.Ultimate)
            {
                foreach (var mini in _miniBoards)
                {
                    var miniSize = cellSize * _spec.InnerSize
                                   + (_spec.InnerSize * (_gridGapHalf * 2f))
                                   + (_miniBoardPadding * 2f)
                                   + (_miniBoardBorder * 2f);
                    mini.style.width = miniSize;
                    mini.style.height = miniSize;
                }
            }
        }

        private void OnBackClicked()
        {
            if (_backInProgress)
                return;

            _backInProgress = true;
            BackToModeSelectionAsync().Forget();
        }

        private async UniTask BackToModeSelectionAsync()
        {
            try
            {
                await _backHandler.HandleBackAsync(CancellationToken.None);
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
            var fixedExtras = (_miniBoardPadding * 2f) + (_miniBoardBorder * 2f) + innerMargins;

            var cellAvailableWidth = miniAvailableWidth - fixedExtras;
            var cellAvailableHeight = miniAvailableHeight - fixedExtras;

            if (cellAvailableWidth <= 0f || cellAvailableHeight <= 0f)
                return 0;

            return Mathf.FloorToInt(Mathf.Min(cellAvailableWidth / inner, cellAvailableHeight / inner));
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt) =>
            UpdateSpacingFromCustomStyle(evt.customStyle);

        private void UpdateSpacingFromCustomStyle(ICustomStyle customStyle)
        {
            if (customStyle == null)
                return;

            var changed = false;

            var gridGapHalfSet = false;
            if (customStyle.TryGetValue(GridGapHalfProperty, out var gridGapHalf))
            {
                _gridGapHalf = gridGapHalf;
                gridGapHalfSet = true;
                changed = true;
            }

            if (customStyle.TryGetValue(GridGapProperty, out var gridGap))
            {
                if (!gridGapHalfSet)
                {
                    var normalized = gridGap / 2f;
                    _gridGapHalf = normalized > 0f ? normalized : _gridGapHalf;
                    changed = true;
                }
            }

            if (customStyle.TryGetValue(MiniBoardGapHalfProperty, out var miniGapHalf))
            {
                _miniBoardGapHalf = miniGapHalf;
                changed = true;
            }

            if (customStyle.TryGetValue(MiniBoardBorderProperty, out var miniBorder))
            {
                _miniBoardBorder = miniBorder;
                changed = true;
            }

            if (customStyle.TryGetValue(MiniBoardPaddingProperty, out var miniPadding))
            {
                _miniBoardPadding = miniPadding;
                changed = true;
            }

            if (changed)
            {
                _lastCellSize = 0;
                if (_fieldContainer != null)
                    UpdateCellSizes(_fieldContainer.contentRect);
            }
        }
    }
}
