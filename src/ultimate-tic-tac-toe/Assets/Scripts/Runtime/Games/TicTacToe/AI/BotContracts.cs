#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI
{
    // ── RNG ──

    /// <summary>
    /// Bot-local RNG abstraction (ADR-3). Passed per-request to guarantee
    /// determinism and isolation in Bot vs Bot scenarios.
    /// </summary>
    public interface IBotRandom
    {
        float NextFloat01();
        int NextInt(int minInclusive, int maxExclusive);
    }

    // ── Win Length Provider ──

    /// <summary>
    /// Provides the K (win length) for a given board size (ADR-11).
    /// Implemented alongside ClassicRulesEngine; AI must NOT hard-code K.
    /// </summary>
    public interface IClassicWinLengthProvider
    {
        int GetWinLength(int boardSize);
    }

    // ── Decision Engine ──

    /// <summary>
    /// Input for <see cref="IBotDecisionEngine.ChooseMoveAsync"/>.
    /// <see cref="LegalMoves"/> has stable deterministic order (row-major by CellId).
    /// </summary>
    public readonly struct BotDecisionRequest
    {
        public int BoardSize { get; }
        public int WinLength { get; }
        public PlayerMark[] Cells { get; }
        public int ActivePlayerSlot { get; }
        public CellId? LastMove { get; }
        public IReadOnlyList<CellId> LegalMoves { get; }
        public long CommandSequence { get; }
        public IBotRandom Rng { get; }
        public BotSearchSettingsData? SearchSettingsOverride { get; }

        public BotDecisionRequest(
            int boardSize,
            int winLength,
            PlayerMark[] cells,
            int activePlayerSlot,
            CellId? lastMove,
            IReadOnlyList<CellId> legalMoves,
            long commandSequence,
            IBotRandom rng,
            BotSearchSettingsData? searchSettingsOverride = null)
        {
            BoardSize = boardSize;
            WinLength = winLength;
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
            ActivePlayerSlot = activePlayerSlot;
            LastMove = lastMove;
            LegalMoves = legalMoves ?? throw new ArgumentNullException(nameof(legalMoves));
            CommandSequence = commandSequence;
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            SearchSettingsOverride = searchSettingsOverride;
        }
    }

    /// <summary>
    /// Async decision engine contract (ADR-1, ADR-9).
    /// Accepts only <see cref="BotProfileData"/> — no ScriptableObject dependency.
    /// </summary>
    public interface IBotDecisionEngine
    {
        UniTask<CellId> ChooseMoveAsync(
            BotDecisionRequest request,
            BotProfileData profile,
            CancellationToken ct);
    }

    // ── Turn Driver ──

    public enum BotStartStatus
    {
        Started,
        NotEnabled,
        UnsupportedConfig,
        Failed,
    }

    public readonly struct BotStartResult
    {
        public BotStartStatus Status { get; }
        public string? Error { get; }

        public BotStartResult(BotStartStatus status, string? error = null)
        {
            Status = status;
            Error = error;
        }
    }

    /// <summary>
    /// Runtime driver that listens to gameplay events and submits bot moves (ADR-1, ADR-6, ADR-10).
    /// One driver per bot slot. Dispose cancels any in-flight computation.
    /// </summary>
    public interface IBotTurnDriver : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsBusy { get; }

        /// <summary>
        /// True when bot has been disabled after exhausting all retry attempts (ADR-12).
        /// GameplayStartup should subscribe and surface this to the user.
        /// </summary>
        ReadOnlyReactiveProperty<bool> IsDisabled { get; }

        UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            string difficultyId,
            CancellationToken ct);
    }

    // ── Profile Catalog ──

    /// <summary>
    /// Looks up <see cref="BotProfile"/> by difficulty id string.
    /// Backed by a ScriptableObject asset with serialized profile array.
    /// </summary>
    public interface IBotProfileCatalog
    {
        bool TryGet(string difficultyId, out BotProfile? profile);
    }
}
