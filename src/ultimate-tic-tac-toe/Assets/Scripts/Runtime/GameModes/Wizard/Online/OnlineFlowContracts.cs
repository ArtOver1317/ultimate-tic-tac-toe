#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;

namespace Runtime.GameModes.Wizard
{
    public enum OnlineFlowState
    {
        Idle,
        HostIntentConfirmed,
        HostStarting,
        WaitingForPlayer,
        GuestConnecting,
        ConnectedCountdown,
        InGame,
        Result,
        Reconnecting,
        Failed,
        Terminated
    }

    public enum OnlineErrorCode
    {
        None,
        InvalidSessionIdFormat,
        SessionNotFound,
        SessionFull,
        CannotJoinSelf,
        SessionAlreadyInGame,
        NetworkUnavailable,
        RegionMismatchOrUnavailable,
        DisconnectTimeout,
        OpponentLeft
    }

    public readonly struct SessionId
    {
        public string Value { get; }

        public SessionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

            Value = value;
        }

        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct OnlineFlowSnapshot
    {
        public OnlineFlowState State { get; }
        public OnlineFlowState? PreviousStableState { get; }
        public string CandidateSessionId { get; }
        public string? ActiveSessionId { get; }
        public int FlowEpoch { get; }
        public string Region { get; }
        public bool CanStart { get; }
        public bool IsBusy { get; }
        public OnlineErrorCode ErrorCode { get; }
        public string? ErrorLocalizationKey { get; }
        public string? StatusLocalizationKey { get; }
        public int? CountdownRemainingSeconds { get; }
        public DateTimeOffset? GraceDeadlineUtc { get; }

        public OnlineFlowSnapshot(
            OnlineFlowState state,
            OnlineFlowState? previousStableState,
            string candidateSessionId,
            string? activeSessionId,
            int flowEpoch,
            string region,
            bool canStart,
            bool isBusy,
            OnlineErrorCode errorCode,
            string? errorLocalizationKey,
            string? statusLocalizationKey,
            int? countdownRemainingSeconds,
            DateTimeOffset? graceDeadlineUtc)
        {
            if (candidateSessionId == null)
                throw new ArgumentNullException(nameof(candidateSessionId));

            if (region == null)
                throw new ArgumentNullException(nameof(region));

            if (flowEpoch < 1)
                throw new ArgumentOutOfRangeException(nameof(flowEpoch), flowEpoch, "Value must be at least 1.");

            State = state;
            PreviousStableState = previousStableState;
            CandidateSessionId = candidateSessionId;
            ActiveSessionId = activeSessionId;
            FlowEpoch = flowEpoch;
            Region = region;
            CanStart = canStart;
            IsBusy = isBusy;
            ErrorCode = errorCode;
            ErrorLocalizationKey = errorLocalizationKey;
            StatusLocalizationKey = statusLocalizationKey;
            CountdownRemainingSeconds = countdownRemainingSeconds;
            GraceDeadlineUtc = graceDeadlineUtc;
        }
    }

    public readonly struct MoveCommand
    {
        public Guid CommandId { get; }
        public string SenderUserId { get; }
        public int CellIndex { get; }
        public long ClientTick { get; }

        public MoveCommand(Guid commandId, string senderUserId, int cellIndex, long clientTick)
        {
            if (commandId == Guid.Empty)
                throw new ArgumentException("Value cannot be an empty GUID.", nameof(commandId));

            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (cellIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex, "Value cannot be negative.");

            CommandId = commandId;
            SenderUserId = senderUserId;
            CellIndex = cellIndex;
            ClientTick = clientTick;
        }
    }

    public readonly struct OnlineSessionConfig
    {
        public SessionId SessionId { get; }
        public string Region { get; }
        public string HostUserId { get; }

        public OnlineSessionConfig(SessionId sessionId, string region, string hostUserId)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(hostUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(hostUserId));

            SessionId = sessionId;
            Region = region;
            HostUserId = hostUserId;
        }
    }

    public readonly struct GatewayOperationResult
    {
        public bool IsSuccess { get; }
        public OnlineErrorCode ErrorCode { get; }

        public GatewayOperationResult(bool isSuccess, OnlineErrorCode errorCode)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
        }

        public static GatewayOperationResult Success() => new(true, OnlineErrorCode.None);

        public static GatewayOperationResult Failed(OnlineErrorCode errorCode)
        {
            if (errorCode == OnlineErrorCode.None)
                throw new ArgumentException("Failure result cannot use OnlineErrorCode.None.", nameof(errorCode));

            return new GatewayOperationResult(false, errorCode);
        }
    }

    public readonly struct GatewayLifecycleEvent
    {
        public string Kind { get; }
        public string? SessionId { get; }
        public string? UserId { get; }

        public GatewayLifecycleEvent(string kind, string? sessionId, string? userId)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(kind));

            Kind = kind;
            SessionId = sessionId;
            UserId = userId;
        }
    }

    public readonly struct GameplayNetworkSnapshot
    {
        public int MatchRoundId { get; }
        public bool IsCompleted { get; }
        public string? WinnerUserId { get; }
        public long AuthoritativeTick { get; }
        public long CountdownTargetTick { get; }

        public GameplayNetworkSnapshot(
            int matchRoundId,
            bool isCompleted,
            string? winnerUserId,
            long authoritativeTick,
            long countdownTargetTick)
        {
            if (matchRoundId < 1)
                throw new ArgumentOutOfRangeException(nameof(matchRoundId), matchRoundId, "Value must be at least 1.");

            MatchRoundId = matchRoundId;
            IsCompleted = isCompleted;
            WinnerUserId = winnerUserId;
            AuthoritativeTick = authoritativeTick;
            CountdownTargetTick = countdownTargetTick;
        }
    }

    public readonly struct RoundReadySignal
    {
        public string SenderUserId { get; }
        public bool IsReady { get; }
        public int MatchRoundId { get; }
        public long ClientTick { get; }

        public RoundReadySignal(string senderUserId, bool isReady, int matchRoundId, long clientTick)
        {
            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (matchRoundId < 1)
                throw new ArgumentOutOfRangeException(nameof(matchRoundId), matchRoundId, "Value must be at least 1.");

            SenderUserId = senderUserId;
            IsReady = isReady;
            MatchRoundId = matchRoundId;
            ClientTick = clientTick;
        }
    }

    public readonly struct OnlineTimeoutSignal
    {
        public string SenderUserId { get; }
        public int LoserSlot { get; }
        public long ClientTick { get; }

        public OnlineTimeoutSignal(string senderUserId, int loserSlot, long clientTick)
        {
            if (string.IsNullOrWhiteSpace(senderUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(senderUserId));

            if (loserSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(loserSlot), loserSlot, "Value cannot be negative.");

            SenderUserId = senderUserId;
            LoserSlot = loserSlot;
            ClientTick = clientTick;
        }
    }

    public interface IOnlineSessionFlowService : IDisposable
    {
        ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot { get; }

        UniTask EnterHumanSetupAsync(string region, string currentUserId);
        UniTask ConfirmHostIntentAsync();
        UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig);
        UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId);
        UniTask CopyVisibleSessionIdAsync();
        UniTask BackAsync();
        UniTask ExitAsync();
        UniTask SetReadyForNextMatchAsync(bool isReady);
        UniTask OnOpponentReadyForNextMatchAsync(bool isReady);

        UniTask OnHostCreatedAsync();
        UniTask OnJoinSucceededAsync();
        UniTask OnJoinFailedAsync(OnlineErrorCode errorCode);
        UniTask OnGuestJoinedAsync();
        UniTask OnCountdownTickAsync(int remainingSeconds);
        UniTask OnGameplayEnteredAsync();
        UniTask OnRoundCompletedAsync();
        UniTask OnDisconnectDetectedAsync();
        UniTask OnReconnectSucceededAsync();
        UniTask OnGraceTimeoutAsync(int eventEpoch);
        UniTask OnOpponentLeftAsync();
    }

    public interface IPhotonSessionGateway : IDisposable
    {
        ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent { get; }
        double NetworkTimeSeconds { get; }

        UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config);
        UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId);
        UniTask LeaveSessionAsync();
        UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId);
    }

    public interface IGameplayNetworkBridge : IDisposable
    {
        ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot { get; }
        Observable<MoveCommand> IncomingMoves { get; }
        Observable<RoundReadySignal> IncomingRoundReadySignals { get; }
        Observable<OnlineTimeoutSignal> IncomingTimeoutSignals { get; }

        UniTask BindAsync(string localUserId, bool isHost);
        UniTask UnbindAsync();
        UniTask SubmitMoveAsync(MoveCommand command);
        UniTask SubmitRoundReadyAsync(RoundReadySignal signal);
        UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal);
    }

    public static class OnlineLocalizationKeys
    {
        public const string WaitingForPlayerStatus = "GameWizard.MatchSetup.Status.WaitingForPlayer";
        public const string ConnectingStatus = "GameWizard.MatchSetup.Status.Connecting";
        public const string PlayerFoundStartingSoonStatus = "GameWizard.MatchSetup.Status.PlayerFoundStartingSoon";
        public const string ReconnectingStatus = "GameWizard.MatchSetup.Status.Reconnecting";
        public const string SessionIdCopiedStatus = "GameWizard.MatchSetup.Status.SessionIdCopied";
        public const string HostIntentConfirmedStatus = "GameWizard.MatchSetup.Status.HostIntentConfirmed";

        public static string? ErrorKey(OnlineErrorCode errorCode) => errorCode switch
        {
            OnlineErrorCode.None => null,
            OnlineErrorCode.SessionNotFound => "Errors.Online.SessionNotFound",
            OnlineErrorCode.SessionFull => "Errors.Online.SessionFull",
            OnlineErrorCode.CannotJoinSelf => "Errors.Online.CannotJoinSelf",
            OnlineErrorCode.SessionAlreadyInGame => "Errors.Online.SessionAlreadyInGame",
            OnlineErrorCode.NetworkUnavailable => "Errors.Online.NetworkUnavailable",
            OnlineErrorCode.RegionMismatchOrUnavailable => "Errors.Online.RegionMismatchOrUnavailable",
            OnlineErrorCode.InvalidSessionIdFormat => "Errors.Online.InvalidSessionIdFormat",
            OnlineErrorCode.DisconnectTimeout => "Errors.Online.DisconnectTimeout",
            OnlineErrorCode.OpponentLeft => "Errors.Online.OpponentLeft",
            _ => null,
        };
    }
}

#nullable restore