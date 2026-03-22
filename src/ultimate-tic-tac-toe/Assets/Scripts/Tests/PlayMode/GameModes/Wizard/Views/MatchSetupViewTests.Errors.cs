using System.Collections;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Views
{
    public partial class MatchSetupViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBlockingErrorIsPresentInMatchSetupView_ThenStartIsDisabled()
        {
            _session.SetCanStart(true);
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.Blocking",
                true,
                ErrorDisplayType.Modal);
           
            yield return null;

            GetStartButton().enabledInHierarchy.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenNonBlockingModalErrorIsPresentInMatchSetupView_ThenStartRemainsEnabledIfCanStart()
        {
            _session.SetCanStart(true);
            _view.SetViewModel(_viewModel);
            yield return null;

            _currentError.Value = new WizardError(
                "code",
                "Errors.GameWizard.NonBlocking",
                false,
                ErrorDisplayType.Modal);
            
            yield return null;

            GetStartButton().enabledInHierarchy.Should().BeTrue();
        }
    }
}
