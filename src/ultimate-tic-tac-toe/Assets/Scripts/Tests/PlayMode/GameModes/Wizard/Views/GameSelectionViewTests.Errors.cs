#nullable enable

using System.Collections;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.UI.Components;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode.GameModes.Wizard.Views
{
    public partial class GameSelectionViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenGameSelectionViewBindsError_ThenOverlayReactsToCoordinatorError()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Modal",
                true,
                ErrorDisplayType.Modal);
            
            yield return null;

            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewResetForPoolCalledWithActiveError_ThenBinderDisposedAndOverlayCleared()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Modal",
                true,
                ErrorDisplayType.Modal);
            
            yield return null;

            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();

            _view.ResetForPool();
            yield return null;

            GetErrorOverlay().style.display.value.Should().Be(DisplayStyle.None);
            GetErrorOverlay().Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewReusedAfterPooling_ThenNewBinderWorksCorrectly()
        {
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

            currentErrorB.Value = new WizardError(
                "code",
                "Errors.GameWizard.Toast",
                false,
                ErrorDisplayType.Toast);
            
            yield return null;

            GetErrorOverlay().Q<WizardToast>("WizardToast").IsVisible.Should().BeTrue();

            viewModelB.Dispose();
            currentErrorB.Dispose();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBlockingErrorIsPresentInGameSelectionView_ThenContinueIsDisabledRegardlessOfCanContinue()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Blocking",
                true,
                ErrorDisplayType.Modal);
            
            yield return null;

            GetContinueButton().enabledInHierarchy.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenNonBlockingToastErrorIsPresentInGameSelectionView_ThenContinueRemainsEnabledIfCanContinue()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Toast",
                false,
                ErrorDisplayType.Toast);
           
            yield return null;

            GetContinueButton().enabledInHierarchy.Should().BeTrue();
        }
    }
}