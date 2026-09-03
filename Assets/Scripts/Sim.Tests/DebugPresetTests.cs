using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Sim;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The "Vollausbau" debug preset: a full build-out must fill every slot
    /// through ordinary delta commands, never remove anything, charge no
    /// capex, and add exactly nothing when applied a second time.
    /// </summary>
    public class DebugPresetTests
    {
        private const string FirstShiftRelPath = "Assets/StreamingAssets/Scenarios/00-first-shift.scenario";

        private static Simulation NewSim(string scenarioText)
        {
            return new Simulation(TestData.LoadBalance(), Scenario.Parse(scenarioText));
        }

        private static TickReport ApplyAndTick(Simulation sim, out List<SimCommand> cmds)
        {
            cmds = DebugPresets.FullBuild(SiteInventory.From(sim.State), SiteLimits.Default);
            foreach (var c in cmds) sim.Enqueue(c);
            return sim.Tick();
        }

        private static int Count(List<SimCommand> cmds, CommandKind kind)
        {
            int n = 0;
            foreach (var c in cmds) if (c.Kind == kind) n++;
            return n;
        }

        private static double Plant(List<SimCommand> cmds, PlantKind kind)
        {
            foreach (var c in cmds)
                if (c.Kind == CommandKind.AddPlant && (int)c.A == (int)kind) return c.B;
            return 0.0;
        }

        [Test]
        public void FullBuildFillsEverySlotFromAnEmptySiteAndChargesNoCapex()
        {
            var sim = NewSim("SEED = 1\nCASH = 100000");
            double cashBefore = sim.State.CashEur;

            var plan = DebugPresets.PlanFullBuild(SiteInventory.From(sim.State), SiteLimits.Default);
            Assert.That(plan.AddNodes, Is.EqualTo(320), "32 racks x 10 nodes");
            Assert.That(plan.AddFreecoolUnits, Is.EqualTo(3));
            Assert.That(plan.AddEvapUnits, Is.EqualTo(2));
            Assert.That(plan.AddChillerUnits, Is.EqualTo(2));
            Assert.That(plan.AddBatteryPacks, Is.EqualTo(1));
            Assert.That(plan.AddSolarRows, Is.EqualTo(12));

            var r = ApplyAndTick(sim, out var cmds);
            Assert.That(Count(cmds, CommandKind.AddNodes), Is.EqualTo(1));
            Assert.That(Count(cmds, CommandKind.AddPlant), Is.EqualTo(7), "F, E, C, battery kWh + kW, solar, diesel");

            var s = sim.State;
            Assert.That(s.NodesInstalled, Is.EqualTo(320));
            Assert.That(s.FreecoolKwTh, Is.EqualTo(750.0));
            Assert.That(s.EvapKwTh, Is.EqualTo(500.0));
            Assert.That(s.ChillerKwTh, Is.EqualTo(500.0));
            Assert.That(s.BatteryKwhCap, Is.EqualTo(250.0));
            Assert.That(s.BatteryKw, Is.EqualTo(100.0));
            Assert.That(s.SolarKwp, Is.EqualTo(600.0));
            Assert.That(s.DieselKw, Is.EqualTo(1000.0));
            Assert.That(s.GridTier, Is.EqualTo(0), "the permit is not part of a build-out");

            // AddNodes/AddPlant are free today (only the tier permit is capex).
            // When plant capex lands this is the assertion that goes first.
            Assert.That(r.CapexEur, Is.EqualTo(0.0));
            Assert.That(cashBefore - s.CashEur, Is.LessThan(5000.0),
                "one hour of opex at most, no lump sum");
        }

        [Test]
        public void BaselineYardIsAlreadyFullSoOnlyPartialUnitsAreToppedUp()
        {
            // baseline-year: 85 nodes, 900/900 kW_th free-cool/evap (four
            // slots each — the yard's 8 are taken), 400 kWp, 500 kW diesel.
            var sim = TestData.NewBaselineSim();
            var plan = DebugPresets.PlanFullBuild(SiteInventory.From(sim.State), SiteLimits.Default);

            Assert.That(plan.AddNodes, Is.EqualTo(235));
            Assert.That(plan.AddFreecoolUnits, Is.EqualTo(0));
            Assert.That(plan.AddEvapUnits, Is.EqualTo(0));
            Assert.That(plan.AddChillerUnits, Is.EqualTo(0), "no free slot even though chillers are the priority");
            Assert.That(plan.AddBatteryPacks, Is.EqualTo(0));
            Assert.That(plan.AddFreecoolKwTh, Is.EqualTo(100.0), "900 -> 4 whole units of 250");
            Assert.That(plan.AddEvapKwTh, Is.EqualTo(100.0));
            Assert.That(plan.AddChillerKwTh, Is.EqualTo(0.0));
            Assert.That(plan.AddSolarRows, Is.EqualTo(4));
            Assert.That(plan.AddSolarKwp, Is.EqualTo(200.0));
            Assert.That(plan.AddDieselKw, Is.EqualTo(500.0));

            ApplyAndTick(sim, out _);
            var s = sim.State;
            Assert.That(s.NodesInstalled, Is.EqualTo(320));
            Assert.That(s.FreecoolKwTh, Is.EqualTo(1000.0));
            Assert.That(s.EvapKwTh, Is.EqualTo(1000.0));
            Assert.That(s.ChillerKwTh, Is.EqualTo(0.0));
            Assert.That(s.BatteryKwhCap, Is.EqualTo(0.0));
            Assert.That(s.SolarKwp, Is.EqualTo(600.0));
            Assert.That(s.DieselKw, Is.EqualTo(1000.0));
        }

        [Test]
        public void SecondApplicationAddsNothing()
        {
            var sim = TestData.NewBaselineSim();
            ApplyAndTick(sim, out var first);
            Assert.That(first.Count, Is.GreaterThan(0));

            var again = DebugPresets.PlanFullBuild(SiteInventory.From(sim.State), SiteLimits.Default);
            Assert.That(again.IsEmpty, Is.True, again.Describe());
            Assert.That(DebugPresets.FullBuild(SiteInventory.From(sim.State), SiteLimits.Default), Is.Empty);

            // And from a truly empty start as well.
            var empty = NewSim("SEED = 1\nCASH = 100000");
            ApplyAndTick(empty, out _);
            Assert.That(DebugPresets.PlanFullBuild(SiteInventory.From(empty.State), SiteLimits.Default).IsEmpty, Is.True);
        }

        [Test]
        public void PlanNeverEmitsNegativeDeltas()
        {
            var inventories = new List<SiteInventory>
            {
                SiteInventory.From(TestData.NewBaselineSim().State),
                new SiteInventory(),
                // Oversubscribed yard: more of everything than the mix wants.
                new SiteInventory { Nodes = 400, FreecoolKwTh = 2000, EvapKwTh = 1500, ChillerKwTh = 1250,
                                    SolarKwp = 900, BatteryKwhCap = 1000, DieselKw = 2500 },
                // Partial units everywhere.
                new SiteInventory { Nodes = 7, FreecoolKwTh = 10, EvapKwTh = 260, ChillerKwTh = 499,
                                    SolarKwp = 51, BatteryKwhCap = 1, DieselKw = 999 },
            };
            foreach (var inv in inventories)
            {
                var p = DebugPresets.PlanFullBuild(inv, SiteLimits.Default);
                Assert.That(p.AddNodes, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddFreecoolUnits, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddEvapUnits, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddChillerUnits, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddBatteryPacks, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddSolarRows, Is.GreaterThanOrEqualTo(0));
                Assert.That(p.AddFreecoolKwTh, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(p.AddEvapKwTh, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(p.AddChillerKwTh, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(p.AddSolarKwp, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(p.AddDieselKw, Is.GreaterThanOrEqualTo(0.0));
                foreach (var c in DebugPresets.FullBuild(inv, SiteLimits.Default))
                {
                    Assert.That(c.A, Is.GreaterThan(0.0) | Is.EqualTo(0.0), c.Kind.ToString());
                    Assert.That(c.B, Is.GreaterThanOrEqualTo(0.0), c.Kind.ToString());
                    if (c.Kind == CommandKind.AddPlant) Assert.That(c.B, Is.GreaterThan(0.0));
                }
            }

            // The baseline yard is full: nothing new, only top-ups.
            var baseline = DebugPresets.PlanFullBuild(inventories[0], SiteLimits.Default);
            Assert.That(baseline.AddFreecoolUnits, Is.EqualTo(0));
            Assert.That(baseline.AddEvapUnits, Is.EqualTo(0));

            // Oversubscribed: literally nothing to add (and never a removal).
            Assert.That(DebugPresets.PlanFullBuild(inventories[2], SiteLimits.Default).IsEmpty, Is.True);
        }

        [Test]
        public void FirstShiftScenarioGetsTheFullMix()
        {
            // 00-first-shift is what the game boots into: 20 nodes, one
            // free-cooling and one evap unit, 250 kW diesel, no solar.
            string text = File.ReadAllText(Path.Combine(TestData.RepoRoot(), FirstShiftRelPath));
            var sim = new Simulation(TestData.LoadBalance(), Scenario.Parse(text));
            var plan = DebugPresets.PlanFullBuild(SiteInventory.From(sim.State), SiteLimits.Default);

            Assert.That(plan.AddNodes, Is.EqualTo(300));
            Assert.That(plan.AddChillerUnits, Is.EqualTo(2));
            Assert.That(plan.AddBatteryPacks, Is.EqualTo(1));
            Assert.That(plan.AddEvapUnits, Is.EqualTo(1));
            Assert.That(plan.AddFreecoolUnits, Is.EqualTo(2));
            Assert.That(plan.AddFreecoolKwTh, Is.EqualTo(500.0));
            Assert.That(plan.AddEvapKwTh, Is.EqualTo(250.0));
            Assert.That(plan.AddChillerKwTh, Is.EqualTo(500.0));
            Assert.That(plan.AddSolarKwp, Is.EqualTo(600.0));
            Assert.That(plan.AddDieselKw, Is.EqualTo(750.0));

            var r = ApplyAndTick(sim, out var cmds);
            Assert.That(Plant(cmds, PlantKind.BatteryKwh), Is.EqualTo(250.0));
            Assert.That(Plant(cmds, PlantKind.BatteryKw), Is.EqualTo(100.0));
            Assert.That(r.CapexEur, Is.EqualTo(0.0));

            var s = sim.State;
            Assert.That(s.NodesInstalled, Is.EqualTo(320));
            Assert.That(s.FreecoolKwTh, Is.EqualTo(750.0));
            Assert.That(s.EvapKwTh, Is.EqualTo(500.0));
            Assert.That(s.ChillerKwTh, Is.EqualTo(500.0));
            Assert.That(s.BatteryKwhCap, Is.EqualTo(250.0));
            Assert.That(s.BatteryKw, Is.EqualTo(100.0));
            Assert.That(s.SolarKwp, Is.EqualTo(600.0));
            Assert.That(s.DieselKw, Is.EqualTo(1000.0));
            Assert.That(s.GridTier, Is.EqualTo(1), "tier untouched");

            // The report a client sees must produce the same (now empty) plan.
            Assert.That(SiteInventory.From(r), Is.EqualTo(SiteInventory.From(s)));
            Assert.That(DebugPresets.PlanFullBuild(SiteInventory.From(r), SiteLimits.Default).IsEmpty, Is.True);
        }

        [Test]
        public void ProtestTurnsAwayOnlyTheNodeDelivery()
        {
            var sim = TestData.NewBaselineSim();
            // Commands apply at tick start, before the community pass
            // re-derives the stage, so setting it directly is enough.
            sim.State.Stage = EscalationStage.Protest;

            ApplyAndTick(sim, out var cmds);
            Assert.That(Count(cmds, CommandKind.AddNodes), Is.EqualTo(1), "the planner does not know about the gate");

            var s = sim.State;
            Assert.That(s.NodesInstalled, Is.EqualTo(85), "delivery turned away");
            Assert.That(s.FreecoolKwTh, Is.EqualTo(1000.0), "plant still goes in");
            Assert.That(s.SolarKwp, Is.EqualTo(600.0));
            Assert.That(s.DieselKw, Is.EqualTo(1000.0));

            // What is left is exactly the node delivery — the UI's
            // "gate closed" state, not "fully built".
            var left = DebugPresets.PlanFullBuild(SiteInventory.From(s), SiteLimits.Default);
            Assert.That(left.IsEmpty, Is.False);
            Assert.That(left.IsEmptyExceptNodes, Is.True);
            Assert.That(left.AddNodes, Is.EqualTo(235));
        }

        [Test]
        public void FullBuildScenarioIsAlreadyFullyBuilt()
        {
            // The shipped end-state scenario must be exactly what the planner
            // produces, otherwise the two drift apart silently.
            string path = Path.Combine(TestData.RepoRoot(), "Assets/StreamingAssets/Scenarios/full-build.scenario");
            var sim = new Simulation(TestData.LoadBalance(), Scenario.Parse(File.ReadAllText(path)));
            var plan = DebugPresets.PlanFullBuild(SiteInventory.From(sim.State), SiteLimits.Default);
            Assert.That(plan.IsEmpty, Is.True, plan.Describe());
            Assert.That(sim.State.BatteryKw, Is.EqualTo(SiteLimits.Default.BatteryPackKw));
        }

        [Test]
        public void DescribeIsCultureInvariantAndNamesEveryDelta()
        {
            // Ledger lines are replicated, so the text must not depend on
            // the host's locale. de-DE groups and punctuates differently;
            // with the "0" format only a later format change would show it,
            // but the culture is set so that such a change is caught here.
            CultureInfo prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var plan = DebugPresets.PlanFullBuild(new SiteInventory { FreecoolKwTh = 10 }, SiteLimits.Default);
                string text = plan.Describe();
                Assert.That(text, Does.Contain("+320 nodes"));
                Assert.That(text, Does.Contain("+2 chiller (+500 kW_th)"));
                Assert.That(text, Does.Contain("+1 battery"));
                Assert.That(text, Does.Contain("+2 evap (+500 kW_th)"));
                Assert.That(text, Does.Contain("+2 free-cooling (+740 kW_th incl. 240 top-up)"));
                Assert.That(text, Does.Contain("+12 solar rows (+600 kWp)"));
                Assert.That(text, Does.Contain("+1000 kW diesel"));

                // Every row present but not every kWp: a top-up, never "+0 rows".
                var solarShort = new SiteInventory { Nodes = 320, FreecoolKwTh = 750, EvapKwTh = 500, ChillerKwTh = 500,
                                                     SolarKwp = 575, BatteryKwhCap = 250, DieselKw = 1000 };
                string topUp = DebugPresets.PlanFullBuild(solarShort, SiteLimits.Default).Describe();
                Assert.That(topUp, Is.EqualTo("+25 kWp solar top-up"));

                var full = new SiteInventory { Nodes = 320, FreecoolKwTh = 750, EvapKwTh = 500, ChillerKwTh = 500,
                                               SolarKwp = 600, BatteryKwhCap = 250, DieselKw = 1000 };
                Assert.That(DebugPresets.PlanFullBuild(full, SiteLimits.Default).Describe(), Does.Contain("fully built"));
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
        }
    }
}
