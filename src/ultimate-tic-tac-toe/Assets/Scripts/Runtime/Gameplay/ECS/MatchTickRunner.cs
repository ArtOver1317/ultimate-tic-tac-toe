using UnityEngine;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Lazy tick runner (ADR-7). Ticks the ECS World only when commands are pending.
    /// Lifecycle is tied to the gameplay scene (destroyed when scene unloads).
    /// 
    /// NOTE: Currently unused — MatchStateProvider.SubmitCommand() auto-ticks synchronously,
    /// which is ideal for turn-based local play. External code must NOT call Tick() after
    /// SubmitCommand (single tick-driver contract). This runner is reserved for future
    /// network mode where commands arrive asynchronously and need frame-by-frame processing.
    /// </summary>
    public sealed class MatchTickRunner : MonoBehaviour
    {
        private IMatchEcsLifecycle _lifecycle;
        private CommandQueue _commandQueue;

        public void Initialize(IMatchEcsLifecycle lifecycle, CommandQueue commandQueue)
        {
            _lifecycle = lifecycle;
            _commandQueue = commandQueue;
        }

        private void Update()
        {
            if (_lifecycle == null || !_lifecycle.IsActive)
                return;

            if (_commandQueue.Count == 0)
                return;

            _lifecycle.Tick(Time.deltaTime);
        }
    }
}
