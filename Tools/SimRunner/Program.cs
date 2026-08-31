using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;

namespace Game.Sim.Runner
{
    /// <summary>
    /// Headless year runner. Simulates a scenario and writes one CSV row per
    /// tick — every meter, per docs' Milestone 1 contract. Zero Unity.
    ///
    ///   dotnet run --project Tools/SimRunner -- ^
    ///     --balance Assets/StreamingAssets/Tuning/balance.tuning ^
    ///     --scenario Assets/StreamingAssets/Scenarios/baseline-year.scenario ^
    ///     --out out.csv [--seed 42] [--ticks 8760]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            string balancePath = null, scenarioPath = null, outPath = "sim-out.csv";
            ulong? seedOverride = null;
            int? ticksOverride = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--balance": balancePath = args[++i]; break;
                    case "--scenario": scenarioPath = args[++i]; break;
                    case "--out": outPath = args[++i]; break;
                    case "--seed": seedOverride = ulong.Parse(args[++i], ci); break;
                    case "--ticks": ticksOverride = int.Parse(args[++i], ci); break;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[i]);
                        return 2;
                }
            }
            if (balancePath == null || scenarioPath == null)
            {
                Console.Error.WriteLine("Usage: SimRunner --balance <file> --scenario <file> [--out csv] [--seed n] [--ticks n]");
                return 2;
            }

            Balance balance = Balance.Parse(File.ReadAllText(balancePath));
            Scenario scenario = Scenario.Parse(File.ReadAllText(scenarioPath));
            if (seedOverride.HasValue) scenario.Seed = seedOverride.Value;
            if (ticksOverride.HasValue) scenario.Ticks = ticksOverride.Value;

            var sim = new Simulation(balance, scenario);
            var sb = new StringBuilder(scenario.Ticks * 320);
            sb.AppendLine(TickReport.CsvHeader);

            double revenue = 0, energyCost = 0, penalties = 0, water = 0;
            double dieselKwh = 0, gridKwh = 0;
            for (int t = 0; t < scenario.Ticks; t++)
            {
                TickReport r = sim.Tick();
                sb.AppendLine(r.ToCsvRow());
                revenue += r.RevenueEur; energyCost += r.EnergyCostEur;
                penalties += r.PenaltyEur; water += r.WaterLPerH / 1000.0;
                dieselKwh += r.DieselKwh; gridKwh += r.GridImportKwh;
            }
            File.WriteAllText(outPath, sb.ToString());

            var s = sim.State;
            Console.WriteLine("=== " + Path.GetFileName(scenarioPath) + "  seed " + scenario.Seed + "  " + scenario.Ticks + " ticks ===");
            Console.WriteLine("revenue        EUR " + revenue.ToString("N0", ci));
            Console.WriteLine("energy cost    EUR " + energyCost.ToString("N0", ci));
            Console.WriteLine("SLA penalties  EUR " + penalties.ToString("N0", ci));
            Console.WriteLine("water          " + water.ToString("N0", ci) + " m3   grid " + (gridKwh / 1000.0).ToString("N0", ci) + " MWh   diesel " + (dieselKwh / 1000.0).ToString("N1", ci) + " MWh");
            Console.WriteLine("final          cash EUR " + s.CashEur.ToString("N0", ci)
                + "   GNI " + s.Gni.ToString("0.0", ci)
                + "   reputation " + s.Reputation.ToString("0.0", ci)
                + "   stage " + s.Stage
                + "   tier " + s.GridTier
                + (s.RunOver ? "   RUN OVER: " + s.RunOverReason : ""));
            Console.WriteLine();
            Console.WriteLine("--- event log (" + s.Events.Count + ") ---");
            foreach (SimEvent e in s.Events)
                Console.WriteLine("d" + SimClock.DayIndex(e.Tick).ToString("000", ci)
                    + " h" + SimClock.HourOfDay(e.Tick).ToString("00", ci)
                    + " [" + e.Category + "] " + e.Message);
            Console.WriteLine();
            Console.WriteLine("CSV: " + Path.GetFullPath(outPath));
            return 0;
        }
    }
}
