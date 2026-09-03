using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Sim
{
    /// <summary>What the site owns right now — the fields both the host's
    /// SimState and a client's replicated TickReport can answer, so the same
    /// plan is computed on every machine.</summary>
    public struct SiteInventory : IEquatable<SiteInventory>
    {
        public int Nodes;
        public double FreecoolKwTh, EvapKwTh, ChillerKwTh, SolarKwp, BatteryKwhCap, DieselKw;

        public static SiteInventory From(SimState s)
        {
            return new SiteInventory
            {
                Nodes = s.NodesInstalled,
                FreecoolKwTh = s.FreecoolKwTh, EvapKwTh = s.EvapKwTh, ChillerKwTh = s.ChillerKwTh,
                SolarKwp = s.SolarKwp, BatteryKwhCap = s.BatteryKwhCap, DieselKw = s.DieselKw,
            };
        }

        /// <summary>Client path. TickReport carries no BatteryKw: the plan only
        /// ever adds BatteryKw together with a new pack, so a site with
        /// BATTERY_KWH > 0 and BATTERY_KW = 0 is left as it is by design.</summary>
        public static SiteInventory From(in TickReport r)
        {
            return new SiteInventory
            {
                Nodes = r.NodesInstalled,
                FreecoolKwTh = r.FreecoolKwTh, EvapKwTh = r.EvapKwTh, ChillerKwTh = r.ChillerKwTh,
                SolarKwp = r.SolarKwp, BatteryKwhCap = r.BatteryKwhCap, DieselKw = r.DieselKw,
            };
        }

        public bool Equals(SiteInventory o)
        {
            return Nodes == o.Nodes && FreecoolKwTh == o.FreecoolKwTh && EvapKwTh == o.EvapKwTh &&
                   ChillerKwTh == o.ChillerKwTh && SolarKwp == o.SolarKwp &&
                   BatteryKwhCap == o.BatteryKwhCap && DieselKw == o.DieselKw;
        }

        public override bool Equals(object obj) { return obj is SiteInventory o && Equals(o); }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Nodes;
                h = h * 31 + FreecoolKwTh.GetHashCode();
                h = h * 31 + EvapKwTh.GetHashCode();
                h = h * 31 + ChillerKwTh.GetHashCode();
                h = h * 31 + SolarKwp.GetHashCode();
                h = h * 31 + BatteryKwhCap.GetHashCode();
                h = h * 31 + DieselKw.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>Site geometry and unit sizes. The Unity layer passes the live
    /// slot counts (SiteBuilder) and the FacilityController constants; Default
    /// mirrors them for tests and headless use.</summary>
    public struct SiteLimits
    {
        public int RackSlots, PlantSlots, SolarSlots, NodesPerRack;
        public double PlantUnitKwTh, SolarRowKwp, BatteryPackKwh, BatteryPackKw, DieselTargetKw;

        public static SiteLimits Default
        {
            get
            {
                return new SiteLimits
                {
                    RackSlots = 32, PlantSlots = 8, SolarSlots = 12, NodesPerRack = 10,
                    PlantUnitKwTh = 250, SolarRowKwp = 50, BatteryPackKwh = 250, BatteryPackKw = 100,
                    DieselTargetKw = 1000,
                };
            }
        }
    }

    /// <summary>Every delta a full build-out adds. Unit counts drive the
    /// ghost visuals; the kW figures are the actual command payloads and
    /// include the top-up that rounds a partial unit up to a whole one.</summary>
    public struct FullBuildPlan
    {
        public int AddNodes, AddFreecoolUnits, AddEvapUnits, AddChillerUnits, AddBatteryPacks, AddSolarRows;
        public double AddFreecoolKwTh, AddEvapKwTh, AddChillerKwTh, AddSolarKwp, AddDieselKw;
        /// <summary>Unit size the plan was made with, so Describe can split a
        /// kW_th delta into whole units and top-up.</summary>
        public double PlantUnitKwTh;

        /// <summary>Nothing at all to add — the site is fully built.</summary>
        public bool IsEmpty { get { return AddNodes == 0 && IsEmptyExceptNodes; } }

        /// <summary>Only the node delivery (if anything) is outstanding — the
        /// button's state during Protest, when the gate refuses racks.</summary>
        public bool IsEmptyExceptNodes
        {
            get
            {
                return AddFreecoolUnits == 0 && AddEvapUnits == 0 && AddChillerUnits == 0 &&
                       AddBatteryPacks == 0 && AddSolarRows == 0 &&
                       AddFreecoolKwTh == 0 && AddEvapKwTh == 0 && AddChillerKwTh == 0 &&
                       AddSolarKwp == 0 && AddDieselKw == 0;
            }
        }

        /// <summary>One ledger-ready line, e.g. "+235 nodes, +100 kW_th
        /// free-cooling top-up, +4 solar rows (+200 kWp), +500 kW diesel".
        /// Invariant culture: ledger lines are replicated to every client.</summary>
        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            double unitKwTh = PlantUnitKwTh; // local functions in a struct cannot touch 'this'
            void Part(string text)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(text);
            }
            void Cooling(string name, int units, double kwTh)
            {
                if (units <= 0 && kwTh <= 0) return;
                double topUp = kwTh - units * unitKwTh;
                if (units <= 0)
                {
                    Part("+" + kwTh.ToString("0", ci) + " kW_th " + name + " top-up");
                    return;
                }
                string s = "+" + units.ToString(ci) + " " + name + " (+" + kwTh.ToString("0", ci) + " kW_th";
                if (topUp > 0) s += " incl. " + topUp.ToString("0", ci) + " top-up";
                Part(s + ")");
            }

            if (AddNodes > 0) Part("+" + AddNodes.ToString(ci) + " nodes");
            Cooling("chiller", AddChillerUnits, AddChillerKwTh);
            if (AddBatteryPacks > 0) Part("+" + AddBatteryPacks.ToString(ci) + " battery");
            Cooling("evap", AddEvapUnits, AddEvapKwTh);
            Cooling("free-cooling", AddFreecoolUnits, AddFreecoolKwTh);
            // A yard between 550 and 600 kWp has every row but not every kWp.
            if (AddSolarRows > 0)
                Part("+" + AddSolarRows.ToString(ci) + " solar rows (+" + AddSolarKwp.ToString("0", ci) + " kWp)");
            else if (AddSolarKwp > 0)
                Part("+" + AddSolarKwp.ToString("0", ci) + " kWp solar top-up");
            if (AddDieselKw > 0) Part("+" + AddDieselKw.ToString("0", ci) + " kW diesel");
            return sb.Length > 0 ? sb.ToString() : "nothing to add - site is fully built";
        }
    }

    /// <summary>
    /// "Vollausbau": the debug preset that fills every slot the site has. Pure
    /// planning — it reads an inventory and emits ordinary AddNodes/AddPlant
    /// DELTA commands, so the result goes through the same recorded, netted
    /// path as any player placement and replays from a save.
    ///
    /// Neither AddNodes nor AddPlant charges cash (Simulation.ApplyCommand;
    /// the only capex in the sim is the tier permit). Revisit when plant/node
    /// capex (cooling-water.md §1 CAPEX lines) is implemented; the test
    /// FullBuildFillsEverySlotFromAnEmptySiteAndChargesNoCapex will fail first.
    ///
    /// The grid tier is deliberately NOT part of the plan: it is a permit with
    /// a lead time gated on cash, GNI and a referendum, and no command sets it.
    /// </summary>
    public static class DebugPresets
    {
        /// <summary>Target yard mix for an empty 8-slot yard. Chillers are the
        /// only water-free summer capacity, so a full build owns them; free
        /// cooling stays the majority but is never stacked past the mix
        /// (cooling-water.md §1.3, §3).</summary>
        public const int TargetFreecoolUnits = 3, TargetEvapUnits = 2, TargetChillerUnits = 2, TargetBatteryPacks = 1;

        /// <summary>THE ceil rule the visual mirror uses: a partial unit
        /// occupies a whole slot.</summary>
        public static int Units(double kw, double unit)
        {
            return unit > 0 && kw > 0 ? (int)Math.Ceiling(kw / unit) : 0;
        }

        public static FullBuildPlan PlanFullBuild(in SiteInventory inv, in SiteLimits lim)
        {
            var plan = new FullBuildPlan { PlantUnitKwTh = lim.PlantUnitKwTh };
            plan.AddNodes = Math.Max(0, lim.RackSlots * lim.NodesPerRack - inv.Nodes);

            // Yard: fill deficits toward the target mix in priority order
            // chiller -> battery -> evap -> freecool, never removing anything.
            int haveF = Units(inv.FreecoolKwTh, lim.PlantUnitKwTh);
            int haveE = Units(inv.EvapKwTh, lim.PlantUnitKwTh);
            int haveC = Units(inv.ChillerKwTh, lim.PlantUnitKwTh);
            int haveB = inv.BatteryKwhCap > 0 ? 1 : 0;
            int free = Math.Max(0, lim.PlantSlots - (haveF + haveE + haveC + haveB));
            plan.AddChillerUnits = Take(TargetChillerUnits, haveC, ref free);
            plan.AddBatteryPacks = Take(TargetBatteryPacks, haveB, ref free);
            plan.AddEvapUnits = Take(TargetEvapUnits, haveE, ref free);
            plan.AddFreecoolUnits = Take(TargetFreecoolUnits, haveF, ref free);
            // A yard larger than the mix (never the shipped 8) gets chillers
            // for the rest: the drought-proof choice, and it cannot oversubscribe.
            plan.AddChillerUnits += free;
            free = 0;

            // Top-up: after the build every unit is a real whole unit, which
            // is what makes a second application add exactly nothing.
            plan.AddFreecoolKwTh = TopUp(haveF + plan.AddFreecoolUnits, inv.FreecoolKwTh, lim.PlantUnitKwTh);
            plan.AddEvapKwTh = TopUp(haveE + plan.AddEvapUnits, inv.EvapKwTh, lim.PlantUnitKwTh);
            plan.AddChillerKwTh = TopUp(haveC + plan.AddChillerUnits, inv.ChillerKwTh, lim.PlantUnitKwTh);

            int solarRows = Math.Min(lim.SolarSlots, Units(inv.SolarKwp, lim.SolarRowKwp));
            plan.AddSolarRows = Math.Max(0, lim.SolarSlots - solarRows);
            plan.AddSolarKwp = Math.Max(0.0, lim.SolarSlots * lim.SolarRowKwp - inv.SolarKwp);

            // Diesel has no slot and no visual: a plain sim number.
            plan.AddDieselKw = Math.Max(0.0, lim.DieselTargetKw - inv.DieselKw);
            return plan;
        }

        private static int Take(int target, int have, ref int free)
        {
            int add = Math.Min(Math.Max(0, target - have), free);
            free -= add;
            return add;
        }

        private static double TopUp(int unitsAfter, double current, double unit)
        {
            return Math.Max(0.0, unitsAfter * unit - current);
        }

        /// <summary>The plan as commands, in a fixed order, zero deltas
        /// omitted. The list is empty exactly when plan.IsEmpty.</summary>
        public static List<SimCommand> FullBuild(in SiteInventory inv, in SiteLimits lim)
        {
            FullBuildPlan plan = PlanFullBuild(inv, lim);
            var cmds = new List<SimCommand>(8);
            if (plan.AddNodes > 0)
                cmds.Add(new SimCommand { Kind = CommandKind.AddNodes, A = plan.AddNodes });
            AddPlant(cmds, PlantKind.FreecoolKwTh, plan.AddFreecoolKwTh);
            AddPlant(cmds, PlantKind.EvapKwTh, plan.AddEvapKwTh);
            AddPlant(cmds, PlantKind.ChillerKwTh, plan.AddChillerKwTh);
            if (plan.AddBatteryPacks > 0)
            {
                // One pack = both figures, exactly like a placed pack.
                AddPlant(cmds, PlantKind.BatteryKwh, plan.AddBatteryPacks * lim.BatteryPackKwh);
                AddPlant(cmds, PlantKind.BatteryKw, plan.AddBatteryPacks * lim.BatteryPackKw);
            }
            AddPlant(cmds, PlantKind.SolarKwp, plan.AddSolarKwp);
            AddPlant(cmds, PlantKind.DieselKw, plan.AddDieselKw);
            return cmds;
        }

        private static void AddPlant(List<SimCommand> cmds, PlantKind kind, double delta)
        {
            if (delta <= 0) return;
            cmds.Add(new SimCommand { Kind = CommandKind.AddPlant, A = (int)kind, B = delta });
        }
    }
}
