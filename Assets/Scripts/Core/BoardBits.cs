using System.Collections.Generic;

namespace ShiftRegister.Core
{
    /// <summary>
    /// The 8×8 board packed into a ulong. Row r is the byte at bits 8r..8r+7, and within a row
    /// column 0 (leftmost) is the most significant bit, so a row reads as its number (leftmost = 128).
    /// Columns are read the same way: row 0 (top) is the most significant bit of a column byte.
    /// </summary>
    public static class BoardBits
    {
        public const int Size = 8;

        public static int CellBit(int row, int column) => row * 8 + (7 - column);

        public static ulong CellMask(int row, int column) => 1UL << CellBit(row, column);

        public static bool GetCell(ulong board, int row, int column) => ((board >> CellBit(row, column)) & 1UL) != 0;

        public static ulong SetCell(ulong board, int row, int column, bool on) =>
            on ? board | CellMask(row, column) : board & ~CellMask(row, column);

        public static byte GetRow(ulong board, int row) => (byte)(board >> (row * 8));

        public static ulong SetRow(ulong board, int row, byte value)
        {
            int shift = row * 8;
            return (board & ~(0xFFUL << shift)) | ((ulong)value << shift);
        }

        public static byte GetColumn(ulong board, int column)
        {
            int bit = 7 - column;
            int value = 0;
            for (int row = 0; row < Size; row++)
            {
                value |= (int)((board >> (row * 8 + bit)) & 1UL) << (7 - row);
            }

            return (byte)value;
        }

        public static ulong SetColumn(ulong board, int column, byte value)
        {
            int bit = 7 - column;
            for (int row = 0; row < Size; row++)
            {
                ulong mask = 1UL << (row * 8 + bit);
                if (((value >> (7 - row)) & 1) != 0)
                {
                    board |= mask;
                }
                else
                {
                    board &= ~mask;
                }
            }

            return board;
        }

        /// <summary>Spreads a column byte (top row = MSB) into board cells of that column.</summary>
        public static ulong ColumnToBoard(int column, byte value) => SetColumn(0UL, column, value);

        public static ulong FromRows(IReadOnlyList<int> rows)
        {
            ulong board = 0;
            if (rows == null)
            {
                return board;
            }

            for (int row = 0; row < Size && row < rows.Count; row++)
            {
                board = SetRow(board, row, (byte)rows[row]);
            }

            return board;
        }

        public static int[] ToRows(ulong board)
        {
            var rows = new int[Size];
            for (int row = 0; row < Size; row++)
            {
                rows[row] = GetRow(board, row);
            }

            return rows;
        }

        public static int PopCount(ulong x)
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        public static string ToBinary(byte value)
        {
            var chars = new char[8];
            for (int i = 0; i < 8; i++)
            {
                chars[i] = ((value >> (7 - i)) & 1) != 0 ? '1' : '0';
            }

            return new string(chars);
        }

        /// <summary>Multi-line picture for logs and test failures: '#' bit, '.' empty.</summary>
        public static string ToPicture(ulong board)
        {
            var builder = new System.Text.StringBuilder(Size * (Size + 1));
            for (int row = 0; row < Size; row++)
            {
                for (int column = 0; column < Size; column++)
                {
                    builder.Append(GetCell(board, row, column) ? '#' : '.');
                }

                if (row < Size - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }
    }
}
