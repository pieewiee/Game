using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The Milestone 3–5 command surface: facility placement, routed-run
    /// losses, and the hazard/media consequences the 3D layer feeds into the
    /// simulation. Everything the presentation can do to the sim goes through
    /// these commands, so they are the contract worth pinning.
    /// </summary>
    [TestFixture]
    public class HazardCommandTests
    {
        private static Simulation NewSim(string extra = "")
        {
            return new Simulation(TestData.LoadBalance(), Scenario.Parse(
                "SEED = 5\nNODES = 40\nGRID_TIER = 1\nFREECOOL_KWTH = 500\nEVAP_KWTH = 500\n" +
                "DIESEL_KW = 300\nSPOT_ENABLED = 1\nREPUTATION = 62\nCASH = 500000\n" +
                "AT 0 SIGN_INFERENCE 200 300\n" + extra));
        }

        private static void Cmd(Simulation sim, CommandKind kind, double a = 0, double b = 0)
        {
            sim.Enqueue(new SimCommand { Kind = kind, A = a, B = b });
        }

        [Test]
        public void SetPlantChangesEveryAsset()
        {
            Simulation sim = NewSim();
            Cmd(sim, CommandKind.SetPlant, (int)PlantKind.ChillerKwTh, 750);
            Cmd(sim, CommandKind.SetPlant, (int)PlantKind.SolarKwp, 123);
            Cmd(sim, CommandKind.SetPlant, (int)PlantKind.DieselKw, 999);
            sim.Tick();
            Assert.That(sim.State.ChillerKwTh, Is.EqualTo(750));
            Assert.That(sim.State.SolarKwp, Is.EqualTo(123));
            Assert.That(sim.State.DieselKw, Is.EqualTo(999));
        }

        [Test]
        public void RouteLossEntersAuxDemandAndTheLedgerStillCloses()
        {
            Simulation sim = NewSim();
            TickReport before = sim.Tick();
            Cmd(sim, CommandKind.SetRouteLossKw, 40);
            TickReport after = sim.Tick();
            Assert.That(after.PAuxKw, Is.GreaterThan(before.PAuxKw + 35),
                "route loss must appear in aux demand");
            double lhs = after.SolarPotentialKwh + after.WindPotentialKwh
                       + after.GridImportKwh + after.DieselKwh + after.BatteryDischargeKwh;
            double rhs = after.PItKw + after.PCoolKw + after.PAuxKw
                       - after.LoadShedKwh + after.BatteryChargeKwh + after.CurtailedKwh;
            Assert.That(lhs, Is.EqualTo(rhs).Within(1e-6), "energy ledger must still close");
        }

        [Test]
        public void DestroyNodesShrinksTheFleetAndNeverGoesNegative()
        {
            Simulation sim = NewSim();
            Cmd(sim, CommandKind.DestroyNodes, 15);
            sim.Tick();
            Assert.That(sim.State.NodesInstalled, Is.EqualTo(25));
            Cmd(sim, CommandKind.DestroyNodes, 999);
            sim.Tick();
            Assert.That(sim.State.NodesInstalled, Is.EqualTo(0));
        }

        [Test]
        public void EpoCutsDeliveryForItsDuration()
        {
            Simulation sim = NewSim();
            for (int t = 0; t < 48; t++) sim.Tick(); // settle
            TickReport before = sim.Tick();
            Assert.That(before.DeliveredBillableKw, Is.GreaterThan(50), "test premise: load exists");
            Cmd(sim, CommandKind.EpoTrip, 1.0, 2);
            TickReport during = sim.Tick();
            Assert.That(during.DeliveredBillableKw, Is.EqualTo(0).Within(1e-9),
                "full EPO must cut all delivery");
            sim.Tick(); // second outage hour
            TickReport afterR = sim.Tick();
            Assert.That(afterR.DeliveredBillableKw, Is.GreaterThan(50),
                "delivery must resume after the outage window");
        }

        [Test]
        public void WaterValveScalesEvaporativeCapacity()
        {
            // Evap-only site in July: closing the valve must throttle cooling.
            var sim = new Simulation(TestData.LoadBalance(), Scenario.Parse(
                "SEED = 9\nNODES = 60\nGRID_TIER = 1\nEVAP_KWTH = 700\nSPOT_ENABLED = 1\nREPUTATION = 62\n" +
                "AT 0 SIGN_INFERENCE 400 300\n"));
            long julyTick = 190L * SimClock.TicksPerDay;
            while (sim.State.Tick < julyTick) sim.Tick();
            Cmd(sim, CommandKind.SetWaterValve, 0.1);
            sim.Tick();
            TickReport r = sim.Tick();
            Assert.That(r.ThetaCool, Is.LessThan(1.0), "a nearly-shut valve must throttle in July");
            Assert.That(r.QEvapKwTh, Is.LessThanOrEqualTo(700 * 0.1 + 1e-6));
            Cmd(sim, CommandKind.SetWaterValve, 1.0);
            sim.Tick();
            TickReport r2 = sim.Tick();
            Assert.That(r2.QEvapKwTh, Is.GreaterThan(r.QEvapKwTh), "reopening must restore capacity");
        }

        [Test]
        public void CoolingDerateThrottlesLikeMissingPanels()
        {
            Simulation sim = NewSim();
            for (int t = 0; t < 24; t++) sim.Tick();
            Cmd(sim, CommandKind.SetCoolingDerate, 0.05);
            sim.Tick();
            TickReport r = sim.Tick();
            Assert.That(r.ThetaCool, Is.LessThan(1.0));
            Assert.That(r.CoolingDerateMult, Is.EqualTo(0.05).Within(1e-9));
        }

        [Test]
        public void ManualDieselDisplacesGridEvenWithHeadroom()
        {
            Simulation sim = NewSim();
            for (int t = 0; t < 24; t++) sim.Tick();
            TickReport before = sim.Tick();
            Assert.That(before.DieselKwh, Is.EqualTo(0).Within(1e-9), "premise: no diesel needed");
            Assert.That(before.GridImportKwh, Is.GreaterThan(100), "premise: grid carries the site");
            Cmd(sim, CommandKind.SetDieselManual, 1);
            TickReport during = sim.Tick();
            Assert.That(during.DieselKwh, Is.GreaterThan(100),
                "the lever must run the generator although the grid had headroom");
            Assert.That(during.GridImportKwh, Is.LessThan(before.GridImportKwh),
                "manual diesel displaces grid import");
            Cmd(sim, CommandKind.SetDieselManual, 0);
            sim.Tick();
            TickReport after = sim.Tick();
            Assert.That(after.DieselKwh, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void BulletinsDecayCredibilityAndEventuallyBackfire()
        {
            Balance b = TestData.LoadBalance();
            Simulation sim = NewSim();
            sim.Tick();
            double gni0 = sim.State.Gni;
            Cmd(sim, CommandKind.IssueBulletin, 0);
            sim.Tick();
            Assert.That(sim.State.Gni, Is.GreaterThan(gni0), "first bulletin helps");
            // One tick of CREDIBILITY_RECOVERY_PER_WEEK has already accrued.
            Assert.That(sim.State.Credibility, Is.EqualTo(1.0 - b.CredibilityLossPerUse).Within(1e-3));

            // Spam until below the mockery threshold, then one more must HURT.
            for (int i = 0; i < 8; i++) { Cmd(sim, CommandKind.IssueBulletin, 0); sim.Tick(); }
            Assert.That(sim.State.Credibility, Is.LessThan(b.CredibilityMockeryThreshold));
            double gniBefore = sim.State.Gni;
            Cmd(sim, CommandKind.IssueBulletin, 0);
            sim.Tick();
            Assert.That(sim.State.Gni, Is.LessThan(gniBefore + 1e-9),
                "below the mockery line a bulletin must not help");
        }

        [Test]
        public void NamingAResponsibleEmployeeHasDiminishingReturns()
        {
            Simulation sim = NewSim();
            sim.Tick();
            double g0 = sim.State.Gni;
            Cmd(sim, CommandKind.IssueBulletin, 1);
            sim.Tick();
            double firstGain = sim.State.Gni - g0;
            double g1 = sim.State.Gni;
            Cmd(sim, CommandKind.IssueBulletin, 1);
            sim.Tick();
            double secondGain = sim.State.Gni - g1;
            Assert.That(secondGain, Is.LessThan(firstGain),
                "the second naming must be worth less than the first");
            Assert.That(sim.State.NamingCount, Is.EqualTo(2));
        }

        [Test]
        public void AccidentsPenaliseGoodwillAndDecay()
        {
            Balance b = TestData.LoadBalance();
            Simulation sim = NewSim();
            sim.Tick();
            Cmd(sim, CommandKind.ReportAccident);
            sim.Tick();
            Assert.That(sim.State.AccidentScore, Is.GreaterThan(b.AccidentGniPenalty * 0.9));
            double peak = sim.State.AccidentScore;
            for (int t = 0; t < b.AccidentHalflifeDays * SimClock.TicksPerDay; t++) sim.Tick();
            Assert.That(sim.State.AccidentScore, Is.EqualTo(peak * 0.5).Within(peak * 0.05),
                "accident memory must halve over its half-life");
            Assert.That(sim.State.TotalAccidents, Is.EqualTo(1));
        }
    }
}
