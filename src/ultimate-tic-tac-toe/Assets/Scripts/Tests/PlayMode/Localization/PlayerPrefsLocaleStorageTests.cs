using System.Collections;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Localization
{
    [Category("PlayMode")]
    public class PlayerPrefsLocaleStorageTests
    {
        private const string _testLocaleKey = "Runtime.Localization.Locale";
        private PlayerPrefsLocaleStorage _storage;

        [SetUp]
        public void Setup()
        {
            _storage = new PlayerPrefsLocaleStorage();
            PlayerPrefs.DeleteKey(_testLocaleKey);
        }

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(_testLocaleKey);

        [UnityTest]
        public IEnumerator WhenLoadAsyncWithNoSavedData_ThenReturnsNull()
        {
            // Act
            var task = _storage.LoadAsync();
            yield return task.ToCoroutine();

            // Assert
            task.GetAwaiter().GetResult().Should().BeNull();
        }

        [UnityTest]
        public IEnumerator WhenSaveAsyncAndLoad_ThenReturnsLocale()
        {
            // Arrange
            var locale = new LocaleId("en-US");

            // Act
            var saveTask = _storage.SaveAsync(locale);
            yield return saveTask.ToCoroutine();

            var loadTask = _storage.LoadAsync();
            yield return loadTask.ToCoroutine();

            // Assert
            loadTask.GetAwaiter().GetResult().Should().Be(locale);
        }

        [UnityTest]
        public IEnumerator WhenSaveAsyncMultipleTimes_ThenLastValuePersists()
        {
            // Arrange
            var locale1 = new LocaleId("en-US");
            var locale2 = new LocaleId("ru-RU");

            // Act
            var saveTask1 = _storage.SaveAsync(locale1);
            yield return saveTask1.ToCoroutine();

            var saveTask2 = _storage.SaveAsync(locale2);
            yield return saveTask2.ToCoroutine();

            var loadTask = _storage.LoadAsync();
            yield return loadTask.ToCoroutine();

            // Assert
            loadTask.GetAwaiter().GetResult().Should().Be(locale2);
        }

        [UnityTest]
        public IEnumerator WhenLoadAsyncWithEmptyString_ThenReturnsNull()
        {
            // Arrange
            PlayerPrefs.SetString(_testLocaleKey, string.Empty);

            // Act
            var task = _storage.LoadAsync();
            yield return task.ToCoroutine();

            // Assert
            task.GetAwaiter().GetResult().Should().BeNull();
        }

        [UnityTest]
        public IEnumerator WhenLoadAsyncWithWhitespace_ThenReturnsNull()
        {
            // Arrange
            PlayerPrefs.SetString(_testLocaleKey, "   ");

            // Act
            var task = _storage.LoadAsync();
            yield return task.ToCoroutine();

            // Assert
            task.GetAwaiter().GetResult().Should().BeNull();
        }
    }
}