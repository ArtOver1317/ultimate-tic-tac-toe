using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runtime.Infrastructure.Logging;
using SimpleJSON;
using UnityEngine;
using VContainer.Unity;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveService : ISaveService, ISaveServiceWithResult, IInitializable
    {
        private const int CurrentVersion = 1;
        private const int SaveFrequencyWarningThreshold = 5;

        private readonly ISaveBackend _backend;
        private readonly SaveEncryptor _saveEncryptor;
        private readonly List<ISaveMigration> _migrations;
        private readonly Dictionary<int, ISaveMigration> _migrationsByFromVersion = new();
        private readonly int _mainThreadId;

        private SaveData _saveData = new() { Version = CurrentVersion };
        private bool _isInitialized;
        private bool _isWriteBlocked;

        private DateTime _saveWindowStartedUtc = DateTime.UtcNow;
        private DateTime _lastSaveFrequencyWarningUtc = DateTime.MinValue;
        private int _saveCallsInWindow;

        public SaveService(ISaveBackend backend, SaveEncryptor saveEncryptor, IEnumerable<ISaveMigration> migrations)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _saveEncryptor = saveEncryptor ?? throw new ArgumentNullException(nameof(saveEncryptor));
            _migrations = migrations?.ToList() ?? new List<ISaveMigration>();
            _mainThreadId = Environment.CurrentManagedThreadId;
        }

        public void Initialize()
        {
            EnsureNoDuplicateMigrations();
            _isWriteBlocked = false;

            GameLog.Info($"[SaveSystem] Path: {_backend.GetDisplayPath()}");

            string raw;
            try
            {
                raw = _backend.Read();
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("backend_read", "initialize", 0, ex);
                _isInitialized = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                _saveData = new SaveData { Version = CurrentVersion };
                _isInitialized = true;
                return;
            }

            string json;
            try
            {
                json = _saveEncryptor.Decrypt(raw);
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("decrypt", "initialize", Encoding.UTF8.GetByteCount(raw), ex);
                _isInitialized = true;
                return;
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("parse", "initialize", Encoding.UTF8.GetByteCount(json), ex);
                _isInitialized = true;
                return;
            }

            if (root == null)
            {
                HandleCorruptedOrInvalidSave("parse returned null", "initialize", Encoding.UTF8.GetByteCount(json), null);
                _isInitialized = true;
                return;
            }

            if (!TryGetVersion(root, out var loadedVersion))
            {
                HandleCorruptedOrInvalidSave($"Save data does not contain valid version. FileVersion=<missing>, CurrentVersion={CurrentVersion}", "initialize", 0, null);
                _isInitialized = true;
                return;
            }

            if (loadedVersion > CurrentVersion)
            {
                HandleCorruptedOrInvalidSave($"Save version is newer than supported. FileVersion={loadedVersion}, CurrentVersion={CurrentVersion}", "initialize", 0, null);
                _isWriteBlocked = true;
                _isInitialized = true;
                return;
            }

            if (loadedVersion < CurrentVersion)
            {
                if (!TryApplyMigrations(root, loadedVersion, out var migratedRoot))
                {
                    _saveData = new SaveData { Version = CurrentVersion };
                    _isWriteBlocked = true;
                    _isInitialized = true;
                    return;
                }

                root = migratedRoot;
                loadedVersion = CurrentVersion;

                if (!TryPersistRoot(root, "migration", out _))
                {
                    GameLog.Error($"[SaveSystem] Migration persisted in-memory only. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section=migration, PayloadBytes=<unknown>, ExceptionType=None");
                }
            }

            _saveData = ParseSaveData(root, loadedVersion);
            _isInitialized = true;
        }

        public T Load<T>(string section, T defaultValue)
        {
            ValidateSection(section);

            if (!_isInitialized)
            {
                GameLog.Error($"[SaveSystem] Load called before Initialize. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType=None");
                return defaultValue;
            }

            EnsureMainThread();

            if (!TryEnsureTypeInfo(typeof(T), section, true))
                return defaultValue;

            if (!_saveData.Sections.TryGetValue(section, out var sectionNode) || sectionNode == null)
                return defaultValue;

            try
            {
                var sectionJson = sectionNode.ToString();
                var payloadBytes = Encoding.UTF8.GetByteCount(sectionJson);

                if (TryDeserializeSection(sectionJson, out T value))
                    return value;

                GameLog.Warning($"[SaveSystem] Failed to deserialize section. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes={payloadBytes}, ExceptionType=None");
                return defaultValue;
            }
            catch (Exception ex)
            {
                GameLog.Warning($"[SaveSystem] Failed to deserialize section. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}");
                return defaultValue;
            }
        }

        public void Save<T>(string section, T data)
        {
            TrySave(section, data);
        }

        public SaveWriteResult TrySave<T>(string section, T data)
        {
            ValidateSection(section);

            if (!_isInitialized)
            {
                GameLog.Error($"[SaveSystem] Save called before Initialize. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType=None");
                return SaveWriteResult.Failed(SaveWriteError.NotInitialized);
            }

            if (_isWriteBlocked)
            {
                GameLog.Error($"[SaveSystem] Save blocked due to incompatible persisted data. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType=None");
                return SaveWriteResult.Failed(SaveWriteError.IncompatiblePersistedData);
            }

            EnsureMainThread();

            if (!TryEnsureTypeInfo(typeof(T), section, false))
                return SaveWriteResult.Failed(SaveWriteError.SerializationFailed);

            string sectionJson;
            try
            {
                sectionJson = SerializeSection(data);
                _saveData.Sections[section] = JsonNode.Parse(sectionJson);
                _saveData.Version = CurrentVersion;
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SaveSystem] Failed to serialize section. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}");
                return SaveWriteResult.Failed(SaveWriteError.SerializationFailed);
            }

            var payloadBytes = Encoding.UTF8.GetByteCount(sectionJson);
            CheckSaveFrequency(section, payloadBytes);

            if (!TryPersistRoot(BuildRootNode(), section, out var totalBytes))
            {
                GameLog.Error($"[SaveSystem] Save write failed. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes={totalBytes}, ExceptionType=<see previous log>");
                return SaveWriteResult.Failed(SaveWriteError.BackendWriteFailed);
            }

            return SaveWriteResult.Success();
        }

        private bool TryPersistRoot(JsonNode root, string section, out int payloadBytes)
        {
            payloadBytes = 0;

            try
            {
                var json = root.ToString();
                payloadBytes = Encoding.UTF8.GetByteCount(json);
                var encrypted = _saveEncryptor.Encrypt(json);
                _backend.Write(encrypted);
                return true;
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SaveSystem] Persistence error. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes={payloadBytes}, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}");
                return false;
            }
        }

        private static SaveData ParseSaveData(JsonNode root, int version)
        {
            var parsed = new SaveData
            {
                Version = version,
                Sections = new Dictionary<string, JsonNode>(),
            };

            var rootObject = root.AsObject;
            if (rootObject == null || !rootObject.HasKey("sections"))
                return parsed;

            var sectionsObject = rootObject["sections"].AsObject;
            if (sectionsObject == null)
                return parsed;

            foreach (var sectionPair in sectionsObject)
            {
                parsed.Sections[sectionPair.Key] = sectionPair.Value;
            }

            return parsed;
        }

        private JsonNode BuildRootNode()
        {
            var root = new JSONObject
            {
                ["version"] = _saveData.Version,
            };

            var sections = new JSONObject();
            foreach (var sectionPair in _saveData.Sections)
            {
                sections[sectionPair.Key] = sectionPair.Value;
            }

            root["sections"] = sections;
            return root;
        }

        private bool TryApplyMigrations(JsonNode root, int startVersion, out JsonNode migratedRoot)
        {
            var currentVersion = startVersion;
            migratedRoot = root;

            while (currentVersion < CurrentVersion)
            {
                if (!_migrationsByFromVersion.TryGetValue(currentVersion, out var migration))
                {
                    GameLog.Error($"[SaveSystem] Missing migration. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section=migration, PayloadBytes=0, ExceptionType=None, FromVersion={currentVersion}, CurrentVersion={CurrentVersion}");
                    return false;
                }

                try
                {
                    migratedRoot = migration.Migrate(migratedRoot);
                }
                catch (Exception ex)
                {
                    GameLog.Error($"[SaveSystem] Migration failed. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section=migration, PayloadBytes=0, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}, FromVersion={currentVersion}");
                    return false;
                }

                currentVersion++;
            }

            var migratedRootObject = migratedRoot.AsObject;
            if (migratedRootObject == null)
            {
                GameLog.Error($"[SaveSystem] Migration result root is not an object. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section=migration, PayloadBytes=0, ExceptionType=None");
                return false;
            }

            migratedRootObject["version"] = CurrentVersion;
            return true;
        }

        private static bool TryGetVersion(JsonNode root, out int version)
        {
            version = 0;
            var rootObject = root.AsObject;
            if (rootObject == null || !rootObject.HasKey("version"))
                return false;

            version = rootObject["version"].AsInt;
            return version > 0;
        }

        private void EnsureNoDuplicateMigrations()
        {
            _migrationsByFromVersion.Clear();

            foreach (var migration in _migrations)
            {
                if (_migrationsByFromVersion.ContainsKey(migration.FromVersion))
                    throw new InvalidOperationException($"Duplicate save migrations found for FromVersion={migration.FromVersion}.");

                _migrationsByFromVersion[migration.FromVersion] = migration;
            }
        }

        private static void ValidateSection(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                throw new ArgumentException("Section must be a non-empty value.", nameof(section));
        }

        private static string SerializeSection<T>(T data)
        {
            if (data == null)
                return "null";

            var type = typeof(T);

            if (type == typeof(string))
                return new JSONString((string)(object)data).ToString();

            if (type == typeof(int))
                return new JSONNumber((int)(object)data).ToString();

            if (type == typeof(bool))
                return new JSONBool((bool)(object)data).ToString();

            return JsonUtility.ToJson(data);
        }

        private static bool TryDeserializeSection<T>(string json, out T value)
        {
            var type = typeof(T);

            if (type == typeof(string))
            {
                var parsed = JsonNode.Parse(json);
                if (parsed is not JSONString)
                {
                    value = default;
                    return false;
                }

                value = (T)(object)parsed.Value;
                return true;
            }

            if (type == typeof(int))
            {
                var parsed = JsonNode.Parse(json);
                if (parsed is not JSONNumber)
                {
                    value = default;
                    return false;
                }

                value = (T)(object)parsed.AsInt;
                return true;
            }

            if (type == typeof(bool))
            {
                var parsed = JsonNode.Parse(json);
                if (parsed is not JSONBool)
                {
                    value = default;
                    return false;
                }

                value = (T)(object)parsed.AsBool;
                return true;
            }

            if (string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
            {
                value = default;
                return false;
            }

            var deserialized = JsonUtility.FromJson<T>(json);
            if (deserialized == null)
            {
                value = default;
                return false;
            }

            value = deserialized;
            return true;
        }

        private bool TryEnsureTypeInfo(Type type, string section, bool isLoad)
        {
            if (SaveDataJsonContext.IsRegistered(type))
                return true;

            var operation = isLoad ? "Load" : "Save";
            var message = $"[SaveSystem] Type is not registered in SaveDataJsonContext. Operation={operation}, Section={section}, Type={type.FullName}";

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            GameLog.Error(message);
            throw new InvalidOperationException(message);
#else
            GameLog.Error(message);
            return false;
#endif
        }

        private void EnsureMainThread()
        {
#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Environment.CurrentManagedThreadId == _mainThreadId)
                return;

            var message = "[SaveSystem] SaveService can be used only from Unity main thread.";
            GameLog.Error(message);
            throw new InvalidOperationException(message);
#endif
        }

        private void CheckSaveFrequency(string section, int payloadBytes)
        {
#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            var now = DateTime.UtcNow;

            if ((now - _saveWindowStartedUtc).TotalSeconds >= 1d)
            {
                _saveWindowStartedUtc = now;
                _saveCallsInWindow = 0;
            }

            _saveCallsInWindow++;

            if (_saveCallsInWindow <= SaveFrequencyWarningThreshold)
                return;

            if ((now - _lastSaveFrequencyWarningUtc).TotalSeconds < 1d)
                return;

            _lastSaveFrequencyWarningUtc = now;
            GameLog.Warning($"[SaveSystem] Save called too frequently. Section={section}, CallsPerSecond={_saveCallsInWindow}, PayloadBytes={payloadBytes}");
#endif
        }

        private void HandleCorruptedOrInvalidSave(string error)
        {
            GameLog.Error($"[SaveSystem] {error}. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}");
            _saveData = new SaveData { Version = CurrentVersion };
        }

        private void HandleCorruptedOrInvalidSave(string error, string section, int payloadBytes, Exception exception)
        {
            var exceptionType = exception?.GetType().Name ?? "None";
            var exceptionMessage = exception?.Message ?? string.Empty;
            GameLog.Error($"[SaveSystem] {error}. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes={payloadBytes}, ExceptionType={exceptionType}, ExceptionMessage={exceptionMessage}");
            _saveData = new SaveData { Version = CurrentVersion };
        }
    }
}