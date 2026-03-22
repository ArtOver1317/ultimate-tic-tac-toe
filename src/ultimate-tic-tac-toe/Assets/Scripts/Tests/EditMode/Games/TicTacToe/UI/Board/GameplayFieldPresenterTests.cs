using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Ultimate.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe.UI.Board
{
    [TestFixture]
    [Category("Integration")]
    public class GameplayFieldPresenterTests
    {
        private GameplayFieldPresenter _presenter;
        private UIDocument _document;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp() =>
            (_presenter, _document, _gameObject) = CreatePresenter(withFieldRoot: true, withBackButton: true);

        [TearDown]
        public void TearDown()
        {
            _presenter?.Dispose();
           
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);

            _presenter = null;
            _document = null;
            _gameObject = null;
        }

        [Test]
        public void WhenGameplayFieldPresenterBindWithNoGameplayFieldRoot_ThenCreatesRootAndContainer()
        {
            // Arrange
            RecreatePresenter(withFieldRoot: false, withBackButton: false);

            // Act
            RunAllowingFailingLogs(
                () => _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult(),
                new Regex(
                    @"(\[Error\]\s*)?\[UI\] \[GameplayFieldPresenter\] BackButton not found\.\s*$",
                    RegexOptions.CultureInvariant));

            // Assert
            var fieldRoot = _document.rootVisualElement.Q<VisualElement>("GameplayFieldRoot");
            fieldRoot.Should().NotBeNull();

            var container = fieldRoot.Q<VisualElement>("FieldContainer");
            container.Should().NotBeNull();
        }

        [Test]
        public void WhenBindClassic_ThenCellNamesAreUniqueInRoot()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var container = _document.rootVisualElement.Q<VisualElement>("FieldContainer");

            // Act
            var cells = CollectByPrefix(container, "Cell_");
            var uniqueNames = cells.Select(c => c.name).Distinct().ToList();

            // Assert
            cells.Count.Should().Be(9);
            uniqueNames.Count.Should().Be(cells.Count);
        }

        [Test]
        public void WhenBindUltimate_ThenMiniBoardNamesAreUniqueInRoot()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var container = _document.rootVisualElement.Q<VisualElement>("FieldContainer");

            // Act
            var minis = CollectByPrefix(container, "Mini_");
            var uniqueNames = minis.Select(m => m.name).Distinct().ToList();

            // Assert
            minis.Count.Should().Be(9);
            uniqueNames.Count.Should().Be(minis.Count);
        }

        [Test]
        public void WhenBindCalledTwiceWithDifferentSpecs_ThenRebuildsWithoutDuplicatingElements()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var container = _document.rootVisualElement.Q<VisualElement>("FieldContainer");
            var minis = CollectByPrefix(container, "Mini_");
            var cells = CollectByPrefix(container, "Cell_");

            // Assert
            minis.Count.Should().Be(9);
            cells.Count.Should().Be(81);

            // Act
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            container = _document.rootVisualElement.Q<VisualElement>("FieldContainer");
            minis = CollectByPrefix(container, "Mini_");
            cells = CollectByPrefix(container, "Cell_");

            // Assert
            minis.Count.Should().Be(0);
            cells.Count.Should().Be(9);
        }

        [Test]
        public void WhenUnbindCalledMultipleTimesBeforeBind_ThenIsIdempotent()
        {
            // Arrange & Act
            Action act = () =>
            {
                _presenter.Unbind();
                _presenter.Unbind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenUnbindCalledMultipleTimesAfterBind_ThenIsIdempotent()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            Action act = () =>
            {
                _presenter.Unbind();
                _presenter.Unbind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenUnbindCalledAfterDispose_ThenIsIdempotent()
        {
            // Arrange
            _presenter.Dispose();

            // Act
            Action act = () =>
            {
                _presenter.Unbind();
                _presenter.Unbind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenDisposedThenBindCalled_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _presenter.Dispose();

            // Act
            Action act = () => _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        // ── TryGetMiniBoardCenter ──

        [Test]
        public void WhenTryGetMiniBoardCenterBeforeBind_ThenReturnsFalse()
        {
            var adapter = (IUltimateGameplayFieldUiAdapter)_presenter;

            var result = adapter.TryGetMiniBoardCenter(0, out var center);

            result.Should().BeFalse();
            center.Should().Be(default(Vector2));
        }

        [Test]
        public void WhenTryGetMiniBoardCenterAfterUltimateBind_AndNoLayoutPass_ThenReturnsFalse()
        {
            // In EditMode with no real panel, worldBound.width == 0 and cache is never populated.
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var adapter = (IUltimateGameplayFieldUiAdapter)_presenter;

            var result = adapter.TryGetMiniBoardCenter(0, out _);

            result.Should().BeFalse();
        }

        [Test]
        public void WhenTryGetMiniBoardCenterAfterClassicBind_ThenReturnsFalse()
        {
            // Classic mode has no mini-boards, so the adapter should always return false.
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var adapter = (IUltimateGameplayFieldUiAdapter)_presenter;

            var result = adapter.TryGetMiniBoardCenter(0, out _);

            result.Should().BeFalse();
        }

        private static (GameplayFieldPresenter presenter, UIDocument document, GameObject gameObject) CreatePresenter(
            bool withFieldRoot,
            bool withBackButton)
        {
            var gameObject = new GameObject("GameplayFieldPresenterTests");
            var document = gameObject.AddComponent<UIDocument>();

            if (withFieldRoot)
            {
                var fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
                
                if (withBackButton)
                    fieldRoot.Add(new Button { name = "BackButton" });
              
                document.rootVisualElement.Add(fieldRoot);
            }

            var backHandler = Substitute.For<IGameplayBackHandler>();
            var presenter = new GameplayFieldPresenter(document, backHandler);
            return (presenter, document, gameObject);
        }

        private void RecreatePresenter(bool withFieldRoot, bool withBackButton)
        {
            _presenter?.Dispose();
         
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);

            (_presenter, _document, _gameObject) = CreatePresenter(withFieldRoot, withBackButton);
        }

        private static void RunAllowingFailingLogs(Action action, params Regex[] expectedFailingLogs)
        {
            var captured = new List<(LogType type, string condition)>();

            void Handler(string condition, string stackTrace, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert)
                    captured.Add((type, condition));
            }

            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Handler;

            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }

            captured.Select(x => x.condition).Count().Should().Be(expectedFailingLogs.Length,
                "любой лишний Error/Exception/Assert лог должен валить тест");

            var messages = captured.Select(x => x.condition).ToList();
           
            for (var i = 0; i < expectedFailingLogs.Length; i++)
            {
                var regex = expectedFailingLogs[i];
               
                regex.IsMatch(messages[i]).Should().BeTrue(
                    $"expected failing log #{i + 1} to match regex '{regex}', but was: {messages[i]}");
            }
        }

        private static List<VisualElement> CollectByPrefix(VisualElement root, string prefix)
        {
            var results = new List<VisualElement>();
            var query = root.Query<VisualElement>().Build();
            
            foreach (var element in query)
            {
                if (element.name != null && element.name.StartsWith(prefix, StringComparison.Ordinal))
                    results.Add(element);
            }

            return results;
        }
    }
}