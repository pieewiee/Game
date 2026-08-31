using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The physical books must close every single tick — and not only in the
    /// gentle baseline. The audit's mutation test proved a conservation bug in
    /// the battery dispatch path passed the whole suite when only the baseline
    /// (no battery, no wind) was asserted, so the ledger test now runs across
    /// scenarios that exercise charge, discharge, curtailment, wind and a
    /// θ_power-limited grid.
    /// </summary>
    [TestFixture]
    public class ConservationTests
    {
        private const double Eps = 1e-6;

        /// <summary>Baseline: evap + free cooling, diesel, no storage.</summary>
        private const string BaselineName = "baseline";
        /// <summary>Battery + wind + solar on a T1 grid: charge/discharge/curtailment active.</summary>
        private const string StorageName = "storage";
        /// <summary>Tight T0 grid, no diesel, no battery: θ_power bites.</summary>
        private const string StarvedName = "starved";

        private static Simulation Build(string which)
        {
            Balance b = TestData.LoadBalance();
            switch (which)
            {
                case BaselineName:
                    return TestData.NewBaselineSim();
                case StorageName:
                    return new Simulation(b, Scenario.Parse(
                        "SEED = 11\nNODES = 60\nGRID_TIER = 1\nFREECOOL_KWTH = 700\nEVAP_KWTH = 700\nCHILLER_KWTH = 200\n" +
                        "SOLAR_KWP = 600\nWIND_KW = 400\nBATTERY_KWH = 1500\nBATTERY_KW = 400\nDIESEL_KW = 300\n" +
                        "DIESEL_POLICY = Always\nSPOT_ENABLED = 1\nCASH = 500000\nREPUTATION = 62\n" +
                        "AT 0 SIGN_INFERENCE 250 300\nAT 2000 SIGN_TRAINING 300 20\n"));
                case StarvedName:
                    return new Simulation(b, Scenario.Parse(
                        "SEED = 23\nNODES = 60\nGRID_TIER = 0\nFREECOOL_KWTH = 700\nEVAP_KWTH = 700\n" +
                        "DIESEL_POLICY = Never\nSPOT_ENABLED = 1\nCASH = 500000\nREPUTATION = 62\n" +
                        "AT 0 SIGN_INFERENCE 200 300\n"));
                default:
                    throw new ArgumentException(which);
            }
        }

        [TestCase(BaselineName)]
        [TestCase(StorageName)]
        [TestCase(StarvedName)]
        public void EnergyBalanceClosesEveryTick(string which)
        {
            Simulation sim = Build(which);
            double sumCharge = 0, sumDischarge = 0, sumWind = 0, sumCurtailed = 0;
            double minThetaPower = double.MaxValue;
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                // Site boundary: everything generated or imported equals
                // everything consumed, stored, curtailed or shed.
                double lhs = r.SolarPotentialKwh + r.WindPotentialKwh
                           + r.GridImportKwh + r.DieselKwh + r.BatteryDischargeKwh;
                double rhs = r.PItKw + r.PCoolKw + r.PAuxKw
                           - r.LoadShedKwh + r.BatteryChargeKwh + r.CurtailedKwh;
                Assert.That(lhs, Is.EqualTo(rhs).Within(Eps),
                    which + ": energy ledger open at tick " + r.Tick);
                sumCharge += r.BatteryChargeKwh; sumDischarge += r.BatteryDischargeKwh;
                sumWind += r.WindUsedKwh; sumCurtailed += r.CurtailedKwh;
                if (r.ThetaPower < minThetaPower) minThetaPower = r.ThetaPower;
            }
            // Guard against the audit's failure mode: the interesting scenarios
            // must actually exercise the paths they exist to test.
            if (which == StorageName)
            {
                Assert.That(sumCharge, Is.GreaterThan(0), "storage scenario never charged");
                Assert.That(sumDischarge, Is.GreaterThan(0), "storage scenario never discharged");
                Assert.That(sumWind, Is.GreaterThan(0), "storage scenario never used wind");
                Assert.That(sumCurtailed, Is.GreaterThan(0), "storage scenario never curtailed");
            }
            if (which == StarvedName)
                Assert.That(minThetaPower, Is.LessThan(1.0), "starved scenario never power-throttled");
        }

        [TestCase(BaselineName)]
        [TestCase(StorageName)]
        public void HeatBalanceClosesAndRespectsPlantRatings(string which)
        {
            Balance b = TestData.LoadBalance();
            Simulation sim = Build(which);
            double freecool = sim.State.FreecoolKwTh;
            double evap = sim.State.EvapKwTh;
            double chiller = sim.State.ChillerKwTh;
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                double removedPlusResidual = r.QFreecoolKwTh + r.QEvapKwTh
                                           + r.QChillerKwTh + r.QUnremovedKwTh;
                Assert.That(removedPlusResidual, Is.EqualTo(r.QItKwTh).Within(Eps),
                    which + ": heat ledger open at tick " + r.Tick);
                Assert.That(r.QItKwTh, Is.EqualTo(r.PItKw * b.HeatFraction).Within(1e-3),
                    which + ": heat does not match IT draw at tick " + r.Tick);
                // The ledger identity alone is tautological (the residual is
                // defined by subtraction), so also pin the physical limits:
                Assert.That(r.QFreecoolKwTh, Is.LessThanOrEqualTo(freecool + Eps), "freecool over rating");
                Assert.That(r.QEvapKwTh, Is.LessThanOrEqualTo(evap + Eps), "evap over rating");
                Assert.That(r.QChillerKwTh, Is.LessThanOrEqualTo(chiller + Eps), "chiller over rating");
                if (r.TdbC > b.FreecoolThresholdC)
                    Assert.That(r.QFreecoolKwTh, Is.EqualTo(0.0).Within(Eps),
                        which + ": free cooling ran above its temperature gate at tick " + r.Tick);
            }
        }

        [Test]
        public void WaterIsNeverNegativeAndRespectsDroughtAllowance()
        {
            Balance b = TestData.LoadBalance();
            Simulation sim = TestData.NewBaselineSim();
            double allowanceLh = b.TownWaterDemandM3Day * 1000.0 * b.DroughtAllowanceFrac / 24.0;
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                Assert.That(r.WaterLPerH, Is.GreaterThanOrEqualTo(0.0),
                    "Negative water at tick " + r.Tick);
                if (r.DroughtActive)
                    Assert.That(r.WaterLPerH, Is.LessThanOrEqualTo(allowanceLh + Eps),
                        "Drought allowance exceeded at tick " + r.Tick);
            }
        }

        /// <summary>
        /// The audit showed the baseline never comes near the allowance, making
        /// the drought clamp structurally untested. Here a large evap-only site
        /// is white-box forced into drought all summer: the clamp must bind
        /// (water pinned at the allowance) and cooling must throttle.
        /// </summary>
        [Test]
        public void DroughtClampActuallyBindsOnAThirstySite()
        {
            Balance b = TestData.LoadBalance();
            var sim = new Simulation(b, Scenario.Parse(
                "SEED = 17\nNODES = 300\nGRID_TIER = 3\nEVAP_KWTH = 4000\nSPOT_ENABLED = 1\nREPUTATION = 62\nCASH = 1000000\n" +
                "AT 0 SIGN_INFERENCE 2500 300\n"));
            double allowanceLh = b.TownWaterDemandM3Day * 1000.0 * b.DroughtAllowanceFrac / 24.0;
            bool clampBound = false;
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                // Force the restriction through the summer half.
                int month = SimClock.Month(sim.State.Tick);
                sim.State.DroughtActive = month >= 6 && month <= 8;
                TickReport r = sim.Tick();
                if (r.DroughtActive)
                {
                    Assert.That(r.WaterLPerH, Is.LessThanOrEqualTo(allowanceLh + Eps),
                        "allowance exceeded at tick " + r.Tick);
                    if (r.WaterLPerH > allowanceLh * 0.98 && r.ThetaCool < 1.0)
                        clampBound = true;
                }
            }
            Assert.That(clampBound,
                "the water clamp never actually bound (water at the allowance while cooling throttles) — the scenario is too gentle to test it");
        }

        [Test]
        public void BatterySocStaysWithinPhysicalBounds()
        {
            Scenario sc = TestData.LoadBaselineScenario();
            sc.BatteryKwh = 2000; sc.BatteryKw = 500;
            var sim = new Simulation(TestData.LoadBalance(), sc);
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                Assert.That(r.BatterySocKwh, Is.GreaterThanOrEqualTo(-Eps));
                Assert.That(r.BatterySocKwh, Is.LessThanOrEqualTo(sc.BatteryKwh + Eps));
            }
        }
    }
}
