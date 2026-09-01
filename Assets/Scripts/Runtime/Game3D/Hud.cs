using System;
using System.Globalization;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The gameplay HUD (M6): clock, GNI + stage, cash, reputation, the wind
    /// arrow (the single most important instrument in the game — it decides who
    /// gets the smoke), interact prompt, placement hints, freeze vignette.
    /// The debug console stays on F1 above all of this.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        private GUIStyle _big;
        private GUIStyle _centre;

        private void OnGUI()
        {
            var driver = GameBootstrap.Driver;
            var player = GameBootstrap.LocalPlayer;
            var ci = CultureInfo.InvariantCulture;
            if (driver == null) return;
            TickReport r;
            long tick;
            if (GameBootstrap.Net != null && GameBootstrap.Net.IsClient)
            {
                r = GameBootstrap.Net.RemoteReport;
                tick = GameBootstrap.Net.RemoteTick;
            }
            else
            {
                if (driver.Sim == null) return;
                r = driver.Latest;
                tick = driver.Sim.State.Tick;
            }

            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                _centre = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            }

            // --- top-left block --------------------------------------------
            GUI.Box(new Rect(8, 8, 330, 92), "");
            GUI.Label(new Rect(16, 12, 320, 22),
                "Day " + SimClock.DayIndex(tick).ToString("0", ci) +
                "  " + SimClock.HourOfDay(tick).ToString("00", ci) + ":00" +
                "  (month " + SimClock.Month(tick) + ")", _big);
            GUI.Label(new Rect(16, 34, 320, 22),
                "GNI " + r.Gni.ToString("0.0", ci) + "  [" + r.Stage + "]" +
                "   Rep " + r.Reputation.ToString("0.0", ci), _big);
            GUI.Label(new Rect(16, 56, 320, 22),
                "Cash € " + r.CashEur.ToString("N0", ci) +
                "   Water " + (r.WaterLPerH * 24.0 / 1000.0).ToString("0.0", ci) + " m³/d", _big);
            GUI.Label(new Rect(16, 76, 320, 20),
                "F1 console · F5 save · F9 load · E interact · Q drop · 1-7 build · 8-0 route");

            // --- wind arrow -------------------------------------------------
            DrawWindArrow(new Vector2(Screen.width - 70, 70), r);

            // --- interact prompt -------------------------------------------
            if (player != null && !player.IsDead)
            {
                player.CurrentTarget(out string prompt);
                if (!string.IsNullOrEmpty(prompt))
                    GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.62f, 600, 26), prompt, _centre);
                var fac = GameBootstrap.Facility;
                if (fac != null && fac.PlacementHint.Length > 0)
                    GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.66f, 600, 26),
                        fac.PlacementHint, _centre);
                // crosshair
                GUI.Label(new Rect(Screen.width / 2f - 4, Screen.height / 2f - 10, 10, 20), "·", _centre);

                if (player.Exposure > 0.05f)
                {
                    GUI.color = new Color(0.5f, 0.75f, 1f, player.Exposure * 0.55f);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height * 0.3f, 400, 26),
                        "IT IS VERY COLD IN HERE", _centre);
                }
            }
        }

        private void DrawWindArrow(Vector2 centre, TickReport r)
        {
            // The wind blows TOWARD WindTowardDeg; 0° = +z (north). Screen up =
            // north, so the arrow rotation is simply the bearing.
            GUI.Box(new Rect(centre.x - 52, centre.y - 52, 104, 118), "");
            Matrix4x4 prev = GUI.matrix;
            GUIUtility.RotateAroundPivot((float)r.WindTowardDeg, centre);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            GUI.color = r.WindTowardTown ? new Color(1f, 0.45f, 0.35f) : Color.white;
            GUI.Label(new Rect(centre.x - 20, centre.y - 24, 40, 48), "↑", style);
            GUI.matrix = prev;
            GUI.Label(new Rect(centre.x - 50, centre.y + 28, 100, 20),
                "wind " + r.WindSpeedMs.ToString("0.0", CultureInfo.InvariantCulture) + " m/s",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(centre.x - 50, centre.y + 44, 100, 20),
                r.WindTowardTown ? "TOWARD TOWN" : "away from town",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
        }
    }
}
