using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly string[] InvalidVersionPayloads =
        {
            "{\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":0,\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":-1,\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":true,\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":{},\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":[],\"sections\":{\"locale\":\"en-US\"}}",
            "{\"version\":\"invalid\",\"sections\":{\"locale\":\"en-US\"}}",
        };

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
        public void WhenSaveCalledWithNullSectionBeforeInitialize_ThenThrowsArgumentException()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);

            Action act = () => service.Save<string>(null, "value");

            act.Should().Throw<ArgumentException>();
            backend.WriteCount.Should().Be(0);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void WhenSaveCalledWithEmptyOrWhitespaceSection_ThenThrowsArgumentException(string section)
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            Action act = () => service.Save(section, "value");

            act.Should().Throw<ArgumentException>();
            backend.WriteCount.Should().Be(0);
        }

        [Test]
        public void WhenLoadCalledWithNullSectionBeforeInitialize_ThenThrowsArgumentException()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);

            Action act = () => service.Load<string>(null, "default");

            act.Should().Throw<ArgumentException>();
        }

        [TestCase("")]
        [TestCase("   ")]
        public void WhenLoadCalledWithEmptyOrWhitespaceSection_ThenThrowsArgumentException(string section)
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            Action act = () => service.Load(section, "default");

            act.Should().Throw<ArgumentException>();
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

            LogAssert.Expect(LogType.Error, new Regex("parse|valid version"));

            service.Initialize();
            var result = service.Load("locale", "ja-JP");

            result.Should().Be("ja-JP");
        }

        [Test]
        public void WhenInitializeWithCorruptedPayload_ThenBackendIsNotOverwritten()
        {
            var backend = new TestSaveBackend
            {
                RawData = "not-a-valid-save-payload",
            };
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("parse|valid version"));

            service.Initialize();

            backend.WriteCount.Should().Be(0);
        }

        [TestCaseSource(nameof(InvalidVersionPayloads))]
        public void WhenInitializeWithValidJsonButMissingOrInvalidVersion_ThenLoadReturnsDefaultAndBackendNotOverwritten(string payload)
        {
            var backend = new TestSaveBackend
            {
                RawData = EncryptJson(payload),
            };
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("valid version"));

            service.Initialize();
            var result = service.Load("locale", "default");

            result.Should().Be("default");
            backend.WriteCount.Should().Be(0);
        }

        [Test]
        public void WhenInitializeAndBackendReadThrows_ThenInitializeDoesNotThrowAndLoadReturnsDefault()
        {
            var backend = new ThrowingOnReadSaveBackend();
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("backend_read"));

            Action act = service.Initialize;

            act.Should().NotThrow();
            service.Load("locale", "fallback").Should().Be("fallback");
        }

        [Test]
        public void WhenBackendWriteThrowsDuringSave_ThenInMemoryDataPreservedAndNoExceptionThrown()
        {
            var backend = new ThrowingOnWriteSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            LogAssert.Expect(LogType.Error, new Regex("Persistence error"));
            LogAssert.Expect(LogType.Error, new Regex("Save write failed"));

            Action act = () => service.Save("locale", "en-US");

            act.Should().NotThrow();
            service.Load("locale", "default").Should().Be("en-US");
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
        public void WhenLoadIntFromStringSection_ThenReturnsDefaultValueAndLogsWarning()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("locale", "ru-RU");

            LogAssert.Expect(LogType.Warning, new Regex("Failed to deserialize section"));

            var result = service.Load("locale", 42);

            result.Should().Be(42);
        }

        [Test]
        public void WhenSaveCalledWithUnregisteredType_ThenThrowsInvalidOperationException()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            LogAssert.Expect(LogType.Error, new Regex("Type is not registered"));

            Action act = () => service.Save("x", new object());

            act.Should().Throw<InvalidOperationException>();
            backend.WriteCount.Should().Be(0);
        }

        [Test]
        public void WhenLoadCalledWithUnregisteredType_ThenThrowsInvalidOperationException()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            LogAssert.Expect(LogType.Error, new Regex("Type is not registered"));

            Action act = () => service.Load("x", new object());

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenSaveCalledBeforeInitialize_ThenDoesNotMutateInMemory()
        {
            var backend = new TestSaveBackend
            {
                RawData = EncryptJson("{\"version\":1,\"sections\":{\"locale\":\"ru-RU\"}}"),
            };
            var service = CreateService(backend);

            LogAssert.Expect(LogType.Error, new Regex("Save called before Initialize"));

            service.Save("locale", "en-US");
            service.Initialize();

            service.Load("locale", "default").Should().Be("ru-RU");
        }

        [Test]
        public void WhenSaveStringNullThenLoad_ThenReturnsDefaultValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("locale", (string)null);

            var result = service.Load("locale", "fallback");

            result.Should().Be("fallback");
        }

        [Test]
        public void WhenSaveAndLoadInt_ThenPersistsValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("score", 42);

            var result = service.Load("score", 0);

            result.Should().Be(42);
        }

        [Test]
        public void WhenSaveAndLoadBool_ThenPersistsValue()
        {
            var backend = new TestSaveBackend();
            var service = CreateService(backend);
            service.Initialize();

            service.Save("tutorial_done", false);

            var result = service.Load("tutorial_done", true);

            result.Should().BeFalse();
        }

        private static SaveService CreateService(ISaveBackend backend, IEnumerable<ISaveMigration> migrations = null)
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

        private sealed class ThrowingOnReadSaveBackend : ISaveBackend
        {
            public string Read() => throw new IOException("Read failed");

            public void Write(string data)
            {
            }

            public string GetDisplayPath() => "ThrowingOnReadBackend";
        }

        private sealed class ThrowingOnWriteSaveBackend : ISaveBackend
        {
            public string Read() => string.Empty;

            public void Write(string data) => throw new IOException("Write failed");

            public string GetDisplayPath() => "ThrowingOnWriteBackend";
        }

        private sealed class TestMigration : ISaveMigration
        {
            public TestMigration(int fromVersion) => FromVersion = fromVersion;

            public int FromVersion { get; }

            public JsonNode Migrate(JsonNode root) => root;
        }
    }
}
