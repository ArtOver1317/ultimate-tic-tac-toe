using System;
using R3;
using Runtime.Infrastructure.Logging;
using StripLog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Runtime.UI.Core
{
    [RequireComponent(typeof(UIDocument))]
    public abstract class BaseView<TViewModel> : MonoBehaviour where TViewModel : BaseViewModel
    {
        private UIDocument _uiDocument;
        private readonly CompositeDisposable _disposables = new();
        private bool _isInitialized;

        protected VisualElement Root { get; private set; }
        protected TViewModel ViewModel { get; private set; }

        public TViewModel GetViewModel() => ViewModel;

        public void SetViewModel(TViewModel viewModel)
        {
            ViewModel = viewModel;
            TryInitializeViewModel();
        }

        public void ClearViewModel()
        {
            ViewModel?.Dispose();
            ViewModel = null;
            _disposables.Clear();
            _isInitialized = false;
        }

        protected virtual void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            Root = _uiDocument.rootVisualElement;
            UxmlBinder.BindElements(this, Root);
            TryInitializeViewModel();
        }

        private void TryInitializeViewModel()
        {
            if (_isInitialized || ViewModel == null || Root == null)
                return;

            _isInitialized = true;
            ViewModel.Initialize();
            BindViewModel();
        }

        protected abstract void BindViewModel();

        protected void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

        protected void BindText<T>(Observable<T> source, VisualElement element)
        {
            if (element is TextElement textElement)
            {
                source.Subscribe(value => textElement.text = value?.ToString() ?? string.Empty)
                    .AddTo(_disposables);
            }
            else
            {
                Log.Error(LogTags.UI, $"Element {element.name} is not a TextElement", this);
            }
        }

        protected void BindVisibility(Observable<bool> source, VisualElement element) =>
            source.Subscribe(isVisible =>
                    element.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None)
                .AddTo(_disposables);

        protected void BindEnabled(Observable<bool> source, VisualElement element) =>
            source.Subscribe(element.SetEnabled)
                .AddTo(_disposables);

        protected virtual void OnDestroy()
        {
            ViewModel?.Dispose();
            _disposables.Dispose();
        }
    }
}