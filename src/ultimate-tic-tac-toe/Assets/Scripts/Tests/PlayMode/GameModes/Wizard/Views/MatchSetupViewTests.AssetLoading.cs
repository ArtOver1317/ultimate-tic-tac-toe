using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Session;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard.Views
{
    public partial class MatchSetupViewTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsBecomesNull_ThenClearsModeOptionsAndDisposesLease() => UniTask.ToCoroutine(async () =>
        {
            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId(null).WithVersion(2));
            await UniTask.Yield();

            _assetProvider.LastLeaseDisposed.Should().BeTrue();
            GetModeOptionsHost().childCount.Should().Be(0);
            _binder.DisposedCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsChangesTwiceQuickly_ThenOnlyLatestSettingsAreApplied() => UniTask.ToCoroutine(async () =>
        {
            _assetProvider.SetDelay("ui/mode-settings/classic", TimeSpan.FromMilliseconds(200));
            _assetProvider.SetDelay("ui/mode-settings/ultimate", TimeSpan.Zero);
            _assetProvider.SetIgnoreCancellation("ui/mode-settings/classic", true);

            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("ultimate").WithVersion(2));

            await _assetProvider.WaitForLastLoadAsync();
            
            await WaitUntilAsync(
                () => _assetProvider.DisposedLeases.ContainsKey("ui/mode-settings/classic"),
                timeoutMs: 1000);

            GetModeOptionsHost().Q<Label>("InfoLabel").Should().NotBeNull();
            _assetProvider.DisposedLeases.Should().ContainKey("ui/mode-settings/classic");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenCancelsPendingLoadAndCleansUp() => UniTask.ToCoroutine(async () =>
        {
            _assetProvider.SetDelay("ui/mode-settings/classic", TimeSpan.FromMilliseconds(500));

            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            await WaitUntilAsync(() => _assetProvider.IsLoadInFlight, timeoutMs: 1000);

            _view.ResetForPool();
            await WaitUntilAsync(() => _assetProvider.WasLastLoadCancelled, timeoutMs: 1000);

            GetModeOptionsHost().childCount.Should().Be(0);
            _assetProvider.WasLastLoadCancelled.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsIsNotNullAndNoBinderCanBind_ThenDoesNotThrowAndStillShowsLoadedUxml() => UniTask.ToCoroutine(async () =>
        {
            var noBinderView = CreateViewWithBinders(Array.Empty<IGameSettingsBinder>());
            var localSession = new FakeGameSession(GameSessionSnapshot.Default);
            var localCoordinator = CreateCoordinator(localSession);
            var viewModel = CreateViewModelWithCatalog(localCoordinator);

            noBinderView.SetViewModel(viewModel);
            noBinderView.RebindUxmlForTests();
            noBinderView.Show();

            localSession.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            var host = noBinderView.RootForTests.Q<ModeOptionsHost>("ModeOptionsHost");
            host.childCount.Should().BeGreaterThan(0);

            Object.Destroy(noBinderView.gameObject);
            viewModel.Dispose();
            localSession.Dispose();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenAssetProviderThrows_ThenDoesNotLeakPreviousLeaseAndHostIsEmpty() => UniTask.ToCoroutine(async () =>
        {
            _assetProvider.SetThrow("ui/mode-settings/ultimate", new InvalidOperationException("boom"));

            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            await WaitUntilAsync(() => GetModeOptionsHost().Q<Label>("BoardSizeValue") != null, timeoutMs: 1000);

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));

            _session.EmitSnapshot(GameSessionSnapshot.Default.WithSelectedGameId("ultimate").WithVersion(2));
            await _assetProvider.WaitForLastLoadAsync();

            _assetProvider.DisposedLeases.Should().ContainKey("ui/mode-settings/classic");
            GetModeOptionsHost().childCount.Should().Be(0);
        });
    }
}
