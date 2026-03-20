using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VContainer;
using CellId = Runtime.Gameplay.CellId;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe
{
    /// <summary>
    /// Integration tests for <see cref="GameplayMovesBinder"/> using the full ECS pipeline:
    /// CommandQueue → ProcessCommandsSystem → game systems → EventPublishSystem → binder reacts.
    /// <see cref="SynchronousEventScheduler"/> ensures deterministic event delivery within Tick().
    /// </summary>
    [TestFixture]
    public sealed class GameplayMovesBinderTests
    {
        private GameplayFieldPresenter _presenter;
        private UIDocument _document;
        private GameObject _gameObject;

        private CommandQueue _commandQueue;
        private MatchEcsLifecycleService _lifecycle;
        private MatchStateProvider _stateProvider;
        private GameplayMovesBinder _binder;

        [SetUp]
        public void SetUp()
        {
            (_presenter, _document, _gameObject) = CreatePresenter();

            var scheduler = new SynchronousEventScheduler();
            _commandQueue = new CommandQueue();
            var eventSystem = new EventPublishSystem(scheduler);
            var rulesEngine = new ClassicRulesEngine();
            var registrar = new TicTacToeEcsRegistrar(rulesEngine);
            _lifecycle = new MatchEcsLifecycleService(
                new[] { registrar }, _commandQueue, eventSystem);
            _stateProvider = new MatchStateProvider(
                _commandQueue, _lifecycle, eventSystem);
        }

        [TearDown]
        public void TearDown()
        {
            _binder?.Dispose();
            _stateProvider?.Dispose();
            _lifecycle?.Dispose();
            _presenter?.Dispose();

            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);

            _binder = null;
            _stateProvider = null;
            _lifecycle = null;
            _presenter = null;
            _document = null;
            _gameObject = null;
        }

        [Test]
        [Category("Unit")]
        public void WhenResolvedFromVContainerWithoutMovesVfxRegistration_ThenBinderResolvesSuccessfully()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(Substitute.For<IGameplayFieldUiAdapter>()).As<IGameplayFieldUiAdapter>();
            builder.RegisterInstance(Substitute.For<IGameplayCommandSink>()).As<IGameplayCommandSink>();
            builder.RegisterInstance(Substitute.For<IGameplayEventStream>()).As<IGameplayEventStream>();
            builder.RegisterInstance(Substitute.For<IGameplaySnapshotProvider>()).As<IGameplaySnapshotProvider>();
            builder.RegisterInstance(Substitute.For<IGameplayMovesModeBehavior>()).As<IGameplayMovesModeBehavior>();
            builder.RegisterInstance(Substitute.For<ILocalizationService>()).As<ILocalizationService>();
            builder.Register<GameplayMovesBinder>(Lifetime.Scoped);

            using var container = builder.Build();

            Action act = () => container.Resolve<GameplayMovesBinder>();

            act.Should().NotThrow();
        }

        [Test]
        [Category("Integration")]
        public void WhenCellClicked_ThenUpdatesMarkAndSwitchesCurrentPlayer()
        {
            // Arrange
            StartMatchAndBind();

            // Act
            ClickAndTick(new CellId(0, 0));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var markRoot).Should().BeTrue();
            markRoot.childCount.Should().BeGreaterThan(0);
            (markRoot[0] as Label).Should().NotBeNull();
            ((Label)markRoot[0]).text.Should().Be("X");

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Player 2 (O)");
        }

        [Test]
        [Category("Integration")]
        public void WhenLocalizationProvided_ThenCurrentPlayerLabelUsesLocalizedTurnText()
        {
            // Arrange
            BindPresenter(FieldRenderSpec.Classic(3));
            StartMatch();

            var localization = Substitute.For<ILocalizationService>();
            localization.TryResolve(
                    Arg.Is<TextTableId>(table => table.Name == "Game"),
                    Arg.Is<TextKey>(key => key.Value == "Game.PlayerTurn.Player1"),
                    out Arg.Any<string>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo =>
                {
                    callInfo[2] = "Игрок 1 (X)";
                    return true;
                });
            localization.TryResolve(
                    Arg.Is<TextTableId>(table => table.Name == "Game"),
                    Arg.Is<TextKey>(key => key.Value == "Game.PlayerTurn.Player2"),
                    out Arg.Any<string>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo =>
                {
                    callInfo[2] = "Игрок 2 (O)";
                    return true;
                });

            _binder = new GameplayMovesBinder(
                (IGameplayFieldUiAdapter)_presenter,
                _stateProvider,
                _stateProvider,
                _stateProvider,
                localization: localization);

            // Act
            _binder.Bind();
            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Игрок 1 (X)");
            ClickAndTick(new CellId(0, 0));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Игрок 2 (O)");
        }

        [Test]
        [Category("Integration")]
        public void WhenTwoMovesApplied_ThenLastMoveClassMovesBetweenCells()
        {
            // Arrange
            StartMatchAndBind();

            // Act
            ClickAndTick(new CellId(0, 0));
            ClickAndTick(new CellId(1, 1));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(1, 1), out var cell11).Should().BeTrue();

            cell00.ClassListContains("cell--lastMove").Should().BeFalse();
            cell11.ClassListContains("cell--lastMove").Should().BeTrue();
        }

        [Test]
        [Category("Integration")]
        public void WhenMoveApplied_ThenDisablesOccupiedCellAndIgnoresPicking()
        {
            // Arrange
            StartMatchAndBind();

            // Act
            ClickAndTick(new CellId(0, 0));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            cell00.enabledSelf.Should().BeFalse();
            cell00.pickingMode.Should().Be(PickingMode.Ignore);
            cell00.ClassListContains("cell--disabled").Should().BeTrue();
        }

        [Test]
        [Category("Integration")]
        public void WhenBindCalledTwice_ThenDoesNotThrow()
        {
            // Arrange
            BindPresenter(FieldRenderSpec.Classic(3));
            StartMatch();

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _stateProvider, _stateProvider, _stateProvider);

            // Act
            Action act = () =>
            {
                _binder.Bind();
                _binder.Bind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        [Category("Integration")]
        public void WhenUnbindCalledTwice_ThenDoesNotThrow()
        {
            // Arrange
            StartMatchAndBind();

            // Act
            Action act = () =>
            {
                _binder.Unbind();
                _binder.Unbind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        [Category("Integration")]
        public void WhenBindAfterMovesAlreadyApplied_ThenRendersMarksAndInteractivityButNotLastMove()
        {
            // Arrange — apply moves via ECS before binding the binder
            BindPresenter(FieldRenderSpec.Classic(3));
            StartMatch();

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(1, 1)));

            // Act — binder reads cold-path snapshot (two occupied cells)
            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _stateProvider, _stateProvider, _stateProvider);
            _binder.Bind();

            // Assert — marks are rendered via cold-path
            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out var cell00, out var mark00).Should().BeTrue();
            mark00.text.Should().Be("X");
            cell00.enabledSelf.Should().BeFalse();
            cell00.pickingMode.Should().Be(PickingMode.Ignore);
            cell00.ClassListContains("cell--disabled").Should().BeTrue();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(1, 1), out var cell11, out var mark11).Should().BeTrue();
            mark11.text.Should().Be("O");
            cell11.enabledSelf.Should().BeFalse();
            cell11.pickingMode.Should().Be(PickingMode.Ignore);
            cell11.ClassListContains("cell--disabled").Should().BeTrue();

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Player 1 (X)");

            // Another hot-path move after bind
            ClickAndTick(new CellId(2, 2));
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(2, 2), out var cell22).Should().BeTrue();
            cell22.ClassListContains("cell--lastMove").Should().BeTrue("hot-path ход устанавливает last-move");
        }

        [Test]
        [Category("Integration")]
        public void WhenUnbindAndReBindAfterRestart_ThenUiClearsMarksReEnablesCellsAndClearsLastMove()
        {
            // Arrange
            StartMatchAndBind();

            ClickAndTick(new CellId(0, 0)); // X
            ClickAndTick(new CellId(1, 1)); // O -> last move expected here

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var markBefore).Should().BeTrue();
            markBefore.text.Should().Be("X");

            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(1, 1), out var lastMoveCellBefore).Should().BeTrue();
            lastMoveCellBefore.ClassListContains("cell--lastMove").Should().BeTrue("после 2-го хода last-move должен быть установлен");

            // Act — simulate restart cycle: Unbind → RestartRoundCommand → Tick → re-Bind
            _binder.Unbind();

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));
            _binder.Bind();

            // Assert — all cells re-enabled, marks cleared
            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out var cell00, out var mark00).Should().BeTrue();
            mark00.text.Should().BeEmpty();
            cell00.enabledSelf.Should().BeTrue();
            cell00.pickingMode.Should().Be(PickingMode.Position);
            cell00.ClassListContains("cell--disabled").Should().BeFalse();
            cell00.ClassListContains("cell--lastMove").Should().BeFalse();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(1, 1), out var cell11, out var mark11).Should().BeTrue();
            mark11.text.Should().BeEmpty();
            cell11.enabledSelf.Should().BeTrue();
            cell11.pickingMode.Should().Be(PickingMode.Position);
            cell11.ClassListContains("cell--lastMove").Should().BeFalse();

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Player 2 (O)");
        }

        [Test]
        [Category("Integration")]
        public void WhenBinderDisposedWithoutUnbind_ThenEcsEventsDoNotUpdateUi()
        {
            // Arrange
            StartMatchAndBind();

            var currentPlayerLabelBefore = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text;
            currentPlayerLabelBefore.Should().Be("Player 1 (X)", "initial state");

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var mark00Before).Should().BeTrue();
            mark00Before.text.Should().BeEmpty("клетка пустая до хода");

            // Act — dispose binder, then apply a move through ECS
            _binder.Dispose();

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            // Assert — UI NOT updated because binder is disposed
            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var mark00After).Should().BeTrue();
            mark00After.text.Should().BeEmpty("UI не обновился после Dispose");

            var currentPlayerLabelAfter = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text;
            currentPlayerLabelAfter.Should().Be("Player 1 (X)", "UI CurrentPlayer не обновился после Dispose");
        }

        [Test]
        [Category("Integration")]
        public void WhenBindUnbindRepeatedTenTimes_ThenSingleClickSubmitsExactlyOneCommand()
        {
            // Arrange
            StartMatchAndBind();

            // Rapidly bind/unbind to ensure no leaked subscriptions
            for (var i = 0; i < 10; i++)
            {
                _binder.Unbind();
                _binder.Bind();
            }

            // Act — single click
            ClickAndTick(new CellId(0, 0));

            // Assert — cell occupied by X, current player switched
            ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var markRoot).Should().BeTrue();
            markRoot.childCount.Should().BeGreaterThan(0);
            ((Label)markRoot[0]).text.Should().Be("X");
            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("Player 2 (O)");
        }

        // --- Helpers ---

        private void StartMatch()
        {
            var config = new GameLaunchConfig(
                TicTacToeEcsRegistrar.TicTacToeGameId,
                new TicTacToeConfig(3),
                new LocalHumanConfig());
            _lifecycle.StartMatch(config);
        }

        private void StartMatchAndBind()
        {
            BindPresenter(FieldRenderSpec.Classic(3));
            StartMatch();

            _binder = new GameplayMovesBinder(
                (IGameplayFieldUiAdapter)_presenter,
                _stateProvider, _stateProvider, _stateProvider);
            _binder.Bind();
        }

        private void ClickAndTick(CellId cellId) =>
            // EmitCellClick → binder → SubmitCommand (auto-ticks)
            _presenter.EmitCellClick(cellId);

        private void BindPresenter(FieldRenderSpec spec)
        {
            // In these tests we don't build the full gameplay HUD (BackButton, etc.).
            // GameplayFieldPresenter logs an Error if BackButton is missing.
            // Ignore failing logs just for the bind to keep these binder tests focused.
            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _presenter.BindAsync(spec, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        private static (GameplayFieldPresenter presenter, UIDocument document, GameObject go) CreatePresenter()
        {
            var go = new GameObject("GameplayFieldPresenterTests");
            var document = go.AddComponent<UIDocument>();
            document.visualTreeAsset = null;

            // Seed a minimal UI tree to keep GameplayFieldPresenter quiet (no error logs)
            // and let it find required elements by name.
            var root = document.rootVisualElement;
            var fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
            var toolbar = new VisualElement { name = "GameplayToolbar" };
            var backButton = new Button { name = "BackButton" };
            fieldRoot.Add(toolbar);
            fieldRoot.Add(backButton);
            root.Add(fieldRoot);

            var backHandler = Substitute.For<IGameplayBackHandler>();
            var presenter = new GameplayFieldPresenter(document, backHandler);
            return (presenter, document, go);
        }
    }
}
