#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Games.TicTacToe.AI;

namespace Runtime.Games.Battleship.AI
{
    public interface IBattleshipBotDriver : IDisposable
    {
        ReadOnlyReactiveProperty<bool> IsThinking { get; }
        bool IsStarted { get; }
        int BotSlot { get; }

        UniTask<BotStartResult> StartAsync(
            GameLaunchConfig config,
            int botSlot,
            CancellationToken ct);
    }
}