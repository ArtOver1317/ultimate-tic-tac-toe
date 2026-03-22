using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.UI.MainMenu;

namespace Tests.EditMode.UI.MainMenu
{
    public partial class MainMenuCoordinatorTests
    {
        [Test]
        public async Task WhenInitializeCalledTwice_ThenOldSubscriptionsDisposed()
        {
            var viewModel1 = new MainMenuViewModel(_localizationMock);
            viewModel1.Initialize();
            var viewModel2 = new MainMenuViewModel(_localizationMock);
            viewModel2.Initialize();

            _coordinator.Initialize(viewModel1);

            _coordinator.Initialize(viewModel2);
            viewModel1.RequestStartGame();

            await _wizardCoordinatorMock.DidNotReceive().StartWizardAsync(Arg.Any<CancellationToken>());

            viewModel1.Dispose();
            viewModel2.Dispose();
        }

        [Test]
        public async Task WhenDispose_ThenUnsubscribesFromEvents()
        {
            _coordinator.Initialize(_viewModel);
            _coordinator.Dispose();

            _viewModel.RequestStartGame();

            await _wizardCoordinatorMock.DidNotReceive().StartWizardAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenDisposeCalledTwice_ThenNoException()
        {
            _coordinator.Initialize(_viewModel);

            Action act = () =>
            {
                _coordinator.Dispose();
                _coordinator.Dispose();
            };

            act.Should().NotThrow("множественные вызовы Dispose должны быть безопасны");
        }
    }
}