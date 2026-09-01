using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Sim
{
    /// <summary>
    /// Every tunable number in the simulation, loaded from a flat key=value
    /// tuning file (see Assets/StreamingAssets/Tuning/balance.tuning).
    /// Nothing in Game.Sim hardcodes a balance constant; if a number appears in
    /// docs/balance-constants.md it must come through here under the same name.
    ///
    /// Since Milestone 2 the values are stored in dictionaries behind property
    /// facades, so the debug console can enumerate and LIVE-EDIT every constant
    /// (GetScalar/SetScalar/GetCurve/SetCurve) and write the result back to the
    /// tuning file with comments preserved (PatchTuningText). Call sites in the
    /// simulation are unchanged. A live-tuned run is no longer reproducible
    /// from the file until it is saved — that is inherent to interactive
    /// balancing and is the reason PatchTuningText exists.
    ///
    /// The format is deliberately not JSON: Unity ships no JSON library that is
    /// usable from a noEngineReferences assembly without adding a package.
    /// </summary>
    public sealed class Balance
    {
        private readonly Dictionary<string, double> _scalars = new Dictionary<string, double>();
        private readonly Dictionary<string, Curve> _curves = new Dictionary<string, Curve>();
        private readonly Dictionary<string, double> _scalarDefaults = new Dictionary<string, double>();
        private readonly Dictionary<string, string> _curveDefaults = new Dictionary<string, string>();
        private readonly List<string> _orderedKeys = new List<string>();   // file order

        // --- Registry API for the debug console -----------------------------

        /// <summary>Keys in tuning-file order; mixed scalars and curves.</summary>
        public IReadOnlyList<string> OrderedKeys { get { return _orderedKeys; } }

        public bool IsCurve(string key) { return _curves.ContainsKey(key); }

        public double GetScalar(string key) { return _scalars[key]; }
        public double GetScalarDefault(string key) { return _scalarDefaults[key]; }

        public void SetScalar(string key, double value)
        {
            if (!_scalars.ContainsKey(key))
                throw new KeyNotFoundException("Unknown scalar tuning key: " + key);
            _scalars[key] = value;
            if (key.StartsWith("GRID_T", StringComparison.Ordinal)) RebuildTierArrays();
        }

        public Curve GetCurve(string key) { return _curves[key]; }
        public string GetCurveDefaultText(string key) { return _curveDefaults[key]; }

        public void SetCurve(string key, Curve curve)
        {
            if (!_curves.ContainsKey(key))
                throw new KeyNotFoundException("Unknown curve tuning key: " + key);
            _curves[key] = curve ?? throw new ArgumentNullException("curve");
        }

        /// <summary>Σ of the five W_* weights; Parse rejects files where this is not 1.</summary>
        public double WeightSum { get { return WNoise + WAir + WWater + WPrice + WVisual; } }

        /// <summary>After a successful save, the file equals the in-memory state,
        /// so "default" (= file value) must follow.</summary>
        public void RefreshDefaultsFromCurrent()
        {
            foreach (string key in _orderedKeys)
            {
                if (_curves.ContainsKey(key)) _curveDefaults[key] = CurveText(_curves[key]);
                else _scalarDefaults[key] = _scalars[key];
            }
        }

        /// <summary>
        /// Returns the given tuning-file text with every value replaced by the
        /// CURRENT in-memory value, preserving comments, blank lines and
        /// ordering. This is how the debug console saves a balancing session.
        /// </summary>
        public string PatchTuningText(string originalText)
        {
            var ci = CultureInfo.InvariantCulture;
            string normalized = originalText.Replace("\r\n", "\n");
            bool endsWithNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
            string[] lines = normalized.Split('\n');
            // A trailing newline yields a final empty split element; dropping it
            // (and re-appending the newline at the end) keeps repeated saves
            // byte-stable instead of growing a blank line per save.
            int lineCount = endsWithNewline ? lines.Length - 1 : lines.Length;
            var sb = new StringBuilder(originalText.Length + 256);
            for (int i = 0; i < lineCount; i++)
            {
                string line = lines[i];
                string code = line;
                string comment = "";
                int hash = line.IndexOf('#');
                if (hash >= 0) { code = line.Substring(0, hash); comment = line.Substring(hash); }
                int eq = code.IndexOf('=');
                string key = eq > 0 ? code.Substring(0, eq).Trim() : null;
                if (key != null && (_scalars.ContainsKey(key) || _curves.ContainsKey(key)))
                {
                    string value = _scalars.ContainsKey(key)
                        ? _scalars[key].ToString("R", ci)   // round-trip exact
                        : CurveText(_curves[key]);
                    sb.Append(key).Append(" = ").Append(value);
                    if (comment.Length > 0) sb.Append("   ").Append(comment);
                    sb.Append('\n');
                }
                else
                {
                    sb.Append(line).Append('\n');
                }
            }
            if (!endsWithNewline && sb.Length > 0)
                sb.Length -= 1; // the loop added one newline the original lacked
            return sb.ToString();
        }

        public static string CurveText(Curve c)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            for (int i = 0; i < c.Xs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(c.Xs[i].ToString("R", ci)).Append(':')
                  .Append(c.Ys[i].ToString("R", ci));
            }
            return sb.ToString();
        }

        // --- Scalar facades (call sites unchanged since M1) ------------------

        private double S(string k) { return _scalars[k]; }
        private Curve C(string k) { return _curves[k]; }

        // Time & world
        public double TownWaterDemandM3Day { get { return S("TOWN_WATER_DEMAND"); } }
        public double WaterPriceEurM3 { get { return S("WATER_PRICE"); } }
        public double DroughtWaterTariffMult { get { return S("DROUGHT_WATER_TARIFF_MULT"); } }

        // Hardware
        public double GpuNodePeakKw { get { return S("GPU_NODE_P_PEAK"); } }
        public double GpuNodeIdleKw { get { return S("GPU_NODE_P_IDLE"); } }
        public double HeatFraction { get { return S("HEAT_FRACTION"); } }
        public double AuxBaseKw { get { return S("AUX_BASE_KW"); } }
        public double AuxFracOfIt { get { return S("AUX_FRAC_OF_IT"); } }

        // Cooling
        public Curve EvapCop { get { return C("EVAP_COP"); } }
        public Curve EvapWue { get { return C("EVAP_WUE"); } }
        public Curve ChillerCop { get { return C("CHILLER_COP"); } }
        public double FreecoolThresholdC { get { return S("FREECOOL_T_THRESHOLD"); } }
        public double FreecoolCop { get { return S("FREECOOL_COP"); } }
        public double DroughtAllowanceFrac { get { return S("DROUGHT_ALLOWANCE_FRAC"); } }

        // Power
        public Curve GridPriceSeason { get { return C("GRID_PRICE_SEASON"); } }
        public Curve GridPriceDiurnal { get { return C("GRID_PRICE_DIURNAL"); } }
        public double RetailMarkup { get { return S("RETAIL_MARKUP"); } }
        public double CongestionK { get { return S("CONGESTION_K"); } }
        public double CongestionGamma { get { return S("CONGESTION_GAMMA"); } }
        public double ScarcityEventMult { get { return S("SCARCITY_EVENT_MULT"); } }
        public double SolarKwhPerKwpYear { get { return S("SOLAR_YIELD_ANNUAL"); } }
        public Curve WindCfMonth { get { return C("WIND_CF_MONTH"); } }
        public double BatteryRte { get { return S("BATTERY_RTE"); } }
        public double BatteryCycleLife { get { return S("BATTERY_CYCLE_LIFE"); } }
        public double DieselCostPerKwh { get { return S("DIESEL_COST_PER_KWH"); } }
        public double DieselOmPerKwh { get { return S("DIESEL_OM"); } }
        public double BatteryArbitrageSpread { get { return S("BATTERY_ARBITRAGE_SPREAD"); } }

        // Grid tiers — kept as arrays so Simulation call sites stay identical;
        // rebuilt whenever a GRID_T* scalar is edited.
        public double[] TierCapKw { get; private set; }
        public double[] TierCapexEur { get; private set; }
        public double[] TierLeadDays { get; private set; }
        public double[] TierSentimentGate { get; private set; }
        public double[] TierRegionCapKw { get; private set; }

        private void RebuildTierArrays()
        {
            double[] Tier(string suffix)
            {
                var arr = new double[5];
                for (int i = 0; i < 5; i++) arr[i] = S("GRID_T" + i + "_" + suffix);
                return arr;
            }
            TierCapKw = Tier("CAP");
            TierCapexEur = Tier("CAPEX");
            TierLeadDays = Tier("LEAD_DAYS");
            TierSentimentGate = Tier("SENTIMENT_GATE");
            TierRegionCapKw = Tier("REGION_CAP");
        }

        // Contracts
        public double RateInference { get { return S("RATE_INFERENCE"); } }
        public double RateTraining { get { return S("RATE_TRAINING"); } }
        public double RateSpot { get { return S("RATE_SPOT"); } }
        public double SlaInference { get { return S("SLA_INFERENCE"); } }
        public double SlaPenaltyK { get { return S("SLA_PENALTY_K"); } }
        public double SlaPenaltyCap { get { return S("SLA_PENALTY_CAP"); } }
        public double CheckpointIntervalH { get { return S("CHECKPOINT_INTERVAL"); } }
        public double CheckpointOverhead { get { return S("CHECKPOINT_OVERHEAD"); } }
        public double TrainingFailPayoutFrac { get { return S("TRAINING_FAIL_PAYOUT_FRAC"); } }
        public double TrainingRequiredFrac { get { return S("TRAINING_REQUIRED_FRAC"); } }
        public double ReputationStart { get { return S("REPUTATION_START"); } }
        public double ReputationGainMonth { get { return S("REPUTATION_GAIN_MONTH"); } }
        public double ReputationLossBreach { get { return S("REPUTATION_LOSS_BREACH"); } }
        public double RepGateInference { get { return S("REP_GATE_INFERENCE"); } }
        public double RepGateTrainingMed { get { return S("REP_GATE_TRAINING_MED"); } }
        public double InferenceDiurnalAmp { get { return S("INFERENCE_DIURNAL_AMP"); } }
        public double SpotDepthMeanKw { get { return S("SPOT_DEPTH_MEAN_KW"); } }
        public double SpotDepthVolatility { get { return S("SPOT_DEPTH_VOLATILITY"); } }

        // Program indicators
        public double WNoise { get { return S("W_NOISE"); } }
        public double WAir { get { return S("W_AIR"); } }
        public double WWater { get { return S("W_WATER"); } }
        public double WPrice { get { return S("W_PRICE"); } }
        public double WVisual { get { return S("W_VISUAL"); } }
        public double HalflifeNoiseDays { get { return S("HALFLIFE_NOISE"); } }
        public double HalflifeAirDays { get { return S("HALFLIFE_AIR"); } }
        public double HalflifeWaterDays { get { return S("HALFLIFE_WATER"); } }
        public double HalflifePriceDays { get { return S("HALFLIFE_PRICE"); } }
        public double HalflifeVisualDays { get { return S("HALFLIFE_VISUAL"); } }
        public double NightNoiseMult { get { return S("NIGHT_NOISE_MULT"); } }
        public double WindTowardMult { get { return S("WIND_TOWARD_MULT"); } }
        public double WindAwayMult { get { return S("WIND_AWAY_MULT"); } }
        public double PriceSensitivity { get { return S("PRICE_SENSITIVITY"); } }
        public double PriceBaselineAdaptPerDay { get { return S("PRICE_BASELINE_ADAPT_PER_DAY"); } }
        public double NoiseFanRefKw { get { return S("NOISE_FAN_REF_KW"); } }
        public double NoiseChillerWeight { get { return S("NOISE_CHILLER_WEIGHT"); } }
        public double NoiseDieselWeight { get { return S("NOISE_DIESEL_WEIGHT"); } }
        public double NoiseTurbineWeight { get { return S("NOISE_TURBINE_WEIGHT"); } }
        public double TurbineUnitKw { get { return S("TURBINE_UNIT_KW"); } }
        public double NoiseScale { get { return S("NOISE_SCALE"); } }
        public double AirEmissionFactor { get { return S("AIR_EMISSION_FACTOR"); } }
        public double DroughtNuisanceMultMax { get { return S("DROUGHT_NUISANCE_MULT_MAX"); } }

        // Good Neighbor Index
        public double GniStart { get { return S("GNI_START"); } }
        public double GniAdjustRate { get { return S("GNI_ADJUST_RATE"); } }
        public double EscalationHysteresis { get { return S("ESCALATION_HYSTERESIS"); } }
        public double BandContent { get { return S("BAND_CONTENT"); } }
        public double BandComplaints { get { return S("BAND_COMPLAINTS"); } }
        public double BandPetition { get { return S("BAND_PETITION"); } }
        public double BandProtest { get { return S("BAND_PROTEST"); } }
        public double BandInjunction { get { return S("BAND_INJUNCTION"); } }
        public double PetitionRatePerTick { get { return S("PETITION_RATE_PER_TICK"); } }
        public double PetitionThreshold { get { return S("PETITION_THRESHOLD"); } }
        public double ReferendumDelayDays { get { return S("REFERENDUM_DELAY_DAYS"); } }
        public double ReferendumKeepThreshold { get { return S("REFERENDUM_KEEP_THRESHOLD"); } }
        public double ReferendumDeltaWeight { get { return S("REFERENDUM_DELTA_WEIGHT"); } }
        public double SabotageTriggerHours { get { return S("SABOTAGE_TRIGGER_HOURS"); } }
        public double FibreCutDurationH { get { return S("FIBRE_CUT_DURATION_H"); } }
        public double LocalHireGniPerFte { get { return S("LOCAL_HIRE_GNI_PER_FTE"); } }

        // Climate
        public double TownBearingDeg { get { return S("TOWN_BEARING_DEG"); } }
        public double TownSectorHalfDeg { get { return S("TOWN_SECTOR_HALF_DEG"); } }
        public double TempSeasonalMeanC { get { return S("TEMP_SEASONAL_MEAN"); } }
        public double TempSeasonalAmpC { get { return S("TEMP_SEASONAL_AMP"); } }
        public double TempDiurnalAmpC { get { return S("TEMP_DIURNAL_AMP"); } }
        public double TempNoiseAmpC { get { return S("TEMP_NOISE_AMP"); } }
        public Curve WetbulbDepression { get { return C("WETBULB_DEPRESSION"); } }
        public Curve WindSpeedMonth { get { return C("WIND_SPEED_MONTH"); } }
        public double WindDirNoiseDeg { get { return S("WIND_DIR_NOISE_DEG"); } }
        public Curve WindDirSeason { get { return C("WIND_DIR_SEASON"); } }
        public double HeatwaveThresholdC { get { return S("HEATWAVE_THRESHOLD"); } }
        public double DunkelflauteWindCf { get { return S("DUNKELFLAUTE_WIND_CF"); } }
        public double DunkelflauteIrradiance { get { return S("DUNKELFLAUTE_IRRADIANCE"); } }
        public double DroughtDryDays { get { return S("DROUGHT_DRY_DAYS"); } }
        public double DroughtRecoverDays { get { return S("DROUGHT_RECOVER_DAYS"); } }
        public double PrecipWetProbability { get { return S("PRECIP_WET_PROBABILITY"); } }

        // --- Parsing ---------------------------------------------------------

        /// <summary>The keys whose values are curves; everything else is a scalar.</summary>
        private static readonly string[] CurveKeys =
        {
            "EVAP_COP", "EVAP_WUE", "CHILLER_COP",
            "GRID_PRICE_SEASON", "GRID_PRICE_DIURNAL", "WIND_CF_MONTH",
            "WETBULB_DEPRESSION", "WIND_SPEED_MONTH", "WIND_DIR_SEASON"
        };

        /// <summary>
        /// Every key the simulation consumes. Parsing validates BOTH ways:
        /// a key in the file but not here is a typo (error), a key here but not
        /// in the file is a missing constant (error).
        /// </summary>
        private static readonly string[] RequiredScalarKeys =
        {
            "TOWN_WATER_DEMAND", "WATER_PRICE", "DROUGHT_WATER_TARIFF_MULT",
            "GPU_NODE_P_PEAK", "GPU_NODE_P_IDLE", "HEAT_FRACTION", "AUX_BASE_KW", "AUX_FRAC_OF_IT",
            "FREECOOL_T_THRESHOLD", "FREECOOL_COP", "DROUGHT_ALLOWANCE_FRAC",
            "RETAIL_MARKUP", "CONGESTION_K", "CONGESTION_GAMMA", "SCARCITY_EVENT_MULT",
            "SOLAR_YIELD_ANNUAL", "BATTERY_RTE", "BATTERY_CYCLE_LIFE",
            "DIESEL_COST_PER_KWH", "DIESEL_OM", "BATTERY_ARBITRAGE_SPREAD",
            "GRID_T0_CAP", "GRID_T0_CAPEX", "GRID_T0_LEAD_DAYS", "GRID_T0_SENTIMENT_GATE", "GRID_T0_REGION_CAP",
            "GRID_T1_CAP", "GRID_T1_CAPEX", "GRID_T1_LEAD_DAYS", "GRID_T1_SENTIMENT_GATE", "GRID_T1_REGION_CAP",
            "GRID_T2_CAP", "GRID_T2_CAPEX", "GRID_T2_LEAD_DAYS", "GRID_T2_SENTIMENT_GATE", "GRID_T2_REGION_CAP",
            "GRID_T3_CAP", "GRID_T3_CAPEX", "GRID_T3_LEAD_DAYS", "GRID_T3_SENTIMENT_GATE", "GRID_T3_REGION_CAP",
            "GRID_T4_CAP", "GRID_T4_CAPEX", "GRID_T4_LEAD_DAYS", "GRID_T4_SENTIMENT_GATE", "GRID_T4_REGION_CAP",
            "RATE_INFERENCE", "RATE_TRAINING", "RATE_SPOT",
            "SLA_INFERENCE", "SLA_PENALTY_K", "SLA_PENALTY_CAP",
            "CHECKPOINT_INTERVAL", "CHECKPOINT_OVERHEAD",
            "TRAINING_FAIL_PAYOUT_FRAC", "TRAINING_REQUIRED_FRAC",
            "REPUTATION_START", "REPUTATION_GAIN_MONTH", "REPUTATION_LOSS_BREACH",
            "REP_GATE_INFERENCE", "REP_GATE_TRAINING_MED",
            "INFERENCE_DIURNAL_AMP", "SPOT_DEPTH_MEAN_KW", "SPOT_DEPTH_VOLATILITY",
            "W_NOISE", "W_AIR", "W_WATER", "W_PRICE", "W_VISUAL",
            "HALFLIFE_NOISE", "HALFLIFE_AIR", "HALFLIFE_WATER", "HALFLIFE_PRICE", "HALFLIFE_VISUAL",
            "NIGHT_NOISE_MULT", "WIND_TOWARD_MULT", "WIND_AWAY_MULT",
            "PRICE_SENSITIVITY", "PRICE_BASELINE_ADAPT_PER_DAY",
            "NOISE_FAN_REF_KW", "NOISE_CHILLER_WEIGHT", "NOISE_DIESEL_WEIGHT",
            "NOISE_TURBINE_WEIGHT", "TURBINE_UNIT_KW", "NOISE_SCALE",
            "AIR_EMISSION_FACTOR", "DROUGHT_NUISANCE_MULT_MAX",
            "GNI_START", "GNI_ADJUST_RATE", "ESCALATION_HYSTERESIS",
            "BAND_CONTENT", "BAND_COMPLAINTS", "BAND_PETITION", "BAND_PROTEST", "BAND_INJUNCTION",
            "PETITION_RATE_PER_TICK", "PETITION_THRESHOLD",
            "REFERENDUM_DELAY_DAYS", "REFERENDUM_KEEP_THRESHOLD", "REFERENDUM_DELTA_WEIGHT",
            "SABOTAGE_TRIGGER_HOURS", "FIBRE_CUT_DURATION_H", "LOCAL_HIRE_GNI_PER_FTE",
            "TOWN_BEARING_DEG", "TOWN_SECTOR_HALF_DEG",
            "TEMP_SEASONAL_MEAN", "TEMP_SEASONAL_AMP", "TEMP_DIURNAL_AMP", "TEMP_NOISE_AMP",
            "WIND_DIR_NOISE_DEG", "HEATWAVE_THRESHOLD",
            "DUNKELFLAUTE_WIND_CF", "DUNKELFLAUTE_IRRADIANCE",
            "DROUGHT_DRY_DAYS", "DROUGHT_RECOVER_DAYS", "PRECIP_WET_PROBABILITY"
        };

        private Balance(Dictionary<string, string> kv, List<string> order)
        {
            var curveSet = new HashSet<string>(CurveKeys);
            var ci = CultureInfo.InvariantCulture;

            foreach (string key in order)
            {
                string val = kv[key];
                if (curveSet.Contains(key))
                {
                    Curve c = Curve.Parse(val);
                    _curves[key] = c;
                    _curveDefaults[key] = CurveText(c);
                }
                else
                {
                    // NumberStyles.Float explicitly: the default includes
                    // AllowThousands, under which InvariantCulture parses "0,5"
                    // as 5 with no error (audit finding #1).
                    double d = double.Parse(val, NumberStyles.Float, ci);
                    _scalars[key] = d;
                    _scalarDefaults[key] = d;
                }
                _orderedKeys.Add(key);
            }

            // Both-ways completeness.
            foreach (string k in RequiredScalarKeys)
                if (!_scalars.ContainsKey(k))
                    throw new FormatException("Tuning file is missing required key: " + k);
            foreach (string k in CurveKeys)
                if (!_curves.ContainsKey(k))
                    throw new FormatException("Tuning file is missing required key: " + k);
            var known = new HashSet<string>(RequiredScalarKeys);
            foreach (string k in CurveKeys) known.Add(k);
            foreach (string k in _orderedKeys)
                if (!known.Contains(k))
                    throw new FormatException("Tuning file contains unknown key: " + k);

            double wSum = WNoise + WAir + WWater + WPrice + WVisual;
            if (Math.Abs(wSum - 1.0) > 1e-9)
                throw new FormatException("Nuisance weights W_* must sum to 1.0, got " +
                    wSum.ToString(ci));

            RebuildTierArrays();
        }

        /// <summary>Parses tuning text: KEY = value, '#' comments, blank lines.</summary>
        public static Balance Parse(string text)
        {
            var kv = new Dictionary<string, string>();
            var order = new List<string>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    throw new FormatException("Tuning line " + (n + 1) + " is not KEY = value: " + line);
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (kv.ContainsKey(key))
                    throw new FormatException("Tuning key defined twice: " + key);
                kv[key] = val;
                order.Add(key);
            }
            return new Balance(kv, order);
        }
    }
}
