using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class LocalizationStoreTests
    {
        private LocalizationStore _store;
        private ILocalizationPolicy _mockPolicy;
        private LocaleId _enUs;
        private LocaleId _ruRu;
        private LocaleId _ru;
        private TextTableId _uiTable;
        private TextTableId _gameplayTable;

        [SetUp]
        public void Setup()
        {
            _enUs = new LocaleId("en-US");
            _ruRu = new LocaleId("ru-RU");
            _ru = new LocaleId("ru");
            _uiTable = new TextTableId("UI");
            _gameplayTable = new TextTableId("Gameplay");

            _mockPolicy = Substitute.For<ILocalizationPolicy>();
            _mockPolicy.MaxCachedTables.Returns(3);
            _mockPolicy.DefaultLocale.Returns(_enUs);
            
            _mockPolicy.GetFallbackChain(Arg.Any<LocaleId>()).Returns(ci =>
            {
                var locale = (LocaleId)ci[0];
                
                if (locale == _ruRu)
                    return new[] { _ruRu, _ru, _enUs };
                
                if (locale == _ru)
                    return new[] { _ru, _enUs };
                
                return new[] { locale };
            });

            _store = new LocalizationStore(_mockPolicy);
        }

        [TearDown]
        public void TearDown() => _store?.Dispose();

        [Test]
        public void WhenPuttingTable_ThenStoresTable()
        {
            // Arrange
            var entries = new Dictionary<string, string> { { "Test.Key", "Test Value" } };
            var table = new LocalizationTable(_enUs, _uiTable, entries);

            // Act
            _store.Put(table);

            // Assert
            _store.SetActiveLocale(_enUs);
            var resolved = _store.TryResolveTemplate(_uiTable, new TextKey("Test.Key"), out var template);
            resolved.Should().BeTrue();
            template.Should().Be("Test Value");
        }

        [Test]
        public void WhenPuttingTableExceedingLimit_ThenEvictsOldest()
        {
            // Arrange
            var table1 = new LocalizationTable(_enUs, _uiTable, new Dictionary<string, string> { { "Key1", "Value1" } });
            var table2 = new LocalizationTable(_enUs, _gameplayTable, new Dictionary<string, string> { { "Key2", "Value2" } });
            var table3 = new LocalizationTable(_ruRu, _uiTable, new Dictionary<string, string> { { "Key3", "Value3" } });
            var table4 = new LocalizationTable(_ruRu, _gameplayTable, new Dictionary<string, string> { { "Key4", "Value4" } });

            // Act
            _store.Put(table1);
            _store.Put(table2);
            _store.Put(table3);
            _store.Put(table4); // Should evict table1

            // Assert
            _store.SetActiveLocale(_enUs);
            _store.TryResolveTemplate(_uiTable, new TextKey("Key1"), out _).Should().BeFalse();
            
            _store.TryResolveTemplate(_gameplayTable, new TextKey("Key2"), out _).Should().BeTrue();
            
            _store.SetActiveLocale(_ruRu);
            _store.TryResolveTemplate(_uiTable, new TextKey("Key3"), out _).Should().BeTrue();
            _store.TryResolveTemplate(_gameplayTable, new TextKey("Key4"), out _).Should().BeTrue();
        }

        [Test]
        public void WhenResolvingWithActiveLocale_ThenReturnsTemplate()
        {
            // Arrange
            var entries = new Dictionary<string, string> { { "Test.Key", "English Value" } };
            var table = new LocalizationTable(_enUs, _uiTable, entries);
            _store.Put(table);
            _store.SetActiveLocale(_enUs);

            // Act
            var resolved = _store.TryResolveTemplate(_uiTable, new TextKey("Test.Key"), out var template);

            // Assert
            resolved.Should().BeTrue();
            template.Should().Be("English Value");
        }

        [Test]
        public void WhenResolvingWithFallback_ThenSearchesChain()
        {
            // Arrange
            var enEntries = new Dictionary<string, string> { { "Test.Key", "English Value" } };
            var enTable = new LocalizationTable(_enUs, _uiTable, enEntries);
            _store.Put(enTable);
            
            _store.SetActiveLocale(_ruRu);

            // Act
            var resolved = _store.TryResolveTemplate(_uiTable, new TextKey("Test.Key"), out var template);

            // Assert
            resolved.Should().BeTrue();
            template.Should().Be("English Value");
        }

        [Test]
        public void WhenResolvingMissingKey_ThenReturnsFalse()
        {
            // Arrange
            var entries = new Dictionary<string, string> { { "Other.Key", "Value" } };
            var table = new LocalizationTable(_enUs, _uiTable, entries);
            _store.Put(table);
            _store.SetActiveLocale(_enUs);

            // Act
            var resolved = _store.TryResolveTemplate(_uiTable, new TextKey("Missing.Key"), out var template);

            // Assert
            resolved.Should().BeFalse();
            template.Should().BeNull();
        }

        [Test]
        public void WhenSettingActiveLocale_ThenEmitsEvent()
        {
            // Arrange
            LocalizationStoreEvent? capturedEvent = null;
            var subscription = _store.Events.Subscribe(e => capturedEvent = e);

            // Act
            _store.SetActiveLocale(_ruRu);

            // Assert
            capturedEvent.Should().NotBeNull();
            capturedEvent.Value.Type.Should().Be(LocalizationStoreEventType.ActiveLocaleChanged);
            capturedEvent.Value.Locale.Should().Be(_ruRu);
            
            subscription.Dispose();
        }

        [Test]
        public void WhenPuttingTable_ThenEmitsTableLoadedEvent()
        {
            // Arrange
            LocalizationStoreEvent? capturedEvent = null;
            
            var subscription = _store.Events.Subscribe(e =>
            {
                if (e.Type == LocalizationStoreEventType.TableLoaded)
                    capturedEvent = e;
            });

            var table = new LocalizationTable(_enUs, _uiTable, new Dictionary<string, string>());

            // Act
            _store.Put(table);

            // Assert
            capturedEvent.Should().NotBeNull();
            capturedEvent.Value.Type.Should().Be(LocalizationStoreEventType.TableLoaded);
            capturedEvent.Value.Locale.Should().Be(_enUs);
            capturedEvent.Value.TableId.Should().Be(_uiTable);
            
            subscription.Dispose();
        }

        [Test]
        public void WhenResolvingUpdatesLRU_ThenRecentTablesNotEvicted()
        {
            // Arrange
            var table1 = new LocalizationTable(_enUs, _uiTable, new Dictionary<string, string> { { "Key1", "Value1" } });
            var table2 = new LocalizationTable(_enUs, _gameplayTable, new Dictionary<string, string> { { "Key2", "Value2" } });
            var table3 = new LocalizationTable(_ruRu, _uiTable, new Dictionary<string, string> { { "Key3", "Value3" } });
            
            _store.Put(table1);
            _store.Put(table2);
            _store.Put(table3);

            // Act - Access table1 to mark it as recently used
            _store.SetActiveLocale(_enUs);
            _store.TryResolveTemplate(_uiTable, new TextKey("Key1"), out _);
            
            // Add table4, should evict table2 (least recently used)
            var table4 = new LocalizationTable(_ruRu, _gameplayTable, new Dictionary<string, string> { { "Key4", "Value4" } });
            _store.Put(table4);

            // Assert
            _store.SetActiveLocale(_enUs);
            _store.TryResolveTemplate(_uiTable, new TextKey("Key1"), out _).Should().BeTrue();
            _store.TryResolveTemplate(_gameplayTable, new TextKey("Key2"), out _).Should().BeFalse();
        }
    }
}