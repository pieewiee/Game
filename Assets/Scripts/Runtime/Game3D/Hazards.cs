using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>Places every hazard into the built site. One call, all wired.</summary>
    public static class HazardInstaller
    {
        public static void Install(SiteRefs site, FacilityController facility)
        {
            PullStation.All.Clear();

            // Pull stations: one per compartment that matters, mounted on the
            // wall by its door (workplace-accidents.md §4.1).
            MakePullStation(site, facility, site.HallA, site.PullHallA);
            MakePullStation(site, facility, site.Hall2, site.PullHall2);
            MakePullStation(site, facility, site.Plant, site.PullPlant);
            MakePullStation(site, facility, site.GoodsIn, site.PullGoodsIn);

            // EPO mushrooms beside each compartment door (§4.5).
            MakeEpo(site, facility, site.HallA, site.EpoHallA);
            MakeEpo(site, facility, site.Hall2, site.EpoHall2);
            MakeEpo(site, facility, site.Plant, site.EpoPlant);
            MakeEpo(site, facility, site.Ups, site.EpoUps);

            // Cooling controls, in the plant room where the docs put them.
            MakeValve(site, site.Valve);
            MakeDial(site, site.Dial);

            // The LV switch room and the battery room each get the control
            // that gives them a reason to exist.
            MakeBreaker(site);
            MakeVentFan(site);
            MakeEyewash(site);

            // Tools live in the workshop end of goods receiving.
            MakeToolCabinet(site, site.CabinetPos);

            // The forklift waits on the apron by the goods door.
            Forklift.Create(site, site.ForkliftSpawn);

            MakeDesk(site, site.BulletinDesk, Palette.Ink, true);
            MakeDesk(site, site.ContractDesk, Palette.Field, false);

            // Freezing is a systemic hazard, not an object.
            var freeze = new GameObject("FreezeSystem");
            freeze.transform.SetParent(site.Root, false);
            freeze.AddComponent<FreezeSystem>().Init(site);
        }

        private static void MakeDesk(SiteRefs site, Vector3 pos, Color screen, bool bulletin)
        {
            var go = new GameObject(bulletin ? "BulletinTerminal" : "ContractTerminal");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.08f, 0.7f), Palette.Earth);
            pm.Box(new Vector3(0, 0.4f, 0), new Vector3(0.1f, 0.8f, 0.6f), Palette.Earth);
            pm.Box(new Vector3(0, 1.05f, -0.1f), new Vector3(0.5f, 0.4f, 0.06f), screen);
            MatLib.Spawn("DeskMesh", pm.Build("desk"), go.transform, Vector3.zero);
            if (bulletin) go.AddComponent<Game.Runtime.Media.BulletinTerminal>();
            else go.AddComponent<Game.Runtime.Media.ContractTerminal>();
        }

        /// <summary>The main breaker (hazard-inventory.md §3): site-wide
        /// isolation for electrical work, and site-wide darkness for anyone who
        /// fancies it. It is a handle, not a dialog.</summary>
        private static void MakeBreaker(SiteRefs site)
        {
            var go = MountedBox(site, "MainBreaker", site.Breaker,
                new Vector3(0.7f, 1.1f, 0.35f));
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.6f, 1.0f, 0.22f), Palette.Amber);
            pm.Box(new Vector3(0, -0.15f, 0.16f), new Vector3(0.1f, 0.34f, 0.1f), Palette.AlarmRed);
            MatLib.Spawn("BreakerMesh", pm.Build("breaker"), go.transform, Vector3.zero, false);
            go.AddComponent<MainBreaker>();
        }

        /// <summary>The hydrogen vent fan switch (hazard-inventory.md §1): the
        /// battery strings off-gas, ventilation is code-required, and the fan
        /// is on a switch BECAUSE IT IS NOISY. Turning it off is a real,
        /// immediate, entirely reasonable-looking act.</summary>
        private static void MakeVentFan(SiteRefs site)
        {
            var go = MountedBox(site, "HydrogenVentFan", site.VentFan,
                new Vector3(0.5f, 0.6f, 0.3f));
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.36f, 0.46f, 0.16f), Palette.Slate);
            pm.Box(new Vector3(0, 0.08f, 0.11f), new Vector3(0.12f, 0.16f, 0.06f), Palette.Field);
            MatLib.Spawn("VentSwitchMesh", pm.Build("vent"), go.transform, Vector3.zero, false);
            go.AddComponent<HydrogenVentFan>();
        }

        private static void MakeEyewash(SiteRefs site)
        {
            var go = new GameObject("EyewashStation");
            go.transform.SetParent(site.Root, false);
            go.transform.position = site.Eyewash.Pos;
            go.transform.rotation = Quaternion.Euler(0, site.Eyewash.Yaw, 0);
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 0, 0), new Vector3(0.5f, 0.3f, 0.4f), Palette.Field);
            pm.Cylinder(new Vector3(0, 0.35f, 0), 0.05f, 0.4f, 6, Palette.Slate);
            MatLib.Spawn("EyewashMesh", pm.Build("eyewash"), go.transform, Vector3.zero, false);
        }

        /// <summary>A control mounted flat on a wall, with a collider generous
        /// enough to be hit by the interaction sweep from a normal standing
        /// position, facing into its room.</summary>
        private static GameObject MountedBox(SiteRefs site, string name, Mount mount, Vector3 colliderSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(site.Root, false);
            go.transform.position = mount.Pos;
            go.transform.rotation = Quaternion.Euler(0, mount.Yaw, 0);
            var box = go.AddComponent<BoxCollider>();
            box.size = colliderSize;
            return go;
        }

        private static void MakePullStation(SiteRefs site, FacilityController fac, Room room, Mount mount)
        {
            var go = MountedBox(site, "PullStation " + room.Name, mount, new Vector3(0.55f, 0.6f, 0.4f));
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.26f, 0.36f, 0.12f), Palette.AlarmRed);
            pm.Box(new Vector3(0, -0.05f, 0.08f), new Vector3(0.12f, 0.18f, 0.04f), Palette.Render);
            MatLib.Spawn("StationMesh", pm.Build("pullstation"), go.transform, Vector3.zero, false);
            var st = go.AddComponent<PullStation>();
            st.Room = room;
            st.Facility = fac;
            st.Index = PullStation.All.Count;
            PullStation.All.Add(st);
        }

        private static void MakeEpo(SiteRefs site, FacilityController fac, Room room, Mount mount)
        {
            var go = MountedBox(site, "EPO " + room.Name, mount, new Vector3(0.5f, 0.5f, 0.35f));
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.24f, 0.24f, 0.08f), Palette.Amber);
            pm.Cylinder(new Vector3(0, 0, 0.09f), 0.08f, 0.09f, 8, Palette.AlarmRed);
            MatLib.Spawn("EpoMesh", pm.Build("epo"), go.transform, Vector3.zero, false);
            var epo = go.AddComponent<EpoButton>();
            epo.Room = room;
            epo.Facility = fac;
        }

        private static void MakeValve(SiteRefs site, Mount mount)
        {
            var go = MountedBox(site, "WaterValve", mount, new Vector3(0.75f, 0.75f, 0.5f));
            var pm = new ProcMesh();
            // The wheel faces into the room, on a stub off the wall.
            pm.Box(new Vector3(0, 0, -0.12f), new Vector3(0.16f, 0.16f, 0.24f), Palette.PaleBlue);
            pm.Cylinder(new Vector3(0, 0, 0.02f), 0.32f, 0.1f, 10, Palette.AlarmRed);
            MatLib.Spawn("ValveMesh", pm.Build("valve"), go.transform, Vector3.zero, false);
            go.AddComponent<WaterValveWheel>();
        }

        private static void MakeDial(SiteRefs site, Mount mount)
        {
            var go = MountedBox(site, "SetpointDial", mount, new Vector3(0.55f, 0.55f, 0.4f));
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.34f, 0.34f, 0.08f), Palette.Slate);
            pm.Cylinder(new Vector3(0, 0, 0.07f), 0.1f, 0.07f, 8, Palette.Render);
            MatLib.Spawn("DialMesh", pm.Build("dial"), go.transform, Vector3.zero, false);
            go.AddComponent<SetpointDial>();
        }

        private static void MakeToolCabinet(SiteRefs site, Vector3 pos)
        {
            var go = new GameObject("ToolCabinet");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 0.9f, 0), new Vector3(1.0f, 1.8f, 0.5f), Palette.ProgramBlue);
            MatLib.Spawn("CabinetMesh", pm.Build("cabinet"), go.transform, Vector3.zero);
            var cabBox = go.AddComponent<BoxCollider>();
            cabBox.size = new Vector3(1.2f, 1.9f, 0.8f);
            cabBox.center = new Vector3(0, 0.95f, 0);
            var cab = go.AddComponent<ToolCabinet>();

            var cutGo = new GameObject("WireCutters");
            cutGo.transform.SetParent(go.transform, false);
            // Clear of the cabinet body, or the probe hits the cabinet first.
            cutGo.transform.localPosition = new Vector3(0, 1.05f, 0.45f);
            var cbox = cutGo.AddComponent<BoxCollider>();
            cbox.size = new Vector3(0.25f, 0.12f, 0.3f);
            var cpm = new ProcMesh();
            cpm.Box(Vector3.zero, new Vector3(0.06f, 0.05f, 0.24f), Palette.AlarmRed);
            MatLib.Spawn("CuttersMesh", cpm.Build("cutters"), cutGo.transform, Vector3.zero, false);
            cab.Cutters = cutGo.AddComponent<WireCutters>();
        }
    }

    /// <summary>
    /// The main breaker (hazard-inventory.md §3): "Site-wide isolation for
    /// electrical work. | Site dark. | Diesel start, if policy says so."
    /// It is the legitimate way to work on the LV distribution and the fastest
    /// way to end everyone's afternoon. No cover, no dialog.
    /// </summary>
    public sealed class MainBreaker : MonoBehaviour, IInteractable
    {
        public bool Open;   // open = site isolated

        public string Prompt(PlayerRig player)
        {
            return Open ? "E: close the main breaker (restore the site)"
                        : "E: OPEN the main breaker (isolates the whole site)";
        }

        public void Interact(PlayerRig player)
        {
            Open = !Open;
            if (Open)
            {
                double hours = GameBootstrap.Driver != null
                    ? Mathf.Max(1f, (float)GameBootstrap.Driver.Balance.EpoOutageHours * 2f) : 2.0;
                GameBootstrap.SendCommand(
                    new SimCommand { Kind = CommandKind.EpoTrip, A = 1.0, B = hours },
                    "opened the main breaker — whole site isolated");
                NewsFeed.Post("The site went dark at the main breaker. The Program describes the " +
                    "interruption as \"a planned opportunity to verify restart procedure\".");
            }
            else
            {
                GameBootstrap.AddLedger(player.PlayerName, "closed the main breaker");
            }
        }
    }

    /// <summary>
    /// The hydrogen vent fan (hazard-inventory.md §1, the design's own favourite
    /// hazard): battery strings off-gas hydrogen, ventilation is code-required,
    /// and the fan sits on a switch BECAUSE IT IS NOISY. Turning it off at night
    /// is defensible, immediate and entirely reasonable-looking. Hydrogen then
    /// accumulates, and a contactor arc does the rest.
    ///
    /// The noise benefit is not modelled — the sim has no per-source noise
    /// command — so this is currently all risk and no reward. Flagged rather
    /// than faked.
    /// </summary>
    public sealed class HydrogenVentFan : MonoBehaviour, IInteractable
    {
        public const float DeflagrationSeconds = 300f;   // ~5 real minutes off
        public bool Running = true;
        private float _accumulated;
        private AudioSource _hum;

        private void Start()
        {
            _hum = gameObject.AddComponent<AudioSource>();
            _hum.clip = SiteAudio.EngineClip();
            _hum.loop = true;
            _hum.spatialBlend = 1f;
            _hum.maxDistance = 22f;
            _hum.rolloffMode = AudioRolloffMode.Linear;
            _hum.pitch = 1.6f;
            _hum.volume = 0.25f * PlayerOptions.MachineryVolume;
            _hum.Play();
        }

        public string Prompt(PlayerRig player)
        {
            if (!Running && _accumulated > DeflagrationSeconds * 0.5f)
                return "E: restart the battery room vent fan (the room smells wrong)";
            return Running ? "E: switch off the battery room vent fan (it is loud)"
                           : "E: switch the battery room vent fan back on";
        }

        public void Interact(PlayerRig player)
        {
            Running = !Running;
            if (_hum != null) _hum.volume = Running ? 0.25f * PlayerOptions.MachineryVolume : 0f;
            GameBootstrap.AddLedger(player.PlayerName,
                Running ? "restarted the battery room vent fan" : "switched off the battery room vent fan");
            if (!Running)
                NewsFeed.Post("Site staff report the evening is noticeably quieter. No explanation " +
                    "has been offered and none has been requested.");
        }

        private void Update()
        {
            if (Running)
            {
                _accumulated = Mathf.Max(0f, _accumulated - Time.deltaTime * 3f);
                return;
            }
            // Only the machine that owns the world state runs the clock.
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return;

            _accumulated += Time.deltaTime;
            if (_accumulated < DeflagrationSeconds) return;
            _accumulated = 0f;
            Running = true;
            if (_hum != null) _hum.volume = 0.25f * PlayerOptions.MachineryVolume;

            // The deflagration: the UPS is destroyed, the site trips, and it
            // is an accident like any other.
            GameBootstrap.SendCommand(
                new SimCommand { Kind = CommandKind.EpoTrip, A = 1.0, B = 6 },
                "hydrogen deflagration in the battery room");
            GameBootstrap.SendCommand(
                new SimCommand { Kind = CommandKind.DestroyNodes, A = 6 }, null);
            GameBootstrap.SendCommand(
                new SimCommand { Kind = CommandKind.ReportAccident, A = 1 }, null);
            SiteAudio.PlaySiren(transform.position, 12f);
            NewsFeed.Post("An explosion in the battery room has been heard across the valley. " +
                "The Program confirms the ventilation was \"under review at the time\".");

            var rig = GameBootstrap.LocalPlayer;
            var site = GameBootstrap.Site;
            if (rig != null && !rig.IsDead && site != null && site.Ups.Contains(rig.transform.position))
                rig.Die("was caught in a hydrogen deflagration");
            if (net != null && net.IsHost && site != null)
                net.HostKillPlayersInRoom(site.Ups, "hydrogen deflagration");
        }
    }

    /// <summary>
    /// Manual fire suppression (workplace-accidents.md §4.1): wire-and-seal
    /// secured — the seal is cut with the wire cutters from the tool cabinet —
    /// then a pull starts a 30 s pre-alarm with siren. The compartment door
    /// still opens from both sides during the countdown, and can be held shut.
    /// Discharge kills everyone inside and destroys SUPPRESSION_DRIVE_LOSS of
    /// the room's nodes via acoustic shock. No confirmation dialog exists; the
    /// countdown IS the confirmation.
    /// </summary>
    public sealed class PullStation : MonoBehaviour, IInteractable
    {
        public const float PreAlarmSeconds = 30f;
        public const byte ActionCutSeal = 1, ActionPull = 2;

        /// <summary>Creation order is deterministic (HazardInstaller), so the
        /// index addresses the same station on every machine.</summary>
        public static readonly List<PullStation> All = new List<PullStation>();

        public int Index;
        public Room Room;
        public FacilityController Facility;
        public bool SealCut;
        public bool CountingDown { get; private set; }
        public float Remaining { get; private set; }
        private GameObject _gasCloud;

        public string Prompt(PlayerRig player)
        {
            if (CountingDown) return "DISCHARGE IN " + Mathf.CeilToInt(Remaining) + " s — GET OUT";
            if (!SealCut)
                return player.Carried is WireCutters
                    ? "E: cut the wire seal"
                    : "fire suppression (sealed — wire cutters required)";
            return "E: PULL fire suppression (" + Room.Name + ")";
        }

        public void Interact(PlayerRig player)
        {
            if (CountingDown) return;
            byte action = SealCut ? ActionPull : ActionCutSeal;
            if (action == ActionCutSeal && !(player.Carried is WireCutters)) return;

            var net = GameBootstrap.Net;
            if (net != null && net.IsClient)
            {
                // Host-authoritative: the intent travels, the host runs the
                // countdown, and the started state comes back as a broadcast.
                net.SendHazard(Index, action);
                return;
            }
            Apply(action, player.PlayerName);
            if (net != null && net.IsHost) net.BroadcastHazard(Index, action);
        }

        /// <summary>Applies a hazard action on this machine. actorName is the
        /// host-stamped name for the ledger, or null on a client mirroring a
        /// broadcast (the host already ledgered and posted the news).</summary>
        public void Apply(byte action, string actorName)
        {
            if (action == ActionCutSeal && !SealCut && !CountingDown)
            {
                SealCut = true;
                if (actorName != null)
                    GameBootstrap.AddLedger(actorName, "cut the suppression seal in " + Room.Name);
            }
            else if (action == ActionPull && SealCut && !CountingDown)
            {
                CountingDown = true;
                Remaining = PreAlarmSeconds;
                SiteAudio.PlaySiren(transform.position, PreAlarmSeconds);
                if (actorName != null)
                {
                    GameBootstrap.AddLedger(actorName, "PULLED the fire suppression in " + Room.Name);
                    NewsFeed.Post("A fire-suppression pre-alarm is sounding in " + Room.Name + ".");
                }
            }
        }

        /// <summary>Late-join sync: adopt the host's REAL station state,
        /// including the true remaining countdown (a fresh 30 s would promise
        /// a doomed joiner time they do not have).</summary>
        public void ForceState(bool sealCut, bool countingDown, float remaining)
        {
            SealCut = sealCut || countingDown;
            if (countingDown && !CountingDown)
                SiteAudio.PlaySiren(transform.position, Mathf.Max(1f, remaining));
            CountingDown = countingDown;
            Remaining = remaining;
        }

        private void Update()
        {
            if (!CountingDown) return;
            Remaining -= Time.deltaTime;
            if (Remaining > 0f) return;
            CountingDown = false;
            Discharge();
        }

        private void Discharge()
        {
            var net = GameBootstrap.Net;
            bool authoritative = net == null || !net.IsClient;

            if (authoritative)
            {
                var driver = GameBootstrap.Driver;
                double lossFrac = driver != null ? driver.Balance.SuppressionDriveLoss : 0.55;
                int roomNodes = Facility != null ? Facility.RoomNodeCount(Room) : 0;
                int destroyed = (int)Math.Round(roomNodes * lossFrac);
                if (destroyed > 0)
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.DestroyNodes, A = destroyed },
                        "suppression discharge destroyed " + destroyed + " nodes in " + Room.Name);
                if (net != null) net.HostKillPlayersInRoom(Room, "inert-gas discharge");
                NewsFeed.Post("Residents report a prolonged alarm from the site. The operator describes it as " +
                    "\"a scheduled validation of the Program's life-safety readiness\".");
            }

            // Deaths are adjudicated ONCE: by the host (via MsgKill for
            // clients, locally for its own rig) or by a solo machine. A client
            // running its cosmetic countdown must not kill its own rig too —
            // its respawned temp worker would catch the host's MsgKill.
            var player = GameBootstrap.LocalPlayer;
            if (authoritative && player != null && !player.IsDead &&
                Room.Contains(player.transform.position))
                player.Die("inert-gas discharge in " + Room.Name);

            var pm = new ProcMesh();
            var c = Palette.Render; c.a = 0.35f;
            pm.Box(Room.Bounds.center, Room.Bounds.size * 0.96f, c);
            _gasCloud = MatLib.Spawn("GasCloud", pm.Build("gas"), transform.parent, Vector3.zero, false, true);
            Destroy(_gasCloud, 25f);

            SealCut = false; // system spent; needs re-arming (and a new seal)
        }
    }

    /// <summary>
    /// The February Chain, presentation side (incidents.md §2.1): while the
    /// setpoint dial sits below freezing, cooled rooms frost up and a player
    /// who lingers freezes into an ice block that blocks the aisle, melts over
    /// hours into a puddle, and finally drains away. The sim keeps charging for
    /// the wasted cooling via the setpoint's efficiency, and the death feeds
    /// the accident statistics like every other one.
    /// </summary>
    public sealed class FreezeSystem : MonoBehaviour
    {
        private SiteRefs _site;
        private readonly List<IceBlock> _blocks = new List<IceBlock>();

        public void Init(SiteRefs site) { _site = site; }

        private void Update()
        {
            var player = GameBootstrap.LocalPlayer;
            if (player == null || player.IsDead) return;
            // The replicated report: a client freezes by the HOST's setpoint,
            // not by its own paused sim's stale one.
            double setpoint = GameBootstrap.CurrentReport.SetpointC;
            // Every compute hall freezes people; the plant room does not,
            // because freezing the dial's own operator is a dead end.
            bool inCooledRoom = false;
            foreach (Room hall in _site.ComputeHalls)
                if (hall.Contains(player.transform.position)) { inCooledRoom = true; break; }
            if (setpoint < 0.0 && inCooledRoom)
            {
                // Exposure builds faster the colder the dial sits.
                player.Exposure += Time.deltaTime * (float)(0.02 + 0.01 * -setpoint);
                if (player.Exposure >= 1f)
                {
                    SpawnIceBlock(player.transform.position);
                    player.Die("froze in the cold aisle at " +
                        setpoint.ToString("0", CultureInfo.InvariantCulture) + "°C");
                }
            }
            else
            {
                player.Exposure = Mathf.Max(0f, player.Exposure - Time.deltaTime * 0.1f);
            }
        }

        private void SpawnIceBlock(Vector3 pos)
        {
            var pm = new ProcMesh();
            var ice = Palette.PaleBlue; ice.a = 0.75f;
            pm.Box(new Vector3(0, 0.95f, 0), new Vector3(0.9f, 1.9f, 0.9f), ice);
            var go = MatLib.Spawn("IceBlock", pm.Build("ice"), _site.Root, pos, true, true);
            var block = go.AddComponent<IceBlock>();
            _blocks.Add(block);
            NewsFeed.Post("A member of site staff was frozen solid in the cold aisle. The block is " +
                "\"an unplanned interaction with cooled airflow\", per the Program.");
        }
    }

    /// <summary>The block blocks the aisle; hours later it is a puddle.</summary>
    public sealed class IceBlock : MonoBehaviour
    {
        private const float MeltSeconds = 240f; // ~ a few sim hours at day-speed
        private float _age;

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / MeltSeconds);
            transform.localScale = new Vector3(1f + t * 0.4f, 1f - t * 0.92f, 1f + t * 0.4f);
            if (_age >= MeltSeconds)
            {
                // The puddle stage: flat, slippery-looking, then gone.
                Destroy(gameObject, 60f);
                foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
                enabled = false;
            }
        }
    }
}
