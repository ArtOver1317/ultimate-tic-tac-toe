using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization.Types;

namespace Tests.EditMode.Localization.Services
{
    public partial class LocalizationServiceTests
    {
        [Test]
        public void WhenResolve_ThenDelegatesToStoreAndFormatter()
        {
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Test Template";
                    return true;
                });

            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockFormatter.Format("Test Template", _enUs, null).Returns("Formatted Text");

            var result = _service.Resolve(_uiTable, _testKey);

            result.Should().Be("Formatted Text");
            _mockFormatter.Received(1).Format("Test Template", _enUs, null);
        }

        [Test]
        public void WhenResolveWithMissingKey_ThenReturnsPlaceholder()
        {
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(false);

            _mockStore.GetActiveLocale().Returns(_enUs);

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            var result = _service.Resolve(_uiTable, _testKey);

            result.Should().Be($"⟦Missing: {_uiTable.Name}.{_testKey.Value}⟧");
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.MissingKey);
        }

        [Test]
        public void WhenResolveWithArgs_ThenFormatsTemplate()
        {
            InitializeService();

            var args = new Dictionary<string, object> { { "name", "Bob" } };

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Hello, {name}!";
                    return true;
                });

            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockFormatter.Format("Hello, {name}!", _enUs, args).Returns("Hello, Bob!");

            var result = _service.Resolve(_uiTable, _testKey, args);

            result.Should().Be("Hello, Bob!");
        }

        [Test]
        public void WhenResolveWithMissingKeyMultipleTimes_ThenEmitsErrorOnce()
        {
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(false);

            _mockStore.GetActiveLocale().Returns(_enUs);

            var errorCount = 0;
            _service.Errors.Subscribe(_ => errorCount++);

            _service.Resolve(_uiTable, _testKey);
            _service.Resolve(_uiTable, _testKey);
            _service.Resolve(_uiTable, _testKey);

            errorCount.Should().Be(1);
        }
    }
}