using System.Collections.Generic;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The town's presence around the site: distant, faceless figures on the
    /// ring road, at the fence line and at the vehicle gate, plus a handful
    /// of cars. Residents are never seen close up (art-bible.md §3) and
    /// there is no staff pathing (open-questions.md Q18), so every figure
    /// keeps its distance from the player and none of them changes the sim.
    ///
    /// Same inputs, evaluated on a local cadence — not lockstep: what the
    /// figures do is read from the replicated report, so a client sees the
    /// same crowd for the same reasons, but not frame-identical. Head-counts
    /// are re-evaluated every few REAL seconds and keyed on (day, hour/3,
    /// slot), because at the default 24 ticks/s a per-tick roll would flicker.
    ///
    /// Nothing here is a person: no faces, no hats, no colliders, no lights,
    /// no dialogue. Placards are blank (open-questions.md Q27).
    /// </summary>
    public sealed class Presence : MonoBehaviour
    {
        /// <summary>Player option "Ambient figures".</summary>
        public static bool Enabled = true;
        /// <summary>Debug multiplier on every target head-count.</summary>
        public static float DebugDensity = 1f;

        public enum FigureKind { Walker, Filmer, Protester, Car }

        private struct Figure
        {
            public FigureKind Kind;
            public int Slot;
            public Transform Tf;
            public Renderer Body, Extra, Placard;   // Extra: umbrella / phone / headlights
            public bool Wanted;        // the last target evaluation wants this slot present
            public bool Shown;         // spawned: scaling in, standing, or scaling out
            public bool Leaving;       // scaling out; sticky until gone
            public bool DormantNear;   // walker vanished from a player: waits until they are far
            public bool Fleeing;       // walker reversed away from a player
            public float Scale;        // 0..1, uniform scale-in from the feet
            public float ShownSince, DormantUntil;
            public int Route, Seg;     // polyline (walkers: route, cars: lane) and cached segment
            public float S, Dir, Speed;
            public float Vel;          // cars: current speed, eased toward the corner speed
            public float Yaw, BaseYaw, SwayPhase;
            public Vector3 Spot;       // standing figures: the validated spot
            public Vector3 Pos;        // world position this frame
        }

        private const uint PresenceSeed = 7331u;
        private const int Walkers = 16, Filmers = 8, Protesters = 6, Cars = 4;
        private const int FigureCount = Walkers + Filmers + Protesters + Cars;

        private const float RecomputeEvery = 4f;      // real seconds between target evaluations
        private const float MinDwell = 6f;            // a shown figure stays at least this long
        private const float ScaleTime = 0.4f;         // scale-in / scale-out duration
        private const float IncidentMemory = 20f;     // an accident keeps the phones out this long
        private const float SpawnClearDist = 20f;     // nothing appears closer to the player
        private const float CullDist = 180f;          // fog has eaten them well before this
        private const float WalkerReverseDist = 14f, WalkerReleaseDist = 20f, WalkerVanishDist = 7f, WalkerReturnDist = 30f;
        private const float StanderTurnDist = 9f, StanderVanishDist = 5f, StanderDormant = 60f;
        private const float WalkSpeed = 1.2f, FleeMult = 1.35f, CarSpeed = 8f, LaneOffset = 2f;
        // The carriageway centre line sits this far outside the fence rectangle
        // (design_site-plan §3: a 7 m road starting 5 m past the fence).
        private const float RingRoadOffset = 8.5f, RoadTopY = 0.04f;
        // Corner arcs: the outer lane turns wide, the inner lane hugs a kerb
        // corner 1.5 m away, so its radius is what keeps the body off the kerb.
        private const float OuterCornerR = 4.5f, InnerCornerR = 2.2f, CornerSpeed = 0.6f, CarAccel = 8f;
        // Past a parked car the lane eases to this far from the centre line,
        // ramping in and out over PinchRamp metres.
        private const float PinchOffset = 0.3f, PinchRamp = 4f, CarHalfWidth = 0.9f, CarClearance = 0.5f;
        private const float LaneClearance = CarHalfWidth + CarClearance;
        // Layout validation. A stander keeps LaneClearance from either lane
        // and RouteClearance from a route, or walkers slide through it.
        private const float SpawnClearance = 9f, ObstacleClearance = 1.5f, RouteClearance = 0.8f, MinRouteLen = 5f;

        private SiteRefs _site;
        private Transform _root;
        private Figure[] _figs;

        // Validated layout. Route indices are kept stable (a dropped route is
        // null) because index 1 has a meaning: the zebra crossing.
        private Vector3[][] _routes = new Vector3[0][];
        private float[][] _routeCum = new float[0][];
        private bool[] _routeClosed = new bool[0];
        private readonly List<Vector3> _fenceSpots = new List<Vector3>();
        private readonly List<Vector3> _gateSpots = new List<Vector3>();
        // Ring road lanes, each closed (first vertex repeated) and in its own
        // direction of travel: 0 outer, 1 inner. Per segment: on a corner arc.
        // Pinch spans are (start, end) arc-length pairs.
        private Vector3[][] _lanes = new Vector3[0][];
        private float[][] _laneCum = new float[0][];
        private bool[][] _laneArc = new bool[0][];
        private float[][] _lanePinch = new float[0][];

        private Mesh _walkerA, _walkerB, _umbrella, _phone, _placard, _car, _carLights;

        private float _nextRecompute;
        private double _lastAccident;
        private bool _accidentSeen;
        private float _incidentUntil;
        private bool _windowsEarly, _windowsLate, _windowsInit;
        private bool _wasEnabled = true;
        private int _protestersShown;
        private Vector3 _protestCentroid;

        // ---- deterministic hashing (murmur3 fmix) --------------------------

        private static uint H(uint a, uint b, uint c)
        {
            uint h = a * 0x9E3779B1u ^ b * 0x85EBCA6Bu ^ c * 0xC2B2AE35u;
            h ^= h >> 16; h *= 0x85EBCA6Bu;
            h ^= h >> 13; h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        /// <summary>[0, 1) from the top 24 hash bits — exactly representable, never 1.</summary>
        private static float F01(uint a, uint b, uint c)
        {
            return (H(a, b, c) >> 8) / 16777216f;
        }

        // ---- lifecycle --------------------------------------------------------

        private void Start()
        {
            _site = GameBootstrap.Site;
            if (_site == null) return;
            _root = new GameObject("Presence").transform;
            _root.SetParent(transform, false);

            ValidateRoutes();
            BuildLanes();
            ValidateSpots();
            BuildMeshes();
            _figs = new Figure[FigureCount];
            int n = 0;
            for (int i = 0; i < Walkers; i++) SpawnFigure(n++, FigureKind.Walker, i);
            for (int i = 0; i < Filmers; i++) SpawnFigure(n++, FigureKind.Filmer, i);
            for (int i = 0; i < Protesters; i++) SpawnFigure(n++, FigureKind.Protester, i);
            for (int i = 0; i < Cars; i++) SpawnFigure(n++, FigureKind.Car, i);

            GateIntercom.Create(_root, _site);
            ListeningPost.Create(_root, _site);
        }

        // ---- layout validation ---------------------------------------------
        // Figures use only validated data: every spot and route vertex keeps
        // 9 m from the player spawn and 1.5 m from every town obstacle.

        /// <summary>A route is all or nothing, as CityBuilder.AddRoute has it:
        /// a polyline with a vertex missing is a different path, possibly
        /// through a wall, or a stub that stacks every walker on one tile.</summary>
        private void ValidateRoutes()
        {
            var routes = _site.ResidentRoutes;
            _routes = new Vector3[routes.Count][];
            _routeCum = new float[routes.Count][];
            _routeClosed = new bool[routes.Count];
            for (int r = 0; r < routes.Count; r++)
            {
                Vector3[] v = routes[r];
                if (v == null || v.Length < 2)
                {
                    Debug.LogWarning("[Presence] route " + r + " has fewer than two vertices; dropped");
                    continue;
                }
                string fault = null;
                for (int i = 0; i < v.Length && fault == null; i++)
                {
                    fault = Fault(v[i]);
                    if (fault != null) Debug.LogWarning("[Presence] route " + r + " dropped whole: vertex " + i + " at " + v[i] + " " + fault);
                }
                if (fault != null) continue;
                float[] cum = Cumulative(v);
                if (cum[cum.Length - 1] < MinRouteLen)
                {
                    Debug.LogWarning("[Presence] route " + r + " is shorter than " + MinRouteLen + " m; dropped");
                    continue;
                }
                _routes[r] = v;
                _routeCum[r] = cum;
                _routeClosed[r] = (v[0] - v[v.Length - 1]).sqrMagnitude < 0.01f;
            }
        }

        /// <summary>After the lanes exist: a stander also keeps out of the
        /// carriageway and off the routes.</summary>
        private void ValidateSpots()
        {
            foreach (Vector3 p in _site.FenceLineSpots)
                if (SpotOk(p, "fence-line spot")) _fenceSpots.Add(p);
            foreach (Vector3 p in _site.GateProtestSpots)
                if (SpotOk(p, "gate protest spot")) _gateSpots.Add(p);
        }

        private bool SpotOk(Vector3 p, string what)
        {
            string fault = Fault(p);
            if (fault == null)
            {
                for (int l = 0; l < _lanes.Length && fault == null; l++)
                    if (PolylineDist2(_lanes[l], p) < LaneClearance * LaneClearance) fault = "stands in a ring road lane";
                for (int r = 0; r < _routes.Length && fault == null; r++)
                    if (_routes[r] != null && PolylineDist2(_routes[r], p) < RouteClearance * RouteClearance)
                        fault = "is within " + RouteClearance + " m of route " + r;
            }
            if (fault == null) return true;
            Debug.LogWarning("[Presence] " + what + " at " + p + " " + fault + "; dropped");
            return false;
        }

        /// <summary>Why a point may not carry a figure, or null.</summary>
        private string Fault(Vector3 p)
        {
            Vector3 spawn = _site.StreetSpawn;
            float dx = p.x - spawn.x, dz = p.z - spawn.z;
            if (dx * dx + dz * dz < SpawnClearance * SpawnClearance)
                return "is within " + SpawnClearance + " m of the spawn";
            var obstacles = _site.TownObstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Bounds b = obstacles[i];
                Vector3 c = b.center, e = b.size * 0.5f;
                float ox = Mathf.Max(0f, Mathf.Abs(p.x - c.x) - e.x);
                float oz = Mathf.Max(0f, Mathf.Abs(p.z - c.z) - e.z);
                if (ox * ox + oz * oz < ObstacleClearance * ObstacleClearance)
                    return "is within " + ObstacleClearance + " m of a town obstacle";
            }
            return null;
        }

        private static float PolylineDist2(Vector3[] v, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < v.Length - 1; i++)
            {
                Vector3 a = v[i], d = v[i + 1] - a;
                float dd = d.x * d.x + d.z * d.z;
                float t = dd > 1e-6f ? Mathf.Clamp01(((p.x - a.x) * d.x + (p.z - a.z) * d.z) / dd) : 0f;
                float dx = a.x + d.x * t - p.x, dz = a.z + d.z * t - p.z;
                float q = dx * dx + dz * dz;
                if (q < best) best = q;
            }
            return best;
        }

        private static float[] Cumulative(Vector3[] v)
        {
            var cum = new float[v.Length];
            for (int i = 1; i < v.Length; i++) cum[i] = cum[i - 1] + (v[i] - v[i - 1]).magnitude;
            return cum;
        }

        /// <summary>Two lanes from the fence rectangle, each in its direction
        /// of travel — drive on the right: the outer lane runs counter-clockwise
        /// seen from above (east along the south road first), the inner lane
        /// clockwise. Corners are quarter-circle arcs tangent to both edges.</summary>
        private void BuildLanes()
        {
            float x0 = _site.FenceX0 - RingRoadOffset, x1 = _site.FenceX1 + RingRoadOffset;
            float z0 = _site.FenceZ0 - RingRoadOffset, z1 = _site.FenceZ1 + RingRoadOffset;
            float o = LaneOffset;
            _lanes = new Vector3[2][];
            _laneCum = new float[2][];
            _laneArc = new bool[2][];
            _lanePinch = new float[2][];
            _lanes[0] = RoundedLoop(new[] { V(x0 - o, z0 - o), V(x1 + o, z0 - o), V(x1 + o, z1 + o), V(x0 - o, z1 + o) },
                OuterCornerR, out _laneArc[0]);
            _lanes[1] = RoundedLoop(new[] { V(x1 - o, z0 + o), V(x0 + o, z0 + o), V(x0 + o, z1 - o), V(x1 - o, z1 - o) },
                InnerCornerR, out _laneArc[1]);
            for (int l = 0; l < 2; l++)
            {
                _laneCum[l] = Cumulative(_lanes[l]);
                _lanePinch[l] = PinchSpans(_lanes[l], _laneCum[l]);
            }
            WarnBlockedLanes(new[] { V(x0, z0), V(x1, z0), V(x1, z1), V(x0, z1), V(x0, z0) });
        }

        private static Vector3 V(float x, float z) { return new Vector3(x, RoadTopY, z); }

        /// <summary>A closed loop through the corners, each replaced by an arc
        /// of radius r; the first vertex is repeated at the end. arc[i] is set
        /// where segment i lies on a corner.</summary>
        private static Vector3[] RoundedLoop(Vector3[] corners, float r, out bool[] arc)
        {
            const int steps = 5;   // segments per corner
            int n = corners.Length;
            var v = new Vector3[n * (steps + 1) + 1];
            arc = new bool[n * (steps + 1)];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = corners[i];
                Vector3 u = (c - corners[(i + n - 1) % n]).normalized;   // into the corner
                Vector3 w = (corners[(i + 1) % n] - c).normalized;       // out of it
                // Tangent points c - u r and c + w r; the arc turns from -w to u.
                Vector3 centre = c - u * r + w * r;
                for (int j = 0; j <= steps; j++)
                {
                    float t = j / (float)steps * Mathf.PI * 0.5f;
                    arc[k] = j < steps;   // the segment leaving the last arc vertex is the straight
                    v[k++] = centre + (u * Mathf.Sin(t) - w * Mathf.Cos(t)) * r;
                }
            }
            v[k] = v[0];
            return v;
        }

        /// <summary>Arc-length spans of a lane where a town obstacle (a parked
        /// car, mostly) would touch a car in it; there the car eases toward the
        /// centre line. A span whose ramp crosses the seam is repeated a lap
        /// away so the per-frame check needs no wrap-around.</summary>
        private float[] PinchSpans(Vector3[] v, float[] cum)
        {
            var spans = new List<float>();
            var obstacles = _site.TownObstacles;
            float len = cum[cum.Length - 1];
            for (int i = 0; i < obstacles.Count; i++)
            {
                Vector3 c = obstacles[i].center, e = obstacles[i].size * 0.5f + new Vector3(LaneClearance, 0, LaneClearance);
                for (int s = 0; s < v.Length - 1; s++)
                {
                    if (!ClipSegment(v[s], v[s + 1], c, e, out float t0, out float t1)) continue;
                    float segLen = cum[s + 1] - cum[s];
                    float a = cum[s] + t0 * segLen, b = cum[s] + t1 * segLen;
                    spans.Add(a); spans.Add(b);
                    if (a - PinchRamp < 0f) { spans.Add(a + len); spans.Add(b + len); }
                    if (b + PinchRamp > len) { spans.Add(a - len); spans.Add(b - len); }
                }
            }
            return spans.ToArray();
        }

        /// <summary>Slab clip of segment a→b against the plan rectangle centre
        /// c, half extents e: the parameter range inside, if any.</summary>
        private static bool ClipSegment(Vector3 a, Vector3 b, Vector3 c, Vector3 e, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            return ClipAxis(a.x - c.x, b.x - a.x, e.x, ref t0, ref t1)
                && ClipAxis(a.z - c.z, b.z - a.z, e.z, ref t0, ref t1);
        }

        private static bool ClipAxis(float p, float d, float e, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-6f) return Mathf.Abs(p) <= e;
            float ta = (-e - p) / d, tb = (e - p) / d;
            if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 <= t1;
        }

        /// <summary>An obstacle the pinch cannot clear means the road moved
        /// without this file: the lanes come from the fence, not from CityBuilder.</summary>
        private void WarnBlockedLanes(Vector3[] centreLine)
        {
            var obstacles = _site.TownObstacles;
            float reach = PinchOffset + LaneClearance;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Vector3 c = obstacles[i].center, e = obstacles[i].size * 0.5f + new Vector3(reach, 0, reach);
                for (int s = 0; s < centreLine.Length - 1; s++)
                {
                    if (!ClipSegment(centreLine[s], centreLine[s + 1], c, e, out _, out _)) continue;
                    Debug.LogWarning("[Presence] town obstacle at " + c + " reaches the ring road centre line; cars will drive through it");
                    break;
                }
            }
        }

        // ---- figure kit --------------------------------------------------------
        // Feet at the origin, facing +z. Two stacked cylinders, nothing else:
        // the player and staff silhouette (capsule, hard hat, hi-vis) is
        // deliberately not shared.

        private void BuildMeshes()
        {
            _walkerA = WalkerMesh(0.20f, Palette.Ink, Palette.Slate, "walkerA");
            _walkerB = WalkerMesh(0.23f, Palette.Slate, Palette.Ink, "walkerB");

            var um = new ProcMesh();
            um.Box(new Vector3(0.12f, 1.55f, 0.05f), new Vector3(0.02f, 0.7f, 0.02f), Palette.Slate);
            um.Cylinder(new Vector3(0.12f, 1.9f, 0.05f), 0.5f, 0.04f, 8, Palette.Slate);
            _umbrella = um.Build("umbrella");

            var ph = new ProcMesh();
            GlowQuad(ph, new Vector3(0.22f, 1.3f, 0.2f), 0.09f, 0.15f, Palette.Amber);
            _phone = ph.Build("phone");

            var pl = new ProcMesh();
            pl.Box(new Vector3(0.3f, 1.25f, 0.15f), new Vector3(0.03f, 1.2f, 0.03f), Palette.Earth);
            pl.Box(new Vector3(0.3f, 1.9f, 0.15f), new Vector3(0.6f, 0.5f, 0.02f), Palette.Render);
            _placard = pl.Build("placard");

            var car = new ProcMesh();
            car.Box(new Vector3(0, 0.7f, 0), new Vector3(1.8f, 1.4f, 4.2f), Palette.Slate);
            car.Box(new Vector3(0, 1.65f, -0.3f), new Vector3(1.6f, 0.5f, 2.4f), Palette.Ink);
            _car = car.Build("car");

            var lights = new ProcMesh();
            GlowQuad(lights, new Vector3(-0.6f, 0.75f, 2.12f), 0.25f, 0.12f, Palette.Amber);
            GlowQuad(lights, new Vector3(0.6f, 0.75f, 2.12f), 0.25f, 0.12f, Palette.Amber);
            _carLights = lights.Build("carLights");
        }

        private static Mesh WalkerMesh(float bodyR, Color body, Color head, string name)
        {
            var pm = new ProcMesh();
            // The head starts where the body ends: a gap reads as daylight
            // through the silhouette at the 7-9 m minimum distances.
            pm.Cylinder(new Vector3(0, 0.625f, 0), bodyR, 1.25f, 8, body, bodyR * 0.85f);
            pm.Cylinder(new Vector3(0, 1.40f, 0), 0.16f, 0.3f, 6, head);
            return pm.Build(name);
        }

        /// <summary>An unlit quad facing +z, both faces, for the transparent
        /// (unlit) material: the glow reads the same from either side.</summary>
        private static void GlowQuad(ProcMesh pm, Vector3 c, float w, float h, Color colour)
        {
            float hw = w * 0.5f, hh = h * 0.5f;
            Vector3 a = c + new Vector3(hw, -hh, 0), b = c + new Vector3(-hw, -hh, 0);
            Vector3 d = c + new Vector3(hw, hh, 0), e = c + new Vector3(-hw, hh, 0);
            pm.Quad(a, b, e, d, colour);   // faces +z
            pm.Quad(b, a, d, e, colour);   // faces -z
        }

        private static Renderer Part(string name, Mesh mesh, Transform parent, bool glow)
        {
            var mr = MatLib.Spawn(name, mesh, parent, Vector3.zero, false, glow).GetComponent<Renderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false;
            return mr;
        }

        private void SpawnFigure(int index, FigureKind kind, int slot)
        {
            ref Figure f = ref _figs[index];
            f.Kind = kind;
            f.Slot = slot;
            // Not "Avatar ": the forklift and the doors parse that prefix.
            var go = new GameObject("Figure " + kind + " " + slot);
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.zero;
            f.Tf = go.transform;
            uint s = (uint)slot;
            switch (kind)
            {
                case FigureKind.Walker:
                    f.Body = Part("body", slot % 2 == 0 ? _walkerA : _walkerB, f.Tf, false);
                    f.Extra = Part("umbrella", _umbrella, f.Tf, false);
                    f.Speed = WalkSpeed * (0.8f + 0.4f * F01(PresenceSeed, s, 2));
                    break;
                case FigureKind.Filmer:
                    f.Body = Part("body", slot % 2 == 0 ? _walkerB : _walkerA, f.Tf, false);
                    f.Extra = Part("phone", _phone, f.Tf, true);
                    break;
                case FigureKind.Protester:
                    f.Body = Part("body", slot % 2 == 0 ? _walkerA : _walkerB, f.Tf, false);
                    f.Extra = Part("phone", _phone, f.Tf, true);
                    if (slot % 3 != 2) f.Placard = Part("placard", _placard, f.Tf, false);
                    break;
                case FigureKind.Car:
                    f.Body = Part("body", _car, f.Tf, false);
                    f.Extra = Part("lights", _carLights, f.Tf, true);
                    // Even slots take the outer lane, odd the inner; the two
                    // cars of a lane start half a lap apart.
                    f.Route = slot % 2;
                    f.Speed = CarSpeed * (0.9f + 0.2f * F01(PresenceSeed, s, 4));
                    f.Vel = f.Speed;
                    float[] laneCum = _laneCum[f.Route];
                    f.S = laneCum[laneCum.Length - 1] * (slot + F01(PresenceSeed, s, 5)) / Cars;
                    break;
            }
            f.SwayPhase = F01(PresenceSeed, s, 6) * Mathf.PI * 2f;
            f.Scale = 0f;
        }

        // ---- frame update ------------------------------------------------------

        private void Update()
        {
            if (_site == null || _figs == null) return;
            long tick = Weather.CurrentTick();
            // The rendered sky's verdict, so windows, phones and headlights
            // flip in the same frame as the lamp heads and the HUD line;
            // the helper is the fallback for a frame without a Weather.
            Weather weather = Weather.Instance;
            bool dark = weather != null ? weather.Sky.Dark : Weather.IsDark(tick);
            int hour = weather != null ? Mathf.Clamp((int)weather.Sky.HourF, 0, 23) : SimClock.HourOfDay(tick);
            UpdateWindows(dark, hour);

            if (!Enabled)
            {
                if (_wasEnabled) HideAll();
                _wasEnabled = false;
                return;
            }
            _wasEnabled = true;
            PlayerRig player = GameBootstrap.LocalPlayer;
            if (player == null || _routes.Length == 0 || _routes[0] == null) return;

            TickReport r = GameBootstrap.CurrentReport;
            float now = Time.unscaledTime;
            if (now >= _nextRecompute)
            {
                _nextRecompute = now + RecomputeEvery;
                RecomputeTargets(tick, r, dark);
            }

            Vector3 pp = player.transform.position;
            bool outside = !_site.InsideFence(pp);
            bool rain = r.RainMmH > 0;
            float dt = Time.deltaTime;
            _protestersShown = 0;
            _protestCentroid = Vector3.zero;
            for (int i = 0; i < _figs.Length; i++)
            {
                ref Figure f = ref _figs[i];
                switch (f.Kind)
                {
                    case FigureKind.Walker: UpdateWalker(ref f, pp, outside, rain, now, dt); break;
                    case FigureKind.Car: UpdateCar(ref f, pp, dark, now, dt); break;
                    default: UpdateStander(ref f, pp, outside, dark, now, dt); break;
                }
            }

            var audio = SiteAudio.Instance;
            if (audio != null)
            {
                if (_protestersShown > 0) _protestCentroid /= _protestersShown;
                audio.SetProtestChant(_protestCentroid, _protestersShown / (float)Protesters);
            }
        }

        /// <summary>Two renderer toggles: the town goes to bed between one and
        /// six, a few windows stay lit until one.</summary>
        private void UpdateWindows(bool dark, int hour)
        {
            bool early = dark && !(hour >= 1 && hour < 6);
            bool late = dark && (hour >= 23 || hour < 1);
            if (!_windowsInit || early != _windowsEarly)
            {
                _windowsEarly = early;
                if (_site.TownWindowsEarly != null) _site.TownWindowsEarly.enabled = early;
            }
            if (!_windowsInit || late != _windowsLate)
            {
                _windowsLate = late;
                if (_site.TownWindowsLate != null) _site.TownWindowsLate.enabled = late;
            }
            _windowsInit = true;
        }

        /// <summary>Head-counts from the replicated report. Every roll is keyed
        /// on (day, hour/3, slot) so the same report gives the same crowd on
        /// every machine; the dwell rule below decides when a slot may flip.</summary>
        private void RecomputeTargets(long tick, TickReport r, bool dark)
        {
            int hour = SimClock.HourOfDay(tick);
            uint day = (uint)SimClock.DayIndex(tick);
            uint bucket = (uint)(hour / 3);
            float now = Time.unscaledTime;

            int walkers = hour < 6 ? 1 : hour < 8 ? 6 : hour < 10 ? 10 : hour < 17 ? 8 : hour < 20 ? 12 : hour < 22 ? 6 : 2;
            float wf = walkers * (r.TdbC < 3 || r.TdbC > 32 ? 0.6f : 1f) * DebugDensity;
            walkers = Mathf.Clamp(Mathf.RoundToInt(wf), 0, Walkers);

            // Camera-presence proxy: the sim has no filming flag, so this is a
            // presentation guess at balance-constants' CAMERA_PRESENCE_* and
            // must never be surfaced as a number.
            float p = dark ? 0.08f : 0.35f;
            switch (r.Stage)
            {
                case EscalationStage.Complaints: p += 0.10f; break;
                case EscalationStage.Petition: p += 0.20f; break;
                case EscalationStage.Protest: p += 0.35f; break;
                case EscalationStage.Injunction: p += 0.35f; break;
                case EscalationStage.Sabotage: p += 0.45f; break;
            }
            if (r.NVisual > 40) p += 0.15f;
            if (_accidentSeen && r.AccidentScore > _lastAccident + 1e-9) _incidentUntil = now + IncidentMemory;
            _lastAccident = r.AccidentScore;
            _accidentSeen = true;
            bool plumeOverTown = r.DieselKwh > 0 && Mathf.Abs(Mathf.DeltaAngle((float)r.WindTowardDeg, 270f)) <= 30f;
            if (plumeOverTown || r.OutageFrac > 0 || r.LoadShedKwh > 0 || now < _incidentUntil) p = Mathf.Max(p, 0.85f);
            int filmerSpots = Mathf.Min(Filmers, _fenceSpots.Count);
            int filmers = Mathf.Clamp(Mathf.RoundToInt(Filmers * Mathf.Clamp01(p) * DebugDensity), 0, filmerSpots);

            int protesters = r.Stage == EscalationStage.Protest ? 6
                : r.Stage == EscalationStage.Injunction ? 4
                : r.Stage == EscalationStage.Sabotage ? 3 : 0;
            protesters = Mathf.Clamp(Mathf.RoundToInt(protesters * DebugDensity), 0, Mathf.Min(Protesters, _gateSpots.Count));

            int cars = hour < 6 ? 1 : hour < 10 ? 4 : hour < 16 ? 2 : hour < 20 ? 4 : 2;
            cars = Mathf.Clamp(Mathf.RoundToInt(cars * DebugDensity), 0, Cars);

            bool protestPass = r.Stage >= EscalationStage.Protest;
            for (int i = 0; i < _figs.Length; i++)
            {
                ref Figure f = ref _figs[i];
                bool want;
                switch (f.Kind)
                {
                    case FigureKind.Walker: want = Rotated(f.Slot, Walkers, walkers, day, bucket, 1u); break;
                    case FigureKind.Filmer: want = Rotated(f.Slot, filmerSpots, filmers, day, bucket, 2u); break;
                    case FigureKind.Protester: want = f.Slot < protesters; break;
                    // Rotated, not "first N": the one night car is not always
                    // in the outer lane.
                    default: want = Rotated(f.Slot, Cars, cars, day, bucket, 3u); break;
                }
                if (want && !f.Wanted && !f.Shown) PrepareSpawn(ref f, day, bucket, protestPass);
                f.Wanted = want;
            }
        }

        /// <summary>Exactly `count` of `n` slots, which ones rotating per
        /// (day, bucket): the crowd changes faces without changing size.</summary>
        private static bool Rotated(int slot, int n, int count, uint day, uint bucket, uint salt)
        {
            if (slot >= n || n <= 0) return false;
            uint rot = H(PresenceSeed + salt, day, bucket) % (uint)n;
            return (int)(((uint)slot + rot) % (uint)n) < count;
        }

        // ---- per-kind behaviour --------------------------------------------

        private void PrepareSpawn(ref Figure f, uint day, uint bucket, bool protestPass)
        {
            uint s = (uint)f.Slot, key = H(PresenceSeed, day, bucket);
            switch (f.Kind)
            {
                case FigureKind.Walker:
                    // The zebra crossing is walked only by figures who set out
                    // during a protest; everyone else keeps to the pavement.
                    // One slot in four: a crossing is short and four is a crowd.
                    bool zebra = protestPass && f.Slot % 4 == 3 && _routes.Length > 1 && _routes[1] != null;
                    f.Route = zebra ? 1 : 0;
                    float[] cum = _routeCum[f.Route];
                    float len = cum[cum.Length - 1];
                    f.S = Mathf.Repeat(((f.Slot + 0.5f) / Walkers + 0.1f * F01(key, s, 7)) * len, len);
                    f.Dir = F01(key, s, 8) < 0.5f ? 1f : -1f;
                    f.Seg = 0;
                    break;
                case FigureKind.Filmer:
                    f.Spot = _fenceSpots[f.Slot];
                    f.BaseYaw = f.Yaw = YawTo(f.Spot, _site.SiteCentre);
                    break;
                case FigureKind.Protester:
                    f.Spot = _gateSpots[f.Slot];
                    f.BaseYaw = f.Yaw = YawTo(f.Spot, _site.VehicleGate);
                    break;
            }
        }

        private void UpdateWalker(ref Figure f, Vector3 pp, bool outside, bool rain, float now, float dt)
        {
            Vector3[] v = _routes[f.Route];
            float[] cum = _routeCum[f.Route];
            if (v == null) { if (f.Shown) Vanish(ref f); return; }
            float len = cum[cum.Length - 1];
            if (!f.Shown)
            {
                if (f.DormantNear && Dist2(f.Pos, pp) > WalkerReturnDist * WalkerReturnDist) f.DormantNear = false;
                if (f.Wanted && !f.DormantNear)
                {
                    f.Pos = Sample(v, cum, f.S, ref f.Seg, out _);
                    TryAppear(ref f, pp, now);
                }
                return;
            }
            f.S += f.Dir * f.Speed * (f.Fleeing ? FleeMult : 1f) * dt;
            if (_routeClosed[f.Route]) f.S = Mathf.Repeat(f.S, len);
            else if (f.S >= len) { f.S = len; f.Dir = -1f; if (f.Fleeing) WalkOff(ref f); }
            else if (f.S <= 0f) { f.S = 0f; f.Dir = 1f; if (f.Fleeing) WalkOff(ref f); }
            f.Pos = Sample(v, cum, f.S, ref f.Seg, out Vector3 heading);
            heading *= f.Dir;

            if (outside)
            {
                float d2 = Dist2(f.Pos, pp);
                if (d2 < WalkerVanishDist * WalkerVanishDist) WalkOff(ref f);
                else if (!f.Fleeing && d2 < WalkerReverseDist * WalkerReverseDist) { f.Fleeing = true; f.Dir = -f.Dir; }
                else if (f.Fleeing && d2 > WalkerReleaseDist * WalkerReleaseDist) f.Fleeing = false;
            }
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell) f.Leaving = true;
            f.Yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            Apply(ref f, pp, dt, rain);
        }

        /// <summary>Too close, or cornered at a route end while fleeing (the
        /// ping-pong would otherwise send it back at the player at flee speed):
        /// scale out and stay away until the player is far.</summary>
        private static void WalkOff(ref Figure f)
        {
            f.Leaving = true;
            f.DormantNear = true;
        }

        private void UpdateStander(ref Figure f, Vector3 pp, bool outside, bool dark, float now, float dt)
        {
            if (!f.Shown)
            {
                if (f.Wanted && now >= f.DormantUntil) { f.Pos = f.Spot; TryAppear(ref f, pp, now); }
                return;
            }
            f.Pos = f.Spot;
            bool turned = false;
            if (outside)
            {
                float d2 = Dist2(f.Pos, pp);
                if (d2 < StanderVanish * StanderVanish) { f.Leaving = true; f.DormantUntil = now + StanderDormant; }
                else if (d2 < StanderTurnDist * StanderTurnDist) turned = true;
            }
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell) f.Leaving = true;

            bool protester = f.Kind == FigureKind.Protester;
            float sway = (protester ? 3f : 2f) * Mathf.Sin(now * (protester ? 2.5f : 1.9f) + f.SwayPhase);
            // Turned away = back to the player, phone down. Not a reaction to
            // being looked at; a person who does not want to be approached.
            float target = turned ? Mathf.Atan2(f.Pos.x - pp.x, f.Pos.z - pp.z) * Mathf.Rad2Deg : f.BaseYaw;
            f.Yaw = Mathf.MoveTowardsAngle(f.Yaw, target + sway, 180f * dt);
            if (protester && !f.Leaving) { _protestersShown++; _protestCentroid += f.Pos; }
            Apply(ref f, pp, dt, dark && !turned);
        }

        private void UpdateCar(ref Figure f, Vector3 pp, bool dark, float now, float dt)
        {
            if (!f.Shown)
            {
                if (f.Wanted) { f.Pos = LanePos(ref f, out _); TryAppear(ref f, pp, now); }
                return;
            }
            float[] cum = _laneCum[f.Route];
            // Brakes for the corners; the arc flag is the segment sampled last frame.
            float target = f.Speed * (_laneArc[f.Route][f.Seg] ? CornerSpeed : 1f);
            f.Vel = Mathf.MoveTowards(f.Vel, target, CarAccel * dt);
            f.S = Mathf.Repeat(f.S + f.Vel * dt, cum[cum.Length - 1]);
            f.Pos = LanePos(ref f, out Vector3 heading);
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell) f.Leaving = true;
            f.Yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            Apply(ref f, pp, dt, dark);
        }

        // ---- shared mechanics --------------------------------------------------

        // The larger of the contract's 5 m and the site's own promise.
        private static readonly float StanderVanish = Mathf.Max(StanderVanishDist, SiteRefs.ResidentClearRadius);

        /// <summary>Appear only out of sight of the player: a figure never pops
        /// into existence within 20 m. Otherwise the slot waits.</summary>
        private static void TryAppear(ref Figure f, Vector3 pp, float now)
        {
            if (Dist2(f.Pos, pp) < SpawnClearDist * SpawnClearDist) return;
            f.Shown = true;
            f.Leaving = false;
            f.Fleeing = false;
            f.Scale = 0f;
            f.ShownSince = now;
            f.Tf.position = f.Pos;
        }

        private static void Apply(ref Figure f, Vector3 pp, float dt, bool extraOn)
        {
            f.Scale = Mathf.MoveTowards(f.Scale, f.Leaving ? 0f : 1f, dt / ScaleTime);
            if (f.Leaving && f.Scale <= 0f) { Vanish(ref f); return; }
            f.Tf.position = f.Pos;
            f.Tf.rotation = Quaternion.Euler(0f, f.Yaw, 0f);
            f.Tf.localScale = new Vector3(f.Scale, f.Scale, f.Scale);
            bool vis = Dist2(f.Pos, pp) < CullDist * CullDist;
            SetRenderer(f.Body, vis);
            SetRenderer(f.Extra, vis && extraOn);
            SetRenderer(f.Placard, vis);
        }

        private static void Vanish(ref Figure f)
        {
            f.Shown = false;
            f.Leaving = false;
            f.Fleeing = false;
            f.Scale = 0f;
            f.Tf.localScale = Vector3.zero;
            SetRenderer(f.Body, false);
            SetRenderer(f.Extra, false);
            SetRenderer(f.Placard, false);
        }

        private void HideAll()
        {
            for (int i = 0; i < _figs.Length; i++) Vanish(ref _figs[i]);
            var audio = SiteAudio.Instance;
            if (audio != null) audio.SetProtestChant(Vector3.zero, 0f);
        }

        private static void SetRenderer(Renderer r, bool on)
        {
            if (r != null && r.enabled != on) r.enabled = on;
        }

        private static float Dist2(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        /// <summary>Point at arc length s along an open polyline; the cached
        /// segment index walks a step or two per frame instead of searching.</summary>
        private static Vector3 Sample(Vector3[] v, float[] cum, float s, ref int seg, out Vector3 heading)
        {
            int last = v.Length - 2;
            if (seg < 0) seg = 0;
            if (seg > last) seg = last;
            while (seg < last && s > cum[seg + 1]) seg++;
            while (seg > 0 && s < cum[seg]) seg--;
            Vector3 a = v[seg], b = v[seg + 1];
            float segLen = cum[seg + 1] - cum[seg];
            float t = segLen > 1e-4f ? Mathf.Clamp01((s - cum[seg]) / segLen) : 0f;
            heading = (b - a).normalized;
            return Vector3.Lerp(a, b, t);
        }

        /// <summary>Position in the car's lane, eased toward the centre line
        /// past a parked car; the heading follows the eased path, not the lane.</summary>
        private Vector3 LanePos(ref Figure f, out Vector3 heading)
        {
            Vector3 p = Sample(_lanes[f.Route], _laneCum[f.Route], f.S, ref f.Seg, out heading);
            float shift = PinchAt(_lanePinch[f.Route], f.S, out float slope) * (LaneOffset - PinchOffset);
            if (shift <= 0f && slope == 0f) return p;
            // The centre line is to the LEFT of travel (drive on the right).
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            heading = (heading - right * (slope * (LaneOffset - PinchOffset))).normalized;
            return p - right * shift;
        }

        /// <summary>0..1 how far into a pinch the arc length s is, smoothstepped
        /// over the ramps, and its slope against s.</summary>
        private static float PinchAt(float[] spans, float s, out float slope)
        {
            float w = 0f;
            slope = 0f;
            for (int i = 0; i < spans.Length; i += 2)
            {
                float a = spans[i], b = spans[i + 1];
                if (s < a - PinchRamp || s > b + PinchRamp) continue;
                float wi = 1f, si = 0f;
                if (s < a || s > b)
                {
                    float sign = s < a ? 1f : -1f;
                    float t = s < a ? (s - (a - PinchRamp)) / PinchRamp : ((b + PinchRamp) - s) / PinchRamp;
                    wi = t * t * (3f - 2f * t);
                    si = sign * 6f * t * (1f - t) / PinchRamp;
                }
                if (wi > w) { w = wi; slope = si; }
            }
            return w;
        }
    }

    /// <summary>The intercom beside the pedestrian gate, lane side. It answers
    /// in the Program's voice, to you alone (NewsFeed.PostLocal: what the gate
    /// told YOU is not news), and never more than once in thirty seconds.</summary>
    public sealed class GateIntercom : MonoBehaviour, IInteractable
    {
        public const float Cooldown = 30f;
        private float _lastUse = -Cooldown;

        public static GateIntercom Create(Transform root, SiteRefs site)
        {
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.35f, 0.35f, 0.12f), Palette.Slate);
            pm.Box(new Vector3(0, 0.04f, 0.065f), new Vector3(0.16f, 0.1f, 0.01f), Palette.Amber);
            pm.Box(new Vector3(0, -0.11f, 0.065f), new Vector3(0.05f, 0.05f, 0.01f), Palette.Render);
            // Beside the gate post, 0.2 m off the fence collider's lane face,
            // grille toward the pavement (-z).
            var pos = new Vector3(site.PedestrianGate.x + 1.5f, 1.4f, site.FenceZ0 - 0.35f);
            var go = MatLib.Spawn("GateIntercom", pm.Build("intercom"), root, pos, true);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            return go.AddComponent<GateIntercom>();
        }

        /// <summary>Hud draws only E-prefixed prompts as offers: while the
        /// intercom is cooling down, E would do nothing, so it does not offer.</summary>
        public string Prompt(PlayerRig player)
        {
            return Time.unscaledTime - _lastUse < Cooldown ? "intercom (busy)" : "E: intercom";
        }

        public void Interact(PlayerRig player)
        {
            if (Time.unscaledTime - _lastUse < Cooldown) return;
            _lastUse = Time.unscaledTime;
            NewsFeed.PostLocal("Intercom: '" + Line(GameBootstrap.CurrentReport.Stage) + "'");
        }

        private static string Line(EscalationStage stage)
        {
            switch (stage)
            {
                case EscalationStage.Complaints: return "Your feedback has been logged. The log is not public.";
                case EscalationStage.Petition: return "The petition has been received and placed in a folder.";
                case EscalationStage.Protest: return "Please keep the gate clear. The gate thanks you.";
                case EscalationStage.Injunction: return "Legal advises the intercom to say nothing.";
                case EscalationStage.Sabotage: return "This intercom is being monitored for its own safety.";
                default: return "Good Neighbor Program. Visitors are welcome to admire the fence.";
            }
        }
    }

    /// <summary>A small sign on the verge outside the south fence: E puts the
    /// ear on the town's side of the wire — the site's own plant ducked, the
    /// gate crowd forward. Audio only; it posts nothing and changes nothing.</summary>
    public sealed class ListeningPost : MonoBehaviour, IInteractable
    {
        private const float LeaveDist = 8f;
        private bool _on;

        public static ListeningPost Create(Transform root, SiteRefs site)
        {
            var pm = new ProcMesh();
            pm.Cylinder(new Vector3(0, 0.8f, 0), 0.04f, 1.6f, 6, Palette.Slate);
            pm.Box(new Vector3(0, 1.55f, 0), new Vector3(0.8f, 0.5f, 0.03f), Palette.Render);
            pm.Box(new Vector3(0, 1.72f, 0), new Vector3(0.8f, 0.08f, 0.035f), Palette.ProgramBlue);
            var pos = new Vector3(40f, 0f, site.FenceZ0 - 1.4f);
            var go = MatLib.Spawn("ListeningPost", pm.Build("listeningPost"), root, pos, true);
            return go.AddComponent<ListeningPost>();
        }

        public string Prompt(PlayerRig player) { return _on ? "E: stop listening" : "E: listen"; }

        public void Interact(PlayerRig player) { Set(!_on); }

        private void Set(bool on)
        {
            _on = on;
            var audio = SiteAudio.Instance;
            if (audio != null) audio.SetFenceMix(on);
        }

        private void Update()
        {
            // Walking off releases the mix; the perspective belongs to the spot.
            if (!_on) return;
            PlayerRig player = GameBootstrap.LocalPlayer;
            if (player == null) { Set(false); return; }
            Vector3 d = player.transform.position - transform.position;
            if (d.x * d.x + d.z * d.z > LeaveDist * LeaveDist) Set(false);
        }
    }
}
