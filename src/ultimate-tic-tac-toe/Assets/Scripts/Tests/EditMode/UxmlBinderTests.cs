using System;
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
            LogAssert.Expect(LogType.Error, "[UxmlBinder] Target or root is null!");

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
            LogAssert.Expect(LogType.Error, "[UxmlBinder] Target or root is null!");

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
            target._testButton.Should().NotBeNull("field should be bound to the found element");
            target._testButton.Should().BeSameAs(button, "field should reference the exact Button instance");
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
            target._myButton.Should().NotBeNull("button field should be bound");
            target._myLabel.Should().NotBeNull("label field should be bound");
            target._container.Should().NotBeNull("container field should be bound");
            
            target._myButton.Should().BeSameAs(button, "button field should reference the exact Button instance");
            target._myLabel.Should().BeSameAs(label, "label field should reference the exact Label instance");
            target._container.Should().BeSameAs(container, "container field should reference the exact VisualElement instance");
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
            target._myButton.Should().NotBeNull("field name should be transformed: _myButton → MyButton");
            target._myButton.Should().BeSameAs(button, "field should reference the exact Button instance");
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
            target._button.Should().NotBeNull("field should be bound using explicit name");
            target._button.Should().BeSameAs(button, "field should reference the Button with name 'CustomName'");
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
            target.myButton.Should().NotBeNull("field name without underscore should be transformed: myButton → MyButton");
            target.myButton.Should().BeSameAs(button, "field should reference the exact Button instance");
        }

        #endregion

        #region Optional/Required Elements Tests

        [Test]
        public void WhenRequiredElementMissing_ThenFieldRemainsNull()
        {
            // Arrange
            var root = new VisualElement();
            var target = new TestViewRequiredElement();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[UxmlBinder\] Required element 'MissingButton'.*"));

            // Act
            UxmlBinder.BindElements(target, root);

            // Assert
            target._missingButton.Should().BeNull("required element not found, field should remain null");
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
            target._optionalButton.Should().BeNull("optional element not found, field should remain null without error");
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
            target._regularButton.Should().BeNull("field without UxmlElement attribute should not be bound");
        }

        [Test]
        public void WhenFieldIsNotVisualElement_ThenIgnored()
        {
            // Arrange
            var root = new VisualElement();
            var target = new TestViewInvalidField();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[UxmlBinder\] Field _notVisualElement.*not a VisualElement type"));

            // Act
            Action act = () => UxmlBinder.BindElements(target, root);

            // Assert
            act.Should().NotThrow("non-VisualElement field should be ignored with warning");
            target._notVisualElement.Should().BeNull("non-VisualElement field should remain null");
        }

        #endregion

        #region Test Helper Classes

        private class TestViewSingleElement
        {
            [Runtime.UI.Core.UxmlElement("TestButton")]
            public Button _testButton;
        }

        private class TestViewMultipleElements
        {
            [Runtime.UI.Core.UxmlElement("MyButton")]
            public Button _myButton;
            
            [Runtime.UI.Core.UxmlElement("MyLabel")]
            public Label _myLabel;
            
            [Runtime.UI.Core.UxmlElement("Container")]
            public VisualElement _container;
        }

        private class TestViewAutoName
        {
            [Runtime.UI.Core.UxmlElement]
            public Button _myButton;
        }

        private class TestViewExplicitName
        {
            [Runtime.UI.Core.UxmlElement("CustomName")]
            public Button _button;
        }

        private class TestViewNoUnderscore
        {
            [Runtime.UI.Core.UxmlElement]
            public Button myButton;
        }

        private class TestViewRequiredElement
        {
            [Runtime.UI.Core.UxmlElement("MissingButton")]
            public Button _missingButton;
        }

        private class TestViewOptionalElement
        {
            [Runtime.UI.Core.UxmlElement("OptionalButton", isOptional: true)]
            public Button _optionalButton;
        }

        private class TestViewNoAttribute
        {
            public Button _regularButton;
        }

        private class TestViewInvalidField
        {
            [Runtime.UI.Core.UxmlElement("Test")]
            public string _notVisualElement;
        }

        #endregion
    }
}

