using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.Services.UI.Assets;
using Runtime.UI.Components;
using Runtime.UI.Core;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchSetupViewEditModeTests
    {
        private const string MatchSetupUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/MatchSetup.uxml";
        private const string MatchSetupPrefabPath = "Assets/Content/UI/GameModes/Wizard/Prefabs/MatchSetup.prefab";

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private MatchSetupView _view;
        private VisualTreeAsset _uxml;

        private MatchSetupViewModel _viewModel;
        private FakeGameModeSession _session;
        private IGameModeWizardCoordinator _coordinator;
        private ILocalizationService _localization;
        private ReactiveProperty<bool> _isTransitioning;
        private ReactiveProperty<bool> _isSubmitting;
        private ReactiveProperty<WizardError?> _currentError;

        [SetUp]
        public void SetUp()
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchSetupUxmlPath);
            _uxml.Should().NotBeNull();

            _gameObject = new GameObject("MatchSetupViewEditMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<MatchSetupView>();

            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));

            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            _isTransitioning = new ReactiveProperty<bool>(false);
            _isSubmitting = new ReactiveProperty<bool>(false);
            _currentError = new ReactiveProperty<WizardError?>(null);

            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default);

            _coordinator = Substitute.For<IGameModeWizardCoordinator>();
            _coordinator.IsTransitioning.Returns(_isTransitioning);
            _coordinator.IsSubmitting.Returns(_isSubmitting);
            _coordinator.CurrentError.Returns(_currentError);
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(callInfo =>
            {
                callInfo[0] = _session;
                return true;
            });

            var catalog = Substitute.For<IGameModeCatalog>();
            var difficultyCatalog = new BotDifficultyCatalog();
            _viewModel = new MatchSetupViewModel(catalog, _coordinator, _localization, difficultyCatalog);

            var assetProvider = new FakeViewAssetProvider();
            var binders = Array.Empty<IModeSettingsBinder>();
            _view.Construct(assetProvider, binders);

            _view.SetViewModel(_viewModel);
            _view.RebindUxmlForTests();
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
            _session?.Dispose();

            _isTransitioning?.Dispose();
            _isSubmitting?.Dispose();
            _currentError?.Dispose();

            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void WhenCanStartFalseOrIsBusyTrue_ThenStartButtonIsDisabled()
        {
            // Arrange
            var startButton = _view.RootForTests.Q<Button>("StartButton");

            // Act
            _session.EmitCanStart(false);
            _isTransitioning.Value = false;
            _isSubmitting.Value = false;

            // Assert
            startButton.enabledSelf.Should().BeFalse();

            // Act
            _session.EmitCanStart(true);
            _isTransitioning.Value = true;

            // Assert
            startButton.enabledSelf.Should().BeFalse();
        }

        [Test]
        public void WhenCanStartTrueAndIsBusyFalse_ThenStartButtonIsEnabled()
        {
            // Arrange
            var startButton = _view.RootForTests.Q<Button>("StartButton");

            // Act
            _session.EmitCanStart(true);
            _isTransitioning.Value = false;
            _isSubmitting.Value = false;

            // Assert
            startButton.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenIsBusyTrue_ThenBackButtonAndOpponentToggleAreDisabled()
        {
            // Arrange
            var backButton = _view.RootForTests.Q<Button>("BackButton");
            var opponentToggle = _view.RootForTests.Q<SegmentedToggle>("OpponentToggle");

            // Act
            _isTransitioning.Value = true;

            // Assert
            backButton.enabledSelf.Should().BeFalse();
            opponentToggle.enabledSelf.Should().BeFalse();

            // Act
            _isTransitioning.Value = false;

            // Assert
            backButton.enabledSelf.Should().BeTrue();
            opponentToggle.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenIsBotSettingsVisibleFalse_ThenBotSettingsSectionIsHidden()
        {
            // Arrange
            var section = _view.RootForTests.Q<VisualElement>("BotSettingsSection");

            // Act
            _viewModel.SetOpponentType(OpponentType.Human);

            // Assert
            section.style.display.value.Should().Be(DisplayStyle.None);
        }

        [Test]
        public void WhenIsBotSettingsVisibleTrue_ThenBotSettingsSectionIsVisible()
        {
            // Arrange
            var section = _view.RootForTests.Q<VisualElement>("BotSettingsSection");
            _viewModel.SetOpponentType(OpponentType.Human);

            // Act
            _viewModel.SetOpponentType(OpponentType.Bot);

            // Assert
            section.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [Test]
        public void WhenDifficultyItemsChanges_ThenDifficultyChipsUpdates()
        {
            // Arrange
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            var items = Array.AsReadOnly(new[]
            {
                new DifficultyChipItem("Easy", "Easy"),
                new DifficultyChipItem("Hard", "Hard")
            });

            // Act
            _viewModel.SetDifficultyItemsForTests(items);

            // Assert
            chips.childCount.Should().Be(2);
            chips.Q<Button>("Easy").text.Should().Be("Easy");
            chips.Q<Button>("Hard").text.Should().Be("Hard");
        }

        [Test]
        public void WhenSelectedDifficultyIdChanges_ThenDifficultyChipsSelectionUpdates()
        {
            // Arrange
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            _viewModel.SetDifficultyItemsForTests(Array.AsReadOnly(new[]
            {
                new DifficultyChipItem("Easy", "Easy"),
                new DifficultyChipItem("Hard", "Hard")
            }));

            // Act
            _viewModel.SetBotDifficultyId("Hard");

            // Assert
            chips.SelectedId.Should().Be("Hard");
        }

        [Test]
        public void WhenIsBusyTrue_ThenDifficultyChipsIsDisabled()
        {
            // Arrange
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");

            // Act
            _isTransitioning.Value = true;

            // Assert
            chips.enabledSelf.Should().BeFalse();
        }

        [Test]
        public void WhenIsBusyFalse_ThenDifficultyChipsIsEnabled()
        {
            // Arrange
            var chips = _view.RootForTests.Q<DifficultyChips>("DifficultyChips");
            _isTransitioning.Value = true;

            // Act
            _isTransitioning.Value = false;

            // Assert
            chips.enabledSelf.Should().BeTrue();
        }

        [Test]
        public void WhenMatchSetupPrefabLoaded_ThenHasRequiredComponentsAndValidUxmlAsset()
        {
            // Arrange
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MatchSetupPrefabPath);
            prefab.Should().NotBeNull();

            // Act
            var view = prefab.GetComponent<MatchSetupView>();
            var document = prefab.GetComponent<UIDocument>();

            // Assert
            view.Should().NotBeNull();
            document.Should().NotBeNull();
            document.visualTreeAsset.Should().NotBeNull();
        }

        [Test]
        public void WhenErrorLabelIsMissingInUxml_ThenBindViewModelDoesNotThrowAndInlineErrorUpdatesDoNotCrash()
        {
            // Arrange
            var root = _view.RootForTests;
            var errorLabel = root.Q<Label>("ErrorLabel");
            errorLabel.Should().NotBeNull();
            errorLabel.RemoveFromHierarchy();

            _view.ClearViewModel();
            UxmlBinder.BindElements(_view, root);

            // Act
            Action act = () => _view.SetViewModel(_viewModel);

            // Assert
            act.Should().NotThrow();

            Action updateAct = () => _session.EmitValidationErrors(new List<ValidationError>
            {
                new("ModeConfig", "Errors.GameModeWizard.ModeConfigRequired"),
            });

            updateAct.Should().NotThrow();
        }

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

            public FakeGameModeSession(GameModeSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void EmitCanStart(bool value) => _canStart.Value = value;

            public void EmitValidationErrors(IReadOnlyList<ValidationError> errors) => _validationErrors.Value = errors;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer)
            {
                var current = _snapshot.Value ?? GameModeSessionSnapshot.Default;
                var updated = reducer(current) ?? GameModeSessionSnapshot.Default;
                var nextVersion = current.Version + 1;
                if (updated.Version < nextVersion)
                    updated = updated.WithVersion(nextVersion);
                _snapshot.Value = updated;
            }

            public void SetModeConfig(IGameModeConfig config) { }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose()
            {
                _snapshot.Dispose();
                _canStart.Dispose();
                _validationErrors.Dispose();
            }
        }

        private sealed class FakeViewAssetProvider : IViewAssetProvider
        {
            public Cysharp.Threading.Tasks.UniTask<IAssetLease<VisualTreeAsset>> LoadVisualTreeAsync(string key, System.Threading.CancellationToken ct) =>
                Cysharp.Threading.Tasks.UniTask.FromException<IAssetLease<VisualTreeAsset>>(new InvalidOperationException());
        }

    }
}