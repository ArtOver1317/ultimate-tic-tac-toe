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

        #endregion
    }
}

