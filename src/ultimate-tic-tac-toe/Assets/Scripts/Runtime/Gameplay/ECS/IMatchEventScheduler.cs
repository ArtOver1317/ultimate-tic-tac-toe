using System;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Abstraction for event scheduling. Runtime uses deferred (next-frame) scheduling
    /// for re-entrancy safety (ADR-5). Tests use synchronous scheduling.
    /// </summary>
    public interface IMatchEventScheduler
    {
        void Schedule(Action publishAction);
    }

    /// <summary>
    /// Publishes events synchronously (for EditMode tests).
    /// </summary>
    public sealed class SynchronousEventScheduler : IMatchEventScheduler
    {
        public void Schedule(Action publishAction) => publishAction?.Invoke();
    }
}
