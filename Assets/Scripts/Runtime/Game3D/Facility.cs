using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    [Serializable]
    public sealed class PlacedItem
    {
        public string kind;   // rack, evap, chiller, freecool, solar, battery, diesel
        public int slot;
    }

    [Serializable]
    public sealed class RoutePath
    {
        public string kind;   // power, cooling, network
        public List<float> xs = new List<float>();
        public List<float> zs = new List<float>();
    }

    [Serializable]
    public sealed class FacilityState
    {
        public List<PlacedItem> items = new List<PlacedItem>();
        public List<RoutePath> routes = new List<RoutePath>();
        public int missingPanels;
    }

    /// <summary>
    /// The dynamic facility layer (M3): placement, hand-routed runs and the
    /// visual mirror of the sim's aggregate assets. The simulation stays
    /// authoritative — every placement and every routing consequence reaches it
    /// exclusively through commands (SetPlant / AddNodes / SetRouteLossKw /
    /// SetCoolingDerate), and the visuals re-derive from SimState, so a
    /// suppression discharge that destroys nodes makes racks disappear without
    /// this class doing anything special.
    ///
    /// Deviation from construction-routing.md, noted for the report: placement
    /// snaps to prepared slots instead of a free 2 m grid, and routing models
    /// only length → loss (the doc's own "depth only where it feeds another
    /// system" rule, at slice depth).
    /// </summary>
    public sealed class FacilityController : MonoBehaviour
    {
        public const int NodesPerRack = 10;
        public const double EvapUnitKwTh = 250, ChillerUnitKwTh = 250, FreecoolUnitKwTh = 250;
        public const double SolarRowKwp = 50, BatteryPackKwh = 250, BatteryPackKw = 100, DieselUnitKw = 250;
        public const int PanelsPerRack = 2;

        public SiteRefs Site;
        public FacilityState State = new FacilityState();

        private readonly List<GameObject> _rackGos = new List<GameObject>();
        private readonly List<GameObject> _plantGos = new List<GameObject>();
        private readonly List<GameObject> _routeGos = new List<GameObject>();
        private readonly List<BlankingPanel> _panels = new List<BlankingPanel>();
        private string _placeKind;
        private RoutePath _draftRoute;
        private GameObject _draftGo;
        private int _lastSyncedNodes = -1;
        public string PlacementHint { get; private set; } = "";

        private static readonly string[] PlaceKinds =
            { "rack", "evap", "chiller", "freecool", "solar", "battery", "diesel" };

        public void Init(SiteRefs site)
        {
            Site = site;
            SyncFromSim(true);
        }

        // ------------------------------------------------------------------
        // Mirror of sim aggregates → visuals
        // ------------------------------------------------------------------

        private int CurrentNodes()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return net.RemoteNodes;
            var driver = GameBootstrap.Driver;
            return driver != null && driver.Sim != null ? driver.Sim.State.NodesInstalled : 0;
        }

        private void Update()
        {
            if (CurrentNodes() != _lastSyncedNodes) SyncFromSim(false);
            HandlePlacementInput();
        }

        /// <summary>Rebuilds racks and plant visuals from the CURRENT sim state.
        /// initial=true also seeds State.items so saves reflect the scenario.</summary>
        public void SyncFromSim(bool initial)
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null || Site == null) return;
            var s = driver.Sim.State;
            _lastSyncedNodes = CurrentNodes();
            int nodeCount = _lastSyncedNodes;

            foreach (GameObject go in _rackGos) if (go != null) Destroy(go);
            foreach (GameObject go in _plantGos) if (go != null) Destroy(go);
            _rackGos.Clear();
            _plantGos.Clear();
            _panels.Clear();

            int racks = Mathf.Min(Site.RackSlots.Count, (nodeCount + NodesPerRack - 1) / NodesPerRack);
            for (int i = 0; i < racks; i++) _rackGos.Add(BuildRack(Site.RackSlots[i], i));

            int plantSlot = 0;
            plantSlot = BuildUnits(plantSlot, (int)Math.Ceiling(s.FreecoolKwTh / FreecoolUnitKwTh), Palette.Render, "FreecoolUnit");
            plantSlot = BuildUnits(plantSlot, (int)Math.Ceiling(s.EvapKwTh / EvapUnitKwTh), Palette.PaleBlue, "EvapTower");
            plantSlot = BuildUnits(plantSlot, (int)Math.Ceiling(s.ChillerKwTh / ChillerUnitKwTh), Palette.Slate, "Chiller");
            if (s.BatteryKwhCap > 0 && plantSlot < Site.PlantSlots.Count)
                plantSlot = BuildUnits(plantSlot, 1, Palette.Amber, "BatteryPack");

            int solarRows = Mathf.Min(Site.SolarSlots.Count, (int)Math.Ceiling(s.SolarKwp / SolarRowKwp));
            for (int i = 0; i < solarRows; i++)
            {
                var pm = new ProcMesh();
                pm.Box(new Vector3(0, 0.9f, 0), new Vector3(6.5f, 0.12f, 2.2f), Palette.Ink);
                pm.Box(new Vector3(0, 0.45f, 0), new Vector3(0.15f, 0.9f, 0.15f), Palette.Slate);
                _plantGos.Add(MatLib.Spawn("SolarRow", pm.Build("solar"), Site.Root, Site.SolarSlots[i]));
            }

            OnPanelChanged();
        }

        private GameObject BuildRack(Vector3 pos, int index)
        {
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 1.1f, 0), new Vector3(0.8f, 2.2f, 1.1f), Palette.Slate);
            pm.Box(new Vector3(0, 2.15f, 0), new Vector3(0.82f, 0.06f, 1.12f), Palette.Ink);
            // status LEDs
            pm.Box(new Vector3(0.42f, 1.8f, 0.3f), new Vector3(0.03f, 0.06f, 0.06f), Palette.Field);
            var rack = MatLib.Spawn("Rack" + index, pm.Build("rack"), Site.Root, pos);

            for (int p = 0; p < PanelsPerRack; p++)
            {
                var ppm = new ProcMesh();
                ppm.Box(Vector3.zero, new Vector3(0.05f, 0.5f, 0.9f), Palette.ProgramBlue);
                var panelGo = MatLib.Spawn("Panel", ppm.Build("panel"), rack.transform,
                    new Vector3(0.44f, 0.6f + p * 0.75f, 0));
                var panel = panelGo.AddComponent<BlankingPanel>();
                panel.Facility = this;
                _panels.Add(panel);
            }
            return rack;
        }

        private int BuildUnits(int slot, int count, Color color, string name)
        {
            for (int i = 0; i < count && slot < Site.PlantSlots.Count; i++, slot++)
            {
                var pm = new ProcMesh();
                pm.Box(new Vector3(0, 1f, 0), new Vector3(1.8f, 2f, 1.8f), color);
                pm.Box(new Vector3(0, 2.05f, 0), new Vector3(1.4f, 0.1f, 1.4f), Palette.Ink); // grille
                _plantGos.Add(MatLib.Spawn(name, pm.Build("unit"), Site.Root, Site.PlantSlots[slot]));
            }
            return slot;
        }

        // ------------------------------------------------------------------
        // Blanking panels → cooling derate
        // ------------------------------------------------------------------

        public void OnPanelChanged()
        {
            int total = Mathf.Max(1, _panels.Count);
            int missing = 0;
            foreach (BlankingPanel p in _panels) if (p != null && !p.Mounted) missing++;
            State.missingPanels = missing;
            // Presentation heuristic (flagged in the report): airflow quality
            // falls to 50 % with every panel missing. The sim only sees the
            // resulting derate, per the command contract.
            double derate = 1.0 - 0.5 * missing / total;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetCoolingDerate, A = derate },
                missing + " blanking panel(s) missing");
        }

        // ------------------------------------------------------------------
        // Room queries for hazards
        // ------------------------------------------------------------------

        public int RoomNodeCount(Room room)
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return 0;
            // Slice: all racks live in Hall A.
            return room == Site.HallA ? driver.Sim.State.NodesInstalled : 0;
        }

        public double RoomLoadFraction(Room room)
        {
            return room == Site.HallA ? 1.0 : 0.15; // plant/office carry aux only
        }

        // ------------------------------------------------------------------
        // Placement + routing (M3)
        // ------------------------------------------------------------------

        private void HandlePlacementInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || GameBootstrap.UiCapturesMouse) return;

            for (int i = 0; i < PlaceKinds.Length; i++)
            {
                Key key = (Key)((int)Key.Digit1 + i);
                if (kb[key].wasPressedThisFrame)
                {
                    _placeKind = _placeKind == PlaceKinds[i] ? null : PlaceKinds[i];
                    _draftRoute = null;
                    UpdateHint();
                }
            }
            if (kb.digit8Key.wasPressedThisFrame) StartRoute("power");
            if (kb.digit9Key.wasPressedThisFrame) StartRoute("cooling");
            if (kb.digit0Key.wasPressedThisFrame) StartRoute("network");

            if (_placeKind != null && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                Cursor.lockState == CursorLockMode.Locked)
            {
                PlaceNext(_placeKind);
            }

            if (_draftRoute != null && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame) AddRoutePoint();
                if (mouse.rightButton.wasPressedThisFrame) FinishRoute();
            }
        }

        private void UpdateHint()
        {
            PlacementHint = _placeKind != null
                ? "placing: " + _placeKind + " (click to build, key again to cancel)"
                : _draftRoute != null
                    ? "routing " + _draftRoute.kind + ": click waypoints, right-click to finish"
                    : "";
        }

        private void PlaceNext(string kind)
        {
            var d = GameBootstrap.Driver;
            if (d == null || d.Sim == null) return;
            var s = d.Sim.State;
            switch (kind)
            {
                case "rack":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.AddNodes, A = NodesPerRack },
                        "rack placed (+10 nodes)");
                    break;
                case "evap":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.EvapKwTh, B = s.EvapKwTh + EvapUnitKwTh }, "evaporative tower placed");
                    break;
                case "chiller":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.ChillerKwTh, B = s.ChillerKwTh + ChillerUnitKwTh }, "chiller placed");
                    break;
                case "freecool":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.FreecoolKwTh, B = s.FreecoolKwTh + FreecoolUnitKwTh }, "free-cooling unit placed");
                    break;
                case "solar":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.SolarKwp, B = s.SolarKwp + SolarRowKwp }, "solar row placed");
                    break;
                case "battery":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.BatteryKwh, B = s.BatteryKwhCap + BatteryPackKwh }, "battery pack placed");
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.BatteryKw, B = s.BatteryKw + BatteryPackKw }, null);
                    break;
                case "diesel":
                    GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetPlant, A = (int)PlantKind.DieselKw, B = s.DieselKw + DieselUnitKw }, "diesel unit placed");
                    break;
            }
            State.items.Add(new PlacedItem { kind = kind, slot = State.items.Count });
            // Racks/plant re-derive from sim state on the next Update; solar too.
            _lastSyncedNodes = -1;
        }

        private void StartRoute(string kind)
        {
            _placeKind = null;
            _draftRoute = new RoutePath { kind = kind };
            UpdateHint();
        }

        private void AddRoutePoint()
        {
            var player = GameBootstrap.LocalPlayer;
            if (player == null || player.Cam == null) return;
            var ray = new Ray(player.Cam.transform.position, player.Cam.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, 30f)) return;
            // 2 m grid snap, per construction-routing.md §1.
            float x = Mathf.Round(hit.point.x / 2f) * 2f;
            float z = Mathf.Round(hit.point.z / 2f) * 2f;
            _draftRoute.xs.Add(x);
            _draftRoute.zs.Add(z);
            RebuildDraft();
        }

        private void FinishRoute()
        {
            if (_draftRoute != null && _draftRoute.xs.Count >= 2)
            {
                State.routes.Add(_draftRoute);
                _routeGos.Add(BuildRouteMesh(_draftRoute, false));
                RecomputeRouteLoss();
            }
            if (_draftGo != null) Destroy(_draftGo);
            _draftGo = null;
            _draftRoute = null;
            UpdateHint();
        }

        private void RebuildDraft()
        {
            if (_draftGo != null) Destroy(_draftGo);
            _draftGo = BuildRouteMesh(_draftRoute, true);
        }

        private GameObject BuildRouteMesh(RoutePath route, bool ghost)
        {
            var pm = new ProcMesh();
            Color c = route.kind == "power" ? Palette.Amber
                    : route.kind == "cooling" ? Palette.PaleBlue : Palette.Field;
            if (ghost) c.a = 0.5f;
            float h = route.kind == "cooling" ? 2.5f : 2.8f;
            for (int i = 1; i < route.xs.Count; i++)
            {
                var a = new Vector3(route.xs[i - 1], h, route.zs[i - 1]);
                var b = new Vector3(route.xs[i], h, route.zs[i]);
                Vector3 mid = (a + b) * 0.5f;
                Vector3 delta = b - a;
                var size = new Vector3(Mathf.Max(0.25f, Mathf.Abs(delta.x)), 0.18f,
                                       Mathf.Max(0.25f, Mathf.Abs(delta.z)));
                pm.Box(mid, size, c);
                // support post at each waypoint
                pm.Box(new Vector3(a.x, h / 2, a.z), new Vector3(0.1f, h, 0.1f), Palette.Slate);
            }
            return MatLib.Spawn("Route-" + route.kind, pm.Build("route"), Site.Root, Vector3.zero,
                !ghost, ghost);
        }

        public double RouteLengthM(RoutePath r)
        {
            double len = 0;
            for (int i = 1; i < r.xs.Count; i++)
            {
                double dx = r.xs[i] - r.xs[i - 1];
                double dz = r.zs[i] - r.zs[i - 1];
                len += Math.Sqrt(dx * dx + dz * dz);
            }
            return len;
        }

        /// <summary>Length → loss, ROUTE_LOSS_PCT_PER_100M of a nominal 100 kW
        /// served per power run, half of that for pumped cooling runs. Network
        /// runs lose nothing. Pushed into the sim's aux demand.</summary>
        public void RecomputeRouteLoss()
        {
            var d = GameBootstrap.Driver;
            if (d == null) return;
            double pct = d.Balance.RouteLossPctPer100M / 100.0;
            double loss = 0;
            foreach (RoutePath r in State.routes)
            {
                double per100 = RouteLengthM(r) / 100.0;
                if (r.kind == "power") loss += per100 * pct * 100.0;
                else if (r.kind == "cooling") loss += per100 * pct * 50.0;
            }
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetRouteLossKw, A = loss },
                "route losses now " + loss.ToString("0.0", CultureInfo.InvariantCulture) + " kW");
        }

        // ------------------------------------------------------------------
        // Persistence (M6) + net blob (M4)
        // ------------------------------------------------------------------

        public string ToJson() { return JsonUtility.ToJson(State); }

        public void ApplyJson(string json)
        {
            State = JsonUtility.FromJson<FacilityState>(json) ?? new FacilityState();
            foreach (GameObject go in _routeGos) if (go != null) Destroy(go);
            _routeGos.Clear();
            foreach (RoutePath r in State.routes) _routeGos.Add(BuildRouteMesh(r, false));
            _lastSyncedNodes = -1; // racks/plant resync from sim
        }
    }
}
