#nullable enable

using UnityEngine;

namespace Runtime.Games.Battleship.Core
{
    public readonly struct BattleshipGameplaySettingsData
    {
        public float BotShotDelaySeconds { get; }

        public BattleshipGameplaySettingsData(float botShotDelaySeconds) =>
            BotShotDelaySeconds = botShotDelaySeconds;

        public static BattleshipGameplaySettingsData Default =>
            new(botShotDelaySeconds: 0.35f);
    }

    [CreateAssetMenu(fileName = "BattleshipGameplaySettings", menuName = "Battleship/Gameplay Settings")]
    public sealed class BattleshipGameplaySettings : ScriptableObject
    {
        [Header("Bot")]
        [SerializeField]
        [Range(0f, 2f)]
        private float BotShotDelaySeconds = 0.35f;

        public BattleshipGameplaySettingsData ToValidatedData()
        {
            var shotDelay = ClampWarn(BotShotDelaySeconds, 0f, 5f, nameof(BotShotDelaySeconds));
            return new BattleshipGameplaySettingsData(shotDelay);
        }

        public static BattleshipGameplaySettings CreateRuntimeDefault(float botShotDelaySeconds = 0.35f)
        {
            var settings = CreateInstance<BattleshipGameplaySettings>();
            settings.BotShotDelaySeconds = botShotDelaySeconds;
            return settings;
        }

        private static float ClampWarn(float value, float min, float max, string fieldName)
        {
            if (value >= min && value <= max)
                return value;

            Debug.LogWarning($"[BattleshipGameplaySettings] {fieldName}={value} out of range [{min}..{max}], clamped.");
            return Mathf.Clamp(value, min, max);
        }
    }
}
