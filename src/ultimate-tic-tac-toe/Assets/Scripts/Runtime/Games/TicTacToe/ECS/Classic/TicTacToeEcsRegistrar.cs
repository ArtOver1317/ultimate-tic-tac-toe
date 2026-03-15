using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Registers TicTacToe-specific ECS systems and initializes board state on the match entity.
    /// Slot mapping: 0 = X, 1 = O.
    /// </summary>
    public sealed class TicTacToeEcsRegistrar : IEcsGameplayRegistrar
    {
        public const int SlotX = PlayerSlotMapping.SlotX;
        public const int SlotO = PlayerSlotMapping.SlotO;
        public const int PlayerCount = PlayerSlotMapping.PlayerCount;
        public const string TicTacToeGameId = TicTacToeStrategy.DefaultGameId;

        private readonly IRulesEngine _rulesEngine;

        public string GameId => TicTacToeGameId;

        public TicTacToeEcsRegistrar(IRulesEngine rulesEngine) =>
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));

        public void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
        {
            if (config.GameConfig is not TicTacToeConfig tttConfig)
            {
                throw new InvalidOperationException(
                    $"Expected TicTacToeConfig but got {config.GameConfig?.GetType().Name ?? "null"}.");
            }

            if (tttConfig.IsUltimate)
            {
                throw new NotSupportedException(
                    "Ultimate Tic-Tac-Toe is not yet supported in the ECS pipeline. " +
                    "Use classic mode (IsUltimate = false).");
            }

            // Initialize PlayersComponent (shared)
            var playersStash = world.GetStash<PlayersComponent>();
            
            playersStash.Set(matchEntity, new PlayersComponent
            {
                PlayerCount = PlayerCount,
                PlayerSlots = new[] { SlotX, SlotO },
                ActivePlayerSlot = SlotX, // X always starts first round
            });

            // Initialize FieldConfigComponent (shared)
            var fieldConfigStash = world.GetStash<FieldConfigComponent>();
            var spec = MapSpec(tttConfig);
            
            fieldConfigStash.Set(matchEntity, new FieldConfigComponent
            {
                Kind = spec.Kind,
                OuterSize = spec.OuterSize,
                InnerSize = spec.InnerSize,
            });

            var majorCount = spec.Kind == FieldKind.Classic ? spec.OuterSize : spec.OuterSize * spec.OuterSize;
            var minorCount = spec.Kind == FieldKind.Classic ? spec.OuterSize : spec.InnerSize * spec.InnerSize;
            BoardStateHelpers.InitializeBoard(world, matchEntity, majorCount, minorCount);

            // Add game-specific systems (order: validate → apply → rules evaluate)
            systemsGroup.AddSystem(new MoveValidationSystem());
            systemsGroup.AddSystem(new ApplyMoveSystem());
            systemsGroup.AddSystem(new RulesEvaluationSystem(_rulesEngine));
            systemsGroup.AddSystem(new RestartRoundSystem());
        }

        public void RegisterPostPublishSystems
            (World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config) { }

        private static FieldRenderSpec MapSpec(TicTacToeConfig config) =>
            config.IsUltimate
                ? FieldRenderSpec.Ultimate()
                : FieldRenderSpec.Classic(config.BoardSize);

        // -- IEcsGameplayRegistrar snapshot methods (ADR-3/ADR-9: game-specific reads) --

        public int GetCellSlot(World world, Entity matchEntity, CellId cellId) =>
            BoardStateHelpers.GetCellSlot(world, matchEntity, cellId);

        public IReadOnlyList<CellSnapshot> GetAllCells(World world, Entity matchEntity) =>
            BoardStateHelpers.GetAllCells(world, matchEntity);
    }
}