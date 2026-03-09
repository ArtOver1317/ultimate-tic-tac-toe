using System;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayWindowPresenter
    {
        private readonly SelfPlayWindowState _state;
        private readonly SelfPlayWindowRunner _runner;
        private readonly SelfPlayWindowProfileSlotsSection _profileSlotsSection;
        private readonly SelfPlayWindowResultsSection _resultsSection;

        public SelfPlayWindowPresenter(SelfPlayWindowState state, SelfPlayWindowRunner runner, SelfPlayWindowExporter exporter)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            
            if (exporter == null)
                throw new ArgumentNullException(nameof(exporter));

            _profileSlotsSection = new SelfPlayWindowProfileSlotsSection(_state);
            _resultsSection = new SelfPlayWindowResultsSection(_state, exporter);
        }

        public void Draw()
        {
            _state.ScrollPosition = EditorGUILayout.BeginScrollView(_state.ScrollPosition);

            DrawHeader();
            EditorGUILayout.Space(8);
            DrawGameSettings();
            EditorGUILayout.Space(8);
            _profileSlotsSection.Draw();
            EditorGUILayout.Space(8);
            DrawRunSettings();
            EditorGUILayout.Space(8);
            DrawRunButton();
            EditorGUILayout.Space(8);
            _resultsSection.Draw();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Bot Self-Play Runner", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Прогоняет матчи бот-vs-бот без ECS/UI. " +
                "Каждая пара профилей играет указанное кол-во матчей, " +
                "чередуя стартового игрока. При 3+ профилях — каждый с каждым.",
                MessageType.Info);
        }

        private void DrawGameSettings()
        {
            EditorGUILayout.LabelField("Game Settings", EditorStyles.boldLabel);

            _state.IsUltimate = EditorGUILayout.Toggle("Ultimate Mode", _state.IsUltimate);

            if (_state.IsUltimate)
            {
                _state.UseWinLengthOverride = false;

                EditorGUILayout.HelpBox(
                    "Ultimate self-play использует фиксированное поле 3x3x3. Board Size и Win Length недоступны.",
                    MessageType.Info);

                return;
            }

            _state.BoardSize = EditorGUILayout.IntSlider("Board Size", _state.BoardSize, 3, 10);
            _state.UseWinLengthOverride = EditorGUILayout.Toggle("Override Win Length", _state.UseWinLengthOverride);

            if (_state.UseWinLengthOverride)
            {
                _state.WinLengthOverride = EditorGUILayout.IntSlider(
                    "Win Length",
                    _state.WinLengthOverride,
                    3,
                    _state.BoardSize);
            }
        }

        private void DrawRunSettings()
        {
            EditorGUILayout.LabelField("Run Settings", EditorStyles.boldLabel);
            _state.MatchCount = EditorGUILayout.IntField("Matches per Pair", _state.MatchCount);
            _state.MatchCount = Mathf.Clamp(_state.MatchCount, 1, 10000);
            _state.BaseSeed = EditorGUILayout.IntField("Base Seed", _state.BaseSeed);
        }

        private void DrawRunButton()
        {
            if (_state.IsRunning)
            {
                DrawRunProgress();
                return;
            }

            var canRun = _runner.CanRun;

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("▶ Run Self-Play", GUILayout.Height(30)))
                    _runner.StartRun();
            }

            if (!canRun)
                EditorGUILayout.HelpBox("Назначьте хотя бы 2 профиля для запуска.", MessageType.Warning);
        }

        private void DrawRunProgress()
        {
            EditorGUILayout.BeginHorizontal();
            var pairRect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(pairRect, _state.PairProgress, _state.PairProgressLabel);

            if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                _runner.Cancel();

            EditorGUILayout.EndHorizontal();

            DrawProgressBar(_state.MatchProgress, _state.MatchProgressLabel, 18);
            DrawProgressBar(_state.MoveProgress, _state.MoveProgressLabel, 18);
        }

        private static void DrawProgressBar(float value, string label, float height) =>
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, height), value, label);
    }
}