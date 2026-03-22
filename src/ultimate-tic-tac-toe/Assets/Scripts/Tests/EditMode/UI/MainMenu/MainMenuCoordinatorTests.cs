using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Online;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using Runtime.UI.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode.UI.MainMenu
{
    [TestFixture]
    public partial class MainMenuCoordinatorTests
    {
        private MainMenuCoordinator _coordinator;
        private IGameStateMachine _stateMachineMock;
        private IUIService _uiServiceMock;
        private ILocalizationService _localizationMock;
        private IGameWizardCoordinator _wizardCoordinatorMock;
        private Subject<GameLaunchConfig> _gameLaunchRequested;
        private Subject<AbortReason> _wizardAborted;
        private MainMenuViewModel _viewModel;
        private CancellationToken _cancellationToken;

        private readonly List<GameObject> _createdGameObjects = new();

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _uiServiceMock = Substitute.For<IUIService>();
            _localizationMock = Substitute.For<ILocalizationService>();
            _wizardCoordinatorMock = Substitute.For<IGameWizardCoordinator>();
            _gameLaunchRequested = new Subject<GameLaunchConfig>();
            _wizardAborted = new Subject<AbortReason>();
            
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));
            
            _localizationMock.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
           
            _localizationMock.PreloadAsync(
                    Arg.Any<LocaleId>(),
                    Arg.Any<IReadOnlyList<TextTableId>>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            
            _wizardCoordinatorMock.GameLaunchRequested.Returns(_gameLaunchRequested);
            _wizardCoordinatorMock.WizardAborted.Returns(_wizardAborted);
            
            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _coordinator = new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, _localizationMock, _wizardCoordinatorMock);
            _viewModel = new MainMenuViewModel(_localizationMock);
            _viewModel.Initialize();
            _cancellationToken = CancellationToken.None;

            _stateMachineMock.EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator?.Dispose();
            _viewModel?.Dispose();
            _gameLaunchRequested?.Dispose();
            _wizardAborted?.Dispose();

            for (var i = 0; i < _createdGameObjects.Count; i++)
            {
                var go = _createdGameObjects[i];
               
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _createdGameObjects.Clear();
        }

        private SettingsView CreateInactiveSettingsView(SettingsViewModel viewModel)
        {
            var go = new GameObject("SettingsView_Test");
            go.SetActive(false);

            var view = go.AddComponent<SettingsView>();
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }

        private TestMainMenuViewForCoordinator CreateInactiveMainMenuView()
        {
            var go = new GameObject("MainMenuView_Test");
            go.SetActive(false);

            var view = go.AddComponent<TestMainMenuViewForCoordinator>();
            _createdGameObjects.Add(go);
            return view;
        }

        private LanguageSelectionView CreateInactiveLanguageSelectionView(LanguageSelectionViewModel viewModel)
        {
            var go = new GameObject("LanguageSelectionView_Test");
            go.SetActive(false);

            var view = go.AddComponent<LanguageSelectionView>();
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }

        private PlayerNameEditView CreateInactivePlayerNameEditView(PlayerNameEditViewModel viewModel)
        {
            var go = new GameObject("PlayerNameEditView_Test");
            go.SetActive(false);

            var view = go.AddComponent<PlayerNameEditView>();
            view.Construct(_localizationMock);
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }

        private PlayerStatisticsView CreateInactivePlayerStatisticsView(PlayerStatisticsViewModel viewModel)
        {
            var go = new GameObject("PlayerStatisticsView_Test");
            go.SetActive(false);

            var view = go.AddComponent<PlayerStatisticsView>();
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }
    }

    internal sealed class TestMainMenuViewForCoordinator : MainMenuView
    {
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }

        protected override void Awake() { }

        protected override void BindViewModel() { }

        public override void Show() => ShowCalls++;

        public override void Hide() => HideCalls++;
    }

    internal static class MainMenuCoordinatorTestsHelpers
    {
        public static OnlineFlowSnapshot CreateFlowSnapshot(OnlineFlowState state) => new(
            state,
            previousStableState: null,
            candidateSessionId: "ABCDEF",
            activeSessionId: "ABCDEF",
            flowEpoch: 1,
            region: "eu",
            canStart: false,
            isBusy: false,
            errorCode: state == OnlineFlowState.Failed ? OnlineErrorCode.NetworkUnavailable : OnlineErrorCode.None,
            errorLocalizationKey: state == OnlineFlowState.Failed ? OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.NetworkUnavailable) : null,
            statusLocalizationKey: null,
            countdownRemainingSeconds: null,
            graceDeadlineUtc: null);

        public static async UniTask<OnlineLaunchPreparationResult> WaitLaunchCancellationAsync(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(30), cancellationToken: ct);
            return OnlineLaunchPreparationResult.Success();
        }
    }
}