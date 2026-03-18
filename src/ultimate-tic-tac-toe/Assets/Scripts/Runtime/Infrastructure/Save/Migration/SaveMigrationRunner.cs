using System;
using System.Collections.Generic;
using System.Linq;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.Save.Serialization;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save.Migration
{
    internal sealed class SaveMigrationRunner
    {
        private const string _migrationSection = "migration";

        private readonly int _currentVersion;
        private readonly List<ISaveMigration> _migrations;
        private readonly Dictionary<int, ISaveMigration> _migrationsByFromVersion = new();

        public SaveMigrationRunner(int currentVersion, IEnumerable<ISaveMigration> migrations)
        {
            _currentVersion = currentVersion;
            _migrations = migrations?.ToList() ?? new List<ISaveMigration>();
        }

        public void RefreshIndex()
        {
            _migrationsByFromVersion.Clear();

            foreach (var migration in _migrations)
            {
                if (!_migrationsByFromVersion.TryAdd(migration.FromVersion, migration))
                    throw new InvalidOperationException($"Duplicate save migrations found for FromVersion={migration.FromVersion}.");
            }
        }

        public bool TryUpgrade(JsonNode root, int loadedVersion, ISaveBackend backend, out JsonNode upgradedRoot, out int upgradedVersion)
        {
            upgradedRoot = root;
            upgradedVersion = loadedVersion;

            if (loadedVersion >= _currentVersion)
                return true;

            while (upgradedVersion < _currentVersion)
            {
                if (!_migrationsByFromVersion.TryGetValue(upgradedVersion, out var migration))
                {
                    GameLog.Error($"[SaveSystem] Missing migration. {BuildLogContext(backend)}, ExceptionType=None, FromVersion={upgradedVersion}, CurrentVersion={_currentVersion}");
                    return false;
                }

                try
                {
                    upgradedRoot = migration.Migrate(upgradedRoot);
                }
                catch (Exception ex)
                {
                    GameLog.Error($"[SaveSystem] Migration failed. {BuildLogContext(backend)}, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}, FromVersion={upgradedVersion}");
                    return false;
                }

                upgradedVersion++;
            }

            var upgradedRootObject = upgradedRoot.AsObject;
            if (upgradedRootObject == null)
            {
                GameLog.Error($"[SaveSystem] Migration result root is not an object. {BuildLogContext(backend)}, ExceptionType=None");
                return false;
            }

            upgradedRootObject[SaveDataEnvelopeFields.VersionKey] = _currentVersion;
            return true;
        }

        private static string BuildLogContext(ISaveBackend backend)
            => $"Backend={backend.GetType().Name}, Path={backend.GetDisplayPath()}, Section={_migrationSection}, PayloadBytes=0";
    }
}