using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.UI.Core
{
    public abstract class UIView<TViewModel> : BaseView<TViewModel>, IUIView<TViewModel> 
        where TViewModel : BaseViewModel
    {
        private const int _fadeInDurationMs = 150;
        private const long _fadeInCleanupDelayMs = 200;

        [Header("View Settings")]
        [SerializeField] 
        private bool ShowOnAwake;

        public bool IsVisible { get; private set; }

        public Type ViewModelType => typeof(TViewModel);

        protected override void Awake()
        {
            base.Awake();
            
            if (ShowOnAwake)
            {
                Root.style.display = DisplayStyle.Flex;
                IsVisible = true;
            }
            else
            {
                Root.style.display = DisplayStyle.None;
                IsVisible = false;
            }
        }

        public virtual void Show()
        {
            if (IsVisible) 
                return;

            Root.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new("opacity") });
            
            Root.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new(_fadeInDurationMs, TimeUnit.Millisecond) });
            
            Root.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new(EasingMode.EaseOut) });
            
            Root.style.opacity = 0f;
            Root.style.display = DisplayStyle.Flex;
            
            Root.schedule.Execute(() =>
            {
                Root.style.opacity = 1f;
                
                Root.schedule.Execute(() =>
                {
                    Root.style.transitionProperty = StyleKeyword.Null;
                    Root.style.transitionDuration = StyleKeyword.Null;
                    Root.style.transitionTimingFunction = StyleKeyword.Null;
                    Root.style.opacity = StyleKeyword.Null;
                }).ExecuteLater(_fadeInCleanupDelayMs);
            });
           
            IsVisible = true;
            OnShow();
        }

        public virtual void Hide()
        {
            if (!IsVisible) 
                return;

            Root.style.display = DisplayStyle.None;
            IsVisible = false;
            OnHide();
        }

        public virtual void Close()
        {
            // In pooled/scene transitions a view might already be destroyed but still referenced.
            // Accessing `gameObject` on a destroyed component throws MissingReferenceException.
            if (!this)
                return;

            Destroy(gameObject);
        }

        protected virtual void OnShow() { }

        protected virtual void OnHide() { }

        public virtual void ResetForPool()
        {
            Hide();
            ClearViewModel();
            OnResetForPool();
        }

        public virtual void InitializeFromPool() => OnInitializeFromPool();

        protected virtual void OnResetForPool() { }

        protected virtual void OnInitializeFromPool() { }
        
        BaseViewModel IUIView.GetViewModel() => ViewModel;
    }
}