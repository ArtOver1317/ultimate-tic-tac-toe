#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class GameSelectionViewTests
    {
        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private GameSelectionView _view = null!;
        private VisualTreeAsset _uxml = null!;

        private GameSelectionViewModel _viewModel = null!;
        private IGameWizardCoordinator _coordinator = null!;
        private ReactiveProperty<WizardError?> _currentError = null!;
        private List<GameMetadata> _modes = null!;
        private ILocalizationService _localization = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("ModeSelectionTest");
            _uxml.Should().NotBeNull("ModeSelectionTest.uxml must exist in Resources for tests");

            _gameObject = new GameObject("GameSelectionViewTests");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<GameSelectionView>();

            _modes = new List<GameMetadata>
            {
                CreateMode("classic", 0),
                CreateMode("ultimate", 1),
                CreateMode("blitz", 2)
            };

            var catalog = Substitute.For<IGameCatalog>();
            catalog.Metadata.Returns(_modes);

            _coordinator = Substitute.For<IGameWizardCoordinator>();
            var initialSession = Substitute.For<IGameSession>();
            _coordinator.TryGetSession(out initialSession).Returns(false);
            _coordinator.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            _coordinator.IsSubmitting.Returns(new ReactiveProperty<bool>(false));
            _currentError = new ReactiveProperty<WizardError?>(null);
            _coordinator.CurrentError.Returns(_currentError);

            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));

            _view.Construct(_localization);

            _viewModel = new GameSelectionViewModel(catalog, _coordinator, _localization);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel?.Dispose();
            _currentError?.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindViewModelCalledWithMissingUxmlElements_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var badUxml = Resources.Load<VisualTreeAsset>("ModeSelectionMissingModeListTest");
            badUxml.Should().NotBeNull("ModeSelectionMissingModeListTest.uxml must exist in Resources for tests");

            LogAssert.Expect(LogType.Error, new Regex(@"\[UxmlBinder\] Required element 'ModeList' of type ListView not found"));

            var go = new GameObject("ModeSelectionBadUxml");
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = badUxml;
            var view = go.AddComponent<GameSelectionView>();

            var catalog = Substitute.For<IGameCatalog>();
            catalog.Metadata.Returns(_modes);

            var coordinator = Substitute.For<IGameWizardCoordinator>();
            var badUxmlSession = Substitute.For<IGameSession>();
            coordinator.TryGetSession(out badUxmlSession).Returns(false);
            coordinator.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            coordinator.IsSubmitting.Returns(new ReactiveProperty<bool>(false));

            var viewModel = new GameSelectionViewModel(catalog, coordinator, _localization);

            yield return null;

            // Act
            Action act = () => view.SetViewModel(viewModel);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ModeList element is missing*");

            Object.Destroy(go);
            viewModel.Dispose();
            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenListViewBindItemCalledWithValidIndex_ThenCardBindsCorrectMetadata()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();
            var element = new GameCardElement();

            // Act
            modeList.bindItem(element, 0);

            // Assert
            element.Q<Label>("Title").text.Should().Be("mode.classic");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenListViewBindItemCalledWithInvalidIndex_ThenDoesNotCrashAndHandlesGracefully()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();
            var element = new GameCardElement();
            element.Bind("Title", "Desc", "icon", isSelected: true);

            // Act
            Action actNegative = () => modeList.bindItem(element, -1);
            Action actLarge = () => modeList.bindItem(element, 99);

            // Assert
            actNegative.Should().NotThrow();
            actLarge.Should().NotThrow();
            element.Q<Label>("Title").text.Should().Be("Title");
            element.Q<Label>("Description").text.Should().Be("Desc");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenListViewBindItemCalledWithNullElement_ThenHandlesGracefully()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();

            // Act
            Action act = () => modeList.bindItem(null, 0);

            // Assert
            act.Should().NotThrow();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindModeCardReceivesNonGameCardElement_ThenDoesNotThrow()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();
            var element = new VisualElement();

            // Act
            Action act = () => modeList.bindItem(element, 0);

            // Assert
            act.Should().NotThrow();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewModelSelectedModeIdChanges_ThenListViewRefreshesItemsAndHighlightUpdates()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();
            modeList.selectedIndex.Should().Be(-1);

            // Act
            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            // Assert
            modeList.selectedIndex.Should().Be(0);

            // Highlight is determined by bindItem reading ViewModel.SelectedGameId.
            var element = new GameCardElement();
            modeList.bindItem(element, 0);
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenUserSelectsItemInListView_ThenViewModelSelectModeIsCalled()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();

            // Act
            modeList.SetSelection(1);

            // Assert
            _viewModel.SelectedGameId.Value.Should().Be("ultimate");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSelectionIsSyncedFromViewModel_ThenListViewUpdatesWithoutChangingSelectedGameId()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            // Act
            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            // Assert
            _viewModel.SelectedGameId.Value.Should().Be("classic");
            GetModeList().selectedIndex.Should().Be(0);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenModeListSelectionChangedWithEmptyOrNullItems_ThenDoesNotAccidentallyChangeSelection()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;


            // Act
            GetModeList().SetSelection(new List<int>());
            GetModeList().ClearSelection();

            // Assert
            _viewModel.SelectedGameId.Value.Should().Be("classic");
        }

        [UnityTest]
        [Timeout(5000)]
        [Category("UIWiring")]
        [Explicit]
        public IEnumerator WhenContinueOrCancelButtonsClickedThroughUIToolkit_ThenViewModelMethodsCalled()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            var cancelButton = GetCancelButton();
            var continueButton = GetContinueButton();

            // Act
            SimulateClick(cancelButton);
            SimulateClick(continueButton);

            // Assert
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewModelCanContinueChangesFalse_ThenContinueButtonIsDisabled()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            var continueButton = GetContinueButton();

            // Act
            _viewModel.SelectedGameId.Value = null;
            yield return null;

            // Assert
            continueButton.enabledInHierarchy.Should().BeFalse();

            // Act
            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            // Assert
            continueButton.enabledInHierarchy.Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenClearsViewModelAndUnbindsCallbacks()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            // Act
            GetModeList().SetSelection(1);
            _viewModel.SelectedGameId.Value.Should().Be("ultimate");

            _view.ResetForPool();
            Action act = () => GetModeList().SetSelection(2);

            // Assert
            act.Should().NotThrow();
            _view.GetViewModel().Should().BeNull();
            _viewModel.SelectedGameId.Value.Should().Be("ultimate");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRebindViewModelAfterReset_ThenNewViewModelHandlesSelectionAndPreviousViewModelStaysUntouched()
        {
            // Arrange
            var viewModelA = _viewModel;
            _view.SetViewModel(viewModelA);
            yield return null;

            _view.ResetForPool();

            var catalogB = Substitute.For<IGameCatalog>();
            catalogB.Metadata.Returns(_modes);

            var coordinatorB = Substitute.For<IGameWizardCoordinator>();
            var rebindSession = Substitute.For<IGameSession>();
            coordinatorB.TryGetSession(out rebindSession).Returns(false);
            coordinatorB.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            coordinatorB.IsSubmitting.Returns(new ReactiveProperty<bool>(false));
            var currentErrorB = new ReactiveProperty<WizardError?>(null);
            coordinatorB.CurrentError.Returns(currentErrorB);

            var viewModelB = new GameSelectionViewModel(catalogB, coordinatorB, _localization);
            _view.SetViewModel(viewModelB);
            yield return null;

            // Act
            GetModeList().SetSelection(2);
            yield return null;

            // Assert
            viewModelB.SelectedGameId.Value.Should().Be("blitz");
            viewModelA.SelectedGameId.Value.Should().BeNull();

            viewModelB.Dispose();
            currentErrorB.Dispose();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindViewModelCalledAndVMHasSelectedModeId_ThenListViewRestoresSelectionHighlight()
        {
            // Arrange
            _viewModel.SelectedGameId.Value = "ultimate";

            _view.SetViewModel(_viewModel);
            yield return null;

            // Act
            var modeList = GetModeList();

            // Assert
            modeList.selectedIndex.Should().Be(1);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenAvailableModesIsNull_ThenViewTreatsAsEmptyAndDoesNotThrow()
        {
            // Arrange
            _viewModel.SetAvailableModesForTests(null!);

            // Act
            Action act = () => _view.SetViewModel(_viewModel);

            // Assert
            act.Should().NotThrow();
            GetModeList().itemsSource.Should().NotBeNull();

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindViewModelCalledAndVMSelectedModeIdIsNull_ThenListViewHasNoSelection()
        {
            // Arrange
            _viewModel.SelectedGameId.Value = null;

            // Act
            _view.SetViewModel(_viewModel);
            yield return null;

            // Assert
            GetModeList().selectedIndex.Should().Be(-1);
        }

        [UnityTest]
        [Timeout(5000)]
        [Category("Optional")]
        public IEnumerator WhenAvailableModesChangeDuringRuntime_ThenListViewRebindsAndRestoresSelection()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            _viewModel.SetAvailableModesForTests(new List<GameMetadata>
            {
                CreateMode("classic", 0),
                CreateMode("ultimate", 1),
                CreateMode("blitz", 2)
            });

            // Act
            yield return null;

            // Assert
            GetModeList().itemsSource.Should().NotBeNull();
            GetModeList().selectedIndex.Should().Be(0);
        }

        [UnityTest]
        [Timeout(5000)]
        [Category("Optional")]
        public IEnumerator WhenAvailableModesUpdatedAndPreviouslySelectedModeIdIsNoLongerPresent_ThenViewClearsSelectionAndContinueStateIsConsistent()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            _viewModel.SetAvailableModesForTests(new List<GameMetadata>
            {
                CreateMode("ultimate", 0),
                CreateMode("blitz", 1)
            });

            // Act
            yield return null;

            // Assert
            GetModeList().selectedIndex.Should().Be(-1);
            GetContinueButton().enabledInHierarchy.Should().BeFalse();
            _viewModel.SelectedGameId.Value.Should().BeNull();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenGameSelectionViewBindsError_ThenOverlayReactsToCoordinatorError()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            // Act
            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Modal",
                true,
                ErrorDisplayType.Modal);
            yield return null;

            // Assert
            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewResetForPoolCalledWithActiveError_ThenBinderDisposedAndOverlayCleared()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Modal",
                true,
                ErrorDisplayType.Modal);
            yield return null;

            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();

            // Act
            _view.ResetForPool();
            yield return null;

            // Assert
            GetErrorOverlay().style.display.value.Should().Be(DisplayStyle.None);
            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewReusedAfterPooling_ThenNewBinderWorksCorrectly()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Modal",
                true,
                ErrorDisplayType.Modal);
            yield return null;

            _view.ResetForPool();
            yield return null;

            var catalogB = Substitute.For<IGameCatalog>();
            catalogB.Metadata.Returns(_modes);

            var coordinatorB = Substitute.For<IGameWizardCoordinator>();
            var reusedViewSession = Substitute.For<IGameSession>();
            coordinatorB.TryGetSession(out reusedViewSession).Returns(false);
            coordinatorB.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            coordinatorB.IsSubmitting.Returns(new ReactiveProperty<bool>(false));
            var currentErrorB = new ReactiveProperty<WizardError?>(null);
            coordinatorB.CurrentError.Returns(currentErrorB);

            var viewModelB = new GameSelectionViewModel(catalogB, coordinatorB, _localization);
            _view.SetViewModel(viewModelB);
            yield return null;

            // Act
            currentErrorB.Value = new WizardError(
                "code",
                "Errors.GameWizard.Toast",
                false,
                ErrorDisplayType.Toast);
            yield return null;

            // Assert
            GetErrorOverlay().Q<WizardToast>("WizardToast").IsVisible.Should().BeTrue();

            viewModelB.Dispose();
            currentErrorB.Dispose();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBlockingErrorIsPresentInGameSelectionView_ThenContinueIsDisabledRegardlessOfCanContinue()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            // Act
            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Blocking",
                true,
                ErrorDisplayType.Modal);
            yield return null;

            // Assert
            GetContinueButton().enabledInHierarchy.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenNonBlockingToastErrorIsPresentInGameSelectionView_ThenContinueRemainsEnabledIfCanContinue()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            // Act
            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Toast",
                false,
                ErrorDisplayType.Toast);
            yield return null;

            // Assert
            GetContinueButton().enabledInHierarchy.Should().BeTrue();
        }

        private ListView GetModeList() => _uiDocument.rootVisualElement.Q<ListView>("ModeList");

        private Button GetCancelButton() => _uiDocument.rootVisualElement.Q<Button>("CancelButton");

        private Button GetContinueButton() => _uiDocument.rootVisualElement.Q<Button>("ContinueButton");

        private WizardErrorOverlay GetErrorOverlay() => _uiDocument.rootVisualElement.Q<WizardErrorOverlay>("ErrorOverlay");

        private static GameMetadata CreateMode(string id, int sortOrder) => new(
            id,
            $"mode.{id}",
            $"desc.{id}",
            $"icon.{id}",
            sortOrder,
            supportsBot: true,
            supportsOnline: true,
            supportsLocal: true);

        private static void SimulateClick(Button button)
        {
            if (button == null)
                return;

            var clickable = button.clickable;
            if (clickable != null)
            {
                var method = clickable.GetType().GetMethod(
                    "SimulateSingleClick",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (TryInvokeClickable(method, clickable))
                    return;

                method = clickable.GetType().GetMethod(
                    "Invoke",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (TryInvokeClickable(method, clickable))
                    return;
            }

            var down = PointerDownEvent.GetPooled();
            button.SendEvent(down);
            down.Dispose();

            var up = PointerUpEvent.GetPooled();
            button.SendEvent(up);
            up.Dispose();

            var click = ClickEvent.GetPooled();
            button.SendEvent(click);
            click.Dispose();
        }

        private static bool TryInvokeClickable(MethodInfo method, Clickable clickable)
        {
            if (method == null)
                return false;

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                method.Invoke(clickable, null);
                return true;
            }

            if (parameters.Length == 1)
            {
                var evt = ClickEvent.GetPooled();
                method.Invoke(clickable, new object[] { evt });
                evt.Dispose();
                return true;
            }

            return false;
        }

    }
}

#nullable restore