#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.Services.UI.Assets;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.GameModes.Wizard.UI.MatchSetup
{
    [TestFixture]
    [Category("Integration")]
    public partial class MatchSetupViewEditModeTests
    {
        private const string _matchSetupUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/MatchSetup.uxml";
        private const string _matchSetupPrefabPath = "Assets/Content/UI/GameModes/Wizard/Prefabs/MatchSetup.prefab";

        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private MatchSetupView _view = null!;
        private VisualTreeAsset _uxml = null!;

        private MatchSetupViewModel _viewModel = null!;
        private FakeGameSession _session = null!;
        private IGameWizardCoordinator _coordinator = null!;
        private ILocalizationService _localization = null!;
        private ReactiveProperty<bool> _isTransitioning = null!;
        private ReactiveProperty<bool> _isSubmitting = null!;
        private ReactiveProperty<WizardError?> _currentError = null!;

        [SetUp]
        public void SetUp()
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(_matchSetupUxmlPath);
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

            _session = new FakeGameSession(GameSessionSnapshot.Default);

            _coordinator = Substitute.For<IGameWizardCoordinator>();
            _coordinator.IsTransitioning.Returns(_isTransitioning);
            _coordinator.IsSubmitting.Returns(_isSubmitting);
            _coordinator.CurrentError.Returns(_currentError);
#pragma warning disable CS8601
            _coordinator.TryGetSession(out Arg.Any<IGameSession>()).Returns(callInfo =>
            {
                callInfo[0] = _session;
                return true;
            });
#pragma warning restore CS8601

            var catalog = Substitute.For<IGameCatalog>();
            var difficultyCatalog = new BotDifficultyCatalog();
            _viewModel = new MatchSetupViewModel(catalog, _coordinator, _localization, difficultyCatalog);
            _viewModel.DisablePlayerLoopForTests();

            var assetProvider = new FakeViewAssetProvider();
            var binders = Array.Empty<IGameSettingsBinder>();
            _view.Construct(assetProvider, binders, _localization);

            _view.SetViewModel(_viewModel);
            _view.RebindUxmlForTests();
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel.Dispose();
            _session.Dispose();

            _isTransitioning.Dispose();
            _isSubmitting.Dispose();
            _currentError.Dispose();

            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);
        }

        private sealed class FakeGameSession : IGameSession
        {
            private readonly ReactiveProperty<GameSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

            public FakeGameSession(GameSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public ReadOnlyReactiveProperty<GameSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void EmitSnapshot(GameSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void EmitCanStart(bool value) => _canStart.Value = value;

            public void EmitValidationErrors(IReadOnlyList<ValidationError> errors) => _validationErrors.Value = errors;

            public void Update(Func<GameSessionSnapshot, GameSessionSnapshot> reducer)
            {
                var current = _snapshot.Value;
                var updated = reducer(current) ?? GameSessionSnapshot.Default;
                var nextVersion = current.Version + 1;
                
                if (updated.Version < nextVersion)
                    updated = updated.WithVersion(nextVersion);
                
                _snapshot.Value = updated;
            }

            public void SetModeConfig(IGameConfig config) { }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameSessionSnapshot.Default;

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

        private MatchSetupViewModel CreateViewModel()
        {
            var catalog = Substitute.For<IGameCatalog>();
            var difficultyCatalog = new BotDifficultyCatalog();
            var viewModel = new MatchSetupViewModel(catalog, _coordinator, _localization, difficultyCatalog);
            viewModel.DisablePlayerLoopForTests();
            return viewModel;
        }
    }
}