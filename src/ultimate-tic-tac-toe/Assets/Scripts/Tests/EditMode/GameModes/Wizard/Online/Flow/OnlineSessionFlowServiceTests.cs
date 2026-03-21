#nullable enable

using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Online.Flow;

namespace Tests.EditMode.GameModes.Wizard.Online.Flow
{
    [TestFixture]
    [Category("Unit")]
    public partial class OnlineSessionFlowServiceTests
    {
        private static readonly OnlineFlowState[] _activeStableStates =
        {
            OnlineFlowState.WaitingForPlayer,
            OnlineFlowState.ConnectedCountdown,
            OnlineFlowState.InGame,
            OnlineFlowState.Result,
        };

        private static async Task BringToStateAsync(OnlineSessionFlowService sut, OnlineFlowState targetState)
        {
            await sut.EnterHumanSetupAsync("eu", "host");

            if (targetState == OnlineFlowState.Idle)
                return;

            if (targetState == OnlineFlowState.HostIntentConfirmed)
            {
                await sut.ConfirmHostIntentAsync();
                return;
            }

            if (targetState == OnlineFlowState.GuestConnecting)
            {
                await sut.JoinBySessionIdAsync("AB2CD7", "eu", "guest");
                return;
            }

            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));

            if (targetState == OnlineFlowState.HostStarting)
                return;

            await sut.OnHostCreatedAsync();

            if (targetState == OnlineFlowState.WaitingForPlayer)
                return;

            await sut.OnGuestJoinedAsync();

            if (targetState == OnlineFlowState.ConnectedCountdown)
                return;

            await sut.OnGameplayEnteredAsync();

            if (targetState == OnlineFlowState.InGame)
                return;

            await sut.OnRoundCompletedAsync();
        }

        private static async Task ExecuteSameTickAsync(OnlineSessionFlowService sut, Func<UniTask> first, Func<UniTask> second)
        {
            using (sut.HoldEventQueueForTests())
            {
                await first();
                await second();
            }

            await sut.DrainEventQueueForTestsAsync();
        }

        private static OnlineSessionFlowService CreateService(params string[] candidates)
        {
            var index = 0;
            var lifecycle = new OnlineSessionIdLifecycle(() => candidates[index++]);
            return new OnlineSessionFlowService(lifecycle);
        }
    }
}