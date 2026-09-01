using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    [TestFixture]
    public class YearStabilityTests
    {
        /// <summary>
        /// A full simulated year produces no NaN and no Inf in any meter, and
        /// terminates. Reflection walks every double field of TickReport so a
        /// future meter cannot dodge the check by being forgotten here.
        /// </summary>
        [Test]
        public void FullYearProducesNoNanAndTerminates()
        {
            Simulation sim = TestData.NewBaselineSim();
            FieldInfo[] fields = typeof(TickReport).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                object boxed = r;
                for (int f = 0; f < fields.Length; f++)
                {
                    if (fields[f].FieldType != typeof(double)) continue;
                    double v = (double)fields[f].GetValue(boxed);
                    Assert.That(double.IsNaN(v), Is.False,
                        fields[f].Name + " is NaN at tick " + r.Tick);
                    Assert.That(double.IsInfinity(v), Is.False,
                        fields[f].Name + " is Inf at tick " + r.Tick);
                }
            }
            Assert.That(sim.State.Tick, Is.EqualTo(SimClock.TicksPerYear));
        }

        /// <summary>Same seed ⇒ byte-identical CSV. Different seed ⇒ different.</summary>
        [Test]
        public void SameSeedProducesIdenticalOutput()
        {
            string a = RunToCsv(42);
            string b = RunToCsv(42);
            Assert.That(a, Is.EqualTo(b), "same seed must reproduce exactly");

            string c = RunToCsv(43);
            Assert.That(c, Is.Not.EqualTo(a), "a different seed must change the run");
        }

        /// <summary>
        /// A live-enqueued command lands at the next tick boundary and produces
        /// the same result as the identical scheduled command — the property
        /// multiplayer intents will rely on (multiplayer.md §4).
        /// </summary>
        [Test]
        public void EnqueuedCommandMatchesScheduledCommand()
        {
            Balance bal = TestData.LoadBalance();
            var scheduled = new Simulation(bal,
                Scenario.Parse("SEED = 9\nNODES = 20\nGRID_TIER = 1\nEVAP_KWTH = 400\nSPOT_ENABLED = 1\n" +
                               "AT 50 SIGN_INFERENCE 100 30\n"));
            var live = new Simulation(bal,
                Scenario.Parse("SEED = 9\nNODES = 20\nGRID_TIER = 1\nEVAP_KWTH = 400\nSPOT_ENABLED = 1\n"));

            var sbA = new StringBuilder();
            var sbB = new StringBuilder();
            for (int t = 0; t < 400; t++)
            {
                if (t == 50) // applied at the start of tick 50, like the schedule
                    live.Enqueue(new SimCommand { Kind = CommandKind.SignInference, A = 100, B = 30 });
                sbA.AppendLine(scheduled.Tick().ToCsvRow());
                sbB.AppendLine(live.Tick().ToCsvRow());
            }
            Assert.That(sbB.ToString(), Is.EqualTo(sbA.ToString()));
        }

        private static string RunToCsv(ulong seed)
        {
            Scenario sc = TestData.LoadBaselineScenario();
            sc.Seed = seed;
            var sim = new Simulation(TestData.LoadBalance(), sc);
            var sb = new StringBuilder(SimClock.TicksPerYear * 320);
            for (int t = 0; t < SimClock.TicksPerYear; t++)
                sb.AppendLine(sim.Tick().ToCsvRow());
            return sb.ToString();
        }
    }
}
