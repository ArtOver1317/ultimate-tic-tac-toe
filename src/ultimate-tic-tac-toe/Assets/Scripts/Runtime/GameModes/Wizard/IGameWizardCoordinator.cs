#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Coordinator-driven wizard for selecting game mode and opponent settings.
    /// Owns intent serialization and session lifetime.
    /// </summary>
    public interface IGameWizardCoordinator
    {
        /// <summary>
        /// Starts the wizard flow (typically from MainMenu).
        /// Idempotent: calling it while active is a no-op.
        /// </summary>
        UniTask StartWizardAsync(CancellationToken ct);

        /// <summary>
        /// Aborts the wizard flow and performs full cleanup.
        /// Must be safe to call multiple times.
        /// </summary>
        UniTask AbortWizardAsync(AbortReason reason);

        /// <summary>
        /// Publish an intent.
        /// During busy state, all intents except <see cref="WizardIntent.Cancel"/> are rejected and this method returns false.
        /// Returns true only if the intent was accepted for processing.
        /// </summary>
        bool TryPublishIntent(WizardIntent intent);

        /// <summary>
        /// Current coordinator error, if any.
        /// </summary>
        ReadOnlyReactiveProperty<WizardError?> CurrentError { get; }

        /// <summary>
        /// Fired when the wizard builds a valid <see cref="GameLaunchConfig"/> and requests game start.
        /// </summary>
        Observable<GameLaunchConfig> GameLaunchRequested { get; }

        /// <summary>
        /// Completes current start attempt.
        /// On success wizard is closed with <see cref="AbortReason.GameStarted"/>;
        /// on failure wizard remains active and displays provided error.
        /// </summary>
        void CompleteStartAttempt(bool succeeded, WizardError? error = null);

        /// <summary>
        /// Cancels current start attempt without publishing an error.
        /// Keeps the wizard active and re-enables interactions.
        /// </summary>
        void CancelStartAttempt();

        /// <summary>
        /// Fired when the wizard is aborted for any reason.
        /// </summary>
        Observable<AbortReason> WizardAborted { get; }

        /// <summary>
        /// Clears the current error after user acknowledgement.
        /// </summary>
        void ClearCurrentError();

        /// <summary>
        /// True while coordinator is transitioning between windows.
        /// </summary>
        ReadOnlyReactiveProperty<bool> IsTransitioning { get; }

        /// <summary>
        /// True while coordinator is performing a submit/start operation.
        /// </summary>
        ReadOnlyReactiveProperty<bool> IsSubmitting { get; }

        /// <summary>
        /// True while wizard is active (a session exists).
        /// Intended for view-models to avoid using exceptions as control flow.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Tries to get the current wizard session.
        /// Returns true only when wizard is active.
        /// During abort, returns false even if the session object has not been cleared yet.
        /// </summary>
        bool TryGetSession([NotNullWhen(true)] out IGameSession? session);

        /// <summary>
        /// Current wizard session.
        /// Throws when wizard is not active.
        /// </summary>
        IGameSession Session { get; }
    }
}
