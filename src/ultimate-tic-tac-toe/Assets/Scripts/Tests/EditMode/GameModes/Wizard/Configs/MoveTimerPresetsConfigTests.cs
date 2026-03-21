#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;

namespace Tests.EditMode.GameModes.Wizard.Configs
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MoveTimerPresetsConfigTests
    {
        [Test]
        public void WhenPresetsContainDuplicates_ThenDuplicatesRemoved()
        {
            var result = MoveTimerPresetsConfig.NormalizePresets(new[] { 0, 15, 15, 30 });

            result.Should().Equal(0, 15, 30);
        }

        [Test]
        public void WhenPresetsContainNegativeValues_ThenNegativesSkipped()
        {
            var result = MoveTimerPresetsConfig.NormalizePresets(new[] { 0, -5, 30, 60 });

            result.Should().Equal(0, 30, 60);
        }

        [Test]
        public void WhenPresetsDoNotContainZero_ThenZeroInsertedFirst()
        {
            var result = MoveTimerPresetsConfig.NormalizePresets(new[] { 15, 30, 60 });

            result.Should().Equal(0, 15, 30, 60);
        }

        [Test]
        public void WhenPresetsContainZeroNotFirst_ThenZeroMovedToFront()
        {
            var result = MoveTimerPresetsConfig.NormalizePresets(new[] { 15, 0, 30 });

            result.Should().Equal(0, 15, 30);
        }

        [Test]
        public void WhenPresetsInputIsNullOrEmpty_ThenReturnsSingleZeroPreset()
        {
            var fromNull = MoveTimerPresetsConfig.NormalizePresets(null);
            var fromEmpty = MoveTimerPresetsConfig.NormalizePresets(System.Array.Empty<int>());

            fromNull.Should().Equal(0);
            fromEmpty.Should().Equal(0);
        }
    }
}
