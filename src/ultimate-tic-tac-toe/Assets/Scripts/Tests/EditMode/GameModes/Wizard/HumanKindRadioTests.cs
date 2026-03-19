using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Session;
using Runtime.UI.GameModes.Wizard;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class HumanKindRadioTests
    {
        private HumanKindRadio _radio;

        [SetUp]
        public void SetUp() => _radio = new HumanKindRadio();

        [TearDown]
        public void TearDown() => _radio = null;

        [TestCase(true)]
        [TestCase(false)]
        public void WhenSetItemsCalledWithNullOrEmpty_ThenClearsButtonsAndSelectedKindIsNull(bool useNull)
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local")));
            _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Act
#pragma warning disable CS8625
            var items = useNull ? null : Array.Empty<HumanKindRadioItem>();
#pragma warning restore CS8625
            _radio.SetItems(items);

            // Assert
            _radio.childCount.Should().Be(0);
            _radio.SelectedKind.Should().BeNull();
        }

        [Test]
        public void WhenSetItemsCalledWithValidItems_ThenCreatesButtonsWithCorrectLabels()
        {
            // Arrange
            var items = CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite"));

            // Act
            _radio.SetItems(items);

            // Assert
            _radio.childCount.Should().Be(2);
            _radio.Q<Button>(HumanOpponentKind.Local.ToString()).text.Should().Be("Local");
            _radio.Q<Button>(HumanOpponentKind.DirectInvite.ToString()).text.Should().Be("Invite");
        }

        [Test]
        public void WhenSetItemsCalledAndPreviouslySelectedKindIsNotInNewItems_ThenSelectedKindIsCleared()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite")));
            _radio.SetSelectedKind(HumanOpponentKind.DirectInvite);

            // Act
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local")));

            // Assert
            _radio.SelectedKind.Should().BeNull();
        }

        [Test]
        public void WhenSetItemsCalledAndPreviouslySelectedKindStillExists_ThenSelectionIsPreserved()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite")));
            _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Act
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.Matchmaking, "Match")));

            // Assert
            _radio.SelectedKind.Should().Be(HumanOpponentKind.Local);
            _radio.Q<Button>(HumanOpponentKind.Local.ToString())
                .ClassListContains("human-kind-radio__item--selected")
                .Should()
                .BeTrue();
        }

        [Test]
        public void WhenSetItemsCalledWithDuplicateKinds_ThenDuplicatesAreIgnored()
        {
            // Arrange
            var items = CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.Local, "Local 2"));

            // Act
            _radio.SetItems(items);

            // Assert
            _radio.childCount.Should().Be(1);
        }

        [Test]
        public void WhenSetItemsCalledWithOnlyNullItems_ThenButtonsAreEmptyAndSelectedKindIsNull()
        {
            // Arrange
#pragma warning disable CS8625
            var items = new HumanKindRadioItem[] { null, null };
#pragma warning restore CS8625

            // Act
            _radio.SetItems(items);

            // Assert
            _radio.childCount.Should().Be(0);
            _radio.SelectedKind.Should().BeNull();
        }

        [Test]
        public void WhenSetSelectedKindCalledWithValidKind_ThenUpdatesSelectedKindAndRaisesEvent()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite")));
            var calls = 0;
            _radio.SelectedKindChanged += _ => calls++;

            // Act
            _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Assert
            _radio.SelectedKind.Should().Be(HumanOpponentKind.Local);
            calls.Should().Be(1);
            _radio.Q<Button>(HumanOpponentKind.Local.ToString())
                .ClassListContains("human-kind-radio__item--selected")
                .Should()
                .BeTrue();
        }

        [Test]
        public void WhenSetSelectedKindCalledWithUnknownKind_ThenIsIgnoredAndSelectionUnchanged()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local")));
            var calls = 0;
            _radio.SelectedKindChanged += _ => calls++;
            _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Act
            _radio.SetSelectedKind(HumanOpponentKind.Matchmaking);

            // Assert
            _radio.SelectedKind.Should().Be(HumanOpponentKind.Local);
            calls.Should().Be(1);
        }

        [Test]
        public void WhenSetSelectedKindCalledBeforeSetItems_ThenDoesNotThrowAndSelectedKindRemainsNull()
        {
            // Arrange
            Action act = () => _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Act / Assert
            act.Should().NotThrow();
            _radio.SelectedKind.Should().BeNull();
        }

        [Test]
        public void WhenSetSelectedKindWithoutNotifyCalled_ThenUpdatesSelectedKindButDoesNotRaiseEvent()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite")));
            var calls = 0;
            _radio.SelectedKindChanged += _ => calls++;

            // Act
            _radio.SetSelectedKindWithoutNotify(HumanOpponentKind.DirectInvite);

            // Assert
            _radio.SelectedKind.Should().Be(HumanOpponentKind.DirectInvite);
            calls.Should().Be(0);
        }

        [Test]
        public void WhenSameKindSetAgain_ThenIsNoOpAndDoesNotRaiseEvent()
        {
            // Arrange
            _radio.SetItems(CreateItems((HumanOpponentKind.Local, "Local"), (HumanOpponentKind.DirectInvite, "Invite")));
            var calls = 0;
            _radio.SelectedKindChanged += _ => calls++;

            // Act
            _radio.SetSelectedKind(HumanOpponentKind.Local);
            _radio.SetSelectedKind(HumanOpponentKind.Local);

            // Assert
            calls.Should().Be(1);
        }

        private static IReadOnlyList<HumanKindRadioItem> CreateItems(params (HumanOpponentKind kind, string label)[] items)
        {
            var result = new HumanKindRadioItem[items.Length];
            for (var i = 0; i < items.Length; i++)
                result[i] = new HumanKindRadioItem(items[i].kind, items[i].label);

            return Array.AsReadOnly(result);
        }
    }
}