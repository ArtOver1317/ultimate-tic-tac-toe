using System;
using System.IO;
using System.Reflection;
using System.Text;
using Editor.Localization;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class CsvToJsonConverterTests
    {
        private string _tempRootPath;

        [TearDown]
        public void TearDown()
        {
            if (string.IsNullOrWhiteSpace(_tempRootPath) || !Directory.Exists(_tempRootPath))
                return;

            Directory.Delete(_tempRootPath, recursive: true);
        }

        [Test]
        public void WhenConvertHeadlessWithValidCsv_ThenWritesLanguageFoldersAndJsonTables()
        {
            // Arrange
            _tempRootPath = Path.Combine(Path.GetTempPath(), nameof(CsvToJsonConverterTests), Guid.NewGuid().ToString("N"));
            var csvPath = Path.Combine(_tempRootPath, "Localization_SourceOfTruth.csv");
            var outputPath = Path.Combine(_tempRootPath, "Output");
            Directory.CreateDirectory(outputPath);

            File.WriteAllText(
                csvPath,
                string.Join(
                    "\n",
                    "Key,en-US,ru-RU,Context",
                    "Common.Hello,Hello,Привет,Greeting",
                    "Errors.Fail,Fail,Ошибка,Failure"),
                Encoding.UTF8);

            var converter = ScriptableObject.CreateInstance<CsvToJsonConverter>();

            try
            {
                SetPrivateField(converter, "_csvPath", csvPath);
                SetPrivateField(converter, "_outputPath", outputPath);

                // Act
                InvokeConvert(converter, showDialogs: false);

                // Assert
                var commonEnglishPath = Path.Combine(outputPath, "en", "Common.json");
                var errorsRussianPath = Path.Combine(outputPath, "ru", "Errors.json");

                File.Exists(commonEnglishPath).Should().BeTrue();
                File.Exists(errorsRussianPath).Should().BeTrue();

                var commonEnglishJson = File.ReadAllText(commonEnglishPath, Encoding.UTF8);
                commonEnglishJson.Should().Contain("\"locale\": \"en-US\"");
                commonEnglishJson.Should().Contain("\"table\": \"Common\"");
                commonEnglishJson.Should().Contain("\"Common.Hello\": \"Hello\"");

                var errorsRussianJson = File.ReadAllText(errorsRussianPath, Encoding.UTF8);
                errorsRussianJson.Should().Contain("\"locale\": \"ru-RU\"");
                errorsRussianJson.Should().Contain("\"table\": \"Errors\"");
                errorsRussianJson.Should().Contain("\"Errors.Fail\": \"Ошибка\"");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(converter);
            }
        }

        private static void SetPrivateField(CsvToJsonConverter converter, string fieldName, string value)
        {
            var field = typeof(CsvToJsonConverter).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(converter, value);
        }

        private static void InvokeConvert(CsvToJsonConverter converter, bool showDialogs)
        {
            var method = typeof(CsvToJsonConverter).GetMethod(
                "Convert",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);

            method.Should().NotBeNull();
            method!.Invoke(converter, new object[] { showDialogs });
        }
    }
}