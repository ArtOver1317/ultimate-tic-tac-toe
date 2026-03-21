using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels.MatchSetup;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class MatchSetupViewModelDifficultyTests : MatchSetupViewModelTestsBase
    {
        [Test]
        public void WhenSetBotDifficultyIdCalled_ThenWritesThroughToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            sut.SetBotDifficultyId("Hard");

            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().Be("Hard");
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
        }

        [Test]
        public void WhenSetBotDifficultyIdCalledWithSameValue_ThenDoesNotCallSessionUpdate()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Easy");
            var callsBefore = session.UpdateCallCount;

            sut.SetBotDifficultyId("Easy");

            session.UpdateCallCount.Should().Be(callsBefore);
        }

        [Test]
        public void WhenSetBotDifficultyIdCalledWithUnknownId_ThenNormalizesToNull()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Easy");

            sut.SetBotDifficultyId("Unknown");

            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenSessionBotDifficultyIdChanges_ThenSelectedDifficultyIdUpdatesWithoutWriteBack()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithBotDifficultyId("Hard")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public void WhenSessionBotDifficultyIdIsUnknownAndOpponentIsBot_ThenVMSanitizesSessionByWritingBackNull()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithBotDifficultyId("UnknownDifficulty")
                .WithOpponentType(OpponentType.Bot)
                .WithVersion(1));

            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.UpdateCallCount.Should().Be(1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChangeAndSelectedIdIsNoLongerAvailable_ThenSelectionClearsAndWritesThroughToSession()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");
            var callsBefore = session.UpdateCallCount;
            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");

            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameWizard.MatchSetup.BotDifficulty.Normal", 1),
            }));

            await WaitForSelectedDifficultyAsync(sut, value => value == null);

            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
            session.UpdateCallCount.Should().Be(callsBefore + 1);
            session.Snapshot.CurrentValue.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenOpponentTypeTogglesBotToHumanToBot_ThenSelectedDifficultyIdIsPreservedAndUIRestoresSelection()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            sut.SetOpponentType(OpponentType.Human);
            sut.SetOpponentType(OpponentType.Bot);

            sut.SelectedDifficultyId.CurrentValue.Should().Be("Hard");
        }

        [Test]
        public void WhenResetCalled_ThenSelectedDifficultyIdIsCleared()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();
            sut.SetBotDifficultyId("Hard");

            sut.Reset();

            sut.SelectedDifficultyId.CurrentValue.Should().BeNull();
        }

        [Test]
        public void WhenSessionContainsUnsupportedMoveTimeLimit_ThenMoveTimerFallsBackToZero()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default.WithVersion(1));
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithMoveTimeLimitSeconds(17)
                .WithVersion(2));

            sut.MoveTimerSettings.MoveTimeLimitSeconds.CurrentValue.Should().Be(0);
            sut.MoveTimerSettings.SelectedPresetId.CurrentValue.Should().Be("0");
        }

        [Test]
        public void WhenSessionStartsWithSupportedMoveTimeLimit_ThenMoveTimerPreservedWithoutWriteBack()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default
                .WithMoveTimeLimitSeconds(30)
                .WithVersion(1));
            
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();

            sut.Initialize();

            sut.MoveTimerSettings.MoveTimeLimitSeconds.CurrentValue.Should().Be(30);
            sut.MoveTimerSettings.SelectedPresetId.CurrentValue.Should().Be("30");
            session.UpdateCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenResetCalled_ThenDifficultyLocalizationSubscriptionsAreDisposed()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var localization = Substitute.For<ILocalizationService>();
            
            localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            
            localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            using var easySubject = new Subject<string>();
            
            localization
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
            }));

            using var sut = new MatchSetupViewModel(Catalog, Coordinator, localization, difficultyCatalog);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Id == "Easy"
                && items[0].Label == "Easy");

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            sut.Reset();

            easySubject.OnNext("Easy+2");

            await WaitForDifficultyItemsAsync(sut, items => items.Count == 0);

            sut.DifficultyItems.CurrentValue.Should().BeEmpty();
        }

        [Test]
        public async Task WhenAvailableDifficultiesChanges_ThenDifficultyItemsAreRebuilt()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            using var sut = CreateSut();
            sut.Initialize();

            SetAvailableDifficulties(sut, Array.AsReadOnly(new[]
            {
                new BotDifficulty("A", "Key.A", 0),
                new BotDifficulty("B", "Key.B", 1),
            }));

            await WaitForDifficultyItemsAsync(sut, items => items.Count == 2);

            sut.DifficultyItems.CurrentValue.Should().HaveCount(2);
            sut.DifficultyItems.CurrentValue[0].Id.Should().Be("A");
            sut.DifficultyItems.CurrentValue[1].Id.Should().Be("B");
        }

        [Test]
        public async Task WhenLocalizationEmitsNewLabel_ThenDifficultyItemsAreUpdated()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);

            var localization = Substitute.For<ILocalizationService>();
           
            localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            
            localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            using var easySubject = new Subject<string>();
           
            localization
                .Observe(Arg.Is<TextTableId>(t => t.Name == "GameWizard"),
                    Arg.Is<TextKey>(k => k.Value == "GameWizard.MatchSetup.BotDifficulty.Easy"),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(easySubject);

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            
            difficultyCatalog.Difficulties.Returns(Array.AsReadOnly(new[]
            {
                new BotDifficulty("Easy", "GameWizard.MatchSetup.BotDifficulty.Easy", 0),
            }));

            using var sut = new MatchSetupViewModel(Catalog, Coordinator, localization, difficultyCatalog);
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            easySubject.OnNext("Easy");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Label == "Easy");

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "Easy");

            easySubject.OnNext("˸����");

            await WaitForDifficultyItemsAsync(sut, items =>
                items.Count == 1
                && items[0].Label == "˸����");

            sut.DifficultyItems.CurrentValue.Should().ContainSingle(item => item.Id == "Easy" && item.Label == "˸����");
        }

        [Test]
        public void WhenBattleshipSelectedInBotModeAndDifficultyMissing_ThenSelectsDefaultDifficultyAndHidesBotDifficultySection()
        {
            var session = new FakeGameSession(GameSessionSnapshot.Default);
            SetupCoordinatorWithSession(session);
            SetupStrategy(BattleshipStrategy.DefaultGameId, CreateBattleshipStrategy());

            using var sut = CreateSut();
            sut.DisablePlayerLoopForTests();
            sut.Initialize();

            session.EmitSnapshot(GameSessionSnapshot.Default
                .WithSelectedGameId(BattleshipStrategy.DefaultGameId)
                .WithGameConfig(new BattleshipConfig(30))
                .WithOpponentType(OpponentType.Bot)
                .WithBotDifficultyId(null)
                .WithVersion(1));

            sut.SelectedDifficultyId.CurrentValue.Should().Be(BattleshipStrategy.DefaultBotDifficultyId);
            sut.IsBotSettingsVisible.CurrentValue.Should().BeFalse();
        }
    }
}