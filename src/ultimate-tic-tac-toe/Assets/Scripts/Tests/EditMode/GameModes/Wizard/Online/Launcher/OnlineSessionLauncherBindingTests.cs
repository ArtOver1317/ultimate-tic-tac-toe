#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;
using Runtime.PlayerProfile;

namespace Tests.EditMode.GameModes.Wizard.Online.Launcher
{
    public partial class OnlineSessionLauncherTests
    {
        [Test]
        public void WhenPlayerNamePayloadReceivedBeforeGameplayBind_ThenBindingAppliesBufferedNameToStore()
        {
            using var harness = CreateHarness();
            var onlineStore = new OnlinePlayerNamesStore();

            harness.ContextStore.SetDirectInviteSession("ABCDEF", harness.LocalUserId, isHost: false);
            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|H|1|HostName"));

            harness.Launcher.BindMatchPlayerNamesStore(onlineStore);

            onlineStore.Snapshot.CurrentValue.HostCustomName.Should().Be("HostName");
            onlineStore.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();
        }

        [Test]
        public void WhenUnbindCalled_ThenBufferedNameIsClearedAndDoesNotLeakToNextBind()
        {
            using var harness = CreateHarness();
            var store1 = new OnlinePlayerNamesStore();
            var store2 = new OnlinePlayerNamesStore();

            harness.Launcher.BindMatchPlayerNamesStore(store1);

            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|H|1|StaleHost"));
            store1.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store1.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();

            harness.Launcher.UnbindMatchPlayerNamesStore(store1);

            harness.ContextStore.SetDirectInviteSession("ABCDEF", harness.LocalUserId, isHost: false);

            harness.Launcher.BindMatchPlayerNamesStore(store2);
            store2.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store2.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();

            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|G|1|FreshGuest"));

            store2.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store2.Snapshot.CurrentValue.GuestCustomName.Should().Be("FreshGuest");
        }

        [Test]
        public async Task WhenGatewayLifecyclePeerJoinedNameMatchesContract_ThenHostFlowStartsGameplay()
        {
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();
            var networkTime = 100d;
           
            harness.Gateway.NetworkTimeSecondsProvider = () =>
            {
                networkTime += 0.6d;
                return networkTime;
            };
            
            var inGameAfterMismatch = false;

            UniTask.Void(async () =>
            {
                await UniTask.Delay(TimeSpan.FromMilliseconds(30));
                harness.Gateway.RaiseLifecycleEvent("PlayerJoined", "ABCDEF", "guest");
                await UniTask.Delay(TimeSpan.FromMilliseconds(30));
                inGameAfterMismatch = harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.InGame;
                harness.Gateway.RaiseLifecycleEvent("peer_joined", "ABCDEF", "guest");
            });

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            inGameAfterMismatch.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.InGame);
        }
    }
}