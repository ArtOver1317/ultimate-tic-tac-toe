using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayFieldPresenterStableIdsTests
    {
        [Test]
        public void WhenBindClassic_ThenCellsAndMarksHaveStableIds()
        {
            // Arrange
            var (presenter, document, gameObject) = CreatePresenter();

            try
            {
                // Act
                presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var container = document.rootVisualElement.Q<VisualElement>("FieldContainer");

                // Assert
                container.Should().NotBeNull();

                var cells = CollectByPrefix(container, "Cell_");
                cells.Count.Should().Be(9);

                foreach (var cell in cells)
                {
                    TrySplitId(cell.name, "Cell_", out var x, out var y).Should().BeTrue();
                    x.Should().BeInRange(0, 2);
                    y.Should().BeInRange(0, 2);

                    cell.userData.Should().BeOfType<CellUserData>();
                    var userData = (CellUserData)cell.userData;
                    userData.CellId.Should().Be(new CellId(x, y));

                    cell.Q<VisualElement>("Mark").Should().NotBeNull();
                }
            }
            finally
            {
                presenter.Dispose();
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void WhenBindUltimate_ThenMiniBoardsCellsAndMarksHaveStableIds()
        {
            // Arrange
            var (presenter, document, gameObject) = CreatePresenter();

            try
            {
                // Act
                presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var container = document.rootVisualElement.Q<VisualElement>("FieldContainer");

                // Assert
                container.Should().NotBeNull();

                var minis = CollectByPrefix(container, "Mini_");
                minis.Count.Should().Be(9);

                foreach (var mini in minis)
                {
                    TrySplitId(mini.name, "Mini_", out var miniX, out var miniY).Should().BeTrue();
                    miniX.Should().BeInRange(0, 2);
                    miniY.Should().BeInRange(0, 2);
                    var expectedMajor = (miniY * 3) + miniX;

                    var cells = CollectByPrefix(mini, "Cell_");
                    cells.Count.Should().Be(9);

                    foreach (var cell in cells)
                    {
                        TrySplitId(cell.name, "Cell_", out var x, out var y).Should().BeTrue();
                        x.Should().BeInRange(0, 2);
                        y.Should().BeInRange(0, 2);
                        var expectedMinor = (y * 3) + x;

                        cell.userData.Should().BeOfType<CellUserData>();
                        var userData = (CellUserData)cell.userData;
                        userData.CellId.Should().Be(new CellId(expectedMajor, expectedMinor));

                        cell.Q<VisualElement>("Mark").Should().NotBeNull();
                    }
                }
            }
            finally
            {
                presenter.Dispose();
                Object.DestroyImmediate(gameObject);
            }
        }

        private static (GameplayFieldPresenter presenter, UIDocument document, GameObject gameObject) CreatePresenter()
        {
            var gameObject = new GameObject("GameplayFieldPresenterTests");
            var document = gameObject.AddComponent<UIDocument>();
            var fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
            var backButton = new Button { name = "BackButton" };
            fieldRoot.Add(backButton);
            document.rootVisualElement.Add(fieldRoot);
            var backHandler = Substitute.For<IGameplayBackHandler>();
            var presenter = new GameplayFieldPresenter(document, backHandler);
            return (presenter, document, gameObject);
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

        private static bool TrySplitId(string name, string prefix, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var parts = name.Substring(prefix.Length).Split('_');
            if (parts.Length != 2)
                return false;

            return int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
        }
    }
}
