using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingViewModelValidationTests
    {
        private ILocalizationService _localization;
        private IMatchmakingService _service;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _localization.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<Observable<IReadOnlyDictionary<string, object>>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            _service = Substitute.For<IMatchmakingService>();
        }

        [TearDown]
        public void TearDown()
        {
            _localization = null;
            _service = null;
        }

        [Test]
        public void WhenConstructedWithNullLocalizationService_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => new MatchmakingViewModel(null, _service);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructedWithNullMatchmakingService_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => new MatchmakingViewModel(_localization, null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenBeginSearchCalledWithNullRequest_ThenThrowsArgumentNullException()
        {
            // Arrange
            var viewModel = new MatchmakingViewModel(_localization, _service);

            // Act
            Action act = () => viewModel.BeginSearch(null, default);

            // Assert
            act.Should().Throw<ArgumentNullException>();

            viewModel.Dispose();
        }
    }
}