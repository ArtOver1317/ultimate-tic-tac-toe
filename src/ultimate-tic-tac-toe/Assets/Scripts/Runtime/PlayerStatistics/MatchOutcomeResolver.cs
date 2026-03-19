#nullable enable

using Runtime.Gameplay.Shared;

namespace Runtime.PlayerStatistics
{
    public sealed class MatchOutcomeResolver : IMatchOutcomeResolver
    {
        public bool TryResolveOutcome(
            RoundFinishedEvent evt,
            StatisticsOpponentType opponentType,
            bool isLocalPlayerHost,
            out MatchOutcome outcome)
        {
            outcome = default;

            if (evt.Status == EcsGameStatus.InProgress)
                return false;

            if (!evt.WinnerSlot.HasValue)
            {
                outcome = MatchOutcome.Draw;
                return true;
            }

            if (!TryResolveLocalSlot(opponentType, isLocalPlayerHost, out var localSlot))
                return false;

            outcome = evt.WinnerSlot.Value == localSlot
                ? MatchOutcome.Win
                : MatchOutcome.Loss;
            
            return true;
        }

        private static bool TryResolveLocalSlot(StatisticsOpponentType opponentType, bool isLocalPlayerHost, out int localSlot)
        {
            localSlot = opponentType switch
            {
                StatisticsOpponentType.HotSeat or StatisticsOpponentType.Bot => 0,
                StatisticsOpponentType.Online => isLocalPlayerHost ? 0 : 1,
                _ => -1,
            };

            return localSlot >= 0;
        }
    }
}