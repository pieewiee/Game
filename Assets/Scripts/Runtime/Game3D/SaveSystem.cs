using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Runtime.DebugTools;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    [Serializable]
    public sealed class SaveData
    {
        public int version = 1;
        public string balanceText;
        public string scenario;
        public string seed;
        public long tick;
        public List<SimDriver.RecordedCommand> commands = new List<SimDriver.RecordedCommand>();
        public string facilityJson;
    }

    /// <summary>
    /// Save/load (M6), built on the one property the whole architecture exists
    /// to protect: determinism. A save is {balance text, scenario, seed,
    /// command log, tick} — loading re-parses the balance, restarts the
    /// scenario and REPLAYS the commands at full speed (a year re-simulates in
    /// well under a second), landing byte-identically where the save was made.
    /// No SimState serializer exists or is needed. F5 saves, F9 loads;
    /// host-only in a network session.
    /// </summary>
    public sealed class SaveSystem : MonoBehaviour
    {
        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, "gnp-save.json"); }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            bool client = GameBootstrap.Net != null && GameBootstrap.Net.IsClient;
            if (kb.f5Key.wasPressedThisFrame && !client) Save();
            if (kb.f9Key.wasPressedThisFrame && !client) Load();
        }

        public void Save()
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return;
            try
            {
                var data = new SaveData
                {
                    balanceText = driver.Balance.PatchTuningText(File.ReadAllText(driver.BalancePath)),
                    scenario = driver.ScenarioName,
                    seed = driver.Seed.ToString(CultureInfo.InvariantCulture),
                    tick = driver.Sim.State.Tick,
                    commands = new List<SimDriver.RecordedCommand>(driver.CommandLog),
                    facilityJson = GameBootstrap.Facility != null ? GameBootstrap.Facility.ToJson() : "",
                };
                File.WriteAllText(SavePath, JsonUtility.ToJson(data));
                NewsFeed.Post("Site state saved (day " + SimClock.DayIndex(data.tick) + ").");
            }
            catch (Exception e)
            {
                Debug.LogError("[GNP] save failed: " + e.Message);
                NewsFeed.Post("SAVE FAILED: " + e.Message);
            }
        }

        public void Load()
        {
            var driver = GameBootstrap.Driver;
            if (driver == null) return;
            if (!File.Exists(SavePath)) { NewsFeed.Post("No save file yet (F5 saves)."); return; }
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                ulong seed = ulong.Parse(data.seed, CultureInfo.InvariantCulture);

                // 1. The exact balance of the saved session. Keep the old one
                // at hand: if the restart fails the running sim survives and
                // must keep the balance it was built with.
                Balance oldBalance = driver.Balance;
                driver.ReplaceBalance(Balance.Parse(data.balanceText));
                // 2. Fresh deterministic run of the same scenario+seed — a
                // failure here (scenario file renamed since the save) must NOT
                // fall through to replaying commands into the stale sim.
                string scen = string.IsNullOrEmpty(data.scenario) ? null : data.scenario;
                if (!driver.Restart(scen, seed))
                {
                    driver.ReplaceBalance(oldBalance);
                    throw new Exception(driver.LastFileOpMessage);
                }
                // 3. ...replayed with every recorded command at its tick.
                // Commands recorded AT the save tick were enqueued but not yet
                // applied; re-enqueue them after the loop so they fire exactly
                // like they would have.
                int next = 0;
                var sim = driver.Sim;
                while (sim.State.Tick < data.tick)
                {
                    while (next < data.commands.Count && data.commands[next].tick == sim.State.Tick)
                    {
                        var rc = data.commands[next++];
                        sim.Enqueue(new SimCommand { Kind = (CommandKind)rc.kind, A = rc.a, B = rc.b });
                    }
                    driver.Step(1);
                }
                for (; next < data.commands.Count && data.commands[next].tick == data.tick; next++)
                {
                    var rc = data.commands[next];
                    sim.Enqueue(new SimCommand { Kind = (CommandKind)rc.kind, A = rc.a, B = rc.b });
                }
                driver.CommandLog.Clear();
                driver.CommandLog.AddRange(data.commands);
                driver.Paused = true;

                try
                {
                    if (GameBootstrap.Facility != null && !string.IsNullOrEmpty(data.facilityJson))
                    {
                        GameBootstrap.Facility.ApplyJson(data.facilityJson);
                        GameBootstrap.Facility.SyncFromSim(false);
                    }
                }
                catch (Exception fe)
                {
                    // The SIM restored fine; only the visual layout blob broke.
                    Debug.LogError("[GNP] facility layout restore failed: " + fe.Message);
                    NewsFeed.Post("Site state restored; the layout drawing was unreadable and was rebuilt from the sim.");
                }
                NewsFeed.Post("Site state restored to day " + SimClock.DayIndex(data.tick) +
                    " by deterministic replay. The town remembers everything; so does the save file.");
            }
            catch (Exception e)
            {
                Debug.LogError("[GNP] load failed: " + e.Message);
                NewsFeed.Post("LOAD FAILED: " + e.Message);
            }
        }
    }
}
