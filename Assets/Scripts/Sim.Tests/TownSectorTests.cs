using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// Two residential wind sectors (nuisance.md §2): the west town at
    /// TOWN_BEARING_DEG ± TOWN_SECTOR_HALF_DEG (270° ± 45°, sector 1) and the
    /// north apartment blocks at TOWN2_BEARING_DEG ± TOWN2_SECTOR_HALF_DEG
    /// (0° ± 30°, sector 2). Anything else is sector 0. The air channel
    /// charges WIND_TOWARD_MULT, TOWN2_WIND_MULT or WIND_AWAY_MULT accordingly.
    /// </summary>
    [TestFixture]
    public class TownSectorTests
    {
        private static ClimateModel NewClimate()
        {
            return new ClimateModel(TestData.LoadBalance(), 1UL);
        }

        [Test]
        public void WestSectorIsOneIncludingEdges()
        {
            ClimateModel cm = NewClimate();
            Assert.That(cm.TownSector(270.0), Is.EqualTo(1));
            Assert.That(cm.TownSector(225.0), Is.EqualTo(1), "270-45 edge");
            Assert.That(cm.TownSector(315.0), Is.EqualTo(1), "270+45 edge");
            Assert.That(cm.TownSector(250.0), Is.EqualTo(1));
        }

        [Test]
        public void NorthSectorIsTwoIncludingEdgesAcrossZero()
        {
            ClimateModel cm = NewClimate();
            Assert.That(cm.TownSector(0.0), Is.EqualTo(2));
            Assert.That(cm.TownSector(330.0), Is.EqualTo(2), "0-30 edge, wraps past 360");
            Assert.That(cm.TownSector(30.0), Is.EqualTo(2), "0+30 edge");
        }

        [Test]
        public void OutsideBothSectorsIsZero()
        {
            ClimateModel cm = NewClimate();
            Assert.That(cm.TownSector(90.0), Is.EqualTo(0));
            Assert.That(cm.TownSector(180.0), Is.EqualTo(0));
            Assert.That(cm.IsTowardTown(90.0), Is.False);
            Assert.That(cm.IsTowardTown(0.0), Is.True);
            Assert.That(cm.IsTowardTown(270.0), Is.True);
        }

        [Test]
        public void SampleFillsSectorAndTowardTownConsistently()
        {
            ClimateModel cm = NewClimate();
            for (long t = 0; t < SimClock.TicksPerYear; t += 37)
            {
                ClimateSample s = cm.Sample(t, false);
                Assert.That(s.TownSector, Is.EqualTo(cm.TownSector(s.WindTowardDeg)), "tick " + t);
                Assert.That(s.WindTowardTown, Is.EqualTo(s.TownSector != 0), "tick " + t);
            }
        }

        /// <summary>
        /// Same diesel burn, three wind directions: the west town gets the
        /// full 3.0, the north apartments a smaller 2.0, everyone else 0.3.
        /// StepCommunity is driven directly with a hand-built sample so no
        /// weather seed has to be searched for the right direction.
        /// </summary>
        [Test]
        public void AirNuisanceIsOrderedWestOverNorthOverAway()
        {
            Balance b = TestData.LoadBalance();
            Assert.That(b.WindTowardMult, Is.GreaterThan(b.Town2WindMult));
            Assert.That(b.Town2WindMult, Is.GreaterThan(b.WindAwayMult));

            double west = AirFor(b, 1);
            double north = AirFor(b, 2);
            double away = AirFor(b, 0);

            Assert.That(west, Is.GreaterThan(north));
            Assert.That(north, Is.GreaterThan(away));
            Assert.That(away, Is.GreaterThan(0.0));
            Assert.That(north / away, Is.EqualTo(b.Town2WindMult / b.WindAwayMult).Within(1e-9));
            Assert.That(west / away, Is.EqualTo(b.WindTowardMult / b.WindAwayMult).Within(1e-9));
        }

        private static double AirFor(Balance b, int sector)
        {
            Simulation sim = new Simulation(b, Scenario.Parse("NODES = 1"));
            ClimateSample c = new ClimateSample();
            c.TdbC = 15.0; c.TwbC = 10.0;
            c.TownSector = sector;
            c.WindTowardTown = sector != 0;
            TickReport r = new TickReport();
            r.DieselKwh = 500.0;    // a fixed, modest burn so nothing clamps at 100
            sim.StepCommunity(in c, ref r);
            return r.NAir;
        }
    }
}
