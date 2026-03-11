#nullable enable

using R3;
using Runtime.GameModes.Wizard.Modes;
using Runtime.UI.Core;

namespace Runtime.GameModes.Wizard.ViewModels
{
    public sealed class UltimateTicTacToeSettingsViewModel : BaseViewModel, IGameSettingsViewModel
    {
        private readonly ReactiveProperty<IGameConfig> _config = new(UltimateTicTacToeConfig.Instance);
        private readonly ReactiveProperty<bool> _isValid = new(true);

        public ReadOnlyReactiveProperty<IGameConfig> Config => _config;
        public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

        public bool TryApplyConfig(IGameConfig config) => config is UltimateTicTacToeConfig;

        protected override void OnReset()
        {
            _config.Value = UltimateTicTacToeConfig.Instance;
            _isValid.Value = true;
        }

        protected override void OnDispose()
        {
            _config.Dispose();
            _isValid.Dispose();
            base.OnDispose();
        }
    }
}