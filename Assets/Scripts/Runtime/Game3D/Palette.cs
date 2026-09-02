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

        /// <summary>Every colour, in the order of the art bible's table.</summary>
        public static readonly Color[] All =
        {
            Concrete, Slate, Render, ProgramBlue, PaleBlue, Amber, AlarmRed, Field, Foliage, Earth, Sky, Ink
        };
        public static readonly string[] Names =
        {
            "Concrete", "Slate", "Render", "ProgramBlue", "PaleBlue", "Amber", "AlarmRed", "Field", "Foliage", "Earth", "Sky", "Ink"
        };

        /// <summary>The palette minus the three colours that carry meaning:
        /// Program Blue is what the operator installs, Amber is a warning or
        /// a throttle, Alarm Red is an alarm. Imported town meshes (houses,
        /// cars, street furniture) are quantised to this set so a red roof
        /// never reads as a fault and a yellow taxi never as a throttle.</summary>
        public static readonly Color[] Town =
        {
            Concrete, Slate, Render, PaleBlue, Field, Foliage, Earth, Sky, Ink
        };

        /// <summary>Index into `set` of the colour nearest to c, measured in
        /// CIE Lab: a blue-grey roof lands on Concrete or Slate, not on the
        /// field green an RGB distance would pick, and a saturated orange
        /// awning goes to the pale Render rather than to the brown Earth.</summary>
        public static int NearestIndex(Color c, Color[] set)
        {
            Vector3 lab = ToLab(c);
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < set.Length; i++)
            {
                Vector3 p = ToLab(set[i]);
                float d = (lab - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>sRGB → CIE L*a*b* (D65), the usual formulas.</summary>
        public static Vector3 ToLab(Color c)
        {
            float r = Linear(c.r), g = Linear(c.g), b = Linear(c.b);
            float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
            float y = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 1.00000f;
            float z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float Linear(float v)
        {
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        private static float LabF(float t)
        {
            return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        }

        public static Color Nearest(Color c, Color[] set)
        {
            return set[NearestIndex(c, set)];
        }

        private static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
