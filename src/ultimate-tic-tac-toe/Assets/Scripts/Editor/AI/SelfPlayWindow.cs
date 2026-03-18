using Runtime.Games.TicTacToe.AI.Profiles;
using UnityEditor;
using UnityEngine;

namespace Editor.AI
{
    public sealed class SelfPlayWindow : EditorWindow
    {
        private SelfPlayWindowState _state;
        private SelfPlayWindowRunner _runner;
        private SelfPlayWindowPresenter _presenter;
        private SelfPlayWindowExporter _exporter;

        [MenuItem("Tools/AI/Self-Play Runner")]
        private static void ShowWindow()
        {
            var window = GetWindow<SelfPlayWindow>("Self-Play Runner");
            window.minSize = new Vector2(550, 600);
            window.Show();
        }

        private void OnEnable() => EnsureInitialized();

        private void OnGUI()
        {
            EnsureInitialized();
            _presenter.Draw();

            if (_state.IsRunning)
                Repaint();
        }

        private void OnDestroy() => _runner?.Dispose();

        private void EnsureInitialized()
        {
            _state ??= new SelfPlayWindowState();
            _runner ??= new SelfPlayWindowRunner(_state, Repaint);
            _exporter ??= new SelfPlayWindowExporter(_state);
            _presenter ??= new SelfPlayWindowPresenter(_state, _runner, _exporter);

            if (_state.DefaultSearchSettings == null)
            {
                _state.DefaultSearchSettings = AssetDatabase.LoadAssetAtPath<BotSearchSettings>(
                    SelfPlayWindowConstants.DefaultSearchSettingsPath);
            }
        }
    }
}