using System;

namespace Runtime.Infrastructure.GameStateMachine
{
    public static class StateExceptions
    {
        public static InvalidOperationException FactoryReturnedNull(Type stateType) =>
            new($"StateFactory returned null for state type {stateType.Name}");
    }
}

