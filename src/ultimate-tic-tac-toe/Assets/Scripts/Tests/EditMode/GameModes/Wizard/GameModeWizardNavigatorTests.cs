using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.Localization;
using Runtime.Services.UI;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    public class GameModeWizardNavigatorTests
    {
        private IUIService _uiService;
        private ILocalizationService _localization;
        private readonly List<GameObject> _createdGameObjects = new();

        [SetUp]
        public void SetUp()
        {
            _uiService = Substitute.For<IUIService>();
            _localization = Substitute.For<ILocalizationService>();

            _localization.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            _localization.IsBusy.Returns(new ReactiveProperty<bool>(false));
            _localization.Errors.Returns(Observable.Empty<LocalizationError>());
            _localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));
            _localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<Observable<IReadOnlyDictionary<string, object>>>())
                .Returns(Observable.Return("Test"));
            _localization.PreloadAsync(Arg.Any<LocaleId>(), Arg.Any<IReadOnlyList<TextTableId>>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _createdGameObjects)
            {
                if (gameObject != null)
                    Object.DestroyImmediate(gameObject);
            }

            _createdGameObjects.Clear();
        }

        [Test]
        public async Task WhenOpenMatchmakingAsync_ThenReturnsViewModelFromOpenedView()
        {
            var sut = new GameWizardNavigator(_uiService, _localization);
            var expectedViewModel = CreateMatchmakingViewModel();
            var view = CreateMatchmakingView(expectedViewModel);

            _uiService.Open<MatchmakingView, MatchmakingViewModel>().Returns(view);

            var result = await sut.OpenMatchmakingAsync(CancellationToken.None);

            result.Should().BeSameAs(expectedViewModel);
            await _localization.Received(1).PreloadAsync(
                LocaleId.EnglishUs,
                Arg.Is<IReadOnlyList<TextTableId>>(tables => tables.Count == 2
                    && tables[0] == new TextTableId("GameWizard")
                    && tables[1] == new TextTableId("Game")),
                Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenReplaceMatchSetupWithMatchmakingAsync_ThenReturnsViewModelFromOpenedView()
        {
            var sut = new GameWizardNavigator(_uiService, _localization);
            var expectedViewModel = CreateMatchmakingViewModel();
            var view = CreateMatchmakingView(expectedViewModel);

            _uiService.ReplaceAsync<MatchSetupView, MatchmakingView, MatchmakingViewModel>(
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<MatchmakingViewModel>>(),
                    Arg.Any<ReplaceOptions?>())
                .Returns(UniTask.FromResult(view));

            var result = await sut.ReplaceMatchSetupWithMatchmakingAsync(CancellationToken.None);

            result.Should().BeSameAs(expectedViewModel);
            await _localization.Received(1).PreloadAsync(
                LocaleId.EnglishUs,
                Arg.Is<IReadOnlyList<TextTableId>>(tables => tables.Count == 2
                    && tables[0] == new TextTableId("GameWizard")
                    && tables[1] == new TextTableId("Game")),
                Arg.Any<CancellationToken>());
        }

        private MatchmakingViewModel CreateMatchmakingViewModel()
        {
            var service = Substitute.For<IMatchmakingService>();
            return new MatchmakingViewModel(_localization, service);
        }

        private MatchmakingView CreateMatchmakingView(MatchmakingViewModel viewModel)
        {
            var gameObject = new GameObject(nameof(MatchmakingView));
            gameObject.AddComponent<UIDocument>();
            var view = gameObject.AddComponent<MatchmakingView>();
            view.SetViewModel(viewModel);
            _createdGameObjects.Add(gameObject);
            return view;
        }
    }
}