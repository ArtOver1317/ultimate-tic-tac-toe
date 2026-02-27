using FluentAssertions;
using NUnit.Framework;
using Runtime.PlayerProfile;

namespace Tests.EditMode.PlayerProfile
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerNameValidatorTests
    {
        [Test]
        public void WhenInputIsNull_ThenReturnsEmptyError()
        {
            var result = PlayerNameValidator.ValidateOnConfirm(null);

            result.Should().Be(PlayerNameValidationError.Empty);
        }

        [Test]
        public void WhenInputIsEmpty_ThenReturnsEmptyError()
        {
            var result = PlayerNameValidator.ValidateOnConfirm(string.Empty);

            result.Should().Be(PlayerNameValidationError.Empty);
        }

        [Test]
        public void WhenInputContainsOnlySpaces_ThenReturnsInvalidCharactersError()
        {
            var result = PlayerNameValidator.ValidateOnConfirm("   ");

            result.Should().Be(PlayerNameValidationError.InvalidCharacters);
        }

        [Test]
        public void WhenInputLengthExceedsMax_ThenReturnsTooLongError()
        {
            var result = PlayerNameValidator.ValidateOnConfirm("ABCDEFGHIJKLMN");

            result.Should().Be(PlayerNameValidationError.TooLong);
        }

        [Test]
        public void WhenInputContainsInvalidSymbol_ThenReturnsInvalidCharactersError()
        {
            var result = PlayerNameValidator.ValidateOnConfirm("Name_");

            result.Should().Be(PlayerNameValidationError.InvalidCharacters);
        }

        [Test]
        public void WhenInputContainsAllowedLatinAndCyrillicSymbols_ThenReturnsNone()
        {
            var result = PlayerNameValidator.ValidateOnConfirm("Ёжик007");

            result.Should().Be(PlayerNameValidationError.None);
        }
    }
}