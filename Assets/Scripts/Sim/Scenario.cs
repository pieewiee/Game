using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Sim
{
    public enum CommandKind
    {
        SignInference,   // A = base kW, B = term days
        SignTraining,    // A = block kW, B = deadline in days from issue
        SetSpot,         // A = 1 on / 0 off
        ApplyTier,       // A = target tier
        SetDieselPolicy, // A = (int)DieselPolicy
        AddNodes,        // A = node count (a delivery — blocked during Protest)
        SetLocalFte,     // A = FTE count
        SetVisualPoints  // A = fortification points
    }

    /// <summary>A scheduled player action. In M2+ these come from the UI/intents.</summary>
    public struct SimCommand
    {
        public long Tick;
        public CommandKind Kind;
        public double A, B;
    }

    /// <summary>
    /// Starting site + scheduled commands, parsed from a .scenario file
    /// (same KEY = value format as the tuning file, plus AT lines):
    ///
    ///   NODES = 60
    ///   AT 720 SIGN_TRAINING 400 18
    /// </summary>
    public sealed class Scenario
    {
        public ulong Seed = 1;
        public int Ticks = SimClock.TicksPerYear;
        public int Nodes;
        public int GridTier;
        public double FreecoolKwTh, EvapKwTh, ChillerKwTh;
        public double SolarKwp, WindKw, BatteryKwh, BatteryKw, DieselKw;
        public DieselPolicy Diesel = DieselPolicy.ProtectSla;
        public double LocalFte;
        public double VisualPoints;
        public double CashEur;
        public bool SpotEnabled;
        /// <summary>Starting Reputation; negative = use the balance default.</summary>
        public double ReputationStart = -1.0;
        public readonly List<SimCommand> Commands = new List<SimCommand>();

        public static Scenario Parse(string text)
        {
            var s = new Scenario();
            var ci = CultureInfo.InvariantCulture;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("AT ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3)
                        throw new FormatException("Scenario line " + (n + 1) + ": AT <tick> <COMMAND> [args]");
                    var cmd = new SimCommand { Tick = long.Parse(parts[1], ci) };
                    double A(int i) { return parts.Length > i ? double.Parse(parts[i], NumberStyles.Float, ci) : 0.0; }
                    switch (parts[2])
                    {
                        case "SIGN_INFERENCE": cmd.Kind = CommandKind.SignInference; cmd.A = A(3); cmd.B = A(4); break;
                        case "SIGN_TRAINING": cmd.Kind = CommandKind.SignTraining; cmd.A = A(3); cmd.B = A(4); break;
                        case "SET_SPOT": cmd.Kind = CommandKind.SetSpot; cmd.A = A(3); break;
                        case "APPLY_TIER": cmd.Kind = CommandKind.ApplyTier; cmd.A = A(3); break;
                        case "SET_DIESEL": cmd.Kind = CommandKind.SetDieselPolicy; cmd.A = A(3); break;
                        case "ADD_NODES": cmd.Kind = CommandKind.AddNodes; cmd.A = A(3); break;
                        case "SET_LOCAL_FTE": cmd.Kind = CommandKind.SetLocalFte; cmd.A = A(3); break;
                        case "SET_VISUAL": cmd.Kind = CommandKind.SetVisualPoints; cmd.A = A(3); break;
                        default:
                            throw new FormatException("Scenario line " + (n + 1) + ": unknown command " + parts[2]);
                    }
                    s.Commands.Add(cmd);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    throw new FormatException("Scenario line " + (n + 1) + " is not KEY = value: " + line);
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "SEED": s.Seed = ulong.Parse(val, ci); break;
                    case "TICKS": s.Ticks = int.Parse(val, ci); break;
                    case "NODES": s.Nodes = int.Parse(val, ci); break;
                    case "GRID_TIER": s.GridTier = int.Parse(val, ci); break;
                    case "FREECOOL_KWTH": s.FreecoolKwTh = double.Parse(val, NumberStyles.Float, ci); break;
                    case "EVAP_KWTH": s.EvapKwTh = double.Parse(val, NumberStyles.Float, ci); break;
                    case "CHILLER_KWTH": s.ChillerKwTh = double.Parse(val, NumberStyles.Float, ci); break;
                    case "SOLAR_KWP": s.SolarKwp = double.Parse(val, NumberStyles.Float, ci); break;
                    case "WIND_KW": s.WindKw = double.Parse(val, NumberStyles.Float, ci); break;
                    case "BATTERY_KWH": s.BatteryKwh = double.Parse(val, NumberStyles.Float, ci); break;
                    case "BATTERY_KW": s.BatteryKw = double.Parse(val, NumberStyles.Float, ci); break;
                    case "DIESEL_KW": s.DieselKw = double.Parse(val, NumberStyles.Float, ci); break;
                    case "DIESEL_POLICY": s.Diesel = (DieselPolicy)Enum.Parse(typeof(DieselPolicy), val, true); break;
                    case "LOCAL_FTE": s.LocalFte = double.Parse(val, NumberStyles.Float, ci); break;
                    case "VISUAL_POINTS": s.VisualPoints = double.Parse(val, NumberStyles.Float, ci); break;
                    case "CASH": s.CashEur = double.Parse(val, NumberStyles.Float, ci); break;
                    case "REPUTATION": s.ReputationStart = double.Parse(val, NumberStyles.Float, ci); break;
                    case "SPOT_ENABLED": s.SpotEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    default:
                        throw new FormatException("Scenario line " + (n + 1) + ": unknown key " + key);
                }
            }
            // Stable order: by tick, then original file order (List.Sort is
            // unstable, so decorate with the index).
            var indexed = new List<KeyValuePair<int, SimCommand>>(s.Commands.Count);
            for (int i = 0; i < s.Commands.Count; i++)
                indexed.Add(new KeyValuePair<int, SimCommand>(i, s.Commands[i]));
            indexed.Sort((x, y) =>
            {
                int c = x.Value.Tick.CompareTo(y.Value.Tick);
                return c != 0 ? c : x.Key.CompareTo(y.Key);
            });
            s.Commands.Clear();
            foreach (var kv in indexed) s.Commands.Add(kv.Value);
            return s;
        }
    }
}
