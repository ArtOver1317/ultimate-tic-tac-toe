using System.Collections.Generic;
using Runtime.Gameplay.Shared;

namespace Runtime.Gameplay.ECS.Pipeline
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

        public IGameplayCommand Peek() => _queue.Peek();

        public IGameplayCommand Dequeue() => _queue.Dequeue();

        public void Clear() => _queue.Clear();
    }
}