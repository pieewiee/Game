using System;
using System.IO;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// Loads the real tuning and scenario files for tests. Works from both
    /// runtimes: Unity EditMode runs with CWD = project root; dotnet test runs
    /// from Tools/SimTests/bin/... and walks up to find Assets/.
    /// </summary>
    public static class TestData
    {
        public const string BalanceRelPath = "Assets/StreamingAssets/Tuning/balance.tuning";
        public const string ScenarioRelPath = "Assets/StreamingAssets/Scenarios/baseline-year.scenario";

        public static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, BalanceRelPath))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, BalanceRelPath))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException("Could not locate repo root containing " + BalanceRelPath);
        }

        public static Balance LoadBalance()
        {
            return Balance.Parse(File.ReadAllText(Path.Combine(RepoRoot(), BalanceRelPath)));
        }

        public static Scenario LoadBaselineScenario()
        {
            return Scenario.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ScenarioRelPath)));
        }

        public static Simulation NewBaselineSim()
        {
            return new Simulation(LoadBalance(), LoadBaselineScenario());
        }
    }
}
