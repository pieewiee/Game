using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The escalation ladder fires at the documented bands (sentiment.md §3):
    /// Content ≥70, Complaints 55–70, Petition 40–55, Protest 25–40,
    /// Injunction 15–25, Sabotage &lt;15 — entry at the band edge, exit
    /// requiring the edge plus 5 points of hysteresis.
    ///
    /// The integration tests force the channel *memories* directly. That is
    /// deliberate: with W_VISUAL = 0.15 no single channel can drag GNI below
    /// 85 on its own — the weighted sum means walking the ladder requires
    /// several grievances at once, which is a design property, not a bug.
    /// Forcing memories tests the ladder machinery without re-testing the
    /// physics that feeds it (the conservation and year tests cover that).
    /// </summary>
    [TestFixture]
    public class EscalationTests
    {
        private Simulation NewSim()
        {
            return new Simulation(TestData.LoadBalance(), Scenario.Parse("NODES = 1"));
        }

        private static void ForceAngryTown(Simulation sim, double memory = 95.0)
        {
            for (int i = 0; i < 5; i++) sim.State.ChannelMemory[i] = memory;
        }

        [Test]
        public void StagesEnterAtDocumentedBands()
        {
            Simulation sim = NewSim();
            Assert.That(sim.NextStage(EscalationStage.Content, 75), Is.EqualTo(EscalationStage.Content));
            Assert.That(sim.NextStage(EscalationStage.Content, 69.9), Is.EqualTo(EscalationStage.Complaints));
            Assert.That(sim.NextStage(EscalationStage.Complaints, 54.9), Is.EqualTo(EscalationStage.Petition));
            Assert.That(sim.NextStage(EscalationStage.Petition, 39.9), Is.EqualTo(EscalationStage.Protest));
            Assert.That(sim.NextStage(EscalationStage.Protest, 24.9), Is.EqualTo(EscalationStage.Injunction));
            Assert.That(sim.NextStage(EscalationStage.Injunction, 14.9), Is.EqualTo(EscalationStage.Sabotage));
        }

        [Test]
        public void ExactBandBoundariesUseStrictLessThan()
        {
            // Pinned per the audit. Convention (now fixed by this test):
            // ENTRY is strict '<' at the band edge — GNI exactly AT the edge
            // stays in the better stage. EXIT happens AT edge + 5 (>=) — one
            // stage up exactly at the hysteresis line, staying put just below.
            Simulation sim = NewSim();
            // entry edges: exactly at the band stays put
            Assert.That(sim.NextStage(EscalationStage.Content, 70.0), Is.EqualTo(EscalationStage.Content));
            Assert.That(sim.NextStage(EscalationStage.Complaints, 55.0), Is.EqualTo(EscalationStage.Complaints));
            Assert.That(sim.NextStage(EscalationStage.Petition, 40.0), Is.EqualTo(EscalationStage.Petition));
            Assert.That(sim.NextStage(EscalationStage.Protest, 25.0), Is.EqualTo(EscalationStage.Protest));
            Assert.That(sim.NextStage(EscalationStage.Injunction, 15.0), Is.EqualTo(EscalationStage.Injunction));
            // exit edges: exactly at band+5 exits one stage up
            Assert.That(sim.NextStage(EscalationStage.Complaints, 75.0), Is.EqualTo(EscalationStage.Content));
            Assert.That(sim.NextStage(EscalationStage.Petition, 60.0), Is.EqualTo(EscalationStage.Complaints));
            Assert.That(sim.NextStage(EscalationStage.Protest, 45.0), Is.EqualTo(EscalationStage.Petition));
            Assert.That(sim.NextStage(EscalationStage.Injunction, 30.0), Is.EqualTo(EscalationStage.Protest));
            Assert.That(sim.NextStage(EscalationStage.Sabotage, 20.0), Is.EqualTo(EscalationStage.Injunction));
            // just below the exit edge: the worse stage persists
            Assert.That(sim.NextStage(EscalationStage.Complaints, 74.99), Is.EqualTo(EscalationStage.Complaints));
            Assert.That(sim.NextStage(EscalationStage.Petition, 59.99), Is.EqualTo(EscalationStage.Petition));
            Assert.That(sim.NextStage(EscalationStage.Protest, 44.99), Is.EqualTo(EscalationStage.Protest));
            Assert.That(sim.NextStage(EscalationStage.Injunction, 29.99), Is.EqualTo(EscalationStage.Injunction));
            Assert.That(sim.NextStage(EscalationStage.Sabotage, 19.99), Is.EqualTo(EscalationStage.Sabotage));
        }

        [Test]
        public void StagesExitOnlyWithHysteresis()
        {
            Simulation sim = NewSim();
            Assert.That(sim.NextStage(EscalationStage.Complaints, 72.0), Is.EqualTo(EscalationStage.Complaints),
                "Complaints must persist until GNI clears 75");
            Assert.That(sim.NextStage(EscalationStage.Complaints, 75.5), Is.EqualTo(EscalationStage.Content));
            Assert.That(sim.NextStage(EscalationStage.Protest, 42.0), Is.EqualTo(EscalationStage.Protest),
                "Protest must persist until GNI clears 45");
            Assert.That(sim.NextStage(EscalationStage.Protest, 45.5), Is.EqualTo(EscalationStage.Petition));
            Assert.That(sim.NextStage(EscalationStage.Sabotage, 16.0), Is.EqualTo(EscalationStage.Sabotage),
                "Sabotage must persist until GNI clears 20");
            Assert.That(sim.NextStage(EscalationStage.Sabotage, 20.5), Is.EqualTo(EscalationStage.Injunction));
        }

        [Test]
        public void NoSingleChannelCanWalkTheLadderAlone()
        {
            // The design property stated above, pinned as a test so a future
            // weight change that breaks it is noticed.
            var sc = Scenario.Parse("SEED = 7\nNODES = 5\nEVAP_KWTH = 100\nVISUAL_POINTS = 300\n");
            var sim = new Simulation(TestData.LoadBalance(), sc);
            for (int t = 0; t < SimClock.TicksPerYear; t++) sim.Tick();
            Assert.That(sim.State.Gni, Is.GreaterThan(55.0),
                "a maxed visual channel alone should cost at most its weight");
        }

        [Test]
        public void ASustainedAngryTownWalksTheWholeLadderAndLosesTheReferendum()
        {
            var sim = new Simulation(TestData.LoadBalance(),
                Scenario.Parse("SEED = 7\nNODES = 5\nEVAP_KWTH = 100\n"));

            bool sawPetition = false, sawProtest = false, sawSabotage = false;
            long runOverTick = -1;
            for (int t = 0; t < SimClock.TicksPerYear && runOverTick < 0; t++)
            {
                ForceAngryTown(sim);
                TickReport r = sim.Tick();
                if (r.Stage == EscalationStage.Petition) sawPetition = true;
                if (r.Stage == EscalationStage.Protest) sawProtest = true;
                if (r.Stage == EscalationStage.Sabotage) sawSabotage = true;
                if (sim.State.RunOver) runOverTick = r.Tick;
            }
            Assert.That(sawPetition, "never reached Petition");
            Assert.That(sawProtest, "never reached Protest");
            Assert.That(sawSabotage, "never reached Sabotage");
            Assert.That(runOverTick, Is.GreaterThan(0), "the referendum never ended the run");
        }

        [Test]
        public void PermitIsRefusedAtCompletionWhenGniHasFallen()
        {
            // Apply for T2 with a healthy GNI, then keep the town angry for the
            // whole 210-day lead time. docs/systems/power.md §2: the re-check at
            // completion must refuse and keep the money.
            var sc = Scenario.Parse(
                "SEED = 3\nNODES = 10\nGRID_TIER = 1\nFREECOOL_KWTH = 200\nEVAP_KWTH = 200\nCASH = 3000000\n" +
                "AT 100 APPLY_TIER 2\n");
            var sim = new Simulation(TestData.LoadBalance(), sc);
            for (int t = 0; t < 8000; t++)
            {
                // Anger is windowed around the completion tick (~5140). Held
                // low for the whole run, the petition reaches 800 signatures
                // and the referendum ends the run BEFORE the permit completes —
                // the ladder outruns the paperwork, which is the design working.
                // Memories at 60 pull GNI toward 40: below the T2 gate of 55,
                // above the Injunction band (which would pause the clock).
                if (t >= 4000 && t < 5200) ForceAngryTown(sim, 60.0);
                sim.Tick();
            }

            Assert.That(sim.State.GridTier, Is.EqualTo(1), "tier must NOT have been granted");
            bool refused = false;
            foreach (SimEvent e in sim.State.Events)
                if (e.Category == "permit" && e.Message.Contains("REFUSED")) refused = true;
            Assert.That(refused, "no REFUSED permit event was logged");
            Assert.That(sim.State.CashEur, Is.LessThan(3000000 - 2000000),
                "the committed capex should be gone");
        }

        [Test]
        public void ProtestBlocksNodeDeliveries()
        {
            var sc = Scenario.Parse(
                "SEED = 5\nNODES = 10\nGRID_TIER = 1\nEVAP_KWTH = 300\n" +
                "AT 6000 ADD_NODES 10\n");
            var sim = new Simulation(TestData.LoadBalance(), sc);
            for (int t = 0; t < 6500; t++)
            {
                ForceAngryTown(sim);
                sim.Tick();
            }
            Assert.That(sim.State.Stage, Is.GreaterThanOrEqualTo(EscalationStage.Protest),
                "test premise: the site must be at Protest or worse by tick 6000");
            Assert.That(sim.State.NodesInstalled, Is.EqualTo(10),
                "the delivery must have been turned away at the gate");
        }
    }
}
