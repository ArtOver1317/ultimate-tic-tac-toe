using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Infrastructure.Save;
using UnityEngine;
using UnityEngine.TestTools;
using JsonNode = SimpleJSON.JSONNode;

namespace Tests.EditMode.Infrastructure.Save
{
    [Category("Unit")]
    public class SaveServiceTests
    {
        [Test]
        public void WhenSaveCalledBeforeInitialize_ThenDoesNotWriteBackend()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("Save called before Initialize"));

            service.Save("locale", "en-US");

            backend.WriteCount.Should().Be(0);
        }

        [Test]
        public void WhenLoadCalledBeforeInitialize_ThenReturnsDefaultValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("Load called before Initialize"));

            var result = service.Load("locale", "ru-RU");

            result.Should().Be("ru-RU");
        }

        [Test]
        public void WhenInitializeWithEmptyStorageAndSaveThenLoad_ThenReturnsSavedValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("locale", "en-US");
            var result = service.Load("locale", "ru-RU");

            backend.WriteCount.Should().Be(1);
            result.Should().Be("en-US");
        }

        [Test]
        public void WhenSaveAndLoadLocaleCode_ThenPersistsValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("locale", "ru-RU");
            var result = service.Load("locale", string.Empty);

            backend.RawData.Should().Contain("\"locale\":\"ru-RU\"");
            result.Should().Be("ru-RU");
        }

        [Test]
        public void WhenInitializeWithCorruptedPayload_ThenLoadReturnsDefaultValue()
        {
            var backend = new TestSaveBackend
            {
                RawData = "not-a-valid-save-payload",
            };
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("Failed to read save data|Save data does not contain valid version"));

            service.Initialize();
            var result = service.Load("locale", "ja-JP");

            result.Should().Be("ja-JP");
        }

        [Test]
        public void WhenInitializeAndMigrationsContainDuplicateFromVersion_ThenThrowsInvalidOperationException()
        {
            var backend = new TestSaveBackend
            {
                RawData = EncryptJson("{\"version\":1,\"sections\":{}}"),
            };

            var migrations = new ISaveMigration[]
            {
                new TestMigration(0),
                new TestMigration(0),
            };

            var service = new SaveService(backend, new SaveEncryptor(), migrations);

            Action act = service.Initialize;

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenInitializeWithFutureVersion_ThenSaveIsBlockedAndBackendNotOverwritten()
        {
            var backend = new TestSaveBackend
            {
                RawData = EncryptJson("{\"version\":999,\"sections\":{\"locale\":\"en-US\"}}"),
            };

            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("Save version is newer than supported"));
            LogAssert.Expect(LogType.Error, new Regex("Save blocked due to incompatible persisted data"));

            service.Initialize();

            var originalPayload = backend.RawData;
            service.Save("locale", "ru-RU");

            backend.WriteCount.Should().Be(0);
            backend.RawData.Should().Be(originalPayload);
        }

        [Test]
        public void WhenLoadIntFromObjectSection_ThenReturnsDefaultValueAndLogsWarning()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("locale", "ru-RU");

            LogAssert.Expect(LogType.Warning, new Regex("Failed to deserialize section"));

            var result = service.Load("locale", 42);

            result.Should().Be(42);
        }

        private static SaveService CreateService(TestSaveBackend backend, IEnumerable<ISaveMigration> migrations = null)
            => new(backend, new SaveEncryptor(), migrations ?? Array.Empty<ISaveMigration>());

        private static string EncryptJson(string json)
            => new SaveEncryptor().Encrypt(json);

        private sealed class TestSaveBackend : ISaveBackend
        {
            public string RawData { get; set; } = string.Empty;
            public int WriteCount { get; private set; }

            public string Read() => RawData;

            public void Write(string data)
            {
                RawData = data;
                WriteCount++;
            }

            public string GetDisplayPath() => "TestBackend";
        }

        private sealed class TestMigration : ISaveMigration
        {
            public TestMigration(int fromVersion) => FromVersion = fromVersion;

            public int FromVersion { get; }

            public JsonNode Migrate(JsonNode root) => root;
        }
    }
}