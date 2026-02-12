using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Rules;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    public sealed class SelfPlayWindow : EditorWindow
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
        private readonly List<BotProfile> _profiles = new() { null, null };
        private readonly List<BotSearchSettings> _profileSearchOverrides = new() { null, null };
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
            public SelfPlayReport Report;
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

            _boardSize = EditorGUILayout.IntSlider("Board Size", _boardSize, 3, 10);
            _isUltimate = EditorGUILayout.Toggle("Ultimate Mode", _isUltimate);

            if (_isUltimate)
            {
                EditorGUILayout.HelpBox(
                    "⚠️ Бот пока не поддерживает Ultimate. Результаты будут некорректными.",
                    MessageType.Warning);
            }

            _useWinLengthOverride = EditorGUILayout.Toggle("Override Win Length", _useWinLengthOverride);
            if (_useWinLengthOverride)
                _winLengthOverride = EditorGUILayout.IntSlider("Win Length", _winLengthOverride, 3, _boardSize);
        }

        private void DrawProfileSlots()
        {
            EditorGUILayout.LabelField("Bot Profiles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Перетащите BotProfile ассеты. Для каждого можно опционально назначить override общих search-настроек. " +
                "При 3+ профилях — round-robin (каждый с каждым).",
                MessageType.None);

            _defaultSearchSettings = (BotSearchSettings)EditorGUILayout.ObjectField(
                "Default Search Settings",
                _defaultSearchSettings,
                typeof(BotSearchSettings),
                false);

            EnsureOverrideSlotsCount();

            for (int i = 0; i < _profiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();

                _profiles[i] = (BotProfile)EditorGUILayout.ObjectField($"Profile {i + 1}", _profiles[i], typeof(BotProfile), false);
                _profileSearchOverrides[i] = (BotSearchSettings)EditorGUILayout.ObjectField(
                    "Search Override",
                    _profileSearchOverrides[i],
                    typeof(BotSearchSettings),
                    false);

                EditorGUILayout.EndVertical();

                GUI.enabled = _profiles.Count > 2;
                if (GUILayout.Button("✕", GUILayout.Width(25)))
                {
                    _profiles.RemoveAt(i);
                    _profileSearchOverrides.RemoveAt(i);
                    i--;
                }
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Profile Slot", GUILayout.Width(150)))
            {
                _profiles.Add(null);
                _profileSearchOverrides.Add(null);
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

        private void DrawMatchupResult(MatchupResult r)
        {
            var report = r.Report;
            int total = report.Player1Wins + report.Player2Wins + report.Draws;

            EditorGUILayout.LabelField(
                $"{r.Profile1Name} vs {r.Profile2Name}",
                EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField(
                $"W/D/L (P1): {report.Player1Wins}/{report.Draws}/{report.Player2Wins}  " +
                $"({Pct(report.Player1Wins, total)}% / {Pct(report.Draws, total)}% / {Pct(report.Player2Wins, total)}%)");

            EditorGUILayout.LabelField(
                $"Avg ms/move: P1={report.AvgMsPerMoveP1:F1}  P2={report.AvgMsPerMoveP2:F1}");

            EditorGUILayout.LabelField(
                $"Tactical misses: " +
                $"P1 win={report.MissedWinP1} block={report.MissedBlockP1}  " +
                $"P2 win={report.MissedWinP2} block={report.MissedBlockP2}");

            EditorGUILayout.LabelField(
                $"Total: {report.TotalMoves} moves, {report.TotalTimeMs:F0}ms");

            EditorGUILayout.EndVertical();
        }

        // ── Run logic ──

        private bool HasValidProfiles()
        {
            int filled = 0;
            foreach (var p in _profiles)
                if (p != null) filled++;
            return filled >= 2;
        }

        private async UniTaskVoid RunAsync()
        {
            _isRunning = true;
            _results.Clear();
            _logText = "";
            _pairProgress = 0f;
            _pairProgressLabel = "Preparing...";
            _matchProgress = 0f;
            _matchProgressLabel = "";
            _moveProgress = 0f;
            _moveProgressLabel = "";
            _cts = new CancellationTokenSource();

            var rules = new ClassicRulesEngine();
            var engine = new MinimaxDecisionEngine(rules, _defaultSearchSettings);
            var winLengthProvider = new ClassicWinLengthProvider();
            var runner = new SelfPlayRunner(engine, rules, winLengthProvider);

            // Collect valid profiles
            var valid = new List<(int index, BotProfile profile, BotSearchSettings overrideSettings)>();
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i] != null)
                    valid.Add((i, _profiles[i], _profileSearchOverrides[i]));
            }

            // Build matchup pairs (round-robin)
            var pairs = new List<(int a, int b)>();
            for (int i = 0; i < valid.Count; i++)
            for (int j = i + 1; j < valid.Count; j++)
                pairs.Add((i, j));

            int totalPairs = pairs.Count;
            var sb = new StringBuilder();

            try
            {
                for (int pairIdx = 0; pairIdx < pairs.Count; pairIdx++)
                {
                    var (a, b) = pairs[pairIdx];
                    var p1 = valid[a].profile;
                    var p2 = valid[b].profile;
                    var p1Override = valid[a].overrideSettings;
                    var p2Override = valid[b].overrideSettings;

                    _pairProgress = totalPairs > 0 ? (float)pairIdx / totalPairs : 0f;
                    _pairProgressLabel = $"Pairs: {pairIdx + 1}/{totalPairs} ({p1.Id} vs {p2.Id})";
                    _matchProgress = 0f;
                    _matchProgressLabel = "Matches: 0/0";
                    _moveProgress = 0f;
                    _moveProgressLabel = "Moves: 0/0";

                    int? winLen = _useWinLengthOverride ? _winLengthOverride : null;

                    var config = new SelfPlayConfig(
                        _boardSize,
                        p1.ToValidatedData(),
                        p2.ToValidatedData(),
                        _matchCount,
                        _baseSeed + pairIdx * 1000,
                        winLen,
                        p1Override != null ? p1Override.ToValidatedData() : null,
                        p2Override != null ? p2Override.ToValidatedData() : null);

                    var report = await runner.RunAsync(config, _cts.Token, progress =>
                    {
                        int totalMatches = Math.Max(progress.TotalMatches, 1);
                        int maxTurns = Math.Max(progress.MaxTurns, 1);
                        int currentMatch = Mathf.Clamp(progress.MatchIndex + 1, 1, totalMatches);
                        int currentTurn = Mathf.Clamp(progress.TurnIndex + 1, 1, maxTurns);

                        _matchProgress = (float)progress.MatchIndex / totalMatches;
                        _matchProgressLabel = $"Matches: {currentMatch}/{totalMatches}";

                        _moveProgress = (float)progress.TurnIndex / maxTurns;
                        _moveProgressLabel = $"Moves: {currentTurn}/{maxTurns}";
                    });

                    _matchProgress = 1f;
                    _moveProgress = 1f;

                    _results.Add(new MatchupResult
                    {
                        Profile1Name = p1.Id,
                        Profile2Name = p2.Id,
                        Report = report,
                    });

                    sb.AppendLine($"[{p1.Id} vs {p2.Id}] P1 wins={report.Player1Wins}, " +
                                  $"P2 wins={report.Player2Wins}, Draws={report.Draws}, " +
                                  $"Time={report.TotalTimeMs:F0}ms");
                }

                _pairProgress = 1f;
                _pairProgressLabel = "Done";
                _matchProgress = 1f;
                _matchProgressLabel = "Matches: done";
                _moveProgress = 1f;
                _moveProgressLabel = "Moves: done";
                sb.AppendLine($"\nAll {totalPairs} matchups complete.");
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine("Cancelled.");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                _logText = sb.ToString();
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
                Repaint();
            }
        }

        // ── Export ──

        private void ExportResults()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Self-Play Report",
                "", $"SelfPlay_{DateTime.Now:yyyyMMdd_HHmmss}.txt", "txt");

            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== Self-Play Report ===");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Board: {_boardSize}x{_boardSize}, Ultimate={_isUltimate}");
            if (_useWinLengthOverride) sb.AppendLine($"Win Length Override: {_winLengthOverride}");
            sb.AppendLine($"Matches per pair: {_matchCount}");
            sb.AppendLine($"Base Seed: {_baseSeed}");
            sb.AppendLine();

            foreach (var r in _results)
            {
                var rpt = r.Report;
                int total = rpt.Player1Wins + rpt.Player2Wins + rpt.Draws;

                sb.AppendLine($"--- {r.Profile1Name} vs {r.Profile2Name} ---");
                sb.AppendLine($"  P1 Wins: {rpt.Player1Wins} ({Pct(rpt.Player1Wins, total)}%)");
                sb.AppendLine($"  P2 Wins: {rpt.Player2Wins} ({Pct(rpt.Player2Wins, total)}%)");
                sb.AppendLine($"  Draws:   {rpt.Draws} ({Pct(rpt.Draws, total)}%)");
                sb.AppendLine($"  Avg ms/move: P1={rpt.AvgMsPerMoveP1:F2}, P2={rpt.AvgMsPerMoveP2:F2}");
                sb.AppendLine($"  Tactical misses P1: win={rpt.MissedWinP1}, block={rpt.MissedBlockP1}");
                sb.AppendLine($"  Tactical misses P2: win={rpt.MissedWinP2}, block={rpt.MissedBlockP2}");
                sb.AppendLine($"  Total: {rpt.TotalMoves} moves, {rpt.TotalTimeMs:F0}ms");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(_logText))
            {
                sb.AppendLine("--- Log ---");
                sb.AppendLine(_logText);
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[SelfPlay] Report exported to: {path}");
        }

        private static int Pct(int count, int total) =>
            total > 0 ? Mathf.RoundToInt((float)count / total * 100) : 0;

        private void EnsureOverrideSlotsCount()
        {
            while (_profileSearchOverrides.Count < _profiles.Count)
                _profileSearchOverrides.Add(null);

            while (_profileSearchOverrides.Count > _profiles.Count)
                _profileSearchOverrides.RemoveAt(_profileSearchOverrides.Count - 1);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
