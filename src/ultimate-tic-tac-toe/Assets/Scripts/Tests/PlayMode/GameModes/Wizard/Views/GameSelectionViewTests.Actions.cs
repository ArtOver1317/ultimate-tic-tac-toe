#nullable enable

using System;
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
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Views
{
    public partial class GameSelectionViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        [Category("UIWiring")]
        [Explicit]
        public IEnumerator WhenContinueOrCancelButtonsClickedThroughUIToolkit_ThenViewModelMethodsCalled()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            var cancelButton = GetCancelButton();
            var continueButton = GetContinueButton();

            SimulateClick(cancelButton);
            SimulateClick(continueButton);

            _coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
            _coordinator.Received(1).TryPublishIntent(WizardIntent.Continue);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewModelCanContinueChangesFalse_ThenContinueButtonIsDisabled()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            var continueButton = GetContinueButton();

            _viewModel.SelectedGameId.Value = null;
            yield return null;

            continueButton.enabledInHierarchy.Should().BeFalse();

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            continueButton.enabledInHierarchy.Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenClearsViewModelAndUnbindsCallbacks()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            GetModeList().SetSelection(1);
            _viewModel.SelectedGameId.Value.Should().Be("ultimate");

            _view.ResetForPool();
            Action act = () => GetModeList().SetSelection(2);

            act.Should().NotThrow();
            _view.GetViewModel().Should().BeNull();
            _viewModel.SelectedGameId.Value.Should().Be("ultimate");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRebindViewModelAfterReset_ThenNewViewModelHandlesSelectionAndPreviousViewModelStaysUntouched()
        {
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

            GetModeList().SetSelection(2);
            yield return null;

            viewModelB.SelectedGameId.Value.Should().Be("blitz");
            viewModelA.SelectedGameId.Value.Should().BeNull();

            viewModelB.Dispose();
            currentErrorB.Dispose();
        }
    }
}