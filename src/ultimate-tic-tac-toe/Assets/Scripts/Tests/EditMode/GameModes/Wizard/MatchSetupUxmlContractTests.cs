using FluentAssertions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchSetupUxmlContractTests
    {
        private const string MatchSetupUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/MatchSetup.uxml";
        private const string ClassicSettingsUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/ModeSettings/ClassicModeSettings.uxml";
        private const string UltimateSettingsUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/ModeSettings/UltimateModeSettings.uxml";

        [Test]
        public void WhenMatchSetupUxmlLoaded_ThenHasAllRequiredNamedElements()
        {
            // Arrange
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchSetupUxmlPath);
            uxml.Should().NotBeNull();

            var root = uxml.CloneTree();

            // Assert
            root.Q<Button>("BackButton").Should().NotBeNull();
            root.Q<Label>("TitleLabel").Should().NotBeNull();
            root.Q<Label>("ModeOptionsTitle").Should().NotBeNull();
            root.Q<VisualElement>("ModeOptionsHost").Should().NotBeNull();
            root.Q<Label>("OpponentTitle").Should().NotBeNull();
            root.Q<VisualElement>("OpponentToggle").Should().NotBeNull();
            root.Q<Button>("CancelButton").Should().NotBeNull();
            root.Q<Button>("StartButton").Should().NotBeNull();
        }

        [Test]
        public void WhenMatchSetupUxmlLoaded_ThenHasBotSettingsElements()
        {
            // Arrange
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchSetupUxmlPath);
            uxml.Should().NotBeNull();

            var root = uxml.CloneTree();

            // Assert
            root.Q<VisualElement>("BotSettingsSection").Should().NotBeNull();
            root.Q<Label>("BotSettingsTitle").Should().NotBeNull();
            root.Q<VisualElement>("DifficultyChips").Should().NotBeNull();
        }

        [Test]
        public void WhenClassicModeSettingsUxmlLoaded_ThenHasAllRequiredNamedElements()
        {
            // Arrange
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ClassicSettingsUxmlPath);
            uxml.Should().NotBeNull();

            var root = uxml.CloneTree();

            // Assert
            root.Q<Button>("DecrementButton").Should().NotBeNull();
            root.Q<Button>("IncrementButton").Should().NotBeNull();
            root.Q<Label>("BoardSizeValue").Should().NotBeNull();
            root.Q<Label>("BoardSizeTitle").Should().NotBeNull();
        }

        [Test]
        public void WhenUltimateModeSettingsUxmlLoaded_ThenHasAllRequiredNamedElements()
        {
            // Arrange
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UltimateSettingsUxmlPath);
            uxml.Should().NotBeNull();

            var root = uxml.CloneTree();

            // Assert
            root.Q<Label>("InfoLabel").Should().NotBeNull();
        }
    }
}