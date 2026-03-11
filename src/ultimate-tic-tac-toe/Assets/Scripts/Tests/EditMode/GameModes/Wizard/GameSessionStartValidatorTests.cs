using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;

#nullable enable

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameSessionStartValidatorTests
    {
        [Test]
        public void WhenStrategyImplementsStartValidatorAndReturnsError_ThenSessionValidationContainsThatError()
        {
            // Arrange
            var expectedError = new ValidationError("Start", "Errors.GameWizard.StartValidation");
            var strategy = new StartValidatedStrategy(expectedError);
            var catalog = new GameCatalog(new IGameStrategy[] { strategy });

            using var sut = new GameSession(catalog, GameSessionSnapshot.Default
                .WithSelectedGameId(strategy.GameId)
                .WithGameConfig(new TestConfig())
                .WithBotDifficultyId("Easy"));

            // Act
            var errors = sut.ValidationErrors.CurrentValue;

            // Assert
            errors.Should().ContainSingle(e => e.Field == expectedError.Field && e.MessageKey == expectedError.MessageKey);
        }

        private sealed class StartValidatedStrategy : IGameStrategy, IGameStartValidator
        {
            private readonly ValidationError _error;

            public StartValidatedStrategy(ValidationError error)
            {
                _error = error;
                GameId = "validated";
                Metadata = new GameMetadata(
                    id: GameId,
                    displayNameKey: "Game.Validated",
                    descriptionKey: "Game.Validated.Description",
                    iconAssetKey: "icons/validated",
                    sortOrder: 0,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true);
            }

            public string GameId { get; }
            public GameMetadata Metadata { get; }

            public GameSettingsPresentation CreatePresentation() =>
                new("ui/mode-settings/validated", new FakeSettingsViewModel());

            public IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config) => Array.Empty<ValidationError>();

            public IEnumerable<string> GetSupportedBotDifficultyIds() => new[] { "Easy" };

            public IReadOnlyList<ValidationError> ValidateForStart(GameSessionSnapshot snapshot) => new[] { _error };
        }

        private sealed class FakeSettingsViewModel : Runtime.UI.Core.BaseViewModel, IGameSettingsViewModel
        {
            private readonly R3.ReactiveProperty<IGameConfig> _config = new(new TestConfig());
            private readonly R3.ReactiveProperty<bool> _isValid = new(true);

            public R3.ReadOnlyReactiveProperty<IGameConfig> Config => _config;
            public R3.ReadOnlyReactiveProperty<bool> IsValid => _isValid;
            public bool TryApplyConfig(IGameConfig config) => true;

            protected override void OnDispose()
            {
                _config.Dispose();
                _isValid.Dispose();
                base.OnDispose();
            }
        }

        private sealed class TestConfig : IGameConfig
        {
            public IReadOnlyList<KeyValuePair<string, string>> GetMatchmakingParams() => Array.Empty<KeyValuePair<string, string>>();
        }
    }
}
