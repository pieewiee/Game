using System;
using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// The entire mutable world. Everything the simulation knows lives here:
    /// same seed + same scenario ⇒ identical SimState at every tick. No system
    /// keeps private state outside this object, and nothing here references
    /// Unity. Serialising this struct-of-lists IS the save format later.
    /// </summary>
    public sealed class SimState
    {
        public long Tick;
        public SimRandom Rng;
        public bool RunOver;                 // referendum lost — the run has ended
        public string RunOverReason = "";

        // --- Site: compute -------------------------------------------------
        public int NodesInstalled;           // GPU nodes; the only node type in M1

        // --- Site: cooling plant (kW_th ratings) ---------------------------
        public double FreecoolKwTh;
        public double EvapKwTh;
        public double ChillerKwTh;

        // --- Site: power assets --------------------------------------------
        public int GridTier;                 // 0..4
        public PendingPermit Permit;         // null when none in flight
        public double SolarKwp;
        public double WindKw;
        public double BatteryKwhCap;
        public double BatteryKw;             // charge/discharge power limit
        public double BatterySocKwh;
        public double BatteryCycles;         // equivalent full cycles
        public double DieselKw;
        public DieselPolicy Diesel = DieselPolicy.ProtectSla;

        // --- Contracts -----------------------------------------------------
        public readonly List<Contract> Contracts = new List<Contract>();
        public int NextContractId = 1;
        public bool SpotEnabled;
        public double SpotDepthKw;           // random-walk market depth
        public double Reputation;

        // --- Community -----------------------------------------------------
        /// <summary>Channel memories M_i, order: noise, air, water, price, visual.</summary>
        public readonly double[] ChannelMemory = new double[5];
        public double Gni;
        public EscalationStage Stage = EscalationStage.Content;
        public double PetitionSignatures;
        public long ReferendumVoteTick = -1;  // -1 = none scheduled
        public double GniAtPetitionTrigger;
        public bool ReferendumHeld;
        public double SabotageExposureTicks;  // cumulative ticks spent in the Sabotage band
        public long FibreCutUntilTick = -1;   // sabotage effect: no delivery until this tick
        public double LocalFte;               // goodwill source (scenario-set in M1)
        public double VisualPoints;           // fortification total (scenario-set in M1)
        public double PriceBaselineEurKwh;    // the town's remembered resident price

        // --- Water / climate-derived state ---------------------------------
        public bool DroughtActive;
        public int DryStreakDays;
        public int WetStreakDays;
        public long LastProcessedDay = -1;

        // --- Money ---------------------------------------------------------
        public double CashEur;

        // --- Log -----------------------------------------------------------
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public void Log(string category, string message)
        {
            Events.Add(new SimEvent(Tick, category, message));
        }

        public double MaxBillableKw { get { return NodesInstalled * _peakKw; } }
        private double _peakKw;
        public void SetNodeSpec(double peakKw) { _peakKw = peakKw; }
    }
}
