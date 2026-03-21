#nullable enable

using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard.Online.Flow
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineSessionIdPolicyTests
    {
        [Test]
        public void WhenNormalizeCalledWithDecoratedInput_ThenReturnsCanonicalUppercase()
        {
            // Arrange
            const string raw = "  ab2-cd7  ";

            // Act
            var canonical = OnlineSessionIdFormatter.Normalize(raw);

            // Assert
            canonical.Should().Be("AB2CD7");
        }

        [Test]
        public void WhenTryNormalizeCalledWithInvalidInput_ThenReturnsFalse()
        {
            // Arrange
            const string raw = "ABCD10";

            // Act
            var ok = OnlineSessionIdFormatter.TryNormalizeToCanonical(raw, out var canonical);

            // Assert
            ok.Should().BeFalse();
            canonical.Should().Be("ABCD10");
        }

        [Test]
        public void WhenResetToIdleAfterCancelledOrFailedFlow_ThenInvalidatesActiveAndRegeneratesCandidate()
        {
            // Arrange
            var candidates = new[] { "ABCDEF", "GHIJKL" };
            var idx = 0;
            var lifecycle = new OnlineSessionIdLifecycle(() => candidates[idx++]);
            lifecycle.EnterHumanSetup();
            lifecycle.ActivateCandidateAfterHostStart();

            // Act
            lifecycle.ResetToIdleAfterCancelledOrFailedFlow();

            // Assert
            lifecycle.ActiveSessionId.Should().BeNull();
            lifecycle.CandidateSessionId.Should().Be("GHIJKL");
        }

        [Test]
        public async Task WhenJoinInputInvalid_ThenReturnsInvalidFormatAndDoesNotCallGateway()
        {
            // Arrange
            var gateway = new SpyPhotonSessionGateway();

            // Act
            var result = await OnlineJoinBySessionId.ExecuteAsync("bad", "eu", "user-1", gateway);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(OnlineErrorCode.InvalidSessionIdFormat);
            gateway.JoinCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenJoinInputValid_ThenCallsGatewayWithCanonicalSessionId()
        {
            // Arrange
            var gateway = new SpyPhotonSessionGateway();

            // Act
            var result = await OnlineJoinBySessionId.ExecuteAsync("ab2-cd7", "eu", "user-1", gateway);

            // Assert
            result.IsSuccess.Should().BeTrue();
            gateway.JoinCallCount.Should().Be(1);
            gateway.LastSessionId.Should().Be("AB2CD7");
        }

        private sealed class SpyPhotonSessionGateway : IPhotonSessionGateway
        {
            private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycle = new(null);

            public int JoinCallCount { get; private set; }
            public string? LastSessionId { get; private set; }

            public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycle;
            public double NetworkTimeSeconds => 0d;

            public UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config) =>
                UniTask.FromResult(GatewayOperationResult.Success());

            public UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
            {
                JoinCallCount++;
                LastSessionId = sessionId.Value;
                return UniTask.FromResult(GatewayOperationResult.Success());
            }

            public UniTask LeaveSessionAsync() => UniTask.CompletedTask;

            public UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId) =>
                UniTask.FromResult(GatewayOperationResult.Success());

            public void Dispose() => _lifecycle.Dispose();
        }
    }
}