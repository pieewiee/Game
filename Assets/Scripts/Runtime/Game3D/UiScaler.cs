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
    }
}
