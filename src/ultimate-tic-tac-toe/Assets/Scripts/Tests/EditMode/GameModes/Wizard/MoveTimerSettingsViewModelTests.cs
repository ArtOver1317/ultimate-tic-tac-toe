#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MoveTimerSettingsViewModelTests
    {
        private ILocalizationService _localization = null!;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
        }

        [Test]
        public void WhenCreated_ThenDefaultMoveTimeLimitIsZero()
        {
            using var sut = CreateSut();

            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
        }

        [Test]
        public void WhenPresetSelected_ThenMoveTimeLimitSecondsUpdated()
        {
            using var sut = CreateSut();

            sut.SetSelectedPresetId("30");

            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(30);
        }

        [Test]
        public void WhenSetSelectedPresetIdIsNullOrNonNumeric_ThenNoOp()
        {
            using var sut = CreateSut();

            sut.SetSelectedPresetId(null);
            sut.SetSelectedPresetId("abc");

            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
        }

        [Test]
        public void WhenSetSelectedPresetIdNotInPresets_ThenNoOp()
        {
            using var sut = CreateSut();

            sut.SetSelectedPresetId("999");

            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
        }

        [Test]
        public void WhenTryApplyConfigWithValidValue_ThenReturnsTrueAndUpdatesSelection()
        {
            using var sut = CreateSut();

            var result = sut.TryApplyConfig(60);

            result.Should().BeTrue();
            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(60);
            sut.SelectedPresetId.CurrentValue.Should().Be("60");
        }

        [Test]
        public void WhenTryApplyConfigWithInvalidValue_ThenReturnsFalseAndFallbackToZero()
        {
            using var sut = CreateSut();
            sut.SetSelectedPresetId("30");

            var result = sut.TryApplyConfig(999);

            result.Should().BeFalse();
            sut.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
        }

        private MoveTimerSettingsViewModel CreateSut()
        {
            return new MoveTimerSettingsViewModel(MoveTimerPresetsConfig.CreateRuntimeDefault(), _localization);
        }
    }
}
