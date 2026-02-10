using System.Collections.Generic;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Main-thread-only command queue. Thread safety not required — all sources
    /// (UI/bot/network) are marshalled to main thread before enqueue.
    /// </summary>
    public sealed class CommandQueue
    {
        private readonly Queue<IGameplayCommand> _queue = new();

        public int Count => _queue.Count;

        public void Enqueue(IGameplayCommand command) => _queue.Enqueue(command);

        public IGameplayCommand Dequeue() => _queue.Dequeue();

        public void Clear() => _queue.Clear();
    }
}
