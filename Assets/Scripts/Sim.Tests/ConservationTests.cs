using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The physical books must close every single tick of the baseline year.
    /// docs milestone contract: energy balance, heat balance, water never
    /// negative.
    /// </summary>
    [TestFixture]
    public class ConservationTests
    {
        private const double Eps = 1e-6;

        [Test]
        public void EnergyBalanceClosesEveryTick()
        {
            Simulation sim = TestData.NewBaselineSim();
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
                    "Energy ledger open at tick " + r.Tick);
            }
        }

        [Test]
        public void HeatBalanceClosesEveryTick()
        {
            Simulation sim = TestData.NewBaselineSim();
            for (int t = 0; t < SimClock.TicksPerYear; t++)
            {
                TickReport r = sim.Tick();
                double removedPlusResidual = r.QFreecoolKwTh + r.QEvapKwTh
                                           + r.QChillerKwTh + r.QUnremovedKwTh;
                Assert.That(removedPlusResidual, Is.EqualTo(r.QItKwTh).Within(Eps),
                    "Heat ledger open at tick " + r.Tick);
                Assert.That(r.QItKwTh, Is.EqualTo(r.PItKw * TestData.LoadBalance().HeatFraction).Within(1e-3),
                    "Heat does not match IT draw at tick " + r.Tick);
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

        [Test]
        public void BatterySocStaysWithinPhysicalBounds()
        {
            Scenario sc = TestData.LoadBaselineScenario();
            sc.BatteryKwh = 2000; sc.BatteryKw = 500; // baseline has none; force one in
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
