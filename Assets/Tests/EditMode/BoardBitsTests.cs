using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class BoardBitsTests
    {
        [Test]
        public void Rows_ReadAsNumbers_LeftmostIs128()
        {
            ulong board = BoardBits.SetRow(0, 2, 0b10000001);
            Assert.That(BoardBits.GetCell(board, 2, 0), Is.True);
            Assert.That(BoardBits.GetCell(board, 2, 7), Is.True);
            Assert.That(BoardBits.GetCell(board, 2, 1), Is.False);
            Assert.That(BoardBits.GetRow(board, 2), Is.EqualTo(129));
        }

        [Test]
        public void Columns_RoundTrip_TopIsMostSignificant()
        {
            ulong board = BoardBits.SetColumn(0, 5, 0b10100001);
            Assert.That(BoardBits.GetCell(board, 0, 5), Is.True);
            Assert.That(BoardBits.GetCell(board, 2, 5), Is.True);
            Assert.That(BoardBits.GetCell(board, 7, 5), Is.True);
            Assert.That(BoardBits.GetColumn(board, 5), Is.EqualTo(0b10100001));
            Assert.That(BoardBits.PopCount(board), Is.EqualTo(3));
        }

        [Test]
        public void FromRows_ToRows_RoundTrip()
        {
            var rows = new[] { 60, 66, 165, 129, 165, 153, 66, 60 };
            Assert.That(BoardBits.ToRows(BoardBits.FromRows(rows)), Is.EqualTo(rows));
        }

        [Test]
        public void PopCount_FullBoard()
        {
            Assert.That(BoardBits.PopCount(ulong.MaxValue), Is.EqualTo(64));
            Assert.That(BoardBits.PopCount(0), Is.EqualTo(0));
        }
    }
}
