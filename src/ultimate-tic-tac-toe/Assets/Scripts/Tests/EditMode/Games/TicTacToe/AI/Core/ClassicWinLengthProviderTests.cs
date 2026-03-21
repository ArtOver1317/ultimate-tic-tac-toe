#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.Rules;

namespace Tests.EditMode.Games.TicTacToe.AI.Core
{
    [TestFixture]
    [Category("Unit")]
    public class ClassicWinLengthProviderTests
    {
        private readonly ClassicWinLengthProvider _provider = new();

        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(5, 4)]
        [TestCase(6, 5)]
        [TestCase(8, 5)]
        [TestCase(10, 5)]
        public void WhenBoardSize_ThenReturnsExpectedWinLength(int boardSize, int expectedK) => _provider.GetWinLength(boardSize).Should().Be(expectedK);
    }
}
