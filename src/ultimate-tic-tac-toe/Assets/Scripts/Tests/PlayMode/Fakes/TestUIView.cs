using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Tests.PlayMode.Fakes
{
    public class TestUIView : UIView<TestViewModel>
    {
        public int OnShowCallCount { get; private set; }
        public int OnHideCallCount { get; private set; }
        public int OnResetForPoolCallCount { get; private set; }
        public int OnInitializeFromPoolCallCount { get; private set; }
        public int BindViewModelCallCount { get; private set; }

        public new VisualElement Root => base.Root;

        public void SetShowOnAwakeTo(bool value)
        {
            var field = typeof(UIView<TestViewModel>)
                .GetField("ShowOnAwake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
            field?.SetValue(this, value);
        }

        protected override void OnShow()
        {
            base.OnShow();
            OnShowCallCount++;
        }

        protected override void OnHide()
        {
            base.OnHide();
            OnHideCallCount++;
        }

        protected override void OnResetForPool()
        {
            base.OnResetForPool();
            OnResetForPoolCallCount++;
        }

        protected override void OnInitializeFromPool()
        {
            base.OnInitializeFromPool();
            OnInitializeFromPoolCallCount++;
        }

        protected override void BindViewModel() => BindViewModelCallCount++;
    }
}