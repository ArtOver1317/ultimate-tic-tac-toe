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

        #region Test Helper Classes

        private class TestViewSingleElement
        {
            [Runtime.UI.Core.UxmlElement("TestButton")]
            public Button _testButton;
        }

        #endregion
    }
}

