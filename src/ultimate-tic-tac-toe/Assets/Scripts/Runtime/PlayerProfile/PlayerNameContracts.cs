#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Runtime.PlayerProfile
{
    public enum PlayerNameValidationError
    {
        None = 0,
        Empty = 1,
        TooLong = 2,
        InvalidCharacters = 3,
    }

    public static class PlayerNameValidator
    {
        public const int MinLength = 1;
        public const int MaxLength = 13;

        public static PlayerNameValidationError ValidateOnConfirm(string? input)
        {
            if (input == null || input.Length < MinLength)
                return PlayerNameValidationError.Empty;

            if (input.Length > MaxLength)
                return PlayerNameValidationError.TooLong;

            for (var i = 0; i < input.Length; i++)
            {
                if (!IsAllowedCharacter(input[i]))
                    return PlayerNameValidationError.InvalidCharacters;
            }

            return PlayerNameValidationError.None;
        }

        private static bool IsAllowedCharacter(char symbol)
        {
            if (symbol is >= 'A' and <= 'Z')
                return true;

            if (symbol is >= 'a' and <= 'z')
                return true;

            if (symbol is >= '0' and <= '9')
                return true;

            if (symbol == 'Ё' || symbol == 'ё')
                return true;

            if (symbol is >= 'А' and <= 'Я')
                return true;

            return symbol is >= 'а' and <= 'я';
        }
    }

    public readonly struct PlayerNameSnapshot : IEquatable<PlayerNameSnapshot>
    {
        public string? CustomName { get; }
        public string DisplayName { get; }

        public PlayerNameSnapshot(string? customName, string displayName)
        {
            CustomName = customName;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        }

        public bool Equals(PlayerNameSnapshot other)
            => string.Equals(CustomName, other.CustomName, StringComparison.Ordinal)
               && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PlayerNameSnapshot other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(CustomName, DisplayName);
    }

    public readonly struct PlayerNameChangeResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessageKey { get; }
        public PlayerNameValidationError ValidationError { get; }

        private PlayerNameChangeResult(bool isSuccess, string? errorMessageKey, PlayerNameValidationError validationError)
        {
            IsSuccess = isSuccess;
            ErrorMessageKey = errorMessageKey;
            ValidationError = validationError;
        }

        public static PlayerNameChangeResult Success() => new(true, null, PlayerNameValidationError.None);

        public static PlayerNameChangeResult FailedValidation(string key, PlayerNameValidationError error)
            => new(false, key, error);

        public static PlayerNameChangeResult FailedSave(string key)
            => new(false, key, PlayerNameValidationError.None);
    }

    public static class PlayerNameDefaults
    {
        public const string FallbackDisplayName = "Player";
    }

    internal static class PlayerNameLocalizationKeys
    {
        public const string Table = "Common";
        public const string PlayerWord = "Common.Player";
    }

    internal static class PlayerNameLocalizationResolver
    {
        public static string ResolvePlayerWordOrFallback(ILocalizationService localizationService)
        {
            if (localizationService == null)
                throw new ArgumentNullException(nameof(localizationService));

            try
            {
                var localizedPlayerWord = localizationService.Resolve(
                    new TextTableId(PlayerNameLocalizationKeys.Table),
                    new TextKey(PlayerNameLocalizationKeys.PlayerWord));

                return string.IsNullOrWhiteSpace(localizedPlayerWord)
                    ? PlayerNameDefaults.FallbackDisplayName
                    : localizedPlayerWord;
            }
            catch (InvalidOperationException)
            {
                return PlayerNameDefaults.FallbackDisplayName;
            }
        }
    }

    public interface IPlayerNameService
    {
        ReadOnlyReactiveProperty<PlayerNameSnapshot> Snapshot { get; }

        UniTask<PlayerNameChangeResult> TryChangeNameAsync(string? requestedName, CancellationToken ct);
    }
}