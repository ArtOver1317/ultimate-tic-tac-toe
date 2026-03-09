#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Runtime.GameModes.Wizard.Configs
{
    [CreateAssetMenu(fileName = "MoveTimerPresetsConfig", menuName = "Game/Wizard/Move Timer Presets")]
    public sealed class MoveTimerPresetsConfig : ScriptableObject
    {
        [SerializeField] private int[] PresetSeconds = { 0, 15, 30, 60, 90 };

        public IReadOnlyList<int> GetPresets() => NormalizePresets(PresetSeconds);

        public static MoveTimerPresetsConfig CreateRuntimeDefault()
        {
            var config = CreateInstance<MoveTimerPresetsConfig>();
            config.PresetSeconds = new[] { 0, 15, 30, 60, 90 };
            return config;
        }

        internal static IReadOnlyList<int> NormalizePresets(IReadOnlyList<int>? values)
        {
            if (values == null || values.Count == 0)
                return new[] { 0 };

            var seen = new HashSet<int>();
            var normalized = new List<int>(values.Count);

            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                
                if (value < 0)
                    continue;

                if (seen.Add(value))
                    normalized.Add(value);
            }

            if (!seen.Contains(0))
                normalized.Insert(0, 0);
            else if (normalized.Count > 0 && normalized[0] != 0)
            {
                normalized.Remove(0);
                normalized.Insert(0, 0);
            }

            if (normalized.Count == 0)
                normalized.Add(0);

            return normalized;
        }
    }
}