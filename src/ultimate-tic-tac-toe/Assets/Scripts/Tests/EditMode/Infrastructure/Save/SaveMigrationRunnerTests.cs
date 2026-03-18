using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Infrastructure.Save;
using Runtime.Infrastructure.Save.Migration;
using UnityEngine;
using UnityEngine.TestTools;
using JsonNode = SimpleJSON.JSONNode;

namespace Tests.EditMode.Infrastructure.Save
{
    [Category("Unit")]
    public class SaveMigrationRunnerTests
    {
        [Test]
        public void WhenRefreshIndexAndMigrationsContainDuplicateFromVersion_ThenThrowsInvalidOperationException()
        {
            var runner = new SaveMigrationRunner(2, new ISaveMigration[]
            {
                new TestMigration(1),
                new TestMigration(1),
            });

            Action act = runner.RefreshIndex;

            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenTryUpgradeAndMigrationChainIsComplete_ThenReturnsUpgradedRoot()
        {
            var runner = new SaveMigrationRunner(3, new ISaveMigration[]
            {
                new TestMigration(1, root => AddMarker(root, "step_1")),
                new TestMigration(2, root => AddMarker(root, "step_2")),
            });

            runner.RefreshIndex();

            var result = runner.TryUpgrade(ParseRoot("{\"version\":1,\"sections\":{}}"), 1, new TestSaveBackend(), out var upgradedRoot, out var upgradedVersion);

            result.Should().BeTrue();
            upgradedVersion.Should().Be(3);
            upgradedRoot["version"].AsInt.Should().Be(3);
            upgradedRoot["step_1"].AsBool.Should().BeTrue();
            upgradedRoot["step_2"].AsBool.Should().BeTrue();
        }

        [Test]
        public void WhenTryUpgradeAndMigrationMissing_ThenReturnsFalseAndLogsError()
        {
            var runner = new SaveMigrationRunner(2, Array.Empty<ISaveMigration>());
            runner.RefreshIndex();

            LogAssert.Expect(LogType.Error, new Regex("Missing migration"));

            var result = runner.TryUpgrade(ParseRoot("{\"version\":1,\"sections\":{}}"), 1, new TestSaveBackend(), out _, out var upgradedVersion);

            result.Should().BeFalse();
            upgradedVersion.Should().Be(1);
        }

        [Test]
        public void WhenTryUpgradeAndMigrationThrows_ThenReturnsFalseAndLogsError()
        {
            var runner = new SaveMigrationRunner(2, new ISaveMigration[]
            {
                new TestMigration(1, _ => throw new IOException("Migration failed")),
            });

            runner.RefreshIndex();
            LogAssert.Expect(LogType.Error, new Regex("Migration failed"));

            var result = runner.TryUpgrade(ParseRoot("{\"version\":1,\"sections\":{}}"), 1, new TestSaveBackend(), out _, out _);

            result.Should().BeFalse();
        }

        [Test]
        public void WhenTryUpgradeAndMigrationReturnsNonObjectRoot_ThenReturnsFalseAndLogsError()
        {
            var runner = new SaveMigrationRunner(2, new ISaveMigration[]
            {
                new TestMigration(1, _ => JsonNode.Parse("[]")),
            });

            runner.RefreshIndex();
            LogAssert.Expect(LogType.Error, new Regex("root is not an object"));

            var result = runner.TryUpgrade(ParseRoot("{\"version\":1,\"sections\":{}}"), 1, new TestSaveBackend(), out _, out _);

            result.Should().BeFalse();
        }

        private static JsonNode ParseRoot(string json)
            => JsonNode.Parse(json);

        private static JsonNode AddMarker(JsonNode root, string markerKey)
        {
            var rootObject = root.AsObject;
            rootObject.Should().NotBeNull();
            rootObject[markerKey] = true;
            return root;
        }

        private sealed class TestSaveBackend : ISaveBackend
        {
            public string Read() => string.Empty;

            public void Write(string data)
            {
            }

            public string GetDisplayPath() => "TestBackend";
        }

        private sealed class TestMigration : ISaveMigration
        {
            private readonly Func<JsonNode, JsonNode> _migrate;

            public TestMigration(int fromVersion, Func<JsonNode, JsonNode> migrate = null)
            {
                FromVersion = fromVersion;
                _migrate = migrate ?? (root => root);
            }

            public int FromVersion { get; }

            public JsonNode Migrate(JsonNode root) => _migrate(root);
        }
    }
}