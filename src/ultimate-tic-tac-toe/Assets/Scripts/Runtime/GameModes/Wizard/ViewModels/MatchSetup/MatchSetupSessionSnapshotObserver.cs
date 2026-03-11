#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupSessionSnapshotObserver : Observer<GameSessionSnapshot>
    {
        private readonly Action<GameSessionSnapshot> _onNext;

        public MatchSetupSessionSnapshotObserver(Action<GameSessionSnapshot> onNext) =>
            _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));

        protected override void OnNextCore(GameSessionSnapshot? value)
        {
            if (value == null)
                return;

            _onNext(value);
        }

        protected override void OnErrorResumeCore(Exception error)
        {
            if (error is ObjectDisposedException)
                return;

            GameLog.Error($"[MatchSetupViewModel] Session snapshot error: {error}");
        }

        protected override void OnCompletedCore(Result result) { }
    }
}