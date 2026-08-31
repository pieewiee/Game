using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Sim
{
    /// <summary>
    /// Every tunable number in the simulation, loaded from a flat key=value
    /// tuning file (see Assets/StreamingAssets/Tuning/balance.tuning).
    /// Nothing in Game.Sim hardcodes a balance constant; if a number appears in
    /// docs/balance-constants.md it must come through here under the same name.
    ///
    /// The format is deliberately not JSON: Unity ships no JSON library that is
    /// usable from a noEngineReferences assembly without adding a package, and
    /// M1 adds no dependencies. The parser is ~40 lines and the file diffs well.
    /// </summary>
    public sealed class Balance
    {
        // --- Time & world ---------------------------------------------------
        public readonly double TownWaterDemandM3Day;   // m³/day
        public readonly double WaterPriceEurM3;        // EUR/m³
        public readonly double DroughtWaterTariffMult; // × on WATER_PRICE while restricted

        // --- Hardware -------------------------------------------------------
        public readonly double GpuNodePeakKw;          // kW at u=1
        public readonly double GpuNodeIdleKw;          // kW at u=0
        public readonly double HeatFraction;           // kW_th per kW_e
        public readonly double AuxBaseKw;              // kW, flat
        public readonly double AuxFracOfIt;            // kW per kW of P_IT

        // --- Cooling --------------------------------------------------------
        public readonly Curve EvapCop;                 // COP vs T_wb °C
        public readonly Curve EvapWue;                 // L/kWh_IT vs T_wb °C
        public readonly Curve ChillerCop;              // COP vs T_db °C
        public readonly double FreecoolThresholdC;     // °C T_db
        public readonly double FreecoolCop;            // COP
        public readonly double DroughtAllowanceFrac;   // fraction of TOWN demand allowed while restricted

        // --- Power ----------------------------------------------------------
        public readonly Curve GridPriceSeason;         // EUR/kWh vs day-of-year
        public readonly Curve GridPriceDiurnal;        // multiplier vs hour-of-day
        public readonly double RetailMarkup;           // ×
        public readonly double CongestionK;            // -
        public readonly double CongestionGamma;        // -
        public readonly double ScarcityEventMult;      // ×
        public readonly double SolarKwhPerKwpYear;     // kWh/kWp/yr calibration target
        public readonly Curve WindCfMonth;             // capacity factor vs month 1..12
        public readonly double BatteryRte;             // round-trip efficiency
        public readonly double BatteryCycleLife;       // cycles to 80 %
        public readonly double DieselCostPerKwh;       // EUR/kWh fuel
        public readonly double DieselOmPerKwh;         // EUR/kWh wear
        public readonly double BatteryArbitrageSpread; // discharge when p ≥ avg × this

        // --- Grid tiers (index 0..4 = T0..T4) --------------------------------
        public readonly double[] TierCapKw;
        public readonly double[] TierCapexEur;
        public readonly double[] TierLeadDays;
        public readonly double[] TierSentimentGate;
        public readonly double[] TierRegionCapKw;

        // --- Contracts ------------------------------------------------------
        public readonly double RateInference;          // EUR/kWh_IT
        public readonly double RateTraining;           // EUR/kWh_IT
        public readonly double RateSpot;               // EUR/kWh_IT
        public readonly double SlaInference;           // uptime fraction
        public readonly double SlaPenaltyK;            // × monthly payout per unit shortfall
        public readonly double SlaPenaltyCap;          // fraction of monthly payout
        public readonly double CheckpointIntervalH;    // h
        public readonly double CheckpointOverhead;     // throughput fraction
        public readonly double TrainingFailPayoutFrac; // fraction paid on missed deadline
        public readonly double TrainingRequiredFrac;   // required kWh as fraction of block × term
        public readonly double ReputationStart;        // 0..100
        public readonly double ReputationGainMonth;    // per contract-month met
        public readonly double ReputationLossBreach;   // per breach × severity
        public readonly double RepGateInference;       // 0..100
        public readonly double RepGateTrainingMed;     // 0..100
        public readonly double InferenceDiurnalAmp;    // ±fraction of base load
        public readonly double SpotDepthMeanKw;        // spot market depth random-walk mean
        public readonly double SpotDepthVolatility;    // per-day random walk step fraction

        // --- Nuisance channels ---------------------------------------------
        public readonly double WNoise, WAir, WWater, WPrice, WVisual;      // weights, sum 1.0
        public readonly double HalflifeNoiseDays, HalflifeAirDays, HalflifeWaterDays,
                               HalflifePriceDays, HalflifeVisualDays;      // days
        public readonly double NightNoiseMult;         // × 22:00–06:00
        public readonly double WindTowardMult;         // × on air channel
        public readonly double WindAwayMult;           // × on air channel
        public readonly double PriceSensitivity;       // channel pts per fraction of price rise
        public readonly double PriceBaselineAdaptPerDay; // how fast the town's remembered price drifts
        public readonly double NoiseFanRefKw;          // fan power that counts as "1" in the log term
        public readonly double NoiseChillerWeight;     // relative loudness of chillers
        public readonly double NoiseDieselWeight;      // relative loudness of a genset
        public readonly double NoiseTurbineWeight;     // loudness per wind turbine
        public readonly double TurbineUnitKw;          // kW per modelled turbine, for the count
        public readonly double NoiseScale;             // channel pts per relative dB
        public readonly double AirEmissionFactor;      // channel pts per MWh diesel
        public readonly double DroughtNuisanceMultMax; // × on water channel under restriction

        // --- Sentiment / GNI -------------------------------------------------
        public readonly double GniStart;               // 0..100
        public readonly double GniAdjustRate;          // per tick
        public readonly double EscalationHysteresis;   // pts
        public readonly double BandContent, BandComplaints, BandPetition,
                               BandProtest, BandInjunction;                // lower edges
        public readonly double PetitionRatePerTick;    // signatures/tick per pt below 55
        public readonly double PetitionThreshold;      // signatures → referendum
        public readonly double ReferendumDelayDays;    // days between trigger and vote
        public readonly double ReferendumKeepThreshold; // keep score needed to survive the vote
        public readonly double ReferendumDeltaWeight;  // weight of ΔGNI since the petition triggered
        public readonly double SabotageTriggerHours;   // cumulative hours in the Sabotage band before an act
        public readonly double FibreCutDurationH;      // hours of zero delivery per fibre cut
        public readonly double LocalHireGniPerFte;     // goodwill pts per local FTE

        // --- Climate --------------------------------------------------------
        public readonly double TownBearingDeg;         // direction site→town, deg
        public readonly double TownSectorHalfDeg;      // half-angle of the town sector
        public readonly double TempSeasonalMeanC;      // annual mean °C
        public readonly double TempSeasonalAmpC;       // seasonal half-swing °C
        public readonly double TempDiurnalAmpC;        // diurnal half-swing °C
        public readonly double TempNoiseAmpC;          // multi-day weather wave amplitude °C
        public readonly Curve WetbulbDepression;       // °C below T_db vs day-of-year
        public readonly Curve WindSpeedMonth;          // mean m/s vs month 1..12
        public readonly double WindDirNoiseDeg;        // direction wander amplitude
        public readonly Curve WindDirSeason;           // prevailing "blows toward" bearing vs day-of-year
        public readonly double HeatwaveThresholdC;     // T_db above which scarcity ramps
        public readonly double DunkelflauteWindCf;     // wind CF below which (winter) counts
        public readonly double DunkelflauteIrradiance; // irradiance fraction below which counts
        public readonly double DroughtDryDays;         // consecutive low-precip days to trigger
        public readonly double DroughtRecoverDays;     // wet days to lift
        public readonly double PrecipWetProbability;   // chance a day is a wet day

        private Balance(Dictionary<string, string> kv)
        {
            var seen = new HashSet<string>();
            string S(string key)
            {
                if (!kv.TryGetValue(key, out string v))
                    throw new FormatException("Tuning file is missing required key: " + key);
                seen.Add(key);
                return v;
            }
            double D(string key) { return double.Parse(S(key), CultureInfo.InvariantCulture); }
            Curve C(string key) { return Curve.Parse(S(key)); }
            double[] Tier(string suffix)
            {
                var arr = new double[5];
                for (int i = 0; i < 5; i++) arr[i] = D("GRID_T" + i + "_" + suffix);
                return arr;
            }

            TownWaterDemandM3Day = D("TOWN_WATER_DEMAND");
            WaterPriceEurM3 = D("WATER_PRICE");
            DroughtWaterTariffMult = D("DROUGHT_WATER_TARIFF_MULT");

            GpuNodePeakKw = D("GPU_NODE_P_PEAK");
            GpuNodeIdleKw = D("GPU_NODE_P_IDLE");
            HeatFraction = D("HEAT_FRACTION");
            AuxBaseKw = D("AUX_BASE_KW");
            AuxFracOfIt = D("AUX_FRAC_OF_IT");

            EvapCop = C("EVAP_COP");
            EvapWue = C("EVAP_WUE");
            ChillerCop = C("CHILLER_COP");
            FreecoolThresholdC = D("FREECOOL_T_THRESHOLD");
            FreecoolCop = D("FREECOOL_COP");
            DroughtAllowanceFrac = D("DROUGHT_ALLOWANCE_FRAC");

            GridPriceSeason = C("GRID_PRICE_SEASON");
            GridPriceDiurnal = C("GRID_PRICE_DIURNAL");
            RetailMarkup = D("RETAIL_MARKUP");
            CongestionK = D("CONGESTION_K");
            CongestionGamma = D("CONGESTION_GAMMA");
            ScarcityEventMult = D("SCARCITY_EVENT_MULT");
            SolarKwhPerKwpYear = D("SOLAR_YIELD_ANNUAL");
            WindCfMonth = C("WIND_CF_MONTH");
            BatteryRte = D("BATTERY_RTE");
            BatteryCycleLife = D("BATTERY_CYCLE_LIFE");
            DieselCostPerKwh = D("DIESEL_COST_PER_KWH");
            DieselOmPerKwh = D("DIESEL_OM");
            BatteryArbitrageSpread = D("BATTERY_ARBITRAGE_SPREAD");

            TierCapKw = Tier("CAP");
            TierCapexEur = Tier("CAPEX");
            TierLeadDays = Tier("LEAD_DAYS");
            TierSentimentGate = Tier("SENTIMENT_GATE");
            TierRegionCapKw = Tier("REGION_CAP");

            RateInference = D("RATE_INFERENCE");
            RateTraining = D("RATE_TRAINING");
            RateSpot = D("RATE_SPOT");
            SlaInference = D("SLA_INFERENCE");
            SlaPenaltyK = D("SLA_PENALTY_K");
            SlaPenaltyCap = D("SLA_PENALTY_CAP");
            CheckpointIntervalH = D("CHECKPOINT_INTERVAL");
            CheckpointOverhead = D("CHECKPOINT_OVERHEAD");
            TrainingFailPayoutFrac = D("TRAINING_FAIL_PAYOUT_FRAC");
            TrainingRequiredFrac = D("TRAINING_REQUIRED_FRAC");
            ReputationStart = D("REPUTATION_START");
            ReputationGainMonth = D("REPUTATION_GAIN_MONTH");
            ReputationLossBreach = D("REPUTATION_LOSS_BREACH");
            RepGateInference = D("REP_GATE_INFERENCE");
            RepGateTrainingMed = D("REP_GATE_TRAINING_MED");
            InferenceDiurnalAmp = D("INFERENCE_DIURNAL_AMP");
            SpotDepthMeanKw = D("SPOT_DEPTH_MEAN_KW");
            SpotDepthVolatility = D("SPOT_DEPTH_VOLATILITY");

            WNoise = D("W_NOISE"); WAir = D("W_AIR"); WWater = D("W_WATER");
            WPrice = D("W_PRICE"); WVisual = D("W_VISUAL");
            double wSum = WNoise + WAir + WWater + WPrice + WVisual;
            if (Math.Abs(wSum - 1.0) > 1e-9)
                throw new FormatException("Nuisance weights W_* must sum to 1.0, got " +
                    wSum.ToString(CultureInfo.InvariantCulture));
            HalflifeNoiseDays = D("HALFLIFE_NOISE");
            HalflifeAirDays = D("HALFLIFE_AIR");
            HalflifeWaterDays = D("HALFLIFE_WATER");
            HalflifePriceDays = D("HALFLIFE_PRICE");
            HalflifeVisualDays = D("HALFLIFE_VISUAL");
            NightNoiseMult = D("NIGHT_NOISE_MULT");
            WindTowardMult = D("WIND_TOWARD_MULT");
            WindAwayMult = D("WIND_AWAY_MULT");
            PriceSensitivity = D("PRICE_SENSITIVITY");
            PriceBaselineAdaptPerDay = D("PRICE_BASELINE_ADAPT_PER_DAY");
            NoiseFanRefKw = D("NOISE_FAN_REF_KW");
            NoiseChillerWeight = D("NOISE_CHILLER_WEIGHT");
            NoiseDieselWeight = D("NOISE_DIESEL_WEIGHT");
            NoiseTurbineWeight = D("NOISE_TURBINE_WEIGHT");
            TurbineUnitKw = D("TURBINE_UNIT_KW");
            NoiseScale = D("NOISE_SCALE");
            AirEmissionFactor = D("AIR_EMISSION_FACTOR");
            DroughtNuisanceMultMax = D("DROUGHT_NUISANCE_MULT_MAX");

            GniStart = D("GNI_START");
            GniAdjustRate = D("GNI_ADJUST_RATE");
            EscalationHysteresis = D("ESCALATION_HYSTERESIS");
            BandContent = D("BAND_CONTENT");
            BandComplaints = D("BAND_COMPLAINTS");
            BandPetition = D("BAND_PETITION");
            BandProtest = D("BAND_PROTEST");
            BandInjunction = D("BAND_INJUNCTION");
            PetitionRatePerTick = D("PETITION_RATE_PER_TICK");
            PetitionThreshold = D("PETITION_THRESHOLD");
            ReferendumDelayDays = D("REFERENDUM_DELAY_DAYS");
            ReferendumKeepThreshold = D("REFERENDUM_KEEP_THRESHOLD");
            ReferendumDeltaWeight = D("REFERENDUM_DELTA_WEIGHT");
            SabotageTriggerHours = D("SABOTAGE_TRIGGER_HOURS");
            FibreCutDurationH = D("FIBRE_CUT_DURATION_H");
            LocalHireGniPerFte = D("LOCAL_HIRE_GNI_PER_FTE");

            TownBearingDeg = D("TOWN_BEARING_DEG");
            TownSectorHalfDeg = D("TOWN_SECTOR_HALF_DEG");
            TempSeasonalMeanC = D("TEMP_SEASONAL_MEAN");
            TempSeasonalAmpC = D("TEMP_SEASONAL_AMP");
            TempDiurnalAmpC = D("TEMP_DIURNAL_AMP");
            TempNoiseAmpC = D("TEMP_NOISE_AMP");
            WetbulbDepression = C("WETBULB_DEPRESSION");
            WindSpeedMonth = C("WIND_SPEED_MONTH");
            WindDirNoiseDeg = D("WIND_DIR_NOISE_DEG");
            WindDirSeason = C("WIND_DIR_SEASON");
            HeatwaveThresholdC = D("HEATWAVE_THRESHOLD");
            DunkelflauteWindCf = D("DUNKELFLAUTE_WIND_CF");
            DunkelflauteIrradiance = D("DUNKELFLAUTE_IRRADIANCE");
            DroughtDryDays = D("DROUGHT_DRY_DAYS");
            DroughtRecoverDays = D("DROUGHT_RECOVER_DAYS");
            PrecipWetProbability = D("PRECIP_WET_PROBABILITY");

            // Typo guard: an unknown key in the file is almost always a
            // misspelled attempt to tune something, which would otherwise be
            // silently ignored — the worst possible failure mode for balancing.
            foreach (string key in kv.Keys)
                if (!seen.Contains(key))
                    throw new FormatException("Tuning file contains unknown key: " + key);
        }

        /// <summary>Parses tuning text: KEY = value, '#' comments, blank lines.</summary>
        public static Balance Parse(string text)
        {
            var kv = new Dictionary<string, string>();
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
            }
            return new Balance(kv);
        }
    }
}
