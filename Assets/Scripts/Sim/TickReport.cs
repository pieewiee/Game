using System;
using System.Globalization;
using System.Text;

namespace Game.Sim
{
    /// <summary>
    /// Every meter, once per tick. This is simultaneously the CSV row, the
    /// debug-UI data source (M2), and the thing the balance tests assert on —
    /// one struct so they can never disagree about what happened.
    /// All energy fields are kWh over the tick (tick = 1 h, so kW ≡ kWh).
    /// </summary>
    public struct TickReport
    {
        public long Tick;

        // climate
        public double TdbC, TwbC, WindSpeedMs, WindTowardDeg;
        public bool WindTowardTown, DroughtActive, HeatwaveActive, DunkelflauteActive;
        public double IrradianceFrac, WindCf, ScarcityMult;

        // load
        public double RequestedBillableKw;   // what contracts asked for
        public double DeliveredBillableKw;   // what they got
        public double ThetaCool, ThetaPower; // throttle factors applied
        public double Utilisation;           // final u across the fleet
        public double PItKw, PCoolKw, PAuxKw;

        // cooling
        public double QItKwTh;
        public double QFreecoolKwTh, QEvapKwTh, QChillerKwTh, QUnremovedKwTh;
        public double WaterLPerH;

        // power dispatch (all kWh this tick)
        public double SolarPotentialKwh, WindPotentialKwh;
        public double SolarUsedKwh, WindUsedKwh, CurtailedKwh;
        public double GridImportKwh, DieselKwh;
        public double BatteryChargeKwh, BatteryDischargeKwh, BatterySocKwh;
        public double LoadShedKwh;           // demand nothing could cover (brownout)

        // price
        public double PBaseEurKwh, PGridEurKwh, PResidentEurKwh, PBaselineEurKwh;
        public double LoadRatio, CongestionMult;

        // nuisance channels (instantaneous) and memories
        public double NNoise, NAir, NWater, NPrice, NVisual;
        public double MNoise, MAir, MWater, MPrice, MVisual;

        // media & facility (M3-M5)
        public double Credibility, AccidentScore, RouteLossKw, OutageFrac, CoolingDerateMult;
        public double SetpointC, WaterValveFrac;
        public int NodesInstalled, GridTier;
        public double EvapKwTh, ChillerKwTh, FreecoolKwTh, SolarKwp, BatteryKwhCap, DieselKw;

        // community
        public double GniTarget, Gni;
        public EscalationStage Stage;
        public double PetitionSignatures;
        public double Reputation;

        // money (EUR this tick)
        public double RevenueEur, EnergyCostEur, WaterCostEur, PenaltyEur, CapexEur;
        public double CashEur;

        // contracts
        public int ActiveContracts;
        public double TrainingProgressFrac;  // 0..1 of the most urgent training run, -1 if none

        public const string CsvHeader =
            "tick,day,hour,month," +
            "tdb_c,twb_c,wind_ms,wind_toward_deg,wind_toward_town,irradiance_frac,wind_cf," +
            "drought,heatwave,dunkelflaute,scarcity_mult," +
            "requested_kw,delivered_kw,theta_cool,theta_power,utilisation," +
            "p_it_kw,p_cool_kw,p_aux_kw," +
            "q_it_kwth,q_freecool_kwth,q_evap_kwth,q_chiller_kwth,q_unremoved_kwth,water_l_per_h," +
            "solar_potential_kwh,wind_potential_kwh,solar_used_kwh,wind_used_kwh,curtailed_kwh," +
            "grid_import_kwh,diesel_kwh,battery_charge_kwh,battery_discharge_kwh,battery_soc_kwh,load_shed_kwh," +
            "p_base_eur,p_grid_eur,p_resident_eur,p_baseline_eur,load_ratio,congestion_mult," +
            "n_noise,n_air,n_water,n_price,n_visual," +
            "m_noise,m_air,m_water,m_price,m_visual," +
            "credibility,accident_score,route_loss_kw,outage_frac,cooling_derate," +
            "setpoint_c,water_valve_frac,nodes_installed,grid_tier," +
            "evap_kwth,chiller_kwth,freecool_kwth,solar_kwp,battery_kwh_cap,diesel_kw," +
            "gni_target,gni,stage,petition_signatures,reputation," +
            "revenue_eur,energy_cost_eur,water_cost_eur,penalty_eur,capex_eur,cash_eur," +
            "active_contracts,training_progress_frac";

        public string ToCsvRow()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(640);
            void D(double v) { sb.Append(v.ToString("0.####", ci)); sb.Append(','); }
            void I(long v) { sb.Append(v.ToString(ci)); sb.Append(','); }
            void B(bool v) { sb.Append(v ? '1' : '0'); sb.Append(','); }

            I(Tick); I(SimClock.DayIndex(Tick)); I(SimClock.HourOfDay(Tick)); I(SimClock.Month(Tick));
            D(TdbC); D(TwbC); D(WindSpeedMs); D(WindTowardDeg); B(WindTowardTown); D(IrradianceFrac); D(WindCf);
            B(DroughtActive); B(HeatwaveActive); B(DunkelflauteActive); D(ScarcityMult);
            D(RequestedBillableKw); D(DeliveredBillableKw); D(ThetaCool); D(ThetaPower); D(Utilisation);
            D(PItKw); D(PCoolKw); D(PAuxKw);
            D(QItKwTh); D(QFreecoolKwTh); D(QEvapKwTh); D(QChillerKwTh); D(QUnremovedKwTh); D(WaterLPerH);
            D(SolarPotentialKwh); D(WindPotentialKwh); D(SolarUsedKwh); D(WindUsedKwh); D(CurtailedKwh);
            D(GridImportKwh); D(DieselKwh); D(BatteryChargeKwh); D(BatteryDischargeKwh); D(BatterySocKwh); D(LoadShedKwh);
            D(PBaseEurKwh); D(PGridEurKwh); D(PResidentEurKwh); D(PBaselineEurKwh); D(LoadRatio); D(CongestionMult);
            D(NNoise); D(NAir); D(NWater); D(NPrice); D(NVisual);
            D(MNoise); D(MAir); D(MWater); D(MPrice); D(MVisual);
            D(Credibility); D(AccidentScore); D(RouteLossKw); D(OutageFrac); D(CoolingDerateMult);
            D(SetpointC); D(WaterValveFrac); I(NodesInstalled); I(GridTier);
            D(EvapKwTh); D(ChillerKwTh); D(FreecoolKwTh); D(SolarKwp); D(BatteryKwhCap); D(DieselKw);
            D(GniTarget); D(Gni);
            sb.Append(Stage.ToString()); sb.Append(',');
            D(PetitionSignatures); D(Reputation);
            D(RevenueEur); D(EnergyCostEur); D(WaterCostEur); D(PenaltyEur); D(CapexEur); D(CashEur);
            I(ActiveContracts);
            sb.Append(TrainingProgressFrac.ToString("0.####", ci));
            return sb.ToString();
        }
    }
}
