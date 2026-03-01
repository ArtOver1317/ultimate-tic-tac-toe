using R3;
using Runtime.Extensions;
using Runtime.UI.Core;
using UnityEngine.UIElements;

namespace Runtime.UI.MainMenu
{
    public sealed class PlayerStatisticsView : UIView<PlayerStatisticsViewModel>
    {
        [Core.UxmlElementAttribute("Title")]
        private Label _titleLabel;

        [Core.UxmlElementAttribute("BackButton")]
        private Button _backButton;

        [Core.UxmlElementAttribute("EmptyStateLabel")]
        private Label _emptyStateLabel;

        [Core.UxmlElementAttribute("GroupsContainer")]
        private ScrollView _groupsContainer;

        protected override void BindViewModel()
        {
            BindText(ViewModel.TitleText, _titleLabel);
            BindText(ViewModel.BackButtonText, _backButton);
            BindText(ViewModel.EmptyStateText, _emptyStateLabel);
            BindVisibility(ViewModel.IsEmpty, _emptyStateLabel);
            BindVisibility(ViewModel.IsEmpty.Select(v => !v), _groupsContainer);

            AddDisposable(_backButton.OnClickAsObservable().Subscribe(_ => ViewModel.RequestBack()));
            AddDisposable(ViewModel.Groups.Subscribe(RebuildContent));
        }

        private void RebuildContent(System.Collections.Generic.IReadOnlyList<PlayerStatisticsGroupPresentation> groups)
        {
            _groupsContainer.Clear();

            if (groups == null || groups.Count == 0)
                return;

            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];

                var header = new Label(group.GameTitle);
                header.AddToClassList("statistics-group-header");
                _groupsContainer.Add(header);

                for (var j = 0; j < group.Rows.Count; j++)
                {
                    var row = group.Rows[j];
                    var rowLabel = new Label(row.CompositeLabel);
                    rowLabel.AddToClassList("statistics-row");
                    _groupsContainer.Add(rowLabel);
                }
            }
        }
    }
}
