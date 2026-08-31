using System;

namespace Game.Sim
{
    /// <summary>Calendar helpers. 1 tick = 1 hour, 365-day year, 8760 ticks.</summary>
    public static class SimClock
    {
        public const int TicksPerDay = 24;
        public const int DaysPerYear = 365;
        public const int TicksPerYear = TicksPerDay * DaysPerYear; // 8760

        public static int HourOfDay(long tick) { return (int)(tick % TicksPerDay); }
        public static int DayOfYear(long tick) { return (int)((tick / TicksPerDay) % DaysPerYear); }
        public static long DayIndex(long tick) { return tick / TicksPerDay; }
        /// <summary>Month 1..12 from day-of-year (equal 30.42-day months).</summary>
        public static int Month(long tick)
        {
            int m = 1 + (int)(DayOfYear(tick) / (DaysPerYear / 12.0));
            return m > 12 ? 12 : m;
        }
        public static bool IsNight(long tick)
        {
            int h = HourOfDay(tick);
            return h >= 22 || h < 6;
        }
    }

    /// <summary>One tick's weather. Produced by ClimateModel, consumed read-only.</summary>
    public struct ClimateSample
    {
        public double TdbC;             // dry-bulb °C
        public double TwbC;             // wet-bulb °C, never above TdbC
        public double WindSpeedMs;      // m/s
        public double WindTowardDeg;    // bearing the wind blows TOWARD, deg
        public bool WindTowardTown;     // inside the town sector
        public double WindCf;           // turbine capacity factor 0..1
        public double IrradianceFrac;   // fraction of kWp produced this hour
        public bool WetDay;             // did it rain today
        public bool DroughtActive;      // water restriction in force
        public bool HeatwaveActive;
        public bool DunkelflauteActive;
        public double ScarcityMult;     // multiplier on grid price ≥ 1
    }

    /// <summary>
    /// Deterministic weather. Everything except drought is a pure function of
    /// (seed, tick) via stateless hash noise, so it is random-access and
    /// identical regardless of evaluation order. Drought is a per-day state
    /// machine advanced by the Simulation, from the same deterministic series.
    ///
    /// Docs: systems/seasons.md. Summer = heat + drought + weak wind,
    /// winter = free cooling + strong wind + no solar + price spikes.
    /// </summary>
    public sealed class ClimateModel
    {
        private const ulong ChTemp = 1, ChWindSpd = 2, ChWindDir = 3,
                            ChCloud = 4, ChPrecip = 5;

        private readonly Balance _b;
        private readonly ulong _seed;
        private readonly double _solarScale;   // calibrates annual kWh/kWp
        private readonly double _windModMean;  // normalises the wind noise modifier

        public ClimateModel(Balance b, ulong seed)
        {
            _b = b;
            _seed = seed;

            // Calibrate the solar shape so the annual sum over this seed's
            // cloud series hits SOLAR_YIELD_ANNUAL per kWp. One O(8760) pass at
            // construction, fully deterministic.
            double rawAnnual = 0.0;
            double windModSum = 0.0;
            for (long t = 0; t < SimClock.TicksPerYear; t++)
            {
                rawAnnual += RawSolarShape(t);
                windModSum += RawWindModifier(t);
            }
            _solarScale = rawAnnual > 0 ? _b.SolarKwhPerKwpYear / rawAnnual : 0.0;
            _windModMean = windModSum / SimClock.TicksPerYear;
        }

        // --- temperature ----------------------------------------------------

        public double Tdb(long tick)
        {
            int doy = SimClock.DayOfYear(tick);
            int hour = SimClock.HourOfDay(tick);
            // Seasonal: minimum in mid-January (doy≈19 shifted), max mid-July.
            double seasonal = _b.TempSeasonalMeanC
                - _b.TempSeasonalAmpC * Math.Cos(2.0 * Math.PI * (doy - 19) / (double)SimClock.DaysPerYear);
            // Diurnal: max ~15:00, min ~03:00 (+cos anchors the maximum at
            // the anchor hour; the seasonal term above uses -cos to anchor its
            // minimum at mid-January).
            double diurnal = _b.TempDiurnalAmpC * Math.Cos(2.0 * Math.PI * (hour - 15) / 24.0);
            // Multi-day weather waves (~4-day period).
            double wave = _b.TempNoiseAmpC * SimRandom.SmoothNoise(_seed, ChTemp, tick / 96.0);
            return seasonal + diurnal + wave;
        }

        public double Twb(long tick, double tdb)
        {
            int doy = SimClock.DayOfYear(tick);
            double depression = _b.WetbulbDepression.Evaluate(doy);
            double twb = tdb - depression;
            return twb > tdb ? tdb : twb;
        }

        // --- wind -----------------------------------------------------------

        public double WindSpeed(long tick)
        {
            int month = SimClock.Month(tick);
            double mean = _b.WindSpeedMonth.Evaluate(month);
            double mod = 0.6 + 0.8 * (0.5 + 0.5 * SimRandom.SmoothNoise(_seed, ChWindSpd, tick / 36.0));
            double v = mean * mod;
            return v < 0 ? 0 : v;
        }

        private double RawWindModifier(long tick)
        {
            // Same noise channel as WindSpeed so the capacity factor genuinely
            // co-varies with the audible wind, per power.md §4.2 cf(month, speed);
            // squared-ish to mimic the turbine power curve.
            double n = 0.5 + 0.5 * SimRandom.SmoothNoise(_seed, ChWindSpd, tick / 36.0);
            return 0.15 + 1.7 * n * n;
        }

        public double WindCf(long tick)
        {
            int month = SimClock.Month(tick);
            double cf = _b.WindCfMonth.Evaluate(month) * RawWindModifier(tick) / _windModMean;
            if (cf < 0) cf = 0;
            if (cf > 1) cf = 1;
            return cf;
        }

        public double WindTowardDeg(long tick)
        {
            int doy = SimClock.DayOfYear(tick);
            double prevailing = _b.WindDirSeason.Evaluate(doy);
            double wander = _b.WindDirNoiseDeg * SimRandom.SmoothNoise(_seed, ChWindDir, tick / 30.0);
            double dir = prevailing + wander;
            while (dir < 0) dir += 360.0;
            while (dir >= 360.0) dir -= 360.0;
            return dir;
        }

        public bool IsTowardTown(double windTowardDeg)
        {
            double diff = Math.Abs(windTowardDeg - _b.TownBearingDeg);
            if (diff > 180.0) diff = 360.0 - diff;
            return diff <= _b.TownSectorHalfDeg;
        }

        // --- solar ----------------------------------------------------------

        private double RawSolarShape(long tick)
        {
            int doy = SimClock.DayOfYear(tick);
            int hour = SimClock.HourOfDay(tick);
            // Day length swings 8 h (winter) to 16 h (summer), centred on 13:00.
            double summerness = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (doy - 19) / (double)SimClock.DaysPerYear);
            double halfDay = 4.0 + 4.0 * summerness;
            double x = (hour + 0.5 - 13.0) / halfDay; // -1..1 across daylight
            if (x <= -1.0 || x >= 1.0) return 0.0;    // night: exactly zero, always
            double sun = Math.Cos(x * Math.PI * 0.5); // 0 at horizon, 1 at solar noon
            double seasonalPeak = 0.30 + 0.70 * summerness;
            // Cloudiness: per-day value, smooth, biased sunny in summer.
            double cloud = 0.35 + 0.65 * (0.5 + 0.5 * SimRandom.SmoothNoise(_seed, ChCloud, SimClock.DayIndex(tick) / 2.0));
            return sun * sun * seasonalPeak * cloud;
        }

        public double IrradianceFrac(long tick)
        {
            return RawSolarShape(tick) * _solarScale;
        }

        // --- precipitation --------------------------------------------------

        public bool IsWetDay(long dayIndex)
        {
            // Correlated wet/dry spells (~6-day weather systems), not independent
            // coin flips: with independent days a 28-day dry streak is a 5e-8
            // event and drought would never fire. Correlation makes long dry
            // runs rare-but-real, which is what DROUGHT_DRY_DAYS assumes.
            double n = SimRandom.SmoothNoise(_seed, ChPrecip, dayIndex / 6.0); // [-1,1]
            return n < (_b.PrecipWetProbability * 2.0 - 1.0);
        }

        // --- events ---------------------------------------------------------

        public bool IsHeatwave(double tdb) { return tdb >= _b.HeatwaveThresholdC; }

        public bool IsDunkelflaute(long tick, double windCf, double irradianceFrac)
        {
            int month = SimClock.Month(tick);
            bool winter = month <= 2 || month >= 11;
            return winter && windCf < _b.DunkelflauteWindCf && irradianceFrac < _b.DunkelflauteIrradiance;
        }

        /// <summary>Full sample for one tick. Drought comes from SimState.</summary>
        public ClimateSample Sample(long tick, bool droughtActive)
        {
            var s = new ClimateSample();
            s.TdbC = Tdb(tick);
            s.TwbC = Twb(tick, s.TdbC);
            s.WindSpeedMs = WindSpeed(tick);
            s.WindTowardDeg = WindTowardDeg(tick);
            s.WindTowardTown = IsTowardTown(s.WindTowardDeg);
            s.WindCf = WindCf(tick);
            s.IrradianceFrac = IrradianceFrac(tick);
            s.WetDay = IsWetDay(SimClock.DayIndex(tick));
            s.DroughtActive = droughtActive;
            s.HeatwaveActive = IsHeatwave(s.TdbC);
            s.DunkelflauteActive = IsDunkelflaute(tick, s.WindCf, s.IrradianceFrac);

            double scarcity = 1.0;
            if (s.HeatwaveActive)
            {
                double ramp = (s.TdbC - _b.HeatwaveThresholdC) / 4.0;
                if (ramp > 1.0) ramp = 1.0;
                scarcity = 1.0 + (_b.ScarcityEventMult - 1.0) * ramp;
            }
            if (s.DunkelflauteActive) scarcity = _b.ScarcityEventMult;
            s.ScarcityMult = scarcity;
            return s;
        }
    }
}
