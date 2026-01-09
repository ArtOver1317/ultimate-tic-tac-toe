using System.Collections.Generic;
using R3;
using Runtime.Localization;
using Runtime.UI.Core;

namespace Runtime.UI.Examples
{
    /// <summary>
    /// ⚠️ ПРИМЕР ПАТТЕРНОВ ЛОКАЛИЗАЦИИ - НЕ PRODUCTION ШАБЛОН ⚠️
    /// 
    /// Этот класс демонстрирует корректное использование ILocalizationService.Observe с proper disposal,
    /// НО содержит неоптимальные решения для hot path:
    /// - Dictionary создаётся на каждое изменение реактивного свойства (GC allocation)
    /// - CombineLatest создаёт дополнительные аллокации при объединении стримов
    /// 
    /// ДЛЯ PRODUCTION HOT PATH используйте:
    /// - ObjectPool&lt;Dictionary&gt; для переиспользования словарей
    /// - Struct-based args вместо Dictionary где возможно
    /// - Кэширование форматированных строк при необходимости
    /// 
    /// Этот код корректен для UI (меню, настройки), где производительность не критична.
    /// </summary>
    public sealed class LocalizedExampleViewModel : BaseViewModel
    {
        private readonly ILocalizationService _localization;
        private readonly ReactiveProperty<int> _currentScore = new(0);
        private readonly ReactiveProperty<string> _currentPlayerName = new("Player 1");

        public Observable<string> Title { get; }
        public Observable<string> PlayButton { get; }
        public Observable<string> ScoreText { get; }
        public Observable<string> PlayerTurnText { get; }
        public ReadOnlyReactiveProperty<int> CurrentScore => _currentScore;
        public ReadOnlyReactiveProperty<string> CurrentPlayerName => _currentPlayerName;

        public LocalizedExampleViewModel(ILocalizationService localization)
        {
            _localization = localization ?? throw new System.ArgumentNullException(nameof(localization));

            // ✅ Простая локализация без аргументов
            Title = _localization.Observe(TextTableId.MainMenu, "MainMenu.Title");

            // ✅ Простая локализация без аргументов (альтернативный синтаксис)
            PlayButton = _localization.Observe(TextTableId.MainMenu, new TextKey("MainMenu.Play"));

            // ✅ Локализация с реактивными аргументами
            // Текст обновляется при смене локали ИЛИ при изменении CurrentScore
            var scoreArgsObservable = _currentScore
                .Select(score => new Dictionary<string, object> { { "score", score } } as IReadOnlyDictionary<string, object>);

            ScoreText = _localization.Observe(TextTableId.Gameplay, new TextKey("Game.Score"), scoreArgsObservable);

            // ✅ Комбинирование нескольких реактивных параметров
            var playerTurnArgs = Observable.CombineLatest(
                _currentPlayerName,
                _currentScore,
                (playerName, score) => new Dictionary<string, object>
                {
                    { "playerName", playerName },
                    { "score", score }
                } as IReadOnlyDictionary<string, object>
            );

            // Use existing key from content: Game.OpponentTurn
            PlayerTurnText = _localization.Observe(TextTableId.Gameplay, new TextKey("Game.OpponentTurn"), playerTurnArgs);
        }

        public void UpdateScore(int newScore) => _currentScore.Value = newScore;

        public void UpdatePlayerName(string playerName) => _currentPlayerName.Value = playerName;

        /// <summary>
        /// Пример синхронного резолва (использовать только для non-UI логики).
        /// 
        /// Note: errorKey is resolved as-is inside the "Errors" table.
        /// Example: "Network.ConnectionFailed" resolves to key "Network.ConnectionFailed" in table "Errors".
        /// </summary>
        public string GetLocalizedErrorMessage(string errorKey) =>
            _localization.Resolve(
                new TextTableId("Errors"),
                new TextKey(errorKey));

        protected override void OnDispose()
        {
            _currentScore?.Dispose();
            _currentPlayerName?.Dispose();
        }
    }
}
