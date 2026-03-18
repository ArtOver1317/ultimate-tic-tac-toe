using System;
using System.Collections.Generic;
using Runtime.PlayerStatistics;

namespace Runtime.Infrastructure.Save.Serialization
{
    internal static class SaveDataJsonContext
    {
        private static readonly HashSet<Type> _knownTypes = new()
        {
            typeof(string),
            typeof(int),
            typeof(bool),
            typeof(StatisticsEntryDto[]),
        };

        public static bool IsRegistered(Type type)
            => _knownTypes.Contains(type);
    }
}