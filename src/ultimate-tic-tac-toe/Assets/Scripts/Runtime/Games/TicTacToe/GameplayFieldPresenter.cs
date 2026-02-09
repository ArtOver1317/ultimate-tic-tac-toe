using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

using Runtime.Gameplay;
namespace Runtime.Games.TicTacToe
{
    public sealed partial class GameplayFieldPresenter : IGameplayFieldPresenter, IGameplayFieldUiAdapter
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

    }
}