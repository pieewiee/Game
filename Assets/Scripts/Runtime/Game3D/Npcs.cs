using System.Collections.Generic;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The town around the site: residents walking their own errands through
    /// the streets, filming at the fence line, picketing the vehicle gate,
    /// and a handful of cars on the ring road.
    ///
    /// The owner overruled art-bible.md §3 on 2026-09-02. Residents used to
    /// be silhouettes that turned away at 9 m and dissolved at 5 m; they are
    /// now solid (a capsule you cannot walk through), free to roam the whole
    /// town lattice rather than a fixed polyline, and they answer E with one
    /// line about the state the sim has actually reached. What survives of
    /// the old rule: nobody appears or vanishes within 25 m of a player, and
    /// none of them changes the simulation directly — this whole file is
    /// presentation, reading and never writing the replicated report.
    ///
    /// One exception, added 2026-09-04 (open-questions.md Q29): the ONE
    /// resident an active incursion has cast does cross the wire, for the
    /// one asset Game.Sim.Simulation.Community.StepIncursion has scheduled
    /// against sustained neglect — see UpdateSaboteurActive. The sim alone
    /// decides whether, when and what; this file only walks a figure there
    /// and lets a carried taser (Security.cs) end it in person.
    ///
    /// Same inputs, evaluated on a local cadence — not lockstep: how many
    /// figures are out and what they are doing is read from the replicated
    /// report, so a client sees the same crowd for the same reasons, but
    /// their exact positions are local. Head-counts are re-evaluated every
    /// few REAL seconds and keyed on (day, hour/3, slot), because at a fast
    /// clock a per-tick roll would flicker.
    ///
    /// The bodies are Kenney's CC0 Blocky Characters, baked to the palette
    /// like the rest of the town, with the pack's own idle and walk takes.
    /// Without the pack they fall back to the two-cylinder stand-in.
    /// Placards stay blank (open-questions.md Q27).
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
            public Renderer Extra, Placard;         // Extra: umbrella / phone / headlights
            public GameObject Body;                 // the character model, or the procedural stand-in
            public Animation Anim;                  // the model's clips; null for the stand-in
            public string Clip;                     // what it is playing now
            public Collider Col;
            public Resident Talk;                   // the E prompt, on humans only
            public bool Wanted;        // the last target evaluation wants this slot present
            public bool Shown;         // spawned: scaling in, standing, or scaling out
            public bool Leaving;       // scaling out; sticky until gone
            public float Scale;        // 0..1, uniform scale-in from the feet
            public float ShownSince;
            public List<Vector3> Path; // walkers: the errand, in world points
            public int PathIdx, Errand;
            public float PausedUntil;  // walkers: standing still between errands
            public int Lane, Seg;      // cars: which lane, and the cached segment
            public float S, Speed;
            public float Vel;          // cars: current speed, eased toward the corner speed
            public float Yaw, BaseYaw, SwayPhase;
            public Vector3 Spot;       // standing figures: the validated spot
            public Vector3 Pos;        // world position this frame
        }

        private const uint PresenceSeed = 7331u;
        // The outer loop is 315 m of pavement: at sixteen slots the street
        // read as deserted from the spawn, which is the one view every
        // session starts with.
        private const int Walkers = 24, Filmers = 8, Protesters = 6, Cars = 6;
        private const int FigureCount = Walkers + Filmers + Protesters + Cars;

        private const float RecomputeEvery = 4f;      // real seconds between target evaluations
        private const float MinDwell = 6f;            // a shown figure stays at least this long
        private const float ScaleTime = 0.4f;         // scale-in / scale-out duration
        private const float IncidentMemory = 20f;     // an accident keeps the phones out this long
        // Nobody appears or disappears within this radius. Residents no longer
        // retreat from a player — art-bible §3's "never approachable" was
        // overruled by the owner on 2026-09-02: they are solid, they can be
        // spoken to, and all that survives of the old rule is that the crowd
        // changes size out of sight instead of in front of you.
        private const float PopClearDist = 25f;
        private const float CullDist = 180f;          // fog has eaten them well before this
        private const float WalkSpeed = 1.25f, CarSpeed = 8f, LaneOffset = 2f;
        private const float TurnRate = 260f;          // degrees a second
        private const float ArriveDist = 0.9f;        // waypoint reached
        private const float PausedMin = 2f, PausedMax = 9f;   // a stop between errands
        private const float PlayerPush = 2.2f;        // a walker steps around you inside this
        private const float BodyRadius = 0.32f, BodyHeight = 1.75f;
        private const float BrakeDist = 10f;          // a car stops this far short of a player
        /// <summary>The figures' own layer, so a figure's ground probe cannot
        /// hit itself. It is NOT the player's layer: the interact probe and
        /// the player capsule must both still see them.</summary>
        public const int FigureLayer = 9;
        private const int GroundMask = ~((1 << FigureLayer) | (1 << PlayerRig.PlayerLayer));
        // The carriageway centre line sits this far outside the fence rectangle
        // (design_site-plan §3: a 7 m road starting 5 m past the fence).
        private const float RingRoadOffset = 8.5f, RoadTopY = 0.04f;
        // Corner arcs: the outer lane turns wide, the inner lane hugs a kerb
        // corner 1.5 m away, so its radius is what keeps the body off the kerb.
        private const float OuterCornerR = 4.5f, InnerCornerR = 2.2f, CornerSpeed = 0.6f, CarAccel = 8f;
        // Past a parked car the lane eases to this far from the centre line,
        // ramping in and out over PinchRamp metres.
        // CarHalfWidth is the kit car's (2.4 m wide); the slate box is narrower.
        private const float PinchOffset = 0.3f, PinchRamp = 4f, CarHalfWidth = 1.2f, CarClearance = 0.5f;
        private static readonly string[] CarKeys =
            { "kenney/car-kit/sedan", "kenney/car-kit/hatchback-sports", "kenney/car-kit/suv", "kenney/car-kit/van" };
        private const float LaneClearance = CarHalfWidth + CarClearance;
        // Layout validation. A stander keeps LaneClearance from either lane,
        // or a car drives through it.
        private const float SpawnClearance = 9f, ObstacleClearance = 1.5f;
        /// <summary>Eighteen CC0 Blocky Characters; a slot keeps its own.</summary>
        private static readonly string[] CharacterKeys =
        {
            "kenney/blocky-characters/character-a", "kenney/blocky-characters/character-b",
            "kenney/blocky-characters/character-c", "kenney/blocky-characters/character-d",
            "kenney/blocky-characters/character-e", "kenney/blocky-characters/character-f",
            "kenney/blocky-characters/character-g", "kenney/blocky-characters/character-h",
            "kenney/blocky-characters/character-i", "kenney/blocky-characters/character-j",
            "kenney/blocky-characters/character-k", "kenney/blocky-characters/character-l",
            "kenney/blocky-characters/character-m", "kenney/blocky-characters/character-n",
            "kenney/blocky-characters/character-o", "kenney/blocky-characters/character-p",
            "kenney/blocky-characters/character-q", "kenney/blocky-characters/character-r"
        };

        private SiteRefs _site;
        private Transform _root;
        private Figure[] _figs;

        /// <summary>Where a resident may walk: the whole town outside the
        /// wire, not a fixed route.</summary>
        private TownNav _nav;
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
        private bool _wasIncursionActive;
        private int _lastCastSlot = -1;
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

            _nav = TownNav.Build(_site);
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
        // Standing spots use only validated data: 9 m from the player spawn
        // and 1.5 m from every town obstacle. Walkers need no such list —
        // TownNav rules the whole town in or out, cell by cell.

        /// <summary>After the lanes exist: a stander also keeps out of the
        /// carriageway.</summary>
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
                for (int l = 0; l < _lanes.Length && fault == null; l++)
                    if (PolylineDist2(_lanes[l], p) < LaneClearance * LaneClearance) fault = "stands in a ring road lane";
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
            // 1.74 m to the crown: at 1.55 m the figures read as children
            // beside the town's imported houses. The head starts where the
            // body ends — a gap reads as daylight through the silhouette at
            // the 7-9 m minimum distances.
            pm.Cylinder(new Vector3(0, 0.71f, 0), bodyR, 1.42f, 8, body, bodyR * 0.85f);
            pm.Cylinder(new Vector3(0, 1.58f, 0), 0.16f, 0.32f, 6, head);
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
            go.layer = FigureLayer;
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.zero;
            f.Tf = go.transform;
            uint s = (uint)slot;
            if (kind == FigureKind.Car)
            {
                // A kit car when the packs are in (four types, one per slot),
                // else the slate box; the headlight quads sit at the same
                // front either way (both are 4.1-4.2 m long).
                Mesh kitCar;
                f.Body = Solid("body", AssetKit.TryGetCombined(CarKeys[slot % CarKeys.Length], out kitCar) ? kitCar : _car, f.Tf);
                f.Extra = Part("lights", _carLights, f.Tf, true);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(2.3f, 1.5f, 4.3f);
                box.center = new Vector3(0f, 0.75f, 0f);
                f.Col = box;
                // Even slots take the outer lane, odd the inner; the cars of
                // a lane start a fair share of a lap apart.
                f.Lane = slot % 2;
                f.Speed = CarSpeed * (0.9f + 0.2f * F01(PresenceSeed, s, 4));
                f.Vel = f.Speed;
                float[] laneCum = _laneCum[f.Lane];
                f.S = laneCum[laneCum.Length - 1] * (slot + F01(PresenceSeed, s, 5)) / Cars;
            }
            else
            {
                f.Body = Human(f.Tf, slot, out f.Anim);
                if (kind == FigureKind.Walker) f.Extra = Part("umbrella", _umbrella, f.Tf, false);
                else f.Extra = Part("phone", _phone, f.Tf, true);
                if (kind == FigureKind.Protester && slot % 3 != 2) f.Placard = Part("placard", _placard, f.Tf, false);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = BodyRadius;
                cap.height = BodyHeight;
                cap.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
                f.Col = cap;
                f.Talk = go.AddComponent<Resident>();
                f.Talk.Kind = kind;
                f.Speed = WalkSpeed * (0.85f + 0.3f * F01(PresenceSeed, s, 2));
                f.Path = new List<Vector3>();
            }
            // Kinematic: the figures are driven by their transforms, and a
            // moving collider without a body makes the physics engine rebuild
            // the static tree every frame.
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            f.Col.enabled = false;
            f.Body.SetActive(false);
            f.SwayPhase = F01(PresenceSeed, s, 6) * Mathf.PI * 2f;
            f.Scale = 0f;
        }

        /// <summary>A Blocky Character from the kit with its clips, or the
        /// two-cylinder stand-in when the pack is not installed.</summary>
        private GameObject Human(Transform parent, int slot, out Animation anim)
        {
            GameObject model = AssetKit.TryInstantiate(CharacterKeys[slot % CharacterKeys.Length], parent);
            if (model != null)
            {
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = FigureLayer;
                anim = model.GetComponent<Animation>();
                if (anim != null)
                {
                    anim.playAutomatically = false;
                    anim.wrapMode = WrapMode.Loop;
                }
                return model;
            }
            anim = null;
            return Solid("body", slot % 2 == 0 ? _walkerA : _walkerB, parent);
        }

        /// <summary>A visible part whose object is switched, not its renderer:
        /// the character models carry several renderers each.</summary>
        private static GameObject Solid(string name, Mesh mesh, Transform parent)
        {
            Renderer r = Part(name, mesh, parent, false);
            r.enabled = true;
            r.gameObject.layer = FigureLayer;
            return r.gameObject;
        }

        private static void PlayClip(ref Figure f, string clip)
        {
            if (f.Anim == null || f.Clip == clip) return;
            if (f.Anim.GetClip(clip) == null) return;
            f.Anim.CrossFade(clip, 0.25f);
            f.Clip = clip;
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
            if (player == null || _nav == null || _nav.NodeCount == 0) return;

            TickReport r = GameBootstrap.CurrentReport;
            float now = Time.unscaledTime;
            if (now >= _nextRecompute)
            {
                _nextRecompute = now + RecomputeEvery;
                RecomputeTargets(tick, r, dark);
            }

            Vector3 pp = player.transform.position;
            bool rain = r.RainMmH > 0;
            float dt = Time.deltaTime;

            // The moment an incursion resolves (either way), its actor may be
            // standing inside the fence, where the town lattice has no
            // walkable node at all. Snap it back to the kerb it breached from
            // before ordinary errand-picking ever sees that position, or a
            // TryPath from inside the blocked zone spends a few seconds
            // failing and re-failing right at the wire.
            if (_wasIncursionActive && !r.IncursionActive && _lastCastSlot >= 0 && _lastCastSlot < _figs.Length)
            {
                ref Figure cf = ref _figs[_lastCastSlot];
                Vector3 outside = BreachPointFor(IncursionTargetWorldPos(r));
                cf.Pos = new Vector3(outside.x, GroundY(outside), outside.z);
                cf.Path.Clear();
                cf.PathIdx = 0;
                if (cf.Talk != null) cf.Talk.IsActiveSaboteur = false;
            }
            _wasIncursionActive = r.IncursionActive;
            int incursionCastSlot = (r.IncursionPending || r.IncursionActive)
                ? (int)(r.IncursionCastHash % (uint)Walkers) : -1;
            if (r.IncursionActive) _lastCastSlot = incursionCastSlot;

            _protestersShown = 0;
            _protestCentroid = Vector3.zero;
            for (int i = 0; i < _figs.Length; i++)
            {
                ref Figure f = ref _figs[i];
                switch (f.Kind)
                {
                    case FigureKind.Walker: UpdateWalker(ref f, pp, in r, incursionCastSlot, rain, now, dt); break;
                    case FigureKind.Car: UpdateCar(ref f, pp, dark, now, dt); break;
                    default: UpdateStander(ref f, pp, dark, now, dt); break;
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

            int walkers = hour < 6 ? 2 : hour < 8 ? 11 : hour < 10 ? 18 : hour < 17 ? 14 : hour < 20 ? 21 : hour < 22 ? 11 : 4;
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
            // The sim's own verdict: either residential sector (west houses,
            // north flats) counts, exactly as the air channel charges it.
            bool plumeOverTown = r.DieselKwh > 0 && r.WindTowardTown;
            if (plumeOverTown || r.OutageFrac > 0 || r.LoadShedKwh > 0 || now < _incidentUntil) p = Mathf.Max(p, 0.85f);
            int filmerSpots = Mathf.Min(Filmers, _fenceSpots.Count);
            int filmers = Mathf.Clamp(Mathf.RoundToInt(Filmers * Mathf.Clamp01(p) * DebugDensity), 0, filmerSpots);

            int protesters = r.Stage == EscalationStage.Protest ? 6
                : r.Stage == EscalationStage.Injunction ? 4
                : r.Stage == EscalationStage.Sabotage ? 3 : 0;
            protesters = Mathf.Clamp(Mathf.RoundToInt(protesters * DebugDensity), 0, Mathf.Min(Protesters, _gateSpots.Count));

            int cars = hour < 6 ? 1 : hour < 10 ? 6 : hour < 16 ? 3 : hour < 20 ? 6 : 2;
            cars = Mathf.Clamp(Mathf.RoundToInt(cars * DebugDensity), 0, Cars);

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
                if (want && !f.Wanted && !f.Shown) PrepareSpawn(ref f);
                f.Wanted = want;
            }
        }

        /// <summary>Exactly `count` of `n` slots, which ones rotating per
        /// (day, bucket): the crowd changes faces without changing size.
        ///
        /// The slot index also decides where a walker sets out along the
        /// route, so a plain rotation (slot + rot) picked one CONTIGUOUS arc
        /// and put the whole crowd on a third of the loop — from the spawn
        /// the street looked deserted while fourteen people walked the far
        /// side. Multiplying by a stride coprime with n scatters the chosen
        /// slots around the ring and still selects exactly `count` of them.</summary>
        private static bool Rotated(int slot, int n, int count, uint day, uint bucket, uint salt)
        {
            if (slot >= n || n <= 0) return false;
            uint rot = H(PresenceSeed + salt, day, bucket) % (uint)n;
            return (int)(((uint)slot * Stride(n) + rot) % (uint)n) < count;
        }

        /// <summary>A golden-ratio stride walked down to the nearest value
        /// coprime with n (1 when none exists, e.g. for very small n).</summary>
        private static uint Stride(int n)
        {
            uint s = (uint)Mathf.Max(1, Mathf.RoundToInt(n * 0.618034f));
            while (s > 1 && Gcd(s, (uint)n) != 1) s--;
            return s;
        }

        private static uint Gcd(uint a, uint b)
        {
            while (b != 0) { uint t = a % b; a = b; b = t; }
            return a;
        }

        // ---- per-kind behaviour --------------------------------------------

        private void PrepareSpawn(ref Figure f)
        {
            switch (f.Kind)
            {
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

        /// <summary>A resident going somewhere: a walk across the town lattice
        /// to a hashed destination, a stop, then another errand. Nobody
        /// reacts to the player beyond stepping around them.</summary>
        private void UpdateWalker(ref Figure f, Vector3 pp, in TickReport r, int castSlot, bool rain, float now, float dt)
        {
            bool isCast = f.Slot == castSlot;
            if (f.Talk != null) f.Talk.IsActiveSaboteur = isCast && r.IncursionActive;

            if (!f.Shown)
            {
                if (f.Wanted) Place(ref f, pp, now);
                return;
            }
            if (isCast && r.IncursionActive) { UpdateSaboteurActive(ref f, pp, in r, now, dt); return; }
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell
                && Dist2(f.Pos, pp) > PopClearDist * PopClearDist) f.Leaving = true;

            bool moving = false;
            if (now >= f.PausedUntil)
            {
                if (f.PathIdx >= f.Path.Count)
                {
                    // Telegraph: the resident who will do this keeps drifting
                    // back toward the fence segment they will breach, rather
                    // than picking a random errand — the "spatial countdown"
                    // the market brief asked for.
                    int overrideGoal = (isCast && r.IncursionPending)
                        ? _nav.Nearest(BreachPointFor(IncursionTargetWorldPos(r))) : -1;
                    NewErrand(ref f, now, overrideGoal);
                }
                if (f.PathIdx < f.Path.Count)
                {
                    Vector3 target = f.Path[f.PathIdx];
                    var to = new Vector3(target.x - f.Pos.x, 0f, target.z - f.Pos.z);
                    float d = to.magnitude;
                    if (d < ArriveDist)
                    {
                        f.PathIdx++;
                        if (f.PathIdx >= f.Path.Count) f.PausedUntil = now + Pause(ref f);
                    }
                    else
                    {
                        Vector3 dir = to / d;
                        // Step around a player rather than into them: two
                        // capsules pushing at each other wedge and stay wedged.
                        var away = new Vector3(f.Pos.x - pp.x, 0f, f.Pos.z - pp.z);
                        float ad = away.magnitude;
                        if (ad > 0.01f && ad < PlayerPush)
                            dir = (dir + away / ad * (1.6f * (PlayerPush - ad) / PlayerPush)).normalized;
                        f.Yaw = Mathf.MoveTowardsAngle(f.Yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, TurnRate * dt);
                        Vector3 next = f.Pos + Quaternion.Euler(0f, f.Yaw, 0f) * Vector3.forward * (f.Speed * dt);
                        // Walked into something the lattice knew about: think
                        // again rather than grind along a wall.
                        if (_nav.Walkable(next)) { f.Pos = next; moving = true; }
                        else { f.PathIdx = f.Path.Count; f.PausedUntil = now + 1f; }
                    }
                }
            }
            f.Pos.y = GroundY(f.Pos);
            PlayClip(ref f, moving ? "walk" : "idle");
            Apply(ref f, pp, dt, rain);
        }

        private float Pause(ref Figure f)
        {
            return PausedMin + (PausedMax - PausedMin) * F01(PresenceSeed, (uint)f.Slot, (uint)f.Errand);
        }

        /// <summary>Puts a walker on the lattice, out of sight of the player.</summary>
        private void Place(ref Figure f, Vector3 pp, float now)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                f.Errand++;
                Vector3 p = _nav.NodePos(_nav.NodeFrom(H(PresenceSeed + 3u, (uint)f.Slot, (uint)f.Errand)));
                if (Dist2(p, pp) < PopClearDist * PopClearDist) continue;
                f.Pos = new Vector3(p.x, GroundY(p + Vector3.up), p.z);
                f.Path.Clear();
                f.PathIdx = 0;
                f.PausedUntil = 0f;
                f.Yaw = f.BaseYaw = F01(PresenceSeed, (uint)f.Slot, (uint)f.Errand) * 360f;
                TryAppear(ref f, pp, now);
                return;
            }
        }

        private void NewErrand(ref Figure f, float now, int overrideGoal = -1)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                f.Errand++;
                int goal = overrideGoal >= 0
                    ? overrideGoal : _nav.NodeFrom(H(PresenceSeed + 11u, (uint)f.Slot, (uint)f.Errand));
                if (_nav.TryPath(f.Pos, goal, f.Path) && f.Path.Count > 1)
                {
                    f.PathIdx = 1;      // [0] is the ground it already stands on
                    return;
                }
                if (overrideGoal >= 0) break;   // a fixed goal will not improve by retrying
            }
            f.Path.Clear();
            f.PathIdx = 0;
            f.PausedUntil = now + Pause(ref f);
        }

        /// <summary>World position of whatever an incursion is scheduled
        /// against — the same slot Facility.cs renders as the last real rack
        /// or solar row, so the figure stands exactly where it goes dark.</summary>
        private Vector3 IncursionTargetWorldPos(in TickReport r)
        {
            List<Vector3> slots = r.IncursionTargetKind == (int)IncursionKind.Rack ? _site.RackSlots : _site.SolarSlots;
            if (slots == null || slots.Count == 0) return _site.SiteCentre;
            int slot = Mathf.Clamp(r.IncursionTargetSlot, 0, slots.Count - 1);
            return _site.Root.TransformPoint(slots[slot]);
        }

        /// <summary>The nearest point on the fence LINE to a world position,
        /// pushed 3 m outward — where a figure waits before breaching, and
        /// where it lands back outside once the incident is over.</summary>
        private Vector3 BreachPointFor(Vector3 worldXZ)
        {
            float x = Mathf.Clamp(worldXZ.x, _site.FenceX0, _site.FenceX1);
            float z = Mathf.Clamp(worldXZ.z, _site.FenceZ0, _site.FenceZ1);
            float dW = x - _site.FenceX0, dE = _site.FenceX1 - x;
            float dS = z - _site.FenceZ0, dN = _site.FenceZ1 - z;
            float m = Mathf.Min(Mathf.Min(dW, dE), Mathf.Min(dS, dN));
            Vector3 outward;
            if (m == dW) { x = _site.FenceX0; outward = Vector3.left; }
            else if (m == dE) { x = _site.FenceX1; outward = Vector3.right; }
            else if (m == dS) { z = _site.FenceZ0; outward = Vector3.back; }
            else { z = _site.FenceZ1; outward = Vector3.forward; }
            return new Vector3(x, 0f, z) + outward * 3f;
        }

        /// <summary>The breach itself: straight for the real target, ignoring
        /// the town lattice entirely (it is crossing the fence, not walking
        /// the pavement), then a loop of "working on it" once arrived. No
        /// spectacle grammar (market brief §2) — the same restrained take a
        /// resident uses for any physical task, just held longer.</summary>
        private void UpdateSaboteurActive(ref Figure f, Vector3 pp, in TickReport r, float now, float dt)
        {
            Vector3 target = IncursionTargetWorldPos(r);
            var to = new Vector3(target.x - f.Pos.x, 0f, target.z - f.Pos.z);
            float d = to.magnitude;
            bool moving = d > ArriveDist;
            if (moving)
            {
                Vector3 dir = to / d;
                f.Yaw = Mathf.MoveTowardsAngle(f.Yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, TurnRate * dt);
                f.Pos += Quaternion.Euler(0f, f.Yaw, 0f) * Vector3.forward * (f.Speed * 1.3f * dt);
            }
            f.Path.Clear();
            f.PathIdx = 0;
            f.Pos.y = GroundY(f.Pos);
            PlayClip(ref f, moving ? "walk" : "interact-right");
            Apply(ref f, pp, dt, false);
        }

        /// <summary>The pavement, kerb or road under a figure. The figures'
        /// own layer is excluded, or the ray stops inside its own capsule.</summary>
        private static float GroundY(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out hit, 8f, GroundMask,
                    QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return p.y;
        }

        private void UpdateStander(ref Figure f, Vector3 pp, bool dark, float now, float dt)
        {
            if (!f.Shown)
            {
                if (f.Wanted)
                {
                    f.Pos = new Vector3(f.Spot.x, GroundY(f.Spot + Vector3.up), f.Spot.z);
                    TryAppear(ref f, pp, now);
                }
                return;
            }
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell
                && Dist2(f.Pos, pp) > PopClearDist * PopClearDist) f.Leaving = true;

            bool protester = f.Kind == FigureKind.Protester;
            float sway = (protester ? 3f : 2f) * Mathf.Sin(now * (protester ? 2.5f : 1.9f) + f.SwayPhase);
            f.Yaw = Mathf.MoveTowardsAngle(f.Yaw, f.BaseYaw + sway, 180f * dt);
            if (protester && !f.Leaving) { _protestersShown++; _protestCentroid += f.Pos; }
            // Both hands up is filming; one hand up carries the placard.
            PlayClip(ref f, protester ? "holding-left" : "holding-both");
            Apply(ref f, pp, dt, dark);
        }

        private void UpdateCar(ref Figure f, Vector3 pp, bool dark, float now, float dt)
        {
            if (!f.Shown)
            {
                if (f.Wanted) { f.Pos = LanePos(ref f, out _); TryAppear(ref f, pp, now); }
                return;
            }
            float[] cum = _laneCum[f.Lane];
            // Brakes for the corners; the arc flag is the segment sampled last frame.
            float target = f.Speed * (_laneArc[f.Lane][f.Seg] ? CornerSpeed : 1f);
            // And for anyone standing in the lane. The cars are solid now, and
            // one that shoved the operator down the road would be a hazard the
            // simulation never hears about.
            Vector3 forward = Quaternion.Euler(0f, f.Yaw, 0f) * Vector3.forward;
            var gap = new Vector3(pp.x - f.Pos.x, 0f, pp.z - f.Pos.z);
            float ahead = Vector3.Dot(gap, forward);
            float lateral = Vector3.Dot(gap, Vector3.Cross(Vector3.up, forward));
            if (ahead > -1.5f && ahead < BrakeDist && Mathf.Abs(lateral) < 2.4f) target = 0f;
            f.Vel = Mathf.MoveTowards(f.Vel, target, CarAccel * dt);
            f.S = Mathf.Repeat(f.S + f.Vel * dt, cum[cum.Length - 1]);
            Vector3 heading;
            f.Pos = LanePos(ref f, out heading);
            if (!f.Wanted && !f.Leaving && now - f.ShownSince >= MinDwell
                && Dist2(f.Pos, pp) > PopClearDist * PopClearDist) f.Leaving = true;
            f.Yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            Apply(ref f, pp, dt, dark);
        }

        // ---- shared mechanics --------------------------------------------------

        /// <summary>Appear only well away from the player. Figures no longer
        /// retreat, so this radius is the whole of what is left of the old
        /// distance rule: the crowd's size changes out of sight.</summary>
        private static void TryAppear(ref Figure f, Vector3 pp, float now)
        {
            if (Dist2(f.Pos, pp) < PopClearDist * PopClearDist) return;
            f.Shown = true;
            f.Leaving = false;
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
            if (f.Body.activeSelf != vis) f.Body.SetActive(vis);
            SetRenderer(f.Extra, vis && extraOn);
            SetRenderer(f.Placard, vis);
            // Solid only at full size: a collider on something scaling up out
            // of the ground is a trap you cannot see.
            bool solid = vis && f.Scale > 0.9f;
            if (f.Col.enabled != solid) f.Col.enabled = solid;
        }

        private static void Vanish(ref Figure f)
        {
            f.Shown = false;
            f.Leaving = false;
            f.Scale = 0f;
            f.Tf.localScale = Vector3.zero;
            f.Body.SetActive(false);
            SetRenderer(f.Extra, false);
            SetRenderer(f.Placard, false);
            f.Col.enabled = false;
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
            Vector3 p = Sample(_lanes[f.Lane], _laneCum[f.Lane], f.S, ref f.Seg, out heading);
            float slope;
            float shift = PinchAt(_lanePinch[f.Lane], f.S, out slope) * (LaneOffset - PinchOffset);
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

    /// <summary>A neighbour you can actually speak to. What they say is the
    /// town's own state read back: whichever nuisance the town remembers most
    /// loudly, or failing that the escalation stage. Residents are quoted
    /// plainly and never played for laughs (art-bible.md, on writing) — the
    /// joke, where there is one, is always on the operator.</summary>
    public sealed class Resident : MonoBehaviour, IInteractable
    {
        public const float Cooldown = 10f;
        public Presence.FigureKind Kind;
        private float _lastUse = -Cooldown;

        /// <summary>Set by Presence.UpdateWalker only while this figure is
        /// the currently active incursion actor, cleared the moment it
        /// resolves. While true, ordinary conversation is replaced by the
        /// one thing that matters: can this player stop them.</summary>
        public bool IsActiveSaboteur;

        /// <summary>Hud draws only E-prefixed prompts as offers, so while
        /// they have nothing more to say they do not offer to say it.</summary>
        public string Prompt(PlayerRig player)
        {
            if (IsActiveSaboteur) return player.Carried is TaserTool ? "E: taser them" : "";
            return Time.unscaledTime - _lastUse < Cooldown ? "" : "E: speak to the resident";
        }

        public void Interact(PlayerRig player)
        {
            if (IsActiveSaboteur)
            {
                if (!(player.Carried is TaserTool)) return;
                GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.InterruptIncursion },
                    "used the taser at the fence");
                NewsFeed.PostLocal("A resident: “I've said what I came to say.”");
                return;
            }
            if (Time.unscaledTime - _lastUse < Cooldown) return;
            _lastUse = Time.unscaledTime;
            NewsFeed.PostLocal("A resident: “" + Line(GameBootstrap.CurrentReport, Kind) + "”");
        }

        private static string Line(TickReport r, Presence.FigureKind kind)
        {
            if (kind == Presence.FigureKind.Protester)
            {
                switch (r.Stage)
                {
                    case EscalationStage.Protest: return "We'll be back on Saturday. It isn't personal.";
                    case EscalationStage.Injunction: return "It's with the solicitors now. I'd rather it hadn't come to that.";
                    case EscalationStage.Sabotage: return "I've nothing to say to you.";
                    default: return "We're only standing here.";
                }
            }
            // The loudest thing the town still remembers, if it is loud at all.
            float noise = (float)r.MNoise, air = (float)r.MAir, water = (float)r.MWater;
            float price = (float)r.MPrice, visual = (float)r.MVisual;
            float top = Mathf.Max(noise, Mathf.Max(air, Mathf.Max(water, Mathf.Max(price, visual))));
            if (top > 25f)
            {
                if (top == noise) return "That hum. Is it going to be all night again?";
                if (top == air) return "You can smell it on the washing when the wind turns.";
                if (top == water) return "Pressure's down again. They tell us that isn't you.";
                if (top == price) return "My bill went up. I looked it up — you're the biggest meter on this grid.";
                return "It's taller than the barn was. Bluer, too.";
            }
            switch (r.Stage)
            {
                case EscalationStage.Complaints: return "Someone's put a letter round about the noise.";
                case EscalationStage.Petition: return "There's a clipboard going door to door. I signed it.";
                case EscalationStage.Protest: return "Half the street's at your gate. You could go and talk to them.";
                case EscalationStage.Injunction: return "It's in front of a judge now. Nobody's pleased about that.";
                case EscalationStage.Sabotage: return "Somebody's been at your fence. It wasn't me.";
                default: return "Morning. Quieter than the lorries were, I'll give you that.";
            }
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
