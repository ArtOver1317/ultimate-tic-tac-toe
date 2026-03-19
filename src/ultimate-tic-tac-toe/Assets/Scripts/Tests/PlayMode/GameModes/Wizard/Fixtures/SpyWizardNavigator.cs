using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;

namespace Tests.PlayMode.GameModes.Wizard
{
    internal sealed class SpyWizardNavigator : IGameWizardNavigator
    {
        private readonly object _lock = new();

        public Func<CancellationToken, UniTask> OpenModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> CloseModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> OpenMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> CloseMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask<MatchmakingViewModel>> OpenMatchmakingImpl { get; set; } = _ =>
            UniTask.FromException<MatchmakingViewModel>(new InvalidOperationException(
                "SpyWizardNavigator.OpenMatchmakingAsync is not configured."));
        public Func<CancellationToken, UniTask> CloseMatchmakingImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> ReplaceModeSelectionWithMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> ReplaceMatchSetupWithModeSelectionImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask<MatchmakingViewModel>> ReplaceMatchSetupWithMatchmakingImpl { get; set; } = _ =>
            UniTask.FromException<MatchmakingViewModel>(new InvalidOperationException(
                "SpyWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync is not configured."));
        public Func<CancellationToken, UniTask> ReplaceMatchmakingWithMatchSetupImpl { get; set; } = _ => UniTask.CompletedTask;
        public Func<CancellationToken, UniTask> CloseAllImpl { get; set; } = _ => UniTask.CompletedTask;

        public int OpenModeSelectionCalls { get; private set; }
        public int CloseModeSelectionCalls { get; private set; }
        public int OpenMatchSetupCalls { get; private set; }
        public int CloseMatchSetupCalls { get; private set; }
        public int OpenMatchmakingCalls { get; private set; }
        public int CloseMatchmakingCalls { get; private set; }
        public int ReplaceModeSelectionWithMatchSetupCalls { get; private set; }
        public int ReplaceMatchSetupWithModeSelectionCalls { get; private set; }
        public int ReplaceMatchSetupWithMatchmakingCalls { get; private set; }
        public int ReplaceMatchmakingWithMatchSetupCalls { get; private set; }
        public int CloseAllCalls { get; private set; }

        public int TotalCalls { get; private set; }
        public List<string> CallHistory { get; } = new();

        public void ClearHistory()
        {
            lock (_lock)
            {
                CallHistory.Clear();
            }
        }

        public UniTask OpenModeSelectionAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                OpenModeSelectionCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.OpenModeSelectionAsync));
            }

            return OpenModeSelectionImpl(ct);
        }

        public UniTask CloseModeSelectionAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                CloseModeSelectionCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.CloseModeSelectionAsync));
            }

            return CloseModeSelectionImpl(ct);
        }

        public UniTask OpenMatchSetupAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                OpenMatchSetupCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.OpenMatchSetupAsync));
            }

            return OpenMatchSetupImpl(ct);
        }

        public UniTask CloseMatchSetupAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                CloseMatchSetupCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.CloseMatchSetupAsync));
            }

            return CloseMatchSetupImpl(ct);
        }

        public UniTask<MatchmakingViewModel> OpenMatchmakingAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                OpenMatchmakingCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.OpenMatchmakingAsync));
            }

            return OpenMatchmakingImpl(ct);
        }

        public UniTask CloseMatchmakingAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                CloseMatchmakingCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.CloseMatchmakingAsync));
            }

            return CloseMatchmakingImpl(ct);
        }

        public UniTask ReplaceModeSelectionWithMatchSetupAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                ReplaceModeSelectionWithMatchSetupCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.ReplaceModeSelectionWithMatchSetupAsync));
            }

            return ReplaceModeSelectionWithMatchSetupImpl(ct);
        }

        public UniTask ReplaceMatchSetupWithModeSelectionAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                ReplaceMatchSetupWithModeSelectionCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.ReplaceMatchSetupWithModeSelectionAsync));
            }

            return ReplaceMatchSetupWithModeSelectionImpl(ct);
        }

        public UniTask<MatchmakingViewModel> ReplaceMatchSetupWithMatchmakingAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                ReplaceMatchSetupWithMatchmakingCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.ReplaceMatchSetupWithMatchmakingAsync));
            }

            return ReplaceMatchSetupWithMatchmakingImpl(ct);
        }

        public UniTask ReplaceMatchmakingWithMatchSetupAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                ReplaceMatchmakingWithMatchSetupCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.ReplaceMatchmakingWithMatchSetupAsync));
            }

            return ReplaceMatchmakingWithMatchSetupImpl(ct);
        }

        public UniTask CloseAllWizardWindowsAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                CloseAllCalls++;
                TotalCalls++;
                CallHistory.Add(nameof(IGameWizardNavigator.CloseAllWizardWindowsAsync));
            }

            return CloseAllImpl(ct);
        }
    }
}
