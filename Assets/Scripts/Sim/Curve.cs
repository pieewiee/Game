using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Sim
{
    /// <summary>
    /// Piecewise-linear curve, clamped at both ends. The tuning file expresses
    /// every temperature/efficiency relationship (EVAP_COP, EVAP_WUE,
    /// CHILLER_COP, seasonal price...) as one of these, so no curve shape is
    /// hardcoded anywhere in the sim.
    /// </summary>
    public sealed class Curve
    {
        private readonly double[] _xs;
        private readonly double[] _ys;

        public Curve(double[] xs, double[] ys)
        {
            if (xs == null || ys == null || xs.Length != ys.Length || xs.Length < 1)
                throw new ArgumentException("Curve needs at least one (x,y) pair with equal lengths.");
            for (int i = 1; i < xs.Length; i++)
                if (xs[i] <= xs[i - 1])
                    throw new ArgumentException("Curve x values must be strictly increasing.");
            _xs = xs;
            _ys = ys;
        }

        public double Evaluate(double x)
        {
            if (x <= _xs[0]) return _ys[0];
            int last = _xs.Length - 1;
            if (x >= _xs[last]) return _ys[last];
            // linear scan: curves have < 10 points, binary search is not worth it
            for (int i = 1; i <= last; i++)
            {
                if (x <= _xs[i])
                {
                    double t = (x - _xs[i - 1]) / (_xs[i] - _xs[i - 1]);
                    return _ys[i - 1] + (_ys[i] - _ys[i - 1]) * t;
                }
            }
            return _ys[last]; // unreachable
        }

        /// <summary>
        /// Parses "x1:y1, x2:y2, ..." — the curve syntax of the tuning file.
        /// Always invariant culture: this must parse identically on a machine
        /// with a German locale.
        /// </summary>
        public static Curve Parse(string text)
        {
            string[] pairs = text.Split(',');
            var xs = new List<double>(pairs.Length);
            var ys = new List<double>(pairs.Length);
            foreach (string pair in pairs)
            {
                string p = pair.Trim();
                if (p.Length == 0) continue;
                int colon = p.IndexOf(':');
                if (colon <= 0)
                    throw new FormatException("Curve pair '" + p + "' is not in x:y form.");
                xs.Add(double.Parse(p.Substring(0, colon), CultureInfo.InvariantCulture));
                ys.Add(double.Parse(p.Substring(colon + 1), CultureInfo.InvariantCulture));
            }
            return new Curve(xs.ToArray(), ys.ToArray());
        }
    }
}
