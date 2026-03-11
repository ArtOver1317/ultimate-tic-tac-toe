using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateTicTacToeSettingsViewModelTests
    {
        [Test]
        public void WhenCreated_ThenConfigIsUltimateSingletonAndIsValidTrue()
        {
            using var sut = new UltimateTicTacToeSettingsViewModel();

            sut.IsValid.CurrentValue.Should().BeTrue();
            sut.Config.CurrentValue.Should().BeSameAs(UltimateTicTacToeConfig.Instance);
        }

        [Test]
        public void WhenTryApplyConfigCalledWithUltimateConfig_ThenReturnsTrue()
        {
            using var sut = new UltimateTicTacToeSettingsViewModel();

            var result = sut.TryApplyConfig(UltimateTicTacToeConfig.Instance);

            result.Should().BeTrue();
            sut.Config.CurrentValue.Should().BeSameAs(UltimateTicTacToeConfig.Instance);
        }

        [Test]
        public void WhenTryApplyConfigCalledWithWrongConfig_ThenReturnsFalse()
        {
            using var sut = new UltimateTicTacToeSettingsViewModel();

            var result = sut.TryApplyConfig(new TicTacToeConfig(3));

            result.Should().BeFalse();
        }
    }
}
