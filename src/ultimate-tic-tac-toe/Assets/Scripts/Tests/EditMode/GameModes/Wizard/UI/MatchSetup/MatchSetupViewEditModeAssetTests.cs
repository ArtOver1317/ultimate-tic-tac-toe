#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.UI.Components;
using Runtime.UI.Core;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tests.EditMode.GameModes.Wizard.UI.MatchSetup
{
    public partial class MatchSetupViewEditModeTests
    {
        [Test]
        public void WhenMatchSetupUxmlLoaded_ThenHasHumanSettingsElements()
        {
            var root = _uxml.CloneTree();

            var section = root.Q<VisualElement>("HumanSettingsSection");
            var title = root.Q<Label>("HumanSettingsTitle");
            var radio = root.Q<HumanKindRadio>("HumanKindRadio");
            var playerIdInput = root.Q<PlayerIdInput>("PlayerIdInput");

            section.Should().NotBeNull();
            title.Should().NotBeNull();
            radio.Should().NotBeNull();
            playerIdInput.Should().NotBeNull();
        }

        [Test]
        public void WhenMatchSetupPrefabLoaded_ThenHasRequiredComponentsAndValidUxmlAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_matchSetupPrefabPath);
            prefab.Should().NotBeNull();

            var view = prefab.GetComponent<MatchSetupView>();
            var document = prefab.GetComponent<UIDocument>();

            view.Should().NotBeNull();
            document.Should().NotBeNull();
            document.visualTreeAsset.Should().NotBeNull();
        }

        [Test]
        public void WhenErrorLabelIsMissingInUxml_ThenBindViewModelDoesNotThrowAndInlineErrorUpdatesDoNotCrash()
        {
            var root = _view.RootForTests;
            var errorLabel = root.Q<Label>("ErrorLabel");
            errorLabel.Should().NotBeNull();
            errorLabel.RemoveFromHierarchy();

            _view.ClearViewModel();
            UxmlBinder.BindElements(_view, root);

            Action act = () => _view.SetViewModel(_viewModel);

            act.Should().NotThrow();

            Action updateAct = () => _session.EmitValidationErrors(new List<ValidationError>
            {
                new("GameConfig", "Errors.GameWizard.ConfigRequired"),
            });

            updateAct.Should().NotThrow();
        }
    }
}