#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Coordinator
{
    internal sealed class GameWizardIntentFlow
    {
        private readonly GameWizardCoordinatorContext _context;
        private readonly GameWizardAbortFlow _abortFlow;
        private readonly GameWizardLaunchFlow _launchFlow;

        internal GameWizardIntentFlow(
            GameWizardCoordinatorContext context,
            GameWizardAbortFlow abortFlow,
            GameWizardLaunchFlow launchFlow)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _abortFlow = abortFlow ?? throw new ArgumentNullException(nameof(abortFlow));
            _launchFlow = launchFlow ?? throw new ArgumentNullException(nameof(launchFlow));
        }

        internal bool TryPublishIntent(WizardIntent intent)
        {
            _context.EnsureNotDisposed();

            if (intent == WizardIntent.Cancel)
            {
                GameLog.Debug($"[GameWizardCoordinator] Cancel requested.");
                
                _abortFlow.AbortAsync(AbortReason.UserCancel, awaitProcessingTask: !_context.IsInProcessingLoop.Value)
                    .Forget(GameLog.Exception);
                
                return true;
            }

            if (_context.IsBusy)
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent ignored due to busy state: {intent}");
                return false;
            }

            if (!_context.IsReadyForIntents)
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected because wizard is not ready yet: {intent}");
                return false;
            }

            var queue = _context.IntentQueue;
            
            if (queue == null)
                return false;

            if (!_context.TryReserveIntentSlot())
            {
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected due to pending/in-flight intent: {intent}");
                return false;
            }

            if (!queue.TryEnqueue(intent))
            {
                _context.ReleaseIntentSlot();
                GameLog.Debug($"[GameWizardCoordinator] Intent rejected due to pending intent: {intent}");
                return false;
            }

            return true;
        }

        internal async UniTask ProcessAsync(CancellationToken ct)
        {
            var queue = _context.IntentQueue;
            
            if (queue == null)
                return;

            _context.IsInProcessingLoop.Value = true;

            try
            {
                await UniTask.SwitchToMainThread(ct);
                _context.Signals.FlushPendingErrorOnMainThread();

                while (!ct.IsCancellationRequested)
                {
                    WizardIntent intent;

                    try
                    {
                        intent = await queue.DequeueAsync(ct);
                        await UniTask.SwitchToMainThread(ct);
                        _context.Signals.FlushPendingErrorOnMainThread();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        if (_context.IsBusy && intent != WizardIntent.Cancel)
                            continue;

                        switch (intent)
                        {
                            case WizardIntent.Continue:
                            case WizardIntent.Back:
                            case WizardIntent.Start:
                                await HandleNonCancelIntentAsync(intent, ct);
                                break;

                            case WizardIntent.Cancel:
                                break;

                            default:
                                throw new ArgumentOutOfRangeException(nameof(intent), intent, null);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _context.Signals.TrySetCurrentError(WizardError.FromException(ex));
                        GameLog.Exception(ex);
                        await _abortFlow.AbortAsync(AbortReason.Error, awaitProcessingTask: false);
                        break;
                    }
                    finally
                    {
                        _context.ReleaseIntentSlot();
                    }
                }
            }
            finally
            {
                _context.IsInProcessingLoop.Value = false;
            }
        }

        private async UniTask HandleNonCancelIntentAsync(WizardIntent intent, CancellationToken ct)
        {
            await UniTask.SwitchToMainThread(ct);
            _context.Signals.FlushPendingErrorOnMainThread();

            if (_context.IsBusy)
                return;

            switch (intent)
            {
                case WizardIntent.Continue:
                    await ContinueFromModeSelectionAsync(ct);
                    return;

                case WizardIntent.Back:
                    await ReturnToModeSelectionAsync(ct);
                    return;

                case WizardIntent.Start:
                    if (_context.Step != GameWizardCoordinatorContext.WizardStep.MatchSetup)
                        return;

                    await _launchFlow.HandleStartIntentAsync(ct);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent, null);
            }
        }

        private async UniTask ContinueFromModeSelectionAsync(CancellationToken ct)
        {
            if (_context.Step != GameWizardCoordinatorContext.WizardStep.ModeSelection)
                return;

            await _context.TransitionAsync(
                transition: _context.Navigator.ReplaceModeSelectionWithMatchSetupAsync,
                ct: ct);

            _context.Step = GameWizardCoordinatorContext.WizardStep.MatchSetup;
        }

        private async UniTask ReturnToModeSelectionAsync(CancellationToken ct)
        {
            if (_context.Step != GameWizardCoordinatorContext.WizardStep.MatchSetup)
                return;

            await _context.OnlineSessionFlow.BackAsync();

            await _context.TransitionAsync(
                transition: _context.Navigator.ReplaceMatchSetupWithModeSelectionAsync,
                ct: ct);

            _context.Step = GameWizardCoordinatorContext.WizardStep.ModeSelection;
        }
    }
}