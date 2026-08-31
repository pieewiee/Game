using System;
using NUnit.Framework;
using Game.Sim;

namespace Game.Sim.Tests
{
    [TestFixture]
    public class BalanceTests
    {
        [Test]
        public void RealTuningFileLoads()
        {
            Balance b = TestData.LoadBalance();
            Assert.That(b.GpuNodePeakKw, Is.GreaterThan(0));
            Assert.That(b.TierCapKw[1], Is.GreaterThan(b.TierCapKw[0]));
        }

        [Test]
        public void CurveInterpolatesAndClamps()
        {
            Curve c = Curve.Parse("10:1.0, 20:3.0");
            Assert.That(c.Evaluate(10), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(c.Evaluate(15), Is.EqualTo(2.0).Within(1e-12));
            Assert.That(c.Evaluate(20), Is.EqualTo(3.0).Within(1e-12));
            Assert.That(c.Evaluate(-5), Is.EqualTo(1.0).Within(1e-12), "clamped low");
            Assert.That(c.Evaluate(99), Is.EqualTo(3.0).Within(1e-12), "clamped high");
        }

        [Test]
        public void UnknownKeyIsALoadError()
        {
            Assert.Throws<FormatException>(() => Balance.Parse("TYPO_KEY = 1.0"));
        }

        [Test]
        public void DuplicateKeyIsALoadError()
        {
            Assert.Throws<FormatException>(() => Balance.Parse("W_NOISE = 0.2\nW_NOISE = 0.3"));
        }

        [Test]
        public void ChannelWeightsMustSumToOne()
        {
            // Take the real file and break one weight.
            string text = System.IO.File.ReadAllText(
                System.IO.Path.Combine(TestData.RepoRoot(), TestData.BalanceRelPath));
            string broken = text.Replace("W_NOISE = 0.20", "W_NOISE = 0.50");
            Assert.Throws<FormatException>(() => Balance.Parse(broken));
        }

        [Test]
        public void ScenarioParsesCommandsInTickOrder()
        {
            Scenario s = Scenario.Parse("NODES = 5\nAT 100 SIGN_TRAINING 400 18\nAT 10 SIGN_INFERENCE 200 30\n");
            Assert.That(s.Commands.Count, Is.EqualTo(2));
            Assert.That(s.Commands[0].Tick, Is.EqualTo(10));
            Assert.That(s.Commands[0].Kind, Is.EqualTo(CommandKind.SignInference));
            Assert.That(s.Commands[1].Tick, Is.EqualTo(100));
        }
    }
}
