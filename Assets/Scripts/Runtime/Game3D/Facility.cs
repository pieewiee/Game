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
    /// exclusively through commands (AddPlant / AddNodes / AddRouteLossKw /
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
        private Aggregates _lastSynced;
        private bool _syncDirty = true;
        private GameObject _slotGhost;
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

        /// <summary>What the facility currently owns, from the authoritative
        /// source: the replicated TickReport on a client (its local sim is
        /// paused and stale), the live sim state on the host/solo.</summary>
        private struct Aggregates
        {
            public int Nodes;
            public double EvapKwTh, ChillerKwTh, FreecoolKwTh, SolarKwp, BatteryKwhCap;
        }

        private Aggregates CurrentAggregates()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient)
            {
                var r = net.RemoteReport;
                return new Aggregates
                {
                    Nodes = r.NodesInstalled,
                    EvapKwTh = r.EvapKwTh, ChillerKwTh = r.ChillerKwTh, FreecoolKwTh = r.FreecoolKwTh,
                    SolarKwp = r.SolarKwp, BatteryKwhCap = r.BatteryKwhCap,
                };
            }
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return default;
            var s = driver.Sim.State;
            return new Aggregates
            {
                Nodes = s.NodesInstalled,
                EvapKwTh = s.EvapKwTh, ChillerKwTh = s.ChillerKwTh, FreecoolKwTh = s.FreecoolKwTh,
                SolarKwp = s.SolarKwp, BatteryKwhCap = s.BatteryKwhCap,
            };
        }

        public int CurrentNodes() { return CurrentAggregates().Nodes; }

        private static bool AggregatesEqual(in Aggregates x, in Aggregates y)
        {
            return x.Nodes == y.Nodes && x.EvapKwTh == y.EvapKwTh &&
                   x.ChillerKwTh == y.ChillerKwTh && x.FreecoolKwTh == y.FreecoolKwTh &&
                   x.SolarKwp == y.SolarKwp && x.BatteryKwhCap == y.BatteryKwhCap;
        }

        private void Update()
        {
            // Watch EVERY aggregate, not just nodes: a placed chiller changes
            // no node count but must still appear when its delta lands.
            Aggregates a = CurrentAggregates();
            if (_syncDirty || !AggregatesEqual(a, _lastSynced)) SyncFromSim(false);
            HandlePlacementInput();
            UpdateSlotGhost(a);
        }

        /// <summary>While a delivery is on site, the next rack position glows:
        /// nobody should have to guess where a pallet is supposed to go.</summary>
        private void UpdateSlotGhost(Aggregates a)
        {
            bool want = Pallet.Current != null;
            if (!want)
            {
                if (_slotGhost != null) _slotGhost.SetActive(false);
                return;
            }
            int used = (a.Nodes + NodesPerRack - 1) / NodesPerRack;
            if (used >= Site.RackSlots.Count)
            {
                if (_slotGhost != null) _slotGhost.SetActive(false);
                return;
            }
            if (_slotGhost == null)
            {
                var pm = new ProcMesh();
                var c = Palette.Amber; c.a = 0.28f;
                pm.Box(new Vector3(0, 1.1f, 0), new Vector3(0.9f, 2.2f, 1.2f), c);
                _slotGhost = MatLib.Spawn("NextRackSlot", pm.Build("slotghost"),
                    Site.Root, Site.RackSlots[used], false, true);
            }
            _slotGhost.SetActive(true);
            _slotGhost.transform.localPosition = Site.RackSlots[used];
        }

        /// <summary>Rebuilds racks and plant visuals from the CURRENT
        /// authoritative aggregates. Purely visual: sends NO commands — on load
        /// the replay already reproduced derates and losses, and on a client
        /// the local view must never write back into the host's sim.</summary>
        public void SyncFromSim(bool initial)
        {
            if (Site == null) return;
            Aggregates s = CurrentAggregates();
            _lastSynced = s;
            _syncDirty = false;
            int nodeCount = s.Nodes;

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

            // Restore missing-panel state SILENTLY: rebuilt panels default to
            // Mounted, but the hazard must survive a rack resync (and a load).
            // No command is sent — the sim's derate is already correct (replay
            // or live), only the visuals needed rebuilding. With zero panels
            // (client before its first snapshot) the replicated count survives
            // untouched instead of being clobbered to 0.
            if (_panels.Count > 0)
            {
                State.missingPanels = Mathf.Min(State.missingPanels, _panels.Count);
                ApplyMissingVisuals();
            }
        }

        /// <summary>First-N rule: the count is the replicated truth, the first
        /// State.missingPanels slot panels render as pulled.</summary>
        private void ApplyMissingVisuals()
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                bool missing = i < State.missingPanels;
                if (_panels[i] == null) continue;
                _panels[i].Mounted = !missing;
                _panels[i].gameObject.SetActive(!missing);
            }
        }

        private GameObject BuildRack(Vector3 pos, int index)
        {
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 1.1f, 0), new Vector3(0.8f, 2.2f, 1.1f), Palette.Slate);
            pm.Box(new Vector3(0, 2.15f, 0), new Vector3(0.82f, 0.06f, 1.12f), Palette.Ink);
            // status LEDs
            pm.Box(new Vector3(0.42f, 1.8f, 0.3f), new Vector3(0.03f, 0.06f, 0.06f), Palette.Field);
            var rack = MatLib.Spawn("Rack" + index, pm.Build("rack"), Site.Root, pos);
            rack.AddComponent<RackRemount>().Facility = this;

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

        public bool HasMissingPanel { get { return State.missingPanels > 0; } }

        /// <summary>A panel was physically pulled here: hide this machine's
        /// slot immediately, hand the player a loose panel, and route the
        /// COUNT change to the single authority (the host's State).</summary>
        public void OnPanelPulled(BlankingPanel slot, PlayerRig player)
        {
            slot.Mounted = false;
            slot.gameObject.SetActive(false);
            if (player.Carried == null)
            {
                GameObject loose = SpawnLoosePanel(slot.transform.position + Vector3.up * 0.2f);
                player.PickUp(loose.GetComponent<LoosePanel>());
            }
            RequestPanelDelta(1, player.PlayerName, "pulled a blanking panel");
        }

        /// <summary>Remount a carried loose panel: consume it, reveal a slot
        /// locally for instant feedback, and route the count change.</summary>
        public void RemountPanel(PlayerRig player)
        {
            if (!(player.Carried is LoosePanel) || State.missingPanels <= 0) return;
            player.ConsumeCarried();
            RequestPanelDelta(-1, player.PlayerName, "remounted a blanking panel");
        }

        /// <summary>Panel-count changes are host-authoritative like hazards:
        /// each machine's pulls are per-view, but ONE count drives the derate
        /// (a client-local absolute would erase the host's own pulls).</summary>
        private void RequestPanelDelta(int delta, string actorName, string action)
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient)
            {
                net.SendPanelDelta(delta);
                return;
            }
            ApplyPanelDelta(delta, actorName, action);
        }

        /// <summary>HOST/solo only: mutate the one true count, refresh visuals,
        /// and push the derived derate into the sim. The facility broadcast
        /// then converges every client's panels onto the first-N rule.</summary>
        public void ApplyPanelDelta(int delta, string actorName, string action = null)
        {
            int total = Mathf.Max(1, _panels.Count);
            State.missingPanels = Mathf.Clamp(State.missingPanels + delta, 0, _panels.Count);
            ApplyMissingVisuals();
            double derate = 1.0 - 0.5 * State.missingPanels / total;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetCoolingDerate, A = derate }, null);
            GameBootstrap.AddLedger(actorName,
                (action ?? "changed blanking panels") + " (" + State.missingPanels + " missing)");
        }

        /// <summary>The physical panel object a player carries around.</summary>
        public static GameObject SpawnLoosePanel(Vector3 pos)
        {
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.05f, 0.5f, 0.9f), Palette.ProgramBlue);
            var go = MatLib.Spawn("LoosePanel", pm.Build("panel"), null, pos);
            go.AddComponent<LoosePanel>();
            return go;
        }

        // ------------------------------------------------------------------
        // Room queries for hazards
        // ------------------------------------------------------------------

        public int RoomNodeCount(Room room)
        {
            // Slice: all racks live in Hall A.
            return room == Site.HallA ? CurrentNodes() : 0;
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
            // A locked cursor means the player is IN the world. With the
            // cursor Esc-freed for the debug console, digits typed into its
            // text fields must not arm placement modes.
            if (kb == null || GameBootstrap.UiWantsCursor ||
                Cursor.lockState != CursorLockMode.Locked) return;

            for (int i = 0; i < PlaceKinds.Length; i++)
            {
                Key key = (Key)((int)Key.Digit1 + i);
                if (!kb[key].wasPressedThisFrame) continue;
                if (PlaceKinds[i] == "rack")
                {
                    // Racks are not placed, they are DELIVERED and then
                    // installed with the forklift (workplace-accidents.md §4.4).
                    _placeKind = null;
                    _draftRoute = null;
                    PlacementHint = Forklift.OrderDelivery();
                    continue;
                }
                _placeKind = _placeKind == PlaceKinds[i] ? null : PlaceKinds[i];
                _draftRoute = null;
                UpdateHint();
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

        private static void Place(PlantKind kind, double delta, string desc)
        {
            // DELTAS (never absolute targets computed from local state): the
            // sim composes concurrent placements from any number of players,
            // and a paused client needs no state to place correctly.
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.AddPlant, A = (int)kind, B = delta }, desc);
        }

        private void PlaceNext(string kind)
        {
            var netc = GameBootstrap.Net;
            if (netc != null && netc.IsClient && netc.RemoteTick == 0)
            {
                PlacementHint = "waiting for host state...";
                return;
            }
            Aggregates s = CurrentAggregates();
            switch (kind)
            {
                case "rack":
                    return;   // deliveries only; see HandlePlacementInput
                case "evap":
                    if (PlantSlotsFull(s)) return;
                    Place(PlantKind.EvapKwTh, EvapUnitKwTh, "evaporative tower placed");
                    break;
                case "chiller":
                    if (PlantSlotsFull(s)) return;
                    Place(PlantKind.ChillerKwTh, ChillerUnitKwTh, "chiller placed");
                    break;
                case "freecool":
                    if (PlantSlotsFull(s)) return;
                    Place(PlantKind.FreecoolKwTh, FreecoolUnitKwTh, "free-cooling unit placed");
                    break;
                case "solar":
                    if ((int)Math.Ceiling((s.SolarKwp + SolarRowKwp) / SolarRowKwp) > Site.SolarSlots.Count)
                    { PlacementHint = "no free solar slots"; return; }
                    Place(PlantKind.SolarKwp, SolarRowKwp, "solar row placed");
                    break;
                case "battery":
                    // A second pack grows the existing bank; only the first
                    // pack claims a plant slot for its visual.
                    if (s.BatteryKwhCap <= 0 && PlantSlotsFull(s)) return;
                    Place(PlantKind.BatteryKwh, BatteryPackKwh, "battery pack placed");
                    Place(PlantKind.BatteryKw, BatteryPackKw, null);
                    break;
                case "diesel":
                    Place(PlantKind.DieselKw, DieselUnitKw, "diesel unit placed");
                    break;
            }
            State.items.Add(new PlacedItem { kind = kind, slot = State.items.Count });
        }

        private bool PlantSlotsFull(Aggregates s)
        {
            int used = (int)Math.Ceiling(s.FreecoolKwTh / FreecoolUnitKwTh)
                     + (int)Math.Ceiling(s.EvapKwTh / EvapUnitKwTh)
                     + (int)Math.Ceiling(s.ChillerKwTh / ChillerUnitKwTh)
                     + (s.BatteryKwhCap > 0 ? 1 : 0);
            if (used + 1 > Site.PlantSlots.Count)
            {
                PlacementHint = "no free plant slots";
                return true;
            }
            return false;
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
                var net = GameBootstrap.Net;
                if (net != null && net.IsClient)
                {
                    // The HOST owns the route list: geometry travels there, the
                    // host recomputes the loss itself (never trusting a client
                    // kW) and the facility broadcast brings the run back.
                    net.SendRoute(JsonUtility.ToJson(_draftRoute));
                }
                else
                {
                    State.routes.Add(_draftRoute);
                    _routeGos.Add(BuildRouteMesh(_draftRoute, false));
                    SendRouteLoss(_draftRoute);
                }
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
        /// runs lose nothing. Sent as a DELTA for the one just-finished run,
        /// so several players routing at once (and clients with a paused local
        /// sim) compose correctly in the host's ledger.</summary>
        private double RouteLoss(RoutePath route)
        {
            var d = GameBootstrap.Driver;
            if (d == null) return 0;
            double pct = d.Balance.RouteLossPctPer100M / 100.0;
            double per100 = RouteLengthM(route) / 100.0;
            return route.kind == "power" ? per100 * pct * 100.0
                 : route.kind == "cooling" ? per100 * pct * 50.0 : 0.0;
        }

        private void SendRouteLoss(RoutePath route)
        {
            double loss = RouteLoss(route);
            if (loss <= 0) return;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.AddRouteLossKw, A = loss },
                "routed a " + RouteLengthM(route).ToString("0", CultureInfo.InvariantCulture) + " m " +
                route.kind + " run (+" + loss.ToString("0.0", CultureInfo.InvariantCulture) + " kW loss)");
        }

        /// <summary>HOST: a client-drawn route arrives — validate, own it,
        /// price it, ledger it under the sender's name.</summary>
        public void AddRouteFromNet(RoutePath route, string actorName)
        {
            if (route == null || route.xs == null || route.zs == null) return;
            if (route.xs.Count < 2 || route.xs.Count != route.zs.Count || route.xs.Count > 64) return;
            if (route.kind != "power" && route.kind != "cooling" && route.kind != "network") return;
            State.routes.Add(route);
            _routeGos.Add(BuildRouteMesh(route, false));
            double loss = RouteLoss(route);
            if (loss > 0)
                GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.AddRouteLossKw, A = loss }, null);
            GameBootstrap.AddLedger(actorName,
                "routed a " + RouteLengthM(route).ToString("0", CultureInfo.InvariantCulture) + " m " +
                route.kind + " run (+" + loss.ToString("0.0", CultureInfo.InvariantCulture) + " kW loss)");
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
            _syncDirty = true; // racks/plant/panel visuals resync from state
        }
    }
}
