#nullable enable

using System.Collections;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.UI.GameModes.Wizard;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Views
{
    public partial class GameSelectionViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenViewModelSelectedModeIdChanges_ThenListViewRefreshesItemsAndHighlightUpdates()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();
            modeList.selectedIndex.Should().Be(-1);

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            modeList.selectedIndex.Should().Be(0);

            var element = new GameCardElement();
            modeList.bindItem(element, 0);
            element.ClassListContains(GameCardElement.SelectedClass).Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenUserSelectsItemInListView_ThenViewModelSelectModeIsCalled()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            var modeList = GetModeList();

            modeList.SetSelection(1);

            _viewModel.SelectedGameId.Value.Should().Be("ultimate");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSelectionIsSyncedFromViewModel_ThenListViewUpdatesWithoutChangingSelectedGameId()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            _viewModel.SelectedGameId.Value.Should().Be("classic");
            GetModeList().selectedIndex.Should().Be(0);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenModeListSelectionChangedWithEmptyOrNullItems_ThenDoesNotAccidentallyChangeSelection()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
            yield return null;

            GetModeList().SetSelection(new List<int>());
            GetModeList().ClearSelection();

            _viewModel.SelectedGameId.Value.Should().Be("classic");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindViewModelCalledAndVMHasSelectedModeId_ThenListViewRestoresSelectionHighlight()
        {
            _viewModel.SelectedGameId.Value = "ultimate";

            _view.SetViewModel(_viewModel);
            yield return null;

            GetModeList().selectedIndex.Should().Be(1);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenAvailableModesIsNull_ThenViewTreatsAsEmptyAndDoesNotThrow()
        {
            _viewModel.SetAvailableModesForTests(null!);

            System.Action act = () => _view.SetViewModel(_viewModel);

            act.Should().NotThrow();
            GetModeList().itemsSource.Should().NotBeNull();

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBindViewModelCalledAndVMSelectedModeIdIsNull_ThenListViewHasNoSelection()
        {
            _viewModel.SelectedGameId.Value = null;

            _view.SetViewModel(_viewModel);
            yield return null;

            GetModeList().selectedIndex.Should().Be(-1);
        }

        [UnityTest]
        [Timeout(5000)]
        [Category("Optional")]
        public IEnumerator WhenAvailableModesChangeDuringRuntime_ThenListViewRebindsAndRestoresSelection()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
           
            _viewModel.SetAvailableModesForTests(new List<GameMetadata>
            {
                CreateMode("classic", 0),
                CreateMode("ultimate", 1),
                CreateMode("blitz", 2),
            });

            yield return null;

            GetModeList().itemsSource.Should().NotBeNull();
            GetModeList().selectedIndex.Should().Be(0);
        }

        [UnityTest]
        [Timeout(5000)]
        [Category("Optional")]
        public IEnumerator WhenAvailableModesUpdatedAndPreviouslySelectedModeIdIsNoLongerPresent_ThenViewClearsSelectionAndContinueStateIsConsistent()
        {
            _view.SetViewModel(_viewModel);
            yield return null;

            _viewModel.SelectedGameId.Value = "classic";
           
            _viewModel.SetAvailableModesForTests(new List<GameMetadata>
            {
                CreateMode("ultimate", 0),
                CreateMode("blitz", 1),
            });

            yield return null;

            GetModeList().selectedIndex.Should().Be(-1);
            GetContinueButton().enabledInHierarchy.Should().BeFalse();
            _viewModel.SelectedGameId.Value.Should().BeNull();
        }
    }
}