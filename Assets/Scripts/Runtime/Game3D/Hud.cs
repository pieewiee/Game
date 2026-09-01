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
            GUI.Box(new Rect(8, 8, 330, 110), "");
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
            bool isClient = GameBootstrap.Net != null && GameBootstrap.Net.IsClient;
            GUI.Label(new Rect(16, 76, 320, 20), isClient
                ? "Esc cursor · F1 console · F2 net · F3 options · E/Q"
                : "Esc cursor · F1 console · F2 net · F3 options · F5/F9 save · E/Q");
            GUI.Label(new Rect(16, 94, 320, 20),
                "Space jump · Ctrl duck · Shift run · 1-7 build · 8-0 route");

            // --- wind arrow -------------------------------------------------
            DrawWindArrow(new Vector2(Screen.width - 70, 70), r);

            // --- driving ----------------------------------------------------
            if (player != null && player.Driving != null)
            {
                DrawDriving(player.Driving, ci);
            }

            // --- interact prompt -------------------------------------------
            if (player != null && !player.IsDead)
            {
                player.CurrentTarget(out string prompt);
                bool aimed = !string.IsNullOrEmpty(prompt);
                if (aimed)
                    GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.62f, 600, 26), prompt, _centre);
                var fac = GameBootstrap.Facility;
                if (fac != null && fac.PlacementHint.Length > 0)
                    GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.66f, 600, 26),
                        fac.PlacementHint, _centre);
                DrawCrosshair(aimed);

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

        /// <summary>The cab instruments: speed, mast height, load, and the two
        /// numbers that decide whether the next corner tips you over.</summary>
        private void DrawDriving(Forklift f, CultureInfo ci)
        {
            float w = 300f, h = 96f;
            var box = new Rect(Screen.width / 2f - w / 2f, Screen.height - h - 44f, w, h);
            GUI.Box(box, "");
            float kmh = Mathf.Abs(f.Speed) * 3.6f;
            string gear = f.Speed < -0.2f ? "REVERSE" : f.Speed > 0.2f ? "FORWARD" : "IDLE";
            GUI.Label(new Rect(box.x + 12, box.y + 6, w - 24, 20),
                gear + "   " + kmh.ToString("0.0", ci) + " km/h" +
                (f.HeadlightsOn ? "   lights on" : ""), _big);
            GUI.Label(new Rect(box.x + 12, box.y + 26, w - 24, 20),
                "mast " + f.ForkHeight.ToString("0.00", ci) + " m" +
                (f.Load != null ? "   LOADED" : "   empty"), _big);

            // The tip-over warning is the whole safety briefing.
            bool risky = f.ForkHeight > Forklift.TipForkH && Mathf.Abs(f.Speed) > 1.2f;
            if (risky)
            {
                GUI.color = new Color(1f, 0.5f, 0.3f);
                GUI.Label(new Rect(box.x + 12, box.y + 46, w - 24, 20),
                    "LOAD RAISED — DO NOT TURN AT SPEED", _big);
                GUI.color = Color.white;
            }
            GUI.Label(new Rect(box.x + 12, box.y + 68, w - 24, 20),
                "WASD drive · Space/Ctrl mast · L lights · H horn · E out");
        }

        /// <summary>Four ticks around a gap, opening up and turning amber when
        /// something under the cursor can be operated. A text dot told you
        /// where the centre was; this tells you whether it matters.</summary>
        private static void DrawCrosshair(bool aimed)
        {
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            float gap = aimed ? 7f : 4f, len = aimed ? 7f : 5f, w = 2f;
            GUI.color = aimed ? new Color(1f, 0.72f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(cx - w / 2, cy - gap - len, w, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - w / 2, cy + gap, w, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - gap - len, cy - w / 2, len, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap, cy - w / 2, len, w), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
