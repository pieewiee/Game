using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// One place that decides how big the interface is. IMGUI draws in raw
    /// pixels, which is unreadable at 4K and cramped at 720p, so every OnGUI in
    /// the game brackets itself with Begin/End and lays out against the VIRTUAL
    /// screen size (W/H) instead of Screen.width/height.
    ///
    /// GUI.matrix scales hit-testing along with drawing, so buttons stay where
    /// they look.
    /// </summary>
    public static class UiScaler
    {
        private static Matrix4x4 _saved;

        public static float Scale
        {
            get { return PlayerOptions.UiScale; }
        }

        /// <summary>Virtual screen width to lay out against.</summary>
        public static float W { get { return Screen.width / Scale; } }
        public static float H { get { return Screen.height / Scale; } }

        public static void Begin()
        {
            _saved = GUI.matrix;
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        }

        public static void End()
        {
            GUI.matrix = _saved;
        }

        /// <summary>Bottom strip the windows must not sink under: the news
        /// ticker plus the F2 session label above it.</summary>
        public const float BottomReserve = 60f;

        /// <summary>Keep a dragged (or re-scaled) window reachable: at least
        /// 80 px of it stays inside the virtual screen sideways, its title bar
        /// never leaves the top edge and never goes under the ticker. Without
        /// this a window shoved off-screen — or stranded by a UI-scale change
        /// — can only be recovered by deleting PlayerPrefs.</summary>
        public static Rect Clamp(Rect r)
        {
            r.x = Mathf.Clamp(r.x, 80f - r.width, W - 80f);
            float maxY = Mathf.Max(0f, H - BottomReserve);
            // A sized window stays entirely on screen; a height of 0 is a
            // layout window that sizes itself and only needs its title bar.
            if (r.height > 0f) maxY = Mathf.Min(maxY, Mathf.Max(0f, H - r.height));
            r.y = Mathf.Clamp(r.y, 0f, maxY);
            return r;
        }
    }
}
