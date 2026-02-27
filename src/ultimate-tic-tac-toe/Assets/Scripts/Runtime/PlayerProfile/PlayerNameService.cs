using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using Runtime.Infrastructure.Save;
using Runtime.Localization;
using VContainer.Unity;

namespace Runtime.PlayerProfile
{
    public sealed class PlayerNameService : IPlayerNameService, IInitializable, IDisposable
    {
        private const string SaveSection = "player_name";
        private const string PlayerNameTable = "Common";
        private const string PlayerNameKey = "Common.Player";

        private readonly ISaveService _saveService;
        private readonly ISaveServiceWithResult _saveServiceWithResult;
        private readonly ILocalizationService _localizationService;
        private readonly CompositeDisposable _disposables = new();
        private readonly ReactiveProperty<PlayerNameSnapshot> _snapshot;

        private bool _isInitialized;
        private string? _customName;

        public ReadOnlyReactiveProperty<PlayerNameSnapshot> Snapshot => _snapshot;

        public PlayerNameService(
            ISaveService saveService,
            ISaveServiceWithResult saveServiceWithResult,
            ILocalizationService localizationService)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _saveServiceWithResult = saveServiceWithResult ?? throw new ArgumentNullException(nameof(saveServiceWithResult));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

            _snapshot = new ReactiveProperty<PlayerNameSnapshot>(
                new PlayerNameSnapshot(
                    customName: null,
                    displayName: ResolveDefaultDisplayName()));
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            var loadedCustomName = _saveService.Load<string>(SaveSection, null);

            if (loadedCustomName != null)
            {
                var validation = PlayerNameValidator.ValidateOnConfirm(loadedCustomName);
                if (validation != PlayerNameValidationError.None)
                {
                    GameLog.Warning($"[PlayerNameService] Persisted player name is invalid. ValidationError={validation}. Fallback to default.");
                    loadedCustomName = null;
                }
            }

            _customName = loadedCustomName;
            EmitSnapshot();

            _localizationService.CurrentLocale
                .Subscribe(_ =>
                {
                    if (_customName == null)
                        EmitSnapshot();
                })
                .AddTo(_disposables);
        }

        public UniTask<PlayerNameChangeResult> TrySetOnConfirmAsync(string? confirmedInput, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var validationError = PlayerNameValidator.ValidateOnConfirm(confirmedInput);
            if (validationError != PlayerNameValidationError.None)
            {
                return UniTask.FromResult(
                    PlayerNameChangeResult.FailedValidation(
                        key: ValidationErrorToMessageKey(validationError),
                        error: validationError));
            }

            var saveResult = _saveServiceWithResult.TrySave(SaveSection, confirmedInput);
            if (!saveResult.IsSuccess)
            {
                return UniTask.FromResult(
                    PlayerNameChangeResult.FailedSave("Errors.PlayerProfile.SaveFailed"));
            }

            _customName = confirmedInput;
            EmitSnapshot();
            return UniTask.FromResult(PlayerNameChangeResult.Success());
        }

        public void Dispose()
        {
            _disposables.Dispose();
            _snapshot.Dispose();
        }

        private void EmitSnapshot()
        {
            var displayName = _customName ?? ResolveDefaultDisplayName();
            _snapshot.Value = new PlayerNameSnapshot(_customName, displayName);
        }

        private string ResolveDefaultDisplayName()
        {
            string localizedPlayer;
            try
            {
                localizedPlayer = _localizationService.Resolve(
                    new TextTableId(PlayerNameTable),
                    new TextKey(PlayerNameKey));
            }
            catch (InvalidOperationException)
            {
                return PlayerNameDefaults.FallbackDisplayName;
            }

            return string.IsNullOrWhiteSpace(localizedPlayer)
                ? PlayerNameDefaults.FallbackDisplayName
                : localizedPlayer;
        }

        private static string ValidationErrorToMessageKey(PlayerNameValidationError error)
            => error switch
            {
                PlayerNameValidationError.Empty => "Errors.PlayerProfile.NameEmpty",
                PlayerNameValidationError.TooLong => "Errors.PlayerProfile.NameTooLong",
                PlayerNameValidationError.InvalidCharacters => "Errors.PlayerProfile.NameInvalidChars",
                _ => "Errors.PlayerProfile.NameInvalidChars",
            };
    }
}