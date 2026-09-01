using System;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.DebugTools
{
    /// <summary>
    /// Owns the Simulation inside Unity: loads balance + scenario from
    /// StreamingAssets, advances ticks according to the selected speed, and
    /// keeps a ring buffer of TickReports for the graphs.
    ///
    /// Self-bootstrapping: no scene authoring is required — entering Play mode
    /// in ANY scene spawns the driver and the console. This is deliberate: the
    /// debug interface must never depend on hand-edited scene content, and it
    /// stays in the project permanently (Milestone 2 brief).
    ///
    /// The presentation rule from architecture.md §3 holds here: this class
    /// reads SimState and TickReports, and the ONLY write path into the
    /// simulation is Simulation.Enqueue.
    /// </summary>
    public sealed class SimDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<SimDriver>() != null) return;
            var go = new GameObject("[GNP Debug Console]");
            DontDestroyOnLoad(go);
            go.AddComponent<SimDriver>();
            go.AddComponent<DebugConsole>();
            // Milestones 3-6: the playable world assembles itself on the same
            // object. Still no authored scene content anywhere.
            go.AddComponent<Game.Runtime.World.GameBootstrap>();
        }

        /// <summary>Ring capacity: five simulated years of hourly reports.</summary>
        public const int MaxHistory = 5 * SimClock.TicksPerYear;

        /// <summary>Ticks simulated per real-time second while unpaused.</summary>
        public float TicksPerSecond = 24f;
        public bool Paused = true;

        public Balance Balance { get; private set; }
        public Simulation Sim { get; private set; }
        public string ScenarioName { get; private set; }
        public ulong Seed { get; private set; }
        /// <summary>Bumped on every tick and restart; graphs use it as a dirty flag.</summary>
        public long HistoryVersion { get; private set; }
        public string LastFileOpMessage { get; private set; } = "";

        private TickReport[] _history = new TickReport[MaxHistory];
        private int _histCount;
        private int _histHead;
        private double _accum;

        public string BalancePath
        {
            get { return Path.Combine(Application.streamingAssetsPath, "Tuning", "balance.tuning"); }
        }

        public string ScenarioDir
        {
            get { return Path.Combine(Application.streamingAssetsPath, "Scenarios"); }
        }

        private void Awake()
        {
            try
            {
                Balance = Balance.Parse(File.ReadAllText(BalancePath));
            }
            catch (Exception e)
            {
                Debug.LogError("[GNP] Failed to load balance.tuning: " + e.Message);
                enabled = false;
                return;
            }
            string[] scenarios = ListScenarios();
            Restart(scenarios.Length > 0 ? scenarios[0] : null, 42UL);
        }

        private string[] _scenarioCache;

        /// <summary>Bumped whenever the Balance OBJECT or its defaults change
        /// (reload, save); the tuning panel drops its edit buffers on it.</summary>
        public long BalanceGeneration { get; private set; }

        public void RefreshScenarioList() { _scenarioCache = null; }

        public string[] ListScenarios()
        {
            if (_scenarioCache != null) return _scenarioCache;
            _scenarioCache = ScanScenarios();
            return _scenarioCache;
        }

        private string[] ScanScenarios()
        {
            if (!Directory.Exists(ScenarioDir)) return new string[0];
            string[] files = Directory.GetFiles(ScenarioDir, "*.scenario");
            Array.Sort(files, StringComparer.Ordinal);
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        /// <summary>Recreates the simulation with the CURRENT (possibly live-tuned)
        /// Balance — the core balancing loop is tune → restart → compare.
        /// Returns false (leaving the running sim untouched) when the scenario
        /// cannot be read: callers replaying a save MUST abort on that.</summary>
        public bool Restart(string scenarioName, ulong seed)
        {
            Scenario sc;
            if (scenarioName != null)
            {
                try
                {
                    sc = Scenario.Parse(File.ReadAllText(
                        Path.Combine(ScenarioDir, scenarioName + ".scenario")));
                }
                catch (Exception e)
                {
                    Debug.LogError("[GNP] Failed to load scenario '" + scenarioName + "': " + e.Message);
                    LastFileOpMessage = "SCENARIO LOAD FAILED: " + e.Message;
                    return false;
                }
            }
            else
            {
                sc = new Scenario();
            }
            sc.Seed = seed;
            ScenarioName = scenarioName;
            Seed = seed;
            Sim = new Simulation(Balance, sc);
            CommandLog.Clear();
            _histCount = 0;
            _histHead = 0;
            _accum = 0;
            HistoryVersion++;
            return true;
        }

        /// <summary>Save/load path: swap in an exact Balance (parsed from the
        /// save's embedded tuning text) before the deterministic replay.</summary>
        public void ReplaceBalance(Balance balance)
        {
            Balance = balance;
            BalanceGeneration++;
        }

        public void ReloadBalanceFromFileAndRestart()
        {
            try
            {
                Balance = Balance.Parse(File.ReadAllText(BalancePath));
                BalanceGeneration++;
                LastFileOpMessage = "reloaded balance.tuning";
            }
            catch (Exception e)
            {
                LastFileOpMessage = "RELOAD FAILED: " + e.Message;
                return;
            }
            Restart(ScenarioName, Seed);
        }

        /// <summary>Writes the current in-memory values over the tuning file,
        /// preserving its comments and layout. In the editor this IS the
        /// committed Assets/StreamingAssets file — save, then git diff.</summary>
        public void SaveBalanceToFile()
        {
            // A file whose W_* weights do not sum to 1 will refuse to LOAD
            // (Balance.Parse validation), so saving one would brick the next
            // start. Refuse here instead (audit finding #14).
            if (Math.Abs(Balance.WeightSum - 1.0) > 1e-9)
            {
                LastFileOpMessage = "NOT saved: W_* weights sum to "
                    + Balance.WeightSum.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                    + " (must be 1.0) — fix them first";
                return;
            }
            try
            {
                string original = File.ReadAllText(BalancePath);
                File.WriteAllText(BalancePath, Balance.PatchTuningText(original));
                Balance.RefreshDefaultsFromCurrent(); // file now equals memory
                BalanceGeneration++;
                LastFileOpMessage = "saved balance.tuning";
            }
            catch (Exception e)
            {
                LastFileOpMessage = "SAVE FAILED: " + e.Message;
            }
        }

        private void Update()
        {
            if (Paused || Sim == null) return;
            _accum += Time.unscaledDeltaTime * TicksPerSecond;
            // Frame budget guard: cap the backlog itself, so a hitch or an
            // absurd speed cannot accumulate an unbounded (or int-overflowing)
            // debt that later frames try to repay (audit finding #20).
            if (_accum > 20000.0) _accum = 20000.0;
            int n = (int)_accum;
            if (n <= 0) return;
            _accum -= n;
            for (int i = 0; i < n; i++) PushTick();
        }

        [Serializable]
        public struct RecordedCommand
        {
            public long tick;
            public int kind;
            public double a, b;
        }

        /// <summary>Every player-caused command with the tick it applies at.
        /// With a deterministic sim, {balance text, scenario, seed, this log}
        /// IS the save file (SaveSystem, M6). EVERY mutation path must come
        /// through EnqueueRecorded — the debug console included: contracts are
        /// currently signed there, and an unrecorded command means the replay
        /// lands in a different world than the one that was saved.</summary>
        public readonly System.Collections.Generic.List<RecordedCommand> CommandLog =
            new System.Collections.Generic.List<RecordedCommand>();

        /// <summary>Enqueue AND record for the save system - all gameplay paths.</summary>
        public void EnqueueRecorded(SimCommand cmd)
        {
            if (Sim == null) return;
            CommandLog.Add(new RecordedCommand { tick = Sim.State.Tick, kind = (int)cmd.Kind, a = cmd.A, b = cmd.B });
            Sim.Enqueue(cmd);
        }

        public void Step(int ticks)
        {
            if (Sim == null) return;
            for (int i = 0; i < ticks; i++) PushTick();
        }

        private void PushTick()
        {
            TickReport r = Sim.Tick();
            int idx = (_histHead + _histCount) % MaxHistory;
            _history[idx] = r;
            if (_histCount < MaxHistory) _histCount++;
            else _histHead = (_histHead + 1) % MaxHistory;
            HistoryVersion++;
        }

        public int HistoryCount { get { return _histCount; } }

        /// <summary>i = 0 is the oldest retained report.</summary>
        public TickReport GetHistory(int i)
        {
            return _history[(_histHead + i) % MaxHistory];
        }

        public TickReport Latest
        {
            get { return _histCount > 0 ? GetHistory(_histCount - 1) : default(TickReport); }
        }
    }
}
