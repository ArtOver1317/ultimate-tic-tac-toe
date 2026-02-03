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
        private IObjectResolver _current;

        public IObjectResolver Current => _current;

        public void SetCurrent(IObjectResolver resolver)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            _current = resolver;
        }

        public void Clear(IObjectResolver resolver)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            if (_current == resolver)
                _current = null;
        }
    }
}
