using System;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayWindowProfileSlotsSection
    {
        private readonly SelfPlayWindowState _state;

        public SelfPlayWindowProfileSlotsSection(SelfPlayWindowState state) => _state = state ?? throw new ArgumentNullException(nameof(state));

        public void Draw()
        {
            EditorGUILayout.LabelField("Bot Profiles", EditorStyles.boldLabel);
            DrawProfileSlotsHelpBox();

            if (!_state.IsUltimate)
                DrawDefaultSearchSettingsField();

            for (var slotIndex = 0; slotIndex < _state.ProfileSlots.Count; slotIndex++)
            {
                if (DrawProfileSlotRow(slotIndex))
                    slotIndex--;
            }

            DrawAddProfileSlotButton();
        }

        private void DrawProfileSlotsHelpBox()
        {
            if (_state.IsUltimate)
            {
                EditorGUILayout.HelpBox(
                    "Перетащите UltimateBotProfile ассеты. При 3+ профилях — round-robin (каждый с каждым).",
                    MessageType.None);

                return;
            }

            EditorGUILayout.HelpBox(
                "Перетащите BotProfile ассеты. Для каждого можно опционально назначить override общих search-настроек. " +
                "При 3+ профилях — round-robin (каждый с каждым).",
                MessageType.None);
        }

        private void DrawDefaultSearchSettingsField() =>
            _state.DefaultSearchSettings = (BotSearchSettings)EditorGUILayout.ObjectField(
                "Default Search Settings",
                _state.DefaultSearchSettings,
                typeof(BotSearchSettings),
                false);

        private bool DrawProfileSlotRow(int slotIndex)
        {
            var slot = _state.ProfileSlots[slotIndex];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            DrawProfileSlotFields(slotIndex, slot);
            EditorGUILayout.EndVertical();

            var removed = DrawRemoveProfileSlotButton(slotIndex);
            EditorGUILayout.EndHorizontal();

            return removed;
        }

        private void DrawProfileSlotFields(int slotIndex, ProfileSlot slot)
        {
            if (_state.IsUltimate)
            {
                slot.UltimateProfile = (UltimateBotProfile)EditorGUILayout.ObjectField(
                    $"Profile {slotIndex + 1}",
                    slot.UltimateProfile,
                    typeof(UltimateBotProfile),
                    false);

                return;
            }

            slot.ClassicProfile = (BotProfile)EditorGUILayout.ObjectField(
                $"Profile {slotIndex + 1}",
                slot.ClassicProfile,
                typeof(BotProfile),
                false);

            slot.ClassicSearchOverride = (BotSearchSettings)EditorGUILayout.ObjectField(
                "Search Override",
                slot.ClassicSearchOverride,
                typeof(BotSearchSettings),
                false);
        }

        private bool DrawRemoveProfileSlotButton(int slotIndex)
        {
            using (new EditorGUI.DisabledScope(_state.ProfileSlots.Count <= SelfPlayWindowConstants.MinimumProfileSlotCount))
            {
                if (!GUILayout.Button("✕", GUILayout.Width(25)))
                    return false;
            }

            _state.ProfileSlots.RemoveAt(slotIndex);
            return true;
        }

        private void DrawAddProfileSlotButton()
        {
            if (GUILayout.Button("+ Add Profile Slot", GUILayout.Width(150)))
                _state.ProfileSlots.Add(new ProfileSlot());
        }
    }
}