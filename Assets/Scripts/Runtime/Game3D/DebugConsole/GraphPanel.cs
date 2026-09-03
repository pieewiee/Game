using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.DebugTools
{
    /// <summary>
    /// Time-series graphs over the driver's TickReport history. Every double
    /// meter in TickReport is a selectable series (discovered by reflection, so
    /// a new meter is automatically plottable). Each series autoscales
    /// independently; the legend shows current / min / max. Rendering is a
    /// Texture2D redrawn when the history or the selection changes — crude and
    /// entirely sufficient for a balancing tool.
    /// </summary>
    public sealed class GraphPanel
    {
        private struct Series
        {
            public string Name;
            public FieldInfo Field;
        }

        private static readonly Color32 Background = new Color32(24, 24, 28, 255);
        private static readonly Color32 GridLine = new Color32(48, 48, 56, 255);
        private static readonly Color32[] Palette =
        {
            new Color32(102, 194, 255, 255), new Color32(255, 160,  64, 255),
            new Color32(120, 220, 120, 255), new Color32(240,  98, 146, 255),
            new Color32(255, 224,  96, 255), new Color32(180, 140, 255, 255),
            new Color32( 96, 220, 220, 255), new Color32(255, 255, 255, 255),
            new Color32(255, 110, 110, 255), new Color32(160, 200,  90, 255),
            new Color32(230, 170, 220, 255), new Color32(140, 160, 255, 255),
        };

        private static readonly string[] WindowNames = { "24 h", "7 d", "30 d", "1 y", "all" };
        private static readonly int[] WindowTicks = { 24, 168, 720, SimClock.TicksPerYear, -1 };

        private static readonly HashSet<string> DefaultOn = new HashSet<string>
        {
            "Gni", "GniTarget", "PItKw", "PCoolKw", "GridImportKwh", "DieselKwh",
            "PResidentEurKwh", "NWater", "NNoise",
        };

        private readonly List<Series> _series = new List<Series>();
        private readonly HashSet<string> _enabled = new HashSet<string>();
        private int _window = 3;
        private Texture2D _tex;
        private long _drawnVersion = -1;
        private int _drawnSelectionHash;
        private float _nextRedrawTime;
        private Vector2 _legendScroll;

        public GraphPanel()
        {
            foreach (FieldInfo f in typeof(TickReport).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(double)) continue;
                _series.Add(new Series { Name = f.Name, Field = f });
                if (DefaultOn.Contains(f.Name)) _enabled.Add(f.Name);
            }
        }

        public void Draw(SimDriver driver)
        {
            var ci = CultureInfo.InvariantCulture;

            GUILayout.BeginHorizontal();
            GUILayout.Label("window:", GUILayout.Width(52));
            int newWindow = GUILayout.Toolbar(_window, WindowNames, GUILayout.Width(280));
            if (newWindow != _window) { _window = newWindow; _drawnVersion = -1; }
            GUILayout.FlexibleSpace();
            GUILayout.Label(driver.HistoryCount + " ticks in history (cap " + SimDriver.MaxHistory + ")");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // --- legend + toggles ------------------------------------------
            RefreshLegendLabels(driver, ci);
            _legendScroll = GUILayout.BeginScrollView(_legendScroll, GUILayout.Width(330));
            int colorIndex = 0;
            for (int si = 0; si < _series.Count; si++)
            {
                Series s = _series[si];
                bool on = _enabled.Contains(s.Name);
                Color prev = GUI.color;
                if (on) GUI.color = Palette[colorIndex % Palette.Length];
                bool now = GUILayout.Toggle(on, _legendLabels[si]);
                GUI.color = prev;
                if (now != on)
                {
                    if (now) _enabled.Add(s.Name); else _enabled.Remove(s.Name);
                    _drawnVersion = -1;
                }
                if (on) colorIndex++;
            }
            GUILayout.EndScrollView();

            // --- plot area -------------------------------------------------
            Rect area = GUILayoutUtility.GetRect(200, 4000, 150, 4000,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                EnsureTexture((int)area.width, (int)area.height);
                MaybeRedraw(driver);
                if (_tex != null) GUI.DrawTexture(area, _tex);
            }
            GUILayout.EndHorizontal();
        }

        private string[] _legendLabels;
        private long _legendVersion = -1;

        /// <summary>Rebuilds the 60-odd legend strings at most once per new tick
        /// (one boxing of the latest report), instead of per row per OnGUI pass.</summary>
        private void RefreshLegendLabels(SimDriver driver, CultureInfo ci)
        {
            if (_legendLabels != null && _legendVersion == driver.HistoryVersion) return;
            if (_legendLabels == null) _legendLabels = new string[_series.Count];
            _legendVersion = driver.HistoryVersion;
            bool any = driver.HistoryCount > 0;
            object boxed = any ? (object)driver.Latest : null;
            for (int i = 0; i < _series.Count; i++)
            {
                string suffix = any
                    ? " = " + ((double)_series[i].Field.GetValue(boxed)).ToString("0.###", ci)
                    : "";
                _legendLabels[i] = " " + _series[i].Name + suffix;
            }
        }

        private void EnsureTexture(int w, int h)
        {
            if (w < 16 || h < 16) return;
            if (_tex != null && _tex.width == w && _tex.height == h) return;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _drawnVersion = -1;
        }

        private void MaybeRedraw(SimDriver driver)
        {
            if (_tex == null) return;
            int selHash = 17;
            foreach (string s in _enabled) selHash = selHash * 31 + s.GetHashCode();
            bool selectionChanged = selHash != _drawnSelectionHash || _drawnVersion < 0;
            bool historyChanged = driver.HistoryVersion != _drawnVersion;
            if (!selectionChanged && !historyChanged) return;
            // While history is merely streaming in, redraw at most 4×/s —
            // regardless of HOW MANY ticks arrived per frame (the previous
            // "version+1" clause defeated the throttle at high sim speeds).
            if (!selectionChanged && Time.unscaledTime < _nextRedrawTime) return;
            _nextRedrawTime = Time.unscaledTime + 0.25f;
            _drawnVersion = driver.HistoryVersion;
            _drawnSelectionHash = selHash;
            Redraw(driver);
        }

        private Color32[] _px;
        private double[] _columnValues;

        private void Redraw(SimDriver driver)
        {
            int w = _tex.width, h = _tex.height;
            if (_px == null || _px.Length != w * h) _px = new Color32[w * h];
            if (_columnValues == null || _columnValues.Length != w) _columnValues = new double[w];
            Color32[] px = _px;
            for (int i = 0; i < px.Length; i++) px[i] = Background;
            // Horizontal quarter lines.
            for (int q = 1; q < 4; q++)
            {
                int y = h * q / 4;
                for (int x = 0; x < w; x++) px[y * w + x] = GridLine;
            }

            int count = driver.HistoryCount;
            if (count >= 2)
            {
                int windowTicks = WindowTicks[_window];
                int len = windowTicks < 0 ? count : Math.Min(count, windowTicks);
                int start = count - len;

                int colorIndex = 0;
                foreach (Series s in _series)
                {
                    if (!_enabled.Contains(s.Name)) continue;
                    Color32 col = Palette[colorIndex % Palette.Length];
                    colorIndex++;
                    PlotSeries(driver, s.Field, start, len, w, h, px, col, _columnValues);
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }

        private static void PlotSeries(SimDriver driver, FieldInfo field,
            int start, int len, int w, int h, Color32[] px, Color32 col, double[] values)
        {
            // Per-column sampling with per-series autoscale.
            double min = double.MaxValue, max = double.MinValue;
            for (int x = 0; x < w; x++)
            {
                int i = start + (int)((long)x * (len - 1) / Math.Max(1, w - 1));
                object boxed = driver.GetHistory(i);
                double v = (double)field.GetValue(boxed);
                values[x] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (max - min < 1e-12) { max = min + 1.0; }

            int prevY = -1;
            for (int x = 0; x < w; x++)
            {
                double t = (values[x] - min) / (max - min);
                int y = (int)((h - 3) * t) + 1;
                if (y < 0) y = 0;
                if (y >= h) y = h - 1;
                // Texture v=0 maps to the BOTTOM of the drawn rect, so a
                // larger row index is higher on screen — no flip needed.
                int rowY = y;
                px[rowY * w + x] = col;
                if (prevY >= 0 && Math.Abs(prevY - rowY) > 1)
                {
                    int lo = Math.Min(prevY, rowY), hi = Math.Max(prevY, rowY);
                    for (int yy = lo; yy <= hi; yy++) px[yy * w + x] = col;
                }
                prevY = rowY;
            }
        }
    }
}
