using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>The green LCD palette from the concept doc.</summary>
    public static class Lcd
    {
        public static readonly Color32 Background = new Color32(0xC3, 0xCD, 0x9C, 0xFF);
        /// <summary>Empty cells, panels.</summary>
        public static readonly Color32 Cell = new Color32(0xB2, 0xBD, 0x88, 0xFF);
        /// <summary>Cards, wall hatching ground.</summary>
        public static readonly Color32 Card = new Color32(0xD3, 0xDB, 0xB2, 0xFF);
        /// <summary>Bits and ink.</summary>
        public static readonly Color32 Ink = new Color32(0x23, 0x2C, 0x15, 0xFF);
        /// <summary>The bits' eyes, text on ink.</summary>
        public static readonly Color32 Light = new Color32(0xDC, 0xE4, 0xBC, 0xFF);
        public static readonly Color32 Text2 = new Color32(0x2F, 0x3A, 0x1D, 0xFF);
        public static readonly Color32 Text3 = new Color32(0x46, 0x52, 0x2D, 0xFF);
        /// <summary>Lost bits, broken rules.</summary>
        public static readonly Color32 Loss = new Color32(0xA2, 0x40, 0x2A, 0xFF);
        public static readonly Color32 LossText = new Color32(0x7A, 0x2E, 0x1F, 0xFF);
        /// <summary>The flat eyes of lost bits.</summary>
        public static readonly Color32 LostEye = new Color32(0xF1, 0xE3, 0xC8, 0xFF);
    }
}
