using System;
using System.Collections.Generic;
using Runtime.PlayerStatistics;

namespace Runtime.Infrastructure.Save
{
    internal static class SaveDataJsonContext
    {
        private static readonly HashSet<Type> KnownTypes = new()
        {
            typeof(string),
            typeof(int),
            typeof(bool),
            typeof(StatisticsEntryDto[]),
        };

        public static bool IsRegistered(Type type)
            => KnownTypes.Contains(type);
    }
}