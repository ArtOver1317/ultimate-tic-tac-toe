using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Editor.AI
{
    internal sealed class SelfPlayWindowRunner : IDisposable
    {
        private readonly SelfPlayWindowState _state;
        private readonly Action _repaint;
        private readonly SelfPlayClassicModeRunner _classicModeRunner;
        private readonly SelfPlayUltimateModeRunner _ultimateModeRunner;

        public SelfPlayWindowRunner(SelfPlayWindowState state, Action repaint)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _repaint = repaint ?? throw new ArgumentNullException(nameof(repaint));
            _classicModeRunner = new SelfPlayClassicModeRunner(_state);
            _ultimateModeRunner = new SelfPlayUltimateModeRunner(_state);
        }

        public bool CanRun => CountAssignedProfiles() >= SelfPlayWindowConstants.MinimumProfileSlotCount;

        public void StartRun()
        {
            if (_state.IsRunning || !CanRun)
                return;

            RunAsync().Forget();
        }

        public void Cancel() => _state.CancellationSource?.Cancel();

        public void Dispose()
        {
            _state.CancellationSource?.Cancel();
            _state.CancellationSource?.Dispose();
            _state.CancellationSource = null;
        }

        private int CountAssignedProfiles()
        {
            var filled = 0;

            for (var i = 0; i < _state.ProfileSlots.Count; i++)
            {
                if (!HasAssignedProfile(_state.ProfileSlots[i]))
                    continue;

                filled++;

                if (filled >= SelfPlayWindowConstants.MinimumProfileSlotCount)
                    return filled;
            }

            return filled;
        }

        private bool HasAssignedProfile(ProfileSlot slot) =>
            _state.IsUltimate ? slot.UltimateProfile != null : slot.ClassicProfile != null;

        private async UniTask RunAsync()
        {
            BeginRun();
            var cancellationToken = _state.CancellationSource.Token;

            var logBuilder = new StringBuilder();

            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

                if (_state.IsUltimate)
                    await _ultimateModeRunner.RunAsync(logBuilder, cancellationToken);
                else
                    await _classicModeRunner.RunAsync(logBuilder, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logBuilder.AppendLine("Cancelled.");
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"Error: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                FinishRun(logBuilder);
            }
        }

        private void BeginRun()
        {
            _state.IsRunning = true;
            _state.Results.Clear();
            _state.LogText = string.Empty;
            _state.LastRunSettings = _state.CaptureCurrentRunSettings();
            ResetProgress();
            _state.CancellationSource?.Dispose();
            _state.CancellationSource = new CancellationTokenSource();
            _repaint();
        }

        private void ResetProgress()
        {
            _state.PairProgress = 0f;
            _state.PairProgressLabel = "Preparing...";
            _state.MatchProgress = 0f;
            _state.MatchProgressLabel = string.Empty;
            _state.MoveProgress = 0f;
            _state.MoveProgressLabel = string.Empty;
        }

        private void FinishRun(StringBuilder logBuilder)
        {
            _state.LogText = logBuilder.ToString();
            _state.IsRunning = false;
            _state.CancellationSource?.Dispose();
            _state.CancellationSource = null;
            _repaint();
        }
    }
}