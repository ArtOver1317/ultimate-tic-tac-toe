#nullable enable

namespace Runtime.Gameplay
{
    public interface ITimeSource
    {
        float DeltaTime { get; }
    }

    public sealed class UnscaledDeltaTimeSource : ITimeSource
    {
        public float DeltaTime => UnityEngine.Time.unscaledDeltaTime;
    }

    public sealed class FusionTickTimeSource : ITimeSource
    {
        private Fusion.NetworkRunner? _runner;
        private float _nextLookupAtRealtime;

        public float DeltaTime
        {
            get
            {
                if (_runner == null || !_runner.IsRunning)
                {
                    var now = UnityEngine.Time.realtimeSinceStartup;
                    
                    if (now >= _nextLookupAtRealtime)
                    {
                        _runner = UnityEngine.Object.FindFirstObjectByType<Fusion.NetworkRunner>();
                        _nextLookupAtRealtime = now + 1f;
                    }
                }

                return _runner != null && _runner.IsRunning
                    ? _runner.DeltaTime
                    : UnityEngine.Time.unscaledDeltaTime;
            }
        }
    }
}