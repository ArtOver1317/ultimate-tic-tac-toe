#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.UI.Components;
using UnityEngine.UIElements;

namespace Runtime.UI.GameModes.Wizard
{
    internal sealed class MatchSetupParticipantSectionBinder : IDisposable
    {
        private readonly MatchSetupViewModel _viewModel;
        private readonly SegmentedToggle _opponentToggle;
        private readonly VisualElement _botSettingsSection;
        private readonly VisualElement _humanSettingsSection;
        private readonly DifficultyChips _difficultyChips;
        private readonly HumanKindRadio _humanKindRadio;
        private readonly PlayerIdInput _playerIdInput;
        private readonly CompositeDisposable _disposables = new();

        private string _botLabel = string.Empty;
        private string _humanLabel = string.Empty;
        private string _humanLocalLabel = string.Empty;
        private string _humanDirectInviteLabel = string.Empty;
        private string _humanMatchmakingLabel = string.Empty;
        private bool _isLocalHumanSupported = true;

        public MatchSetupParticipantSectionBinder(
            MatchSetupViewModel viewModel,
            SegmentedToggle opponentToggle,
            VisualElement botSettingsSection,
            VisualElement humanSettingsSection,
            DifficultyChips difficultyChips,
            HumanKindRadio humanKindRadio,
            PlayerIdInput playerIdInput)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _opponentToggle = opponentToggle ?? throw new ArgumentNullException(nameof(opponentToggle));
            _botSettingsSection = botSettingsSection ?? throw new ArgumentNullException(nameof(botSettingsSection));
            _humanSettingsSection = humanSettingsSection ?? throw new ArgumentNullException(nameof(humanSettingsSection));
            _difficultyChips = difficultyChips ?? throw new ArgumentNullException(nameof(difficultyChips));
            _humanKindRadio = humanKindRadio ?? throw new ArgumentNullException(nameof(humanKindRadio));
            _playerIdInput = playerIdInput ?? throw new ArgumentNullException(nameof(playerIdInput));
        }

        public void Bind()
        {
            BindOpponentSection();
            BindHumanSection();
        }

        public void Dispose() => _disposables.Dispose();

        private void BindOpponentSection()
        {
            BindOpponentLabels();
            BindOpponentToggle();
            BindDifficultySelection();
        }

        private void BindOpponentLabels()
        {
            AddDisposable(_viewModel.OpponentBotText.Subscribe(text => UpdateOpponentLabels(botLabel: text)));
            AddDisposable(_viewModel.OpponentHumanText.Subscribe(text => UpdateOpponentLabels(humanLabel: text)));
            UpdateOpponentLabels();
        }

        private void BindOpponentToggle()
        {
            SyncOpponentToggle(_viewModel.OpponentType.CurrentValue);
            AddDisposable(_viewModel.OpponentType.Subscribe(SyncOpponentToggle));
            BindVisibility(_viewModel.IsBotSettingsVisible, _botSettingsSection);
            BindVisibility(_viewModel.IsHumanSettingsVisible, _humanSettingsSection);

            void OnOpponentToggleChanged(int index) =>
                _viewModel.SetOpponentType(index == 0 ? OpponentType.Bot : OpponentType.Human);

            _opponentToggle.SelectedIndexChanged += OnOpponentToggleChanged;
            AddDisposable(Disposable.Create(() => _opponentToggle.SelectedIndexChanged -= OnOpponentToggleChanged));
        }

        private void BindDifficultySelection()
        {
            AddDisposable(_viewModel.DifficultyItems.Subscribe(items =>
            {
                _difficultyChips.SetItems(items);
                _difficultyChips.SetSelectedIdWithoutNotify(_viewModel.SelectedDifficultyId.CurrentValue);
            }));

            AddDisposable(_viewModel.SelectedDifficultyId.Subscribe(_difficultyChips.SetSelectedIdWithoutNotify));

            void OnDifficultySelected(string id) => _viewModel.SetBotDifficultyId(id);

            _difficultyChips.SelectedIdChanged += OnDifficultySelected;
            AddDisposable(Disposable.Create(() => _difficultyChips.SelectedIdChanged -= OnDifficultySelected));
        }

        private void UpdateOpponentLabels(string? botLabel = null, string? humanLabel = null)
        {
            if (botLabel != null)
                _botLabel = botLabel;

            if (humanLabel != null)
                _humanLabel = humanLabel;

            _opponentToggle.SetLabels(_botLabel, _humanLabel);
        }

        private void BindHumanSection()
        {
            BindHumanKindOptions();
            BindHumanKindSelection();
            BindPlayerIdInput();
        }

        private void BindHumanKindOptions()
        {
            AddDisposable(_viewModel.HumanLocalText.Subscribe(text => UpdateHumanKindOptionLabels(localLabel: text)));
            AddDisposable(_viewModel.HumanDirectInviteText.Subscribe(text => UpdateHumanKindOptionLabels(directInviteLabel: text)));
            AddDisposable(_viewModel.HumanMatchmakingText.Subscribe(text => UpdateHumanKindOptionLabels(matchmakingLabel: text)));
            AddDisposable(_viewModel.IsLocalHumanSupported.Subscribe(isSupported => UpdateHumanKindOptionLabels(isLocalHumanSupported: isSupported)));

            UpdateHumanKindOptions();
        }

        private void BindHumanKindSelection()
        {
            _humanKindRadio.SetSelectedKindWithoutNotify(_viewModel.HumanOpponentKind.CurrentValue);
            AddDisposable(_viewModel.HumanOpponentKind.Subscribe(_humanKindRadio.SetSelectedKindWithoutNotify));

            void OnHumanKindSelected(HumanOpponentKind kind) => _viewModel.SetHumanOpponentKind(kind);

            _humanKindRadio.SelectedKindChanged += OnHumanKindSelected;
            AddDisposable(Disposable.Create(() => _humanKindRadio.SelectedKindChanged -= OnHumanKindSelected));
        }

        private void BindPlayerIdInput()
        {
            AddDisposable(_viewModel.PlayerIdLabelText.Subscribe(_playerIdInput.SetLabel));
            AddDisposable(_viewModel.TargetPlayerId.Subscribe(_playerIdInput.SetValueWithoutNotify));
            AddDisposable(_viewModel.PlayerIdErrorText.Subscribe(_playerIdInput.SetError));
            BindVisibility(_viewModel.IsPlayerIdInputVisible, _playerIdInput);

            void OnPlayerIdChanged(string value) => _viewModel.SetTargetPlayerId(value);

            _playerIdInput.ValueChanged += OnPlayerIdChanged;
            AddDisposable(Disposable.Create(() => _playerIdInput.ValueChanged -= OnPlayerIdChanged));
        }

        private void UpdateHumanKindOptionLabels(
            string? localLabel = null,
            string? directInviteLabel = null,
            string? matchmakingLabel = null,
            bool? isLocalHumanSupported = null)
        {
            if (localLabel != null)
                _humanLocalLabel = localLabel;

            if (directInviteLabel != null)
                _humanDirectInviteLabel = directInviteLabel;

            if (matchmakingLabel != null)
                _humanMatchmakingLabel = matchmakingLabel;

            if (isLocalHumanSupported.HasValue)
                _isLocalHumanSupported = isLocalHumanSupported.Value;

            UpdateHumanKindOptions();
        }

        private void UpdateHumanKindOptions()
        {
            var items = new[]
            {
                new HumanKindRadioItem(HumanOpponentKind.Local, _humanLocalLabel, isEnabled: _isLocalHumanSupported),
                new HumanKindRadioItem(HumanOpponentKind.DirectInvite, _humanDirectInviteLabel),
                new HumanKindRadioItem(HumanOpponentKind.Matchmaking, _humanMatchmakingLabel, isEnabled: true),
            };

            _humanKindRadio.SetItems(items);
            _humanKindRadio.SetSelectedKindWithoutNotify(_viewModel.HumanOpponentKind.CurrentValue);
        }

        private void SyncOpponentToggle(OpponentType opponentType)
        {
            var index = opponentType == OpponentType.Bot ? 0 : 1;
            _opponentToggle.SetSelectedIndexWithoutNotify(index);
        }

        private void BindVisibility(Observable<bool> source, VisualElement element) =>
            AddDisposable(source.Subscribe(isVisible =>
                element.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None));

        private void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);
    }
}
