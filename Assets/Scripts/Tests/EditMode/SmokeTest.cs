using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Trivial smoke test with no dependency on game code. Its only job is to
    /// prove that the test pipeline works end to end: the assembly definitions
    /// compile, the Unity Test Framework discovers tests, and CI reports the
    /// result. Delete it once real tests exist.
    ///
    /// Assembly dependency direction is documented in Assets/Scripts/README.md.
    /// </summary>
    public sealed class SmokeTest
    {
        [Test]
        public void TestPipeline_Runs()
        {
            Assert.Pass("EditMode test pipeline is alive.");
        }
    }
}
