using System;
using System.Collections.Generic;
using System.Threading;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Ultimate;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    public sealed partial class SelfPlayWindow : EditorWindow
    {
        private const string DefaultSearchSettingsPath = "Assets/Content/AI/BotSearchSettings_Default.asset";

        // ── Config fields ──
        private int _boardSize = 3;
        private bool _isUltimate;
        private int _matchCount = 20;
        private int _baseSeed = 42;
        private int _winLengthOverride;
        private bool _useWinLengthOverride;

        // ── Profile slots ──
        private readonly List<BotProfile> _classicProfiles = new() { null, null };
        private readonly List<UltimateBotProfile> _ultimateProfiles = new() { null, null };
        private readonly List<BotSearchSettings> _classicProfileSearchOverrides = new() { null, null };
        private BotSearchSettings _defaultSearchSettings;
        private Vector2 _scrollPosition;
        private Vector2 _resultsScroll;

        // ── Run state ──
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private float _pairProgress;
        private string _pairProgressLabel = "";
        private float _matchProgress;
        private string _matchProgressLabel = "";
        private float _moveProgress;
        private string _moveProgressLabel = "";

        // ── Results ──
        private readonly List<MatchupResult> _results = new();
        private string _logText = "";

        private struct MatchupResult
        {
            public string Profile1Name;
            public string Profile2Name;
            public SelfPlayReport? ClassicReport;
            public SelfPlaySeriesReport? UltimateReport;
        }

        [MenuItem("Tools/AI/Self-Play Runner")]
        private static void ShowWindow()
        {
            var window = GetWindow<SelfPlayWindow>("Self-Play Runner");
            window.minSize = new Vector2(550, 600);
            window.Show();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            EditorGUILayout.Space(8);
            DrawGameSettings();
            EditorGUILayout.Space(8);
            DrawProfileSlots();
            EditorGUILayout.Space(8);
            DrawRunSettings();
            EditorGUILayout.Space(8);
            DrawRunButton();
            EditorGUILayout.Space(8);
            DrawResults();

            EditorGUILayout.EndScrollView();

            if (_isRunning)
                Repaint();
        }

        private void OnEnable()
        {
            if (_defaultSearchSettings == null)
                _defaultSearchSettings = AssetDatabase.LoadAssetAtPath<BotSearchSettings>(DefaultSearchSettingsPath);

            EnsureOverrideSlotsCount();
        }

        // ── Sections ──

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

            _isUltimate = EditorGUILayout.Toggle("Ultimate Mode", _isUltimate);

            if (_isUltimate)
            {
                _useWinLengthOverride = false;
                EditorGUILayout.HelpBox(
                    "Ultimate self-play использует фиксированное поле 3x3x3. Board Size и Win Length недоступны.",
                    MessageType.Info);
                return;
            }

            _boardSize = EditorGUILayout.IntSlider("Board Size", _boardSize, 3, 10);

            _useWinLengthOverride = EditorGUILayout.Toggle("Override Win Length", _useWinLengthOverride);
            if (_useWinLengthOverride)
                _winLengthOverride = EditorGUILayout.IntSlider("Win Length", _winLengthOverride, 3, _boardSize);
        }

        private void DrawProfileSlots()
        {
            EditorGUILayout.LabelField("Bot Profiles", EditorStyles.boldLabel);
            if (_isUltimate)
            {
                EditorGUILayout.HelpBox(
                    "Перетащите UltimateBotProfile ассеты. При 3+ профилях — round-robin (каждый с каждым).",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Перетащите BotProfile ассеты. Для каждого можно опционально назначить override общих search-настроек. " +
                    "При 3+ профилях — round-robin (каждый с каждым).",
                    MessageType.None);
            }

            if (!_isUltimate)
            {
                _defaultSearchSettings = (BotSearchSettings)EditorGUILayout.ObjectField(
                    "Default Search Settings",
                    _defaultSearchSettings,
                    typeof(BotSearchSettings),
                    false);
            }

            EnsureOverrideSlotsCount();

            for (var i = 0; i < _classicProfiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();

                if (_isUltimate)
                {
                    _ultimateProfiles[i] = (UltimateBotProfile)EditorGUILayout.ObjectField(
                        $"Profile {i + 1}",
                        _ultimateProfiles[i],
                        typeof(UltimateBotProfile),
                        false);
                }
                else
                {
                    _classicProfiles[i] = (BotProfile)EditorGUILayout.ObjectField(
                        $"Profile {i + 1}",
                        _classicProfiles[i],
                        typeof(BotProfile),
                        false);
                    _classicProfileSearchOverrides[i] = (BotSearchSettings)EditorGUILayout.ObjectField(
                        "Search Override",
                        _classicProfileSearchOverrides[i],
                        typeof(BotSearchSettings),
                        false);
                }

                EditorGUILayout.EndVertical();

                GUI.enabled = _classicProfiles.Count > 2;
                if (GUILayout.Button("✕", GUILayout.Width(25)))
                {
                    _classicProfiles.RemoveAt(i);
                    _ultimateProfiles.RemoveAt(i);
                    _classicProfileSearchOverrides.RemoveAt(i);
                    i--;
                }
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Profile Slot", GUILayout.Width(150)))
            {
                _classicProfiles.Add(null);
                _ultimateProfiles.Add(null);
                _classicProfileSearchOverrides.Add(null);
            }
        }

        private void DrawRunSettings()
        {
            EditorGUILayout.LabelField("Run Settings", EditorStyles.boldLabel);
            _matchCount = EditorGUILayout.IntField("Matches per Pair", _matchCount);
            _matchCount = Mathf.Clamp(_matchCount, 1, 10000);
            _baseSeed = EditorGUILayout.IntField("Base Seed", _baseSeed);
        }

        private void DrawRunButton()
        {
            if (_isRunning)
            {
                EditorGUILayout.BeginHorizontal();
                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, _pairProgress, _pairProgressLabel);
                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    _cts?.Cancel();
                }
                EditorGUILayout.EndHorizontal();

                var matchRect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(matchRect, _matchProgress, _matchProgressLabel);

                var moveRect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(moveRect, _moveProgress, _moveProgressLabel);
            }
            else
            {
                GUI.enabled = HasValidProfiles();
                if (GUILayout.Button("▶ Run Self-Play", GUILayout.Height(30)))
                    RunAsync().Forget();
                GUI.enabled = true;

                if (!HasValidProfiles())
                    EditorGUILayout.HelpBox("Назначьте хотя бы 2 профиля для запуска.", MessageType.Warning);
            }
        }

        private void DrawResults()
        {
            if (_results.Count == 0 && string.IsNullOrEmpty(_logText)) return;

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.MinHeight(200));

            foreach (var r in _results)
            {
                DrawMatchupResult(r);
                EditorGUILayout.Space(4);
            }

            if (!string.IsNullOrEmpty(_logText))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_logText, GUILayout.MinHeight(60));
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Export to File...", GUILayout.Width(150)))
                ExportResults();
        }


        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
