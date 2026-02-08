using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Gameplay
{
    public sealed class GameplayFieldPresenter : IGameplayFieldPresenter, IGameplayFieldUiAdapter
    {
        private readonly UIDocument _uiDocument;
        private readonly IGameplayBackHandler _backHandler;
        private readonly List<VisualElement> _cells = new();
        private readonly List<VisualElement> _miniBoards = new();
        private readonly Dictionary<CellId, VisualElement> _cellById = new();
        private readonly Dictionary<CellId, VisualElement> _markById = new();
        private readonly Dictionary<CellId, Label> _markLabelById = new();
        private VisualElement _root;
        private VisualElement _fieldRoot;
        private VisualElement _fieldContainer;
        private VisualElement _customStyleCallbackElement;
        private Button _backButton;
        private FieldRenderSpec _spec;
        private bool _isBound;
        private bool _disposed;
        private bool _backInProgress;
        private int _lastCellSize;
        private bool _isCellIdCacheValid;
        private CancellationTokenSource _bindCts;

        private readonly Subject<CellId> _cellClicks = new();
        private Label _currentPlayerLabel;

        private float _gridGapHalf;
        private float _miniBoardGapHalf;
        private float _miniBoardBorder;
        private float _miniBoardPadding;
        private float _markFontScale;

        private bool _hasGridGapHalf;
        private bool _hasMiniBoardGapHalf;
        private bool _hasMiniBoardBorder;
        private bool _hasMiniBoardPadding;

        private bool _hasMarkFontScale;

        private static readonly CustomStyleProperty<float> _gridGapHalfProperty = new("--grid-gap-half");
        private static readonly CustomStyleProperty<float> _gridGapProperty = new("--grid-gap");
        private static readonly CustomStyleProperty<float> _miniBoardGapHalfProperty = new("--mini-board-gap-half");
        private static readonly CustomStyleProperty<float> _miniBoardBorderProperty = new("--mini-board-border-width");
        private static readonly CustomStyleProperty<float> _miniBoardPaddingProperty = new("--mini-board-padding");
        private static readonly CustomStyleProperty<float> _markFontScaleProperty = new("--mark-font-scale");

        public GameplayFieldPresenter(
            UIDocument uiDocument,
            IGameplayBackHandler backHandler)
        {
            _uiDocument = uiDocument ? uiDocument : throw new ArgumentNullException(nameof(uiDocument));
            _backHandler = backHandler ?? throw new ArgumentNullException(nameof(backHandler));
        }

        Observable<CellId> IGameplayFieldUiAdapter.CellClicks => _cellClicks;

        bool IGameplayFieldUiAdapter.TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
        {
            cellRoot = null;
            markLabel = null;

            if (!TryGetCell(id, out cellRoot) || cellRoot == null)
                return false;

            return _markLabelById.TryGetValue(id, out markLabel) && markLabel != null;
        }

        Label IGameplayFieldUiAdapter.CurrentPlayerLabel
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(GameplayFieldPresenter));

                if (!_isBound)
                    throw new InvalidOperationException("GameplayFieldPresenter is not bound (CurrentPlayerLabel is unavailable).");

                if (_currentPlayerLabel != null)
                    return _currentPlayerLabel;

                EnsureCurrentPlayerLabelExists();
                return _currentPlayerLabel;
            }
        }

        bool IGameplayFieldUiAdapter.TryGetCell(CellId id, out VisualElement cellRoot) =>
            TryGetCell(id, out cellRoot);

        bool IGameplayFieldUiAdapter.TryGetMark(CellId id, out VisualElement mark) =>
            TryGetMark(id, out mark);

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
            ResetStyleTokenState();

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
                _currentPlayerLabel = null;
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
            _cellById.Clear();
            _markById.Clear();
            _markLabelById.Clear();
            _isCellIdCacheValid = false;
            _fieldContainer?.Clear();
            _backButton = null;
            _spec = null;
            _currentPlayerLabel = null;
            ResetStyleTokenState();
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;
            _isBound = false;
            _lastCellSize = 0;
        }

        private void ResetStyleTokenState()
        {
            _gridGapHalf = 0f;
            _miniBoardGapHalf = 0f;
            _miniBoardBorder = 0f;
            _miniBoardPadding = 0f;
            _markFontScale = 0.62f;

            _hasGridGapHalf = false;
            _hasMiniBoardGapHalf = false;
            _hasMiniBoardBorder = false;
            _hasMiniBoardPadding = false;
            _hasMarkFontScale = false;
        }

        private void CleanupBindings()
        {
            _fieldContainer?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            if (_customStyleCallbackElement != null)
            {
                _customStyleCallbackElement.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                _customStyleCallbackElement = null;
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

            _cellClicks.OnCompleted();
            _cellClicks.Dispose();
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

            fieldRoot.AddToClassList("gameplay-field-root");

            _fieldRoot = fieldRoot;
            EnsureCustomStyleCallbackRegistered(_fieldRoot);

            UpdateSpacingFromCustomStyle(_fieldRoot.customStyle, validate: false);

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

            EnsureCurrentPlayerLabelExists();

            _fieldContainer.Clear();
            _fieldContainer.RemoveFromClassList("field-container--classic");
            _fieldContainer.RemoveFromClassList("field-container--ultimate");

            _cellById.Clear();
            _markById.Clear();
            _markLabelById.Clear();
            _isCellIdCacheValid = true;

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

        private void EnsureCurrentPlayerLabelExists()
        {
            if (_fieldRoot == null)
                return;

            var existing = _fieldRoot.Q<Label>("CurrentPlayerLabel");
            
            if (existing != null)
            {
                _currentPlayerLabel = existing;
                return;
            }

            var toolbar = _fieldRoot.Q<VisualElement>("GameplayToolbar") ?? _fieldRoot;

            var label = new Label
            {
                name = "CurrentPlayerLabel",
                text = string.Empty,
            };
            
            label.AddToClassList("current-player-label");

            toolbar.Add(label);
            _currentPlayerLabel = label;
        }

        internal bool TryGetCell(CellId id, out VisualElement cellRoot)
        {
            if (!_isBound || _disposed || _spec == null || !_isCellIdCacheValid)
            {
                cellRoot = null;
                return false;
            }

            return _cellById.TryGetValue(id, out cellRoot);
        }

        internal bool TryGetMark(CellId id, out VisualElement mark)
        {
            if (!_isBound || _disposed || _spec == null || !_isCellIdCacheValid)
            {
                mark = null;
                return false;
            }

            return _markById.TryGetValue(id, out mark);
        }

        private void EnsureCustomStyleCallbackRegistered(VisualElement fieldRoot)
        {
            if (fieldRoot == null)
                return;

            if (ReferenceEquals(_customStyleCallbackElement, fieldRoot))
                return;

            _customStyleCallbackElement?.UnregisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            _customStyleCallbackElement = fieldRoot;
            _customStyleCallbackElement.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
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
                    var cellId = new CellId(x, y);
                    var cell = CreateCell(x, y, cellId);
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

                    var miniIndex = miniY * outer + miniX;

                    for (var y = 0; y < inner; y++)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("mini-row-inner");

                        for (var x = 0; x < inner; x++)
                        {
                            var minor = y * inner + x;
                            var cellId = new CellId(miniIndex, minor);
                            var cell = CreateCell(x, y, cellId);
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

        private VisualElement CreateCell(int x, int y, CellId cellId)
        {
            var cell = new VisualElement { name = $"Cell_{x}_{y}" };
            cell.AddToClassList("cell");

            cell.userData = new CellUserData(cellId);

            cell.AddManipulator(new Clickable(() => OnCellClicked(cell)));
            
            try
            {
                _cellById.Add(cellId, cell);
            }
            catch (ArgumentException)
            {
                Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected: {cellId}");

                // Don't keep partially broken cache state.
                _cellById.Clear();
                _markById.Clear();
                _markLabelById.Clear();
                _isCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException($"Duplicate CellId detected while building field: {cellId}");
#endif
            }

            var mark = new VisualElement { name = "Mark" };
            mark.AddToClassList("cell-mark");

            var markLabel = new Label { name = "MarkLabel" };
            mark.Add(markLabel);
            cell.Add(mark);

            if (_isCellIdCacheValid)
            {
                try
                {
                    _markById.Add(cellId, mark);
                    _markLabelById.Add(cellId, markLabel);
                }
                catch (ArgumentException)
                {
                    Log.Error(LogTags.UI, $"[GameplayFieldPresenter] Duplicate CellId detected (mark cache): {cellId}");

                    _cellById.Clear();
                    _markById.Clear();
                    _markLabelById.Clear();
                    _isCellIdCacheValid = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    throw new InvalidOperationException($"Duplicate CellId detected while building field mark cache: {cellId}");
#endif
                }
            }

            return cell;
        }

        internal void EmitCellClick(CellId cellId)
        {
            if (!_isBound || _disposed)
                return;

            _cellClicks.OnNext(cellId);
        }

        internal void OnCellClicked(VisualElement cell)
        {
            if (!_isBound || _disposed || cell?.userData is not CellUserData userData)
                return;

            EmitCellClick(userData.CellId);
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