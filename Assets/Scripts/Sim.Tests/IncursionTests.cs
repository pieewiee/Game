using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// A resident breaches the fence for one specific rack or solar row once
    /// the town has sat in the Sabotage band long enough — docs/systems/
    /// incidents.md §3. Same "nothing is random below threshold" contract as
    /// EscalationTests: these tests force the channel memories directly to
    /// reach Sabotage fast, then step ticks and read the mirrored TickReport
    /// fields, never SimState directly where a report field exists, so a
    /// desync between the two would fail here too.
    /// </summary>
    [TestFixture]
    public class IncursionTests
    {
        // No SOLAR_KWP: canSolar is always false, so kind is deterministically
        // Rack regardless of the RNG draw — the tests do not need to branch.
        private static Simulation NewSim(string extra = "")
        {
            return new Simulation(TestData.LoadBalance(), Scenario.Parse(
                "SEED = 5\nNODES = 40\nGRID_TIER = 1\nFREECOOL_KWTH = 500\nEVAP_KWTH = 500\n" +
                "DIESEL_KW = 300\nCASH = 500000\n" + extra));
        }

        private static void ForceAngryTown(Simulation sim)
        {
            for (int i = 0; i < 5; i++) sim.State.ChannelMemory[i] = 95.0;
        }

        private static void Cmd(Simulation sim, CommandKind kind, double a = 0, double b = 0)
        {
            sim.Enqueue(new SimCommand { Kind = kind, A = a, B = b });
        }

        /// <summary>Runs until an incursion is Pending, or fails the test after
        /// a generous budget — every test below needs this as a starting point.
        /// GNI_ADJUST_RATE = 0.004 (a ~250-tick time constant) means forcing the
        /// channel memories does not put GNI itself into the Sabotage band for
        /// several hundred ticks, well before the 48-hour trigger even starts
        /// counting — the budget has to cover both.</summary>
        private static TickReport RunUntilPending(Simulation sim, int budget = 900)
        {
            TickReport r = default;
            for (int t = 0; t < budget; t++)
            {
                ForceAngryTown(sim);
                r = sim.Tick();
                if (r.IncursionPending) return r;
            }
            Assert.Fail("no incursion was scheduled within " + budget + " ticks");
            return r;
        }

        [Test]
        public void ExposureDoesNotAccrueOutsideSabotage()
        {
            Simulation sim = NewSim();
            // Held at Protest (memory 60 lands well inside that band, never
            // reaching Sabotage's <15 GNI), an incursion must never schedule,
            // however long the run.
            for (int t = 0; t < 2000; t++)
            {
                for (int i = 0; i < 5; i++) sim.State.ChannelMemory[i] = 60.0;
                TickReport r = sim.Tick();
                Assert.That(r.IncursionPending, Is.False, "must not schedule outside the Sabotage band");
                Assert.That(r.IncursionActive, Is.False);
            }
        }

        [Test]
        public void SchedulesWithTelegraphThenBreachTicksExactlyOnBalance()
        {
            Simulation sim = NewSim();
            Balance b = TestData.LoadBalance();
            TickReport scheduled = RunUntilPending(sim);
            Assert.That(scheduled.IncursionBreachTick,
                Is.EqualTo(scheduled.Tick + (long)b.IncursionTelegraphHours));
            Assert.That(scheduled.IncursionEndsTick,
                Is.EqualTo(scheduled.IncursionBreachTick + (long)b.IncursionBreachHours));
            Assert.That(scheduled.IncursionActive, Is.False, "must not be active at the scheduling tick");

            TickReport r = scheduled;
            while (!r.IncursionActive)
            {
                ForceAngryTown(sim);
                r = sim.Tick();
                Assert.That(r.Tick, Is.LessThanOrEqualTo(scheduled.IncursionBreachTick),
                    "must not go active later than its own scheduled breach tick");
            }
            Assert.That(r.Tick, Is.EqualTo(scheduled.IncursionBreachTick), "must go active exactly at the breach tick");
        }

        [Test]
        public void UnresolvedBreachDestroysExactlyOneRackAndNeverGoesNegative()
        {
            Simulation sim = NewSim();
            Balance b = TestData.LoadBalance();
            TickReport r = RunUntilPending(sim);
            int nodesBefore = r.NodesInstalled;
            Assert.That(r.IncursionTargetKind, Is.EqualTo((int)IncursionKind.Rack), "test premise: no solar exists");

            TickReport last = r;
            while (sim.State.Tick <= r.IncursionEndsTick)
            {
                ForceAngryTown(sim);
                last = sim.Tick();
            }
            Assert.That(last.IncursionActive, Is.False, "must resolve at the end tick");
            Assert.That(last.IncursionPending, Is.False);
            Assert.That(last.NodesInstalled, Is.EqualTo(nodesBefore - (int)b.IncursionRackNodes));
            Assert.That(last.TotalIncursions, Is.EqualTo(1));
            Assert.That(last.TotalIncursionsStopped, Is.EqualTo(0));
            Assert.That(last.NodesInstalled, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void InterruptCancelsWithZeroDamage()
        {
            Simulation sim = NewSim();
            TickReport r = RunUntilPending(sim);
            int nodesBefore = r.NodesInstalled;

            while (!sim.State.IncursionActive)
            {
                ForceAngryTown(sim);
                sim.Tick();
            }
            Cmd(sim, CommandKind.InterruptIncursion);
            ForceAngryTown(sim);
            TickReport after = sim.Tick();

            Assert.That(after.IncursionActive, Is.False);
            Assert.That(after.IncursionPending, Is.False);
            Assert.That(after.NodesInstalled, Is.EqualTo(nodesBefore), "an interrupted incursion must cost nothing");
            Assert.That(after.TotalIncursionsStopped, Is.EqualTo(1));
            Assert.That(after.TotalIncursions, Is.EqualTo(1), "a stopped incursion still counts as one attempt");
        }

        [Test]
        public void InterruptIsANoOpWithNothingActive()
        {
            Simulation sim = NewSim();
            Cmd(sim, CommandKind.InterruptIncursion);
            TickReport r = sim.Tick();
            Assert.That(r.TotalIncursionsStopped, Is.EqualTo(0));
            Assert.That(r.IncursionActive, Is.False);
        }

        [Test]
        public void FloodlightAbortsExactlyEveryOtherIncidentNotAPercentage()
        {
            Simulation sim = NewSim();
            Balance b = TestData.LoadBalance();
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Floodlight);
            sim.Tick();
            Assert.That(sim.State.HasFloodlight, Is.True, "test premise: floodlight bought");
            int nodesStart = sim.State.NodesInstalled;

            TickReport last = default;
            for (int t = 0; t < 2000 && sim.State.TotalIncursions < 4; t++)
            {
                ForceAngryTown(sim);
                last = sim.Tick();
            }
            Assert.That(last.TotalIncursions, Is.EqualTo(4), "test premise: four incidents resolved");
            Assert.That(last.TotalIncursionsAborted, Is.EqualTo(2), "exactly half, deterministically, not a roll");
            Assert.That(last.NodesInstalled, Is.EqualTo(nodesStart - 2 * (int)b.IncursionRackNodes),
                "only the two that were not aborted must have cost nodes");
        }

        // A bare sim tick also settles its own tiny idle-energy cost even with
        // no contracts signed, so CashEur moves by more than a purchase alone.
        // Isolate the purchase's cost by diffing against an identical sim
        // (same seed, same scenario) that ticks once with no command — same
        // baseline economics, so whatever is left over is exactly the purchase.
        [Test]
        public void BuySecurityDeductsCashAndSetsExactlyOneFlagEach()
        {
            Balance b = TestData.LoadBalance();
            Simulation baseline = NewSim();
            double baselineDelta = baseline.State.CashEur - baseline.Tick().CashEur;

            Simulation sim = NewSim();
            double cash0 = sim.State.CashEur;
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Camera);
            sim.Tick();
            Assert.That(sim.State.HasCamera, Is.True);
            Assert.That(sim.State.HasFloodlight, Is.False);
            Assert.That(sim.State.HasAlarm, Is.False);
            Assert.That(sim.State.HasTaser, Is.False);
            Assert.That(cash0 - sim.State.CashEur, Is.EqualTo(baselineDelta + b.CameraCostEur).Within(1e-6));

            // Per-tick economics drift slightly tick to tick (diurnal price
            // curves and the like), so match against a threshold comfortably
            // between "just the idle cost" and "a second camera's price"
            // rather than the exact baseline figure.
            double cash1 = sim.State.CashEur;
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Camera);   // already owned
            sim.Tick();
            Assert.That(cash1 - sim.State.CashEur, Is.LessThan(b.CameraCostEur / 2.0),
                "a duplicate purchase must not charge again");

            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Taser);
            TickReport r = sim.Tick();
            Assert.That(sim.State.HasTaser, Is.True);
            Assert.That(r.HasTaser, Is.True, "the report must mirror it the same tick");
        }

        [Test]
        public void BuySecurityIsRejectedWhenCashIsShort()
        {
            Balance b = TestData.LoadBalance();
            Simulation sim = NewSim("CASH = 0\n");
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Alarm);
            TickReport r = sim.Tick();
            Assert.That(sim.State.HasAlarm, Is.False);
            // Idle energy cost can still push cash slightly negative; the
            // full alarm price must not have been taken on top of that.
            Assert.That(r.CashEur, Is.GreaterThan(-b.AlarmCostEur / 2.0));
        }

        [Test]
        public void SitedHardwareRaisesVisualPointsButTheCarriedTaserDoesNot()
        {
            Simulation sim = NewSim();
            Balance b = TestData.LoadBalance();
            double visual0 = sim.State.VisualPoints;

            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Camera);
            sim.Tick();
            Assert.That(sim.State.VisualPoints, Is.EqualTo(visual0 + b.SecurityVisualPoints).Within(1e-9),
                "a sited camera is visible hardware, same as any other fortification");

            double visual1 = sim.State.VisualPoints;
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Taser);
            sim.Tick();
            Assert.That(sim.State.VisualPoints, Is.EqualTo(visual1).Within(1e-9),
                "a carried tool has no site footprint of its own");
        }

        [Test]
        public void FloodlightAddsContinuousAuxDraw()
        {
            Simulation sim = NewSim();
            TickReport before = sim.Tick();
            Cmd(sim, CommandKind.BuySecurity, (int)SecurityKind.Floodlight);
            TickReport after = sim.Tick();
            Assert.That(after.PAuxKw, Is.GreaterThan(before.PAuxKw),
                "the floodlight must draw from the same power pool as everything else");
        }
    }
}
