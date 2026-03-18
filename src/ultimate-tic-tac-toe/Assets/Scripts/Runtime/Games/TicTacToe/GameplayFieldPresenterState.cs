using System.Collections.Generic;
using System.Threading;
using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.Games.TicTacToe
{
    internal enum GameplayFieldPresenterMode
    {
        Classic,
        Ultimate,
        BattleshipDual,
    }

    internal sealed class GameplayFieldPresenterState
    {
        internal readonly List<VisualElement> Cells = new();
        internal readonly List<VisualElement> MiniBoards = new();
        internal readonly Dictionary<CellId, VisualElement> CellById = new();
        internal readonly Dictionary<CellId, VisualElement> MarkById = new();
        internal readonly Dictionary<CellId, Label> MarkLabelById = new();
        internal readonly Dictionary<CellId, VisualElement> OwnBoardCellById = new();
        internal readonly Dictionary<CellId, Label> OwnBoardMarkLabelById = new();
        internal readonly Dictionary<int, VisualElement> MiniBoardByMajor = new();
        internal readonly Dictionary<int, Vector2> MiniBoardCenterByMajor = new();

        internal VisualElement Root;
        internal VisualElement FieldRoot;
        internal VisualElement FieldContainer;
        internal VisualElement BattleshipBoardsRoot;
        internal VisualElement CustomStyleCallbackElement;
        internal Button BackButton;
        internal FieldRenderSpec Spec;
        internal bool IsBound;
        internal int LastCellSize;
        internal bool IsCellIdCacheValid;
        internal GameplayFieldPresenterMode CurrentMode;
        internal CancellationTokenSource BindCts;
        internal Label CurrentPlayerLabel;
        internal VisualElement Player1Panel;
        internal VisualElement Player2Panel;
        internal Label Player1ScoreLabel;
        internal Label Player2ScoreLabel;
        internal Label Player1NameLabel;
        internal Label Player2NameLabel;
        internal Label DrawsScoreLabel;
        internal Label MoveTimerLabel;

        internal float GridGapHalf;
        internal float MiniBoardGapHalf;
        internal float MiniBoardBorder;
        internal float MiniBoardPadding;
        internal float MarkFontScale;

        internal bool HasGridGapHalf;
        internal bool HasMiniBoardGapHalf;
        internal bool HasMiniBoardBorder;
        internal bool HasMiniBoardPadding;
        internal bool HasMarkFontScale;

        internal void ClearCellCaches()
        {
            Cells.Clear();
            MiniBoards.Clear();
            CellById.Clear();
            MarkById.Clear();
            MarkLabelById.Clear();
            OwnBoardCellById.Clear();
            OwnBoardMarkLabelById.Clear();
            MiniBoardByMajor.Clear();
            MiniBoardCenterByMajor.Clear();
            IsCellIdCacheValid = false;
        }

        internal void ResetStyleTokenState()
        {
            GridGapHalf = 0f;
            MiniBoardGapHalf = 0f;
            MiniBoardBorder = 0f;
            MiniBoardPadding = 0f;
            MarkFontScale = 0.62f;

            HasGridGapHalf = false;
            HasMiniBoardGapHalf = false;
            HasMiniBoardBorder = false;
            HasMiniBoardPadding = false;
            HasMarkFontScale = false;
        }
    }
}