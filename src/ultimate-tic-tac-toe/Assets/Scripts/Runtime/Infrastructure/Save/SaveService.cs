using System;
using System.Collections.Generic;
using System.Text;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.Save.Migration;
using Runtime.Infrastructure.Save.Serialization;
using VContainer.Unity;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveService : ISaveService, ISaveServiceWithResult, IInitializable
    {
        private const int _currentVersion = 1;
        private const int _saveFrequencyWarningThreshold = 5;
        private const string _initializeSection = "initialize";
        private const string _migrationSection = "migration";

        private enum SaveServiceState
        {
            NotInitialized,
            Ready,
            WriteBlocked,
        }

        private readonly ISaveBackend _backend;
        private readonly SaveEncryptor _saveEncryptor;
        private readonly SaveSerializer _serializer;
        private readonly SaveDataEnvelopeMapper _saveDataEnvelopeMapper = new();
        private readonly SaveFrequencyWarningTracker _saveFrequencyWarningTracker = new(_saveFrequencyWarningThreshold);
        private readonly SaveMigrationRunner _saveMigrationRunner;
        private readonly int _mainThreadId;

        private SaveData _saveData = new() { Version = _currentVersion };
        private SaveServiceState _state = SaveServiceState.NotInitialized;

        public SaveService(ISaveBackend backend, SaveEncryptor saveEncryptor, SaveSerializer serializer, IEnumerable<ISaveMigration> migrations)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _saveEncryptor = saveEncryptor ?? throw new ArgumentNullException(nameof(saveEncryptor));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _saveMigrationRunner = new SaveMigrationRunner(_currentVersion, migrations);
            _mainThreadId = Environment.CurrentManagedThreadId;
        }

        public void Initialize()
        {
            PrepareForInitialization();

            if (!TryReadRawSave(out var raw))
                return;

            if (string.IsNullOrWhiteSpace(raw))
            {
                SetDefaultSaveData(SaveServiceState.Ready);
                return;
            }

            if (!TryParseSaveRoot(raw, out var root))
                return;

            InitializeFromParsedRoot(root);
        }

        private void PrepareForInitialization()
        {
            _saveMigrationRunner.RefreshIndex();

            if (_state == SaveServiceState.WriteBlocked)
                _state = SaveServiceState.Ready;

            GameLog.Info($"[SaveSystem] Path: {_backend.GetDisplayPath()}");
        }

        private bool TryReadRawSave(out string raw)
        {
            try
            {
                raw = _backend.Read();
                return true;
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("backend_read", _initializeSection, 0, ex, SaveServiceState.Ready);
                raw = string.Empty;
                return false;
            }
        }

        private bool TryParseSaveRoot(string raw, out JsonNode root)
        {
            if (!TryDecryptRawSave(raw, out var json))
            {
                root = null;
                return false;
            }

            try
            {
                root = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("parse", _initializeSection, Encoding.UTF8.GetByteCount(json), ex, SaveServiceState.Ready);
                root = null;
                return false;
            }

            if (root != null)
                return true;

            HandleCorruptedOrInvalidSave("parse returned null", _initializeSection, Encoding.UTF8.GetByteCount(json), null, SaveServiceState.Ready);
            return false;
        }

        private bool TryDecryptRawSave(string raw, out string json)
        {
            try
            {
                json = _saveEncryptor.Decrypt(raw);
                return true;
            }
            catch (Exception ex)
            {
                HandleCorruptedOrInvalidSave("decrypt", _initializeSection, Encoding.UTF8.GetByteCount(raw), ex, SaveServiceState.Ready);
                json = string.Empty;
                return false;
            }
        }

        private void InitializeFromParsedRoot(JsonNode root)
        {
            if (!TryGetVersion(root, out var loadedVersion))
            {
                HandleCorruptedOrInvalidSave($"Save data does not contain valid version. FileVersion=<missing>, CurrentVersion={_currentVersion}", _initializeSection, 0, null, SaveServiceState.Ready);
                return;
            }

            if (loadedVersion > _currentVersion)
            {
                HandleCorruptedOrInvalidSave($"Save version is newer than supported. FileVersion={loadedVersion}, CurrentVersion={_currentVersion}", _initializeSection, 0, null, SaveServiceState.WriteBlocked);
                return;
            }

            if (!TryUpgradeRootIfNeeded(root, loadedVersion, out var upgradedRoot, out var upgradedVersion))
            {
                SetDefaultSaveData(SaveServiceState.WriteBlocked);
                return;
            }

            _saveData = _saveDataEnvelopeMapper.ParseRoot(upgradedRoot, upgradedVersion);
            _state = SaveServiceState.Ready;
        }

        private bool TryUpgradeRootIfNeeded(JsonNode root, int loadedVersion, out JsonNode upgradedRoot, out int upgradedVersion)
        {
            if (!_saveMigrationRunner.TryUpgrade(root, loadedVersion, _backend, out upgradedRoot, out upgradedVersion))
                return false;

            if (loadedVersion < upgradedVersion)
                PersistMigratedRoot(upgradedRoot);

            return true;
        }

        private void PersistMigratedRoot(JsonNode root)
        {
            if (!TryPersistRoot(root, _migrationSection, out _))
                GameLog.Error($"[SaveSystem] Migration persisted in-memory only. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={_migrationSection}, PayloadBytes=<unknown>, ExceptionType=None");
        }

        private void SetDefaultSaveData(SaveServiceState state)
        {
            _saveData = new SaveData { Version = _currentVersion };
            _state = state;
        }

        public T Load<T>(string section, T defaultValue)
        {
            ValidateSection(section);

            if (_state == SaveServiceState.NotInitialized)
            {
                GameLog.Error($"[SaveSystem] Load called before Initialize. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType=None");
                return defaultValue;
            }

            EnsureMainThread();

            if (!TryEnsureTypeInfo(typeof(T), section, true) 
                || !_saveData.Sections.TryGetValue(section, out var sectionNode) || sectionNode == null)
                return defaultValue;

            try
            {
                var sectionJson = sectionNode.ToString();
                var payloadBytes = Encoding.UTF8.GetByteCount(sectionJson);

                if (_serializer.TryDeserialize(sectionJson, out T value))
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

        public void Save<T>(string section, T data) => TrySave(section, data);

        public SaveWriteResult TrySave<T>(string section, T data)
        {
            ValidateSection(section);

            if (_state == SaveServiceState.NotInitialized)
            {
                GameLog.Error($"[SaveSystem] Save called before Initialize. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType=None");
                return SaveWriteResult.Failed(SaveWriteError.NotInitialized);
            }

            if (_state == SaveServiceState.WriteBlocked)
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
                sectionJson = _serializer.Serialize(data);
                _saveData.Sections[section] = JsonNode.Parse(sectionJson);
                _saveData.Version = _currentVersion;
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SaveSystem] Failed to serialize section. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes=0, ExceptionType={ex.GetType().Name}, ExceptionMessage={ex.Message}");
                return SaveWriteResult.Failed(SaveWriteError.SerializationFailed);
            }

            var payloadBytes = Encoding.UTF8.GetByteCount(sectionJson);
            _saveFrequencyWarningTracker.Track(section, payloadBytes);

            if (!TryPersistRoot(_saveDataEnvelopeMapper.BuildRoot(_saveData), section, out var totalBytes))
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

        private static bool TryGetVersion(JsonNode root, out int version)
        {
            version = 0;
            var rootObject = root.AsObject;
            
            if (rootObject == null || !rootObject.HasKey(SaveDataEnvelopeFields.VersionKey))
                return false;

            version = rootObject[SaveDataEnvelopeFields.VersionKey].AsInt;
            return version > 0;
        }

        private static void ValidateSection(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                throw new ArgumentException("Section must be a non-empty value.", nameof(section));
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

            const string message = "[SaveSystem] SaveService can be used only from Unity main thread.";
            GameLog.Error(message);
            throw new InvalidOperationException(message);
#endif
        }

        private void HandleCorruptedOrInvalidSave(string error, string section, int payloadBytes, Exception exception, SaveServiceState state)
        {
            var exceptionType = exception?.GetType().Name ?? "None";
            var exceptionMessage = exception?.Message ?? string.Empty;
            GameLog.Error($"[SaveSystem] {error}. Backend={_backend.GetType().Name}, Path={_backend.GetDisplayPath()}, Section={section}, PayloadBytes={payloadBytes}, ExceptionType={exceptionType}, ExceptionMessage={exceptionMessage}");
            SetDefaultSaveData(state);
        }
    }
}