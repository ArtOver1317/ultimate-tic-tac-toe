using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard.Session
{
    [TestFixture]
    [Category("Unit")]
    public partial class GameSessionTests
    {
        private GameSession _sut;
        private IGameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = CreateCatalog();
            _sut = new GameSession(_catalog);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        private static IGameCatalog CreateCatalog() =>
            new GameCatalog(new IGameStrategy[]
            {
                new TicTacToeStrategy(() => new TicTacToeSettingsViewModel()),
            });
    }
}