using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelIntentTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenRequestBackCalled_ThenPublishesBackIntent()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.RequestBack();

            Coordinator.Received(1).TryPublishIntent(WizardIntent.Back);
        }

        [Test]
        public void WhenRequestCancelCalled_ThenPublishesCancelIntent()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.RequestCancel();

            Coordinator.Received(1).TryPublishIntent(WizardIntent.Cancel);
        }

        [Test]
        public void WhenRequestStartCalledAndCanStartIsFalse_ThenDoesNotPublishStartIntent()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(false);

            sut.RequestStart();

            Coordinator.DidNotReceive().TryPublishIntent(WizardIntent.Start);
        }

        [Test]
        public void WhenRequestStartCalledAndCanStartIsTrue_ThenPublishesStartIntent()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(true);

            sut.RequestStart();

            Coordinator.Received(1).TryPublishIntent(WizardIntent.Start);
        }

        [Test]
        public void WhenCoordinatorRejectsIntent_ThenDoesNotThrow()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            Coordinator.TryPublishIntent(Arg.Any<WizardIntent>()).Returns(false);

            using var sut = CreateSut();
            sut.Initialize();
            session.EmitCanStart(true);

            Action act = () =>
            {
                sut.RequestBack();
                sut.RequestStart();
                sut.RequestCancel();
            };

            act.Should().NotThrow();
        }
    }
}