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

            // Pull stations: Hall A and the plant room (workplace-accidents.md §4.1).
            MakePullStation(site, facility, site.HallA, new Vector3(1.0f, 1.5f, 2f));
            MakePullStation(site, facility, site.Plant, new Vector3(21f, 1.5f, 2f));

            // EPO mushrooms beside each compartment door (§4.5).
            MakeEpo(site, facility, site.HallA, new Vector3(18.8f, 1.4f, 8.4f));
            MakeEpo(site, facility, site.Plant, new Vector3(28.8f, 1.4f, 8.4f));

            // Plant room: water valve, setpoint dial, tool cabinet.
            MakeValve(site, new Vector3(22f, 1.1f, 18f));
            MakeDial(site, new Vector3(24f, 1.4f, 18.6f));
            MakeToolCabinet(site, new Vector3(28.5f, 0, 18f));

            // The forklift waits at the delivery door.
            Forklift.Create(site, new Vector3(-4f, 0, 10f));

            // The bulletin terminal on the office desk (M5).
            var desk = new GameObject("BulletinTerminal");
            desk.transform.SetParent(site.Root, false);
            desk.transform.position = new Vector3(35f, 0f, 16f);
            var dpm = new ProcMesh();
            dpm.Box(new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.08f, 0.7f), Palette.Earth);
            dpm.Box(new Vector3(0, 0.4f, 0), new Vector3(0.1f, 0.8f, 0.6f), Palette.Earth);
            dpm.Box(new Vector3(0, 1.05f, -0.1f), new Vector3(0.5f, 0.4f, 0.06f), Palette.Ink);
            MatLib.Spawn("DeskMesh", dpm.Build("desk"), desk.transform, Vector3.zero);
            desk.AddComponent<Game.Runtime.Media.BulletinTerminal>();

            // The contract desk beside it (compute-contracts.md at slice
            // depth): the core loop's signature moment gets a physical place.
            var cdesk = new GameObject("ContractTerminal");
            cdesk.transform.SetParent(site.Root, false);
            cdesk.transform.position = new Vector3(35f, 0f, 11f);
            var cpm2 = new ProcMesh();
            cpm2.Box(new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.08f, 0.7f), Palette.Earth);
            cpm2.Box(new Vector3(0, 0.4f, 0), new Vector3(0.1f, 0.8f, 0.6f), Palette.Earth);
            cpm2.Box(new Vector3(0, 1.05f, -0.1f), new Vector3(0.5f, 0.4f, 0.06f), Palette.Field);
            MatLib.Spawn("ContractDeskMesh", cpm2.Build("cdesk"), cdesk.transform, Vector3.zero);
            cdesk.AddComponent<Game.Runtime.Media.ContractTerminal>();

            // Freezing is a systemic hazard, not an object.
            var freeze = new GameObject("FreezeSystem");
            freeze.transform.SetParent(site.Root, false);
            freeze.AddComponent<FreezeSystem>().Init(site);
        }

        private static void MakePullStation(SiteRefs site, FacilityController fac, Room room, Vector3 pos)
        {
            var go = new GameObject("PullStation " + room.Name);
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.4f, 0.5f, 0.4f);
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.22f, 0.34f, 0.12f), Palette.AlarmRed);
            pm.Box(new Vector3(0, -0.05f, 0.07f), new Vector3(0.1f, 0.16f, 0.04f), Palette.Render);
            MatLib.Spawn("StationMesh", pm.Build("pullstation"), go.transform, Vector3.zero, false);
            var st = go.AddComponent<PullStation>();
            st.Room = room;
            st.Facility = fac;
            st.Index = PullStation.All.Count;
            PullStation.All.Add(st);
        }

        private static void MakeEpo(SiteRefs site, FacilityController fac, Room room, Vector3 pos)
        {
            var go = new GameObject("EPO " + room.Name);
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.35f, 0.35f, 0.35f);
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.2f, 0.2f, 0.08f), Palette.Amber);
            pm.Cylinder(new Vector3(0, 0, 0.08f), 0.07f, 0.08f, 8, Palette.AlarmRed);
            MatLib.Spawn("EpoMesh", pm.Build("epo"), go.transform, Vector3.zero, false);
            var epo = go.AddComponent<EpoButton>();
            epo.Room = room;
            epo.Facility = fac;
        }

        private static void MakeValve(SiteRefs site, Vector3 pos)
        {
            var go = new GameObject("WaterValve");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.6f, 0.6f, 0.6f);
            var pm = new ProcMesh();
            pm.Cylinder(Vector3.zero, 0.3f, 0.1f, 10, Palette.AlarmRed);
            pm.Box(new Vector3(0, -0.45f, 0), new Vector3(0.16f, 0.9f, 0.16f), Palette.PaleBlue);
            MatLib.Spawn("ValveMesh", pm.Build("valve"), go.transform, Vector3.zero, false);
            go.AddComponent<WaterValveWheel>();
        }

        private static void MakeDial(SiteRefs site, Vector3 pos)
        {
            var go = new GameObject("SetpointDial");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.4f, 0.4f, 0.3f);
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.3f, 0.3f, 0.08f), Palette.Slate);
            pm.Cylinder(new Vector3(0, 0, 0.06f), 0.09f, 0.06f, 8, Palette.Render);
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
            var cab = go.AddComponent<ToolCabinet>();

            var cutGo = new GameObject("WireCutters");
            cutGo.transform.SetParent(go.transform, false);
            cutGo.transform.localPosition = new Vector3(0, 1.2f, 0.3f);
            var cbox = cutGo.AddComponent<BoxCollider>();
            cbox.size = new Vector3(0.25f, 0.12f, 0.3f);
            var cpm = new ProcMesh();
            cpm.Box(Vector3.zero, new Vector3(0.06f, 0.05f, 0.24f), Palette.AlarmRed);
            MatLib.Spawn("CuttersMesh", cpm.Build("cutters"), cutGo.transform, Vector3.zero, false);
            cab.Cutters = cutGo.AddComponent<WireCutters>();
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
            // Only the cold aisle freezes people. The plant room holds the
            // dial itself — freezing its operator would make turning the dial
            // back impossible, which is a dead end, not a joke.
            bool inCooledRoom = _site.HallA.Contains(player.transform.position);
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
