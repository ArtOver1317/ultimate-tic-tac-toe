using System;
using VContainer;

namespace Runtime.Gameplay
{
    public interface IGameplayScopeAccessor
    {
        IObjectResolver Current { get; }
        void SetCurrent(IObjectResolver resolver);
        void Clear(IObjectResolver resolver);
    }

    public sealed class GameplayScopeAccessor : IGameplayScopeAccessor
    {
        public IObjectResolver Current { get; private set; }

        public void SetCurrent(IObjectResolver resolver) => Current = resolver ?? throw new ArgumentNullException(nameof(resolver));

        public void Clear(IObjectResolver resolver)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            if (Current == resolver)
                Current = null;
        }
    }
}
