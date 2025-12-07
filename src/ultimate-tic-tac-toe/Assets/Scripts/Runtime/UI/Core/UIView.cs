using System;
using UnityEngine;

namespace Runtime.UI.Core
{
    public abstract class UIView<TViewModel> : BaseView<TViewModel>, IUIView<TViewModel> 
        where TViewModel : BaseViewModel
    {
        [Header("View Settings")]
        [SerializeField] private bool ShowOnAwake;

        public bool IsVisible { get; private set; }

        public Type ViewModelType => typeof(TViewModel);

        protected override void Awake()
        {
            base.Awake();
            
            if (ShowOnAwake)
            {
                Root.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                IsVisible = true;
            }
            else
            {
                Root.style.display = UnityEngine.UIElements.DisplayStyle.None;
                IsVisible = false;
            }
        }

        public virtual void Show()
        {
            if (IsVisible) 
                return;

            Root.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
            IsVisible = true;
            OnShow();
        }

        public virtual void Hide()
        {
            if (!IsVisible) 
                return;

            Root.style.display = UnityEngine.UIElements.DisplayStyle.None;
            IsVisible = false;
            OnHide();
        }

        public virtual void Close()
        {
            if (gameObject != null) 
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

        public TViewModel GetViewModel() => ViewModel;
        
        BaseViewModel IUIView.GetViewModel() => ViewModel;
    }
}

