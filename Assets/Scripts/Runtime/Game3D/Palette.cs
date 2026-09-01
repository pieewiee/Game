using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The 12-colour palette from docs/art-bible.md §1. Every mesh in the game
    /// is painted from this list and nothing else. Program Blue is a mechanic:
    /// everything the operator installs is that blue, so fortification reads
    /// from the road as "more blue, less field green".
    /// </summary>
    public static class Palette
    {
        public static readonly Color Concrete = Rgb(0x8E, 0x8F, 0x8A);
        public static readonly Color Slate = Rgb(0x3A, 0x3F, 0x44);
        public static readonly Color Render = Rgb(0xD9, 0xD5, 0xCC);
        public static readonly Color ProgramBlue = Rgb(0x2E, 0x5C, 0x8A);
        public static readonly Color PaleBlue = Rgb(0x7F, 0xA6, 0xC9);
        public static readonly Color Amber = Rgb(0xE0, 0xA6, 0x3C);
        public static readonly Color AlarmRed = Rgb(0xC4, 0x46, 0x3A);
        public static readonly Color Field = Rgb(0x7A, 0x8C, 0x5A);
        public static readonly Color Foliage = Rgb(0x4A, 0x5B, 0x3C);
        public static readonly Color Earth = Rgb(0x6B, 0x58, 0x44);
        public static readonly Color Sky = Rgb(0xBF, 0xD3, 0xE0);
        public static readonly Color Ink = Rgb(0x22, 0x26, 0x2A);

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
