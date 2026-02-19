#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.GameModes.Wizard
{
    public sealed class NoOpOnlineSessionFlowService : IOnlineSessionFlowService
    {
        public static readonly IOnlineSessionFlowService Instance = new NoOpOnlineSessionFlowService();

        private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot = new(
            new OnlineFlowSnapshot(
                state: OnlineFlowState.Idle,
                previousStableState: null,
                candidateSessionId: string.Empty,
                activeSessionId: null,
                flowEpoch: 1,
                region: string.Empty,
                canStart: false,
                isBusy: false,
                errorCode: OnlineErrorCode.None,
                errorLocalizationKey: null,
                statusLocalizationKey: null,
                countdownRemainingSeconds: null,
                graceDeadlineUtc: null));

        public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

        public UniTask EnterHumanSetupAsync(string region, string currentUserId) => UniTask.CompletedTask;
        public UniTask ConfirmHostIntentAsync() => UniTask.CompletedTask;
        public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig) => UniTask.CompletedTask;
        public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) => UniTask.CompletedTask;
        public UniTask CopyVisibleSessionIdAsync() => UniTask.CompletedTask;
        public UniTask BackAsync() => UniTask.CompletedTask;
        public UniTask ExitAsync() => UniTask.CompletedTask;
        public UniTask SetReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
        public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => UniTask.CompletedTask;
        public UniTask OnHostCreatedAsync() => UniTask.CompletedTask;
        public UniTask OnJoinSucceededAsync() => UniTask.CompletedTask;
        public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => UniTask.CompletedTask;
        public UniTask OnGuestJoinedAsync() => UniTask.CompletedTask;
        public UniTask OnCountdownTickAsync(int remainingSeconds) => UniTask.CompletedTask;
        public UniTask OnGameplayEnteredAsync() => UniTask.CompletedTask;
        public UniTask OnRoundCompletedAsync() => UniTask.CompletedTask;
        public UniTask OnDisconnectDetectedAsync() => UniTask.CompletedTask;
        public UniTask OnReconnectSucceededAsync() => UniTask.CompletedTask;
        public UniTask OnGraceTimeoutAsync(int eventEpoch) => UniTask.CompletedTask;
        public UniTask OnOpponentLeftAsync() => UniTask.CompletedTask;

        public void Dispose() => _snapshot.Dispose();
    }
}

#nullable restore
