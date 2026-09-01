using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.DebugTools
{
    /// <summary>
    /// A slider for every constant. Scalars get slider + text field; curves
    /// unfold into one slider per point (X fixed, Y tunable). Edits apply LIVE
    /// to the running simulation via Balance.SetScalar/SetCurve — the sim reads
    /// its constants through property facades, so the next tick sees the value.
    ///
    /// A live-tuned run is not reproducible from the file until saved
    /// (Scenario tab → Save). Keys whose effect is cached at construction are
    /// marked (restart) or (start).
    /// </summary>
    public sealed class TuningPanel
    {
        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            { "SOLAR_YIELD_ANNUAL", "(restart — calibrated once per sim)" },
            { "GNI_START", "(start value — applies on restart)" },
            { "REPUTATION_START", "(start value on restart; a scenario REPUTATION line overrides it)" },
            { "RETAIL_MARKUP", "(also seeds the town's remembered price at restart)" },
        };

        private readonly Dictionary<string, string> _textBuffers = new Dictionary<string, string>();
        private readonly HashSet<string> _openCurves = new HashSet<string>();
        private string _filter = "";
        private Vector2 _scroll;

        private long _generation = -1;

        public void Draw(SimDriver driver)
        {
            Balance b = driver.Balance;
            var ci = CultureInfo.InvariantCulture;
            if (driver.BalanceGeneration != _generation)
            {
                // Balance object or its defaults changed (reload/save): stale
                // text buffers would later apply values from the old file.
                _generation = driver.BalanceGeneration;
                _textBuffers.Clear();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("filter:", GUILayout.Width(40));
            _filter = GUILayout.TextField(_filter, GUILayout.Width(220));
            if (GUILayout.Button("Reset ALL to file defaults", GUILayout.Width(200)))
                ResetAll(b);
            double wSum = b.WNoise + b.WAir + b.WWater + b.WPrice + b.WVisual;
            if (Math.Abs(wSum - 1.0) > 1e-9)
            {
                Color prev = GUI.color;
                GUI.color = Color.yellow;
                GUILayout.Label("W_* sum = " + wSum.ToString("0.###", ci) + " (docs want 1.0)");
                GUI.color = prev;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("edits are LIVE; save via Scenario tab");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string key in b.OrderedKeys)
            {
                if (_filter.Length > 0 &&
                    key.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (b.IsCurve(key)) DrawCurveRow(b, key);
                else DrawScalarRow(b, key);
            }
            GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        private void DrawScalarRow(Balance b, string key)
        {
            var ci = CultureInfo.InvariantCulture;
            double value = b.GetScalar(key);
            double def = b.GetScalarDefault(key);
            SliderRange(def, out float min, out float max);

            GUILayout.BeginHorizontal();
            GUILayout.Label(key, GUILayout.Width(260));

            // The slider clamps its display value: if the real value sits
            // outside the slider range (set via the text field), applying the
            // slider's output would silently drag it back to the bound. Only
            // accept slider movement while the value is inside the range.
            bool inRange = value >= min && value <= max;
            float shown = (float)Math.Min(Math.Max(value, min), max);
            float slid = GUILayout.HorizontalSlider(shown, min, max, GUILayout.Width(240));
            if (inRange && Math.Abs(slid - value) > (max - min) * 1e-4)
            {
                b.SetScalar(key, slid);
                _textBuffers.Remove(key);
                value = slid;
            }

            if (!_textBuffers.TryGetValue(key, out string buf))
                buf = value.ToString("0.####", ci);
            string edited = GUILayout.TextField(buf, GUILayout.Width(90));
            if (edited != buf) _textBuffers[key] = edited;
            if (GUILayout.Button("set", GUILayout.Width(40)))
            {
                if (_textBuffers.TryGetValue(key, out string t) &&
                    double.TryParse(t, NumberStyles.Float, ci, out double parsed))
                {
                    b.SetScalar(key, parsed);
                }
                _textBuffers.Remove(key);
            }
            GUILayout.Label("default " + def.ToString("0.####", ci), GUILayout.Width(120));
            if (Hints.TryGetValue(key, out string hint)) GUILayout.Label(hint);
            GUILayout.EndHorizontal();
        }

        private static void SliderRange(double def, out float min, out float max)
        {
            // Heuristic debug ranges: 0 → [0,10]; otherwise [0, 3×default].
            // The text field takes any exact value beyond the slider.
            min = 0f;
            max = def <= 0.0 ? 10f : (float)(def * 3.0);
            if (max < 1e-4f) max = 1f;
        }

        // ------------------------------------------------------------------
        private void DrawCurveRow(Balance b, string key)
        {
            var ci = CultureInfo.InvariantCulture;
            Curve c = b.GetCurve(key);
            bool open = _openCurves.Contains(key);

            GUILayout.BeginHorizontal();
            bool nowOpen = GUILayout.Toggle(open, (open ? "▼ " : "► ") + key, GUI.skin.label,
                GUILayout.Width(260));
            if (nowOpen != open)
            {
                // Record the click but keep drawing THIS pass with the pre-event
                // state: adding controls between the Layout and event passes of
                // the same frame breaks IMGUI's layout cache (ArgumentException
                // on every foldout open). The rows appear next frame.
                if (nowOpen) _openCurves.Add(key); else _openCurves.Remove(key);
            }
            GUILayout.Label(Balance.CurveText(c));
            GUILayout.EndHorizontal();

            if (!open) return;

            // Per-point Y sliders; slider range from the default curve's span.
            Curve defCurve = Curve.Parse(b.GetCurveDefaultText(key));
            double defMax = 0;
            for (int i = 0; i < defCurve.Ys.Count; i++)
                if (Math.Abs(defCurve.Ys[i]) > defMax) defMax = Math.Abs(defCurve.Ys[i]);
            float yMax = defMax <= 0 ? 10f : (float)(defMax * 3.0);

            for (int i = 0; i < c.Xs.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                GUILayout.Label("x=" + c.Xs[i].ToString("0.##", ci), GUILayout.Width(70));
                float y = (float)c.Ys[i];
                bool yInRange = y >= 0f && y <= yMax;
                float shownY = Mathf.Clamp(y, 0f, yMax);
                float slid = GUILayout.HorizontalSlider(shownY, 0f, yMax, GUILayout.Width(240));
                string bufKey = key + "[" + i + "]";
                if (yInRange && Math.Abs(slid - y) > yMax * 1e-4)
                {
                    b.SetCurve(key, c.WithY(i, slid));
                    _textBuffers.Remove(bufKey);
                    c = b.GetCurve(key);
                }
                if (!_textBuffers.TryGetValue(bufKey, out string buf))
                    buf = c.Ys[i].ToString("0.####", ci);
                string edited = GUILayout.TextField(buf, GUILayout.Width(90));
                if (edited != buf) _textBuffers[bufKey] = edited;
                if (GUILayout.Button("set", GUILayout.Width(40)))
                {
                    if (_textBuffers.TryGetValue(bufKey, out string t) &&
                        double.TryParse(t, NumberStyles.Float, ci, out double parsed))
                    {
                        b.SetCurve(key, c.WithY(i, parsed));
                        c = b.GetCurve(key);
                    }
                    _textBuffers.Remove(bufKey);
                }
                GUILayout.Label("default " + defCurve.Ys[i].ToString("0.####", ci));
                GUILayout.EndHorizontal();
            }
        }

        private void ResetAll(Balance b)
        {
            foreach (string key in b.OrderedKeys)
            {
                if (b.IsCurve(key)) b.SetCurve(key, Curve.Parse(b.GetCurveDefaultText(key)));
                else b.SetScalar(key, b.GetScalarDefault(key));
            }
            _textBuffers.Clear();
        }
    }
}
