#nullable enable

using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    public sealed class LocalMatchFailSafeGateway : IMatchFailSafeGateway
    {
        public bool IsInputLocked { get; private set; }

        public bool TryEnterAbortState(string userSafeMessageKey)
        {
            if (IsInputLocked) 
                return false;

            IsInputLocked = true;
            UnityEngine.Debug.LogError($"[UltimateBot] Fail-safe abort: {userSafeMessageKey}");
            return true;
        }

        public void ResetAbortState() => IsInputLocked = false;
    }
}