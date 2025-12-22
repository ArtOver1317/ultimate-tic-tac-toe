using System;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.EditMode
{
    [TestFixture]
    public class UxmlBinderTests
    {
        #region Null Handling Tests

        [Test]
        public void WhenTargetIsNull_ThenNoExceptionThrown()
        {
            // Arrange
            var root = new VisualElement();
            LogAssert.Expect(LogType.Error, new Regex(@"\[UxmlBinder\] Target or root is null!"));

            // Act
            Action act = () => UxmlBinder.BindElements(null, root);

            // Assert
            act.Should().NotThrow("BindElements should safely handle null target");
        }

        [Test]
        public void WhenRootIsNull_ThenNoExceptionThrown()
        {
            // Arrange
            var target = new TestViewSingleElement();
            LogAssert.Expect(LogType.Error, new Regex(@"\[UxmlBinder\] Target or root is null!"));

            // Act
            Action act = () => UxmlBinder.BindElements(target, null);

            // Assert
            act.Should().NotThrow("BindElements should safely handle null root");
        }

        #endregion

        #region Successful Binding Tests

        [Test]
        public void WhenElementExists_ThenFieldIsBound()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "TestButton" };
            root.Add(button);
            var target = new TestViewSingleElement();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.TestButton.Should().NotBeNull("field should be bound to the found element");
            target.TestButton.Should().BeSameAs(button, "field should reference the exact Button instance");
        }

        [Test]
        public void WhenMultipleElements_ThenAllFieldsAreBound()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "MyButton" };
            var label = new Label { name = "MyLabel" };
            var container = new VisualElement { name = "Container" };
            
            root.Add(button);
            root.Add(label);
            root.Add(container);
            
            var target = new TestViewMultipleElements();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.MyButton.Should().NotBeNull("button field should be bound");
            target.MyLabel.Should().NotBeNull("label field should be bound");
            target.Container.Should().NotBeNull("container field should be bound");
            
            target.MyButton.Should().BeSameAs(button, "button field should reference the exact Button instance");
            target.MyLabel.Should().BeSameAs(label, "label field should reference the exact Label instance");
            target.Container.Should().BeSameAs(container, "container field should reference the exact VisualElement instance");
        }

        #endregion

        #region Name Transformation Tests

        [Test]
        public void WhenNameNotSpecified_ThenFieldNameTransformed()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "MyButton" };
            root.Add(button);
            var target = new TestViewAutoName();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.MyButton.Should().NotBeNull("field name should be transformed: _myButton → MyButton");
            target.MyButton.Should().BeSameAs(button, "field should reference the exact Button instance");
        }

        [Test]
        public void WhenExplicitName_ThenExplicitNameUsed()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "CustomName" };
            root.Add(button);
            var target = new TestViewExplicitName();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.Button.Should().NotBeNull("field should be bound using explicit name");
            target.Button.Should().BeSameAs(button, "field should reference the Button with name 'CustomName'");
        }

        [Test]
        public void WhenFieldWithoutUnderscore_ThenFirstCharCapitalized()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "MyButton" };
            root.Add(button);
            var target = new TestViewNoUnderscore();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.MyButton.Should().NotBeNull("field name without underscore should be transformed: myButton → MyButton");
            target.MyButton.Should().BeSameAs(button, "field should reference the exact Button instance");
        }

        #endregion

        #region Optional/Required Elements Tests

        [Test]
        public void WhenRequiredElementMissing_ThenFieldRemainsNull()
        {
            // Arrange
            var root = new VisualElement();
            var target = new TestViewRequiredElement();
            LogAssert.Expect(LogType.Error, new Regex(@"\[UxmlBinder\] Required element 'MissingButton'.*"));

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.MissingButton.Should().BeNull("required element not found, field should remain null");
        }

        [Test]
        public void WhenOptionalElementMissing_ThenFieldRemainsNullSilently()
        {
            // Arrange
            var root = new VisualElement();
            var target = new TestViewOptionalElement();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.OptionalButton.Should().BeNull("optional element not found, field should remain null without error");
        }

        #endregion

        #region Field Filtering Tests

        [Test]
        public void WhenFieldWithoutAttribute_ThenNotBound()
        {
            // Arrange
            var root = new VisualElement();
            var button = new Button { name = "RegularButton" };
            root.Add(button);
            var target = new TestViewNoAttribute();

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target.RegularButton.Should().BeNull("field without UxmlElement attribute should not be bound");
        }

        [Test]
        public void WhenFieldIsNotVisualElement_ThenIgnored()
        {
            // Arrange
            var root = new VisualElement();
            var target = new TestViewInvalidField();

            // Act
            Action act = () => UxmlBinder.BindElements(target, root);

            // Assert - verify behavior instead of log (cached bindings don't log on subsequent calls)
            act.Should().NotThrow("non-VisualElement field should be ignored");
            target.NotVisualElement.Should().BeNull("non-VisualElement field should remain null");
        }

        #endregion

        #region Test Helper Classes

        private class TestViewSingleElement
        {
            [Runtime.UI.Core.UxmlElement("TestButton")]
            private Button _testButton;

            public Button TestButton => _testButton;
        }

        private class TestViewMultipleElements
        {
            [Runtime.UI.Core.UxmlElement("MyButton")]
            private Button _myButton;
            
            [Runtime.UI.Core.UxmlElement("MyLabel")]
            private Label _myLabel;
            
            [Runtime.UI.Core.UxmlElement("Container")]
            private VisualElement _container;

            public Button MyButton => _myButton;
            public Label MyLabel => _myLabel;
            public VisualElement Container => _container;
        }

        private class TestViewAutoName
        {
            [Runtime.UI.Core.UxmlElement]
            private Button _myButton;

            public Button MyButton => _myButton;
        }

        private class TestViewExplicitName
        {
            [Runtime.UI.Core.UxmlElement("CustomName")]
            private Button _button;

            public Button Button => _button;
        }

        private class TestViewNoUnderscore
        {
            [Runtime.UI.Core.UxmlElement]
            private Button myButton;

            public Button MyButton => myButton;
        }

        private class TestViewRequiredElement
        {
            [Runtime.UI.Core.UxmlElement("MissingButton")]
            private Button _missingButton;

            public Button MissingButton => _missingButton;
        }

        private class TestViewOptionalElement
        {
            [Runtime.UI.Core.UxmlElement("OptionalButton", isOptional: true)]
            private Button _optionalButton;

            public Button OptionalButton => _optionalButton;
        }

        private class TestViewNoAttribute
        {
            private Button _regularButton;

            public Button RegularButton => _regularButton;
        }

        private class TestViewInvalidField
        {
            [Runtime.UI.Core.UxmlElement("Test")] private string _notVisualElement;

            public string NotVisualElement => _notVisualElement;
        }

        #endregion
    }
}

