using System.Collections.Generic;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Everything OUTSIDE the perimeter fence: the ring road with its
    /// pavements and furniture, and the four city blocks around the site.
    /// The datacentre stands in the middle of town (player-experience.md),
    /// so there is a block in every direction; the dense residential block
    /// stays WEST, because bearing 270° is the town sector the wind channel
    /// charges for (balance.tuning TOWN_BEARING_DEG).
    ///
    /// Also fills the outside-the-fence anchors on SiteRefs (routes, spots,
    /// window and lamp glow renderers) that the presence and weather layers
    /// read. Nothing here has gameplay state.
    ///
    /// One merged mesh per street layer and per block: the whole town is a
    /// dozen renderers. Geometry is deterministic — one System.Random(1234)
    /// consumed in a fixed order — because host and clients each build their
    /// own copy and a house that differs by a metre is a desync you can walk
    /// into.
    /// </summary>
    public static class CityBuilder
    {
        // Ring road datums. The road box sits IN the terrain (top 0.04), the
        // pavement box stands proud of it (top 0.15) so its side is the kerb;
        // markings float 1 cm above the road so nothing is coplanar.
        private const float RoadTop = 0.04f, RoadH = 0.12f;
        private const float PaveTop = 0.15f, PaveH = 0.2f;
        private const float MarkY = 0.04f, MarkH = 0.02f;

        /// <summary>Windows are cut per storey up to this many; the north
        /// apartment blocks would otherwise push the window count past ~1000
        /// boxes across three meshes.</summary>
        private const int MaxStoreys = 3;
        private const float WindowStep = 3f;

        /// <summary>A resident spot or route vertex keeps this far from the
        /// spawn (a respawning body must never appear inside a picket) and
        /// this far from any obstacle footprint.</summary>
        private const float SpawnClear = 9f, ObstacleClear = 1.5f;

        /// <summary>Verge trees are dropped this close to a block footprint;
        /// garden trees this close to any house (their crown radius is 1.6).</summary>
        private const float TreeClear = 10f, HouseTreeClear = 1.8f;

        /// <summary>A building face that gets windows: the wall box and how
        /// many storeys it has.</summary>
        private struct Facade
        {
            public Vector3 Centre, Size;
            public int Storeys;
        }

        public static void Build(Transform root, SiteRefs refs)
        {
            var rnd = new System.Random(1234);
            var facades = new List<Facade>();

            BuildRoads(root);
            BuildMarkings(root);
            // Blocks before furniture: the verge trees are culled against the
            // block footprints, so those have to exist first.
            BuildWest(root, refs, facades, rnd);
            BuildNorth(root, refs, facades);
            BuildEast(root, refs, facades);
            BuildSouth(root, refs, facades);
            BuildFurniture(root, refs);
            BuildWindows(root, refs, facades);
            DefineAnchors(refs);
        }

        // ------------------------------------------------------------------
        // Ring road: carriageways and kerbed pavements
        // ------------------------------------------------------------------

        private static void BuildRoads(Transform root)
        {
            var pm = new ProcMesh();
            // Carriageways. S and N span the corners; W and E butt against them.
            Slab(pm, -24f, 82f, -32f, -25f, RoadTop, RoadH, Palette.Slate);
            Slab(pm, -24f, 82f, 55f, 62f, RoadTop, RoadH, Palette.Slate);
            Slab(pm, -24f, -17f, -25f, 55f, RoadTop, RoadH, Palette.Slate);
            Slab(pm, 75f, 82f, -25f, 55f, RoadTop, RoadH, Palette.Slate);

            // Inner pavements, 2 m of grass verge between them and the fence.
            // The spawn stands on the south one, east of the visitor bays,
            // which are recessed into it (see BayZ0/BayZ1): the presence
            // layer's cars run 2 m off the centre line, so a car parked ON
            // the 7 m road would sit in their lane.
            Slab(pm, -17f, BayAX0, -25f, -22f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, BayAX0, BayBX1, BayZ1, -22f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, BayBX1, 75f, -25f, -22f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, BayAX0, BayBX1, -25f, BayZ1, RoadTop, RoadH, Palette.Slate);
            Slab(pm, -17f, 75f, 52f, 55f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, 72f, 75f, -22f, 52f, PaveTop, PaveH, Palette.Concrete);
            // West one is split by a dropped kerb in front of the vehicle
            // gate (z 14..24): the forklift leaves the site here and a 11 cm
            // step is a wheelie.
            Slab(pm, -17f, -14f, -22f, 13f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, -17f, -14f, 25f, 52f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, -17f, -14f, 13f, 25f, RoadTop, RoadH, Palette.Slate);

            // Outer pavements: the residents' side of the road.
            Slab(pm, -27f, 85f, -35f, -32f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, -27f, 85f, 62f, 65f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, -27f, -24f, -32f, 62f, PaveTop, PaveH, Palette.Concrete);
            Slab(pm, 82f, 85f, -32f, 62f, PaveTop, PaveH, Palette.Concrete);
            MatLib.Spawn("Streets", pm.Build("streets"), root, Vector3.zero);
        }

        /// <summary>Horizontal box from its plan rectangle and TOP height.</summary>
        private static void Slab(ProcMesh pm, float x0, float x1, float z0, float z1,
            float top, float h, Color colour)
        {
            pm.Box(new Vector3((x0 + x1) / 2f, top - h / 2f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, h, z1 - z0), colour);
        }

        // ------------------------------------------------------------------
        // Paint: centre lines, the zebra below the gate, visitor bays
        // ------------------------------------------------------------------

        // Zebra stripes x 52.75 + 0.9k (k 0..5), 0.5 wide: the crossing sits
        // on the pedestrian axis x = 55 (gate and door centre).
        private const float ZebraX0 = 52.5f, ZebraX1 = 57.5f;

        // Visitor bays on the north edge of the south road. The brief's
        // 49..53.5 bay ran into the zebra, so both bays sit 1.5 m further west.
        // In depth they start where the inner traffic lane ends (centre line
        // z -28.5, lane offset 2 m, car half-width 0.9) and cut 1.5 m into
        // the pavement; BayZ1 is the kerb at the back of the recess.
        private const float BayAX0 = 42.5f, BayAX1 = 47f, BayBX0 = 47.5f, BayBX1 = 52f;
        private const float BayZ0 = -25.6f, BayZ1 = -23.5f;
        private const float BayCarZ = -24.55f;

        private static void BuildMarkings(Transform root)
        {
            var pm = new ProcMesh();
            var dash = new Vector3(2f, MarkH, 0.15f);
            var dashZ = new Vector3(0.15f, MarkH, 2f);
            // Centre-line dashes every 4 m, starting 1 m in from the road end.
            for (float x = -23f; x + 1f <= 82f; x += 4f)
            {
                // No dash through the zebra.
                bool onZebra = x + 1f > ZebraX0 - 0.25f && x - 1f < ZebraX1 + 0.25f;
                if (!onZebra) pm.Box(new Vector3(x, MarkY, -28.5f), dash, Palette.Render);
                pm.Box(new Vector3(x, MarkY, 58.5f), dash, Palette.Render);
            }
            for (float z = -24f; z + 1f <= 55f; z += 4f)
            {
                pm.Box(new Vector3(-20.5f, MarkY, z), dashZ, Palette.Render);
                pm.Box(new Vector3(78.5f, MarkY, z), dashZ, Palette.Render);
            }

            for (int k = 0; k < 6; k++)
                pm.Box(new Vector3(52.75f + 0.9f * k, MarkY, -28.5f),
                    new Vector3(0.5f, MarkH, 7f), Palette.Render);

            BayOutline(pm, BayAX0, BayAX1);
            BayOutline(pm, BayBX0, BayBX1);
            MatLib.Spawn("RoadMarkings", pm.Build("markings"), root, Vector3.zero, collider: false);
        }

        /// <summary>Three painted sides of a parallel bay; the recess kerb is
        /// the fourth. The end lines stop 6 cm short of it so they are not
        /// buried inside the pavement box.</summary>
        private static void BayOutline(ProcMesh pm, float x0, float x1)
        {
            const float w = 0.12f;
            float zS = BayZ0 + w, zN = BayZ1 - 0.06f;
            float zMid = (zS + zN) / 2f, len = zN - zS;
            pm.Box(new Vector3((x0 + x1) / 2f, MarkY, BayZ0 + w / 2f), new Vector3(x1 - x0, MarkH, w), Palette.Render);
            pm.Box(new Vector3(x0 + w / 2f, MarkY, zMid), new Vector3(w, MarkH, len), Palette.Render);
            pm.Box(new Vector3(x1 - w / 2f, MarkY, zMid), new Vector3(w, MarkH, len), Palette.Render);
        }

        // ------------------------------------------------------------------
        // Shared recipes
        // ------------------------------------------------------------------

        /// <summary>A flat-roofed block: rendered walls on a concrete plinth
        /// with a slate parapet 0.3 m above the roof. Registers the footprint
        /// as an obstacle and the walls as a window facade.</summary>
        private static void Building(ProcMesh pm, SiteRefs refs, List<Facade> facades,
            float x, float z, float w, float h, float d, Color wall)
        {
            var centre = new Vector3(x, h / 2f, z);
            var size = new Vector3(w, h, d);
            pm.Box(centre, size, wall);
            // Plinth and parapet are 10 cm proud of the walls, and the parapet
            // starts 10 cm below the roof: no face shares a plane with a wall.
            pm.Box(new Vector3(x, 0.5f, z), new Vector3(w + 0.1f, 1f, d + 0.1f), Palette.Concrete);
            pm.Box(new Vector3(x, h + 0.1f, z), new Vector3(w + 0.2f, 0.4f, d + 0.2f), Palette.Slate);
            refs.TownObstacles.Add(new Bounds(centre, size));
            facades.Add(new Facade { Centre = centre, Size = size, Storeys = Storeys(h) });
        }

        /// <summary>Storeys that fit windows at y 1.6 + 3k below the roof.</summary>
        private static int Storeys(float h)
        {
            int n = 0;
            while (n < MaxStoreys && 1.6f + 3f * n + 0.6f <= h) n++;
            return n;
        }

        private static void Obstacle(SiteRefs refs, Vector3 centre, Vector3 size)
        {
            refs.TownObstacles.Add(new Bounds(centre, size));
        }

        /// <summary>The town tree: a five-sided trunk and a cone crown.</summary>
        private static void Tree(ProcMesh pm, float x, float z)
        {
            pm.Cylinder(new Vector3(x, 1.2f, z), 0.2f, 2.4f, 5, Palette.Earth);
            pm.Cylinder(new Vector3(x, 3.4f, z), 1.6f, 2.6f, 5, Palette.Foliage, 0.2f);
        }

        /// <summary>A parked car: slate body and ink cabin, long axis along x or z.</summary>
        private static void Car(ProcMesh pm, SiteRefs refs, float x, float z, bool alongX)
        {
            var body = alongX ? new Vector3(4.2f, 1.4f, 1.8f) : new Vector3(1.8f, 1.4f, 4.2f);
            var cabin = alongX ? new Vector3(2.2f, 0.8f, 1.6f) : new Vector3(1.6f, 0.8f, 2.2f);
            pm.Box(new Vector3(x, 0.7f, z), body, Palette.Slate);
            pm.Box(new Vector3(x, 1.6f, z), cabin, Palette.Ink);
            Obstacle(refs, new Vector3(x, 0.7f, z), body);
        }

        // ------------------------------------------------------------------
        // West: the residential block the plume drifts over
        // ------------------------------------------------------------------

        private static void BuildWest(Transform root, SiteRefs refs, List<Facade> facades,
            System.Random rnd)
        {
            var pm = new ProcMesh();
            // Fourteen houses in four rows; fronts 6.5 m from the outer W
            // pavement (x -27). The random draw order is z, w, d per house
            // and must stay so — see the class summary.
            var houses = new Bounds[14];
            for (int i = 0; i < 14; i++)
            {
                float x = -38f - (i % 4) * 11f;
                float z = -26f + (i / 4) * 14f + (float)rnd.NextDouble() * 4f;
                float w = 6f + (float)rnd.NextDouble() * 3f;
                float d = 5f + (float)rnd.NextDouble() * 3f;
                var centre = new Vector3(x, 1.6f, z);
                var size = new Vector3(w, 3.2f, d);
                pm.Box(centre, size, Palette.Render);
                pm.Box(new Vector3(x, 3.6f, z), new Vector3(w + 0.4f, 0.9f, d + 0.4f), Palette.Earth);
                houses[i] = new Bounds(centre, size);
                refs.TownObstacles.Add(houses[i]);
                facades.Add(new Facade { Centre = centre, Size = size, Storeys = 1 });
            }
            // A garden tree off each house's north-east corner, once every
            // house stands: the crown (r 1.6) must clear the neighbours too.
            foreach (Bounds h in houses)
            {
                var p = new Vector3(h.max.x + 1.5f, 0.5f, h.max.z + 1.5f);
                bool clear = true;
                foreach (Bounds o in houses)
                    if (PlanDistance(p, o) < HouseTreeClear) { clear = false; break; }
                if (clear) Tree(pm, p.x, p.z);
            }

            // The church: nave and tower with a pyramid spire seated inside
            // the tower parapet.
            Building(pm, refs, facades, -45f, 38f, 10f, 8f, 14f, Palette.Render);
            Building(pm, refs, facades, -45f, 30f, 4f, 18f, 4f, Palette.Render);
            Pyramid(pm, new Vector3(-45f, 18.2f, 30f), 2f, 4f, Palette.Slate);

            MatLib.Spawn("CityWest", pm.Build("cityWest"), root, Vector3.zero);
        }

        /// <summary>Square pyramid on a base square of half-width hw at
        /// centre, h tall. The apex is a 10 cm plateau so no face is a
        /// degenerate quad.</summary>
        private static void Pyramid(ProcMesh pm, Vector3 centre, float hw, float h, Color colour)
        {
            const float tip = 0.05f;
            float y1 = centre.y + h;
            var b00 = new Vector3(centre.x - hw, centre.y, centre.z - hw);
            var b10 = new Vector3(centre.x + hw, centre.y, centre.z - hw);
            var b11 = new Vector3(centre.x + hw, centre.y, centre.z + hw);
            var b01 = new Vector3(centre.x - hw, centre.y, centre.z + hw);
            var t00 = new Vector3(centre.x - tip, y1, centre.z - tip);
            var t10 = new Vector3(centre.x + tip, y1, centre.z - tip);
            var t11 = new Vector3(centre.x + tip, y1, centre.z + tip);
            var t01 = new Vector3(centre.x - tip, y1, centre.z + tip);
            // Same corner order per face as ProcMesh.Box, so the winding is outward.
            pm.Quad(b00, b10, t10, t00, colour);   // -z
            pm.Quad(b11, b01, t01, t11, colour);   // +z
            pm.Quad(b10, b11, t11, t10, colour);   // +x
            pm.Quad(b01, b00, t00, t01, colour);   // -x
            pm.Quad(t00, t10, t11, t01, Color.Lerp(colour, Color.white, 0.08f));
        }

        // ------------------------------------------------------------------
        // North: offices, the school and the sports hall
        // ------------------------------------------------------------------

        private static void BuildNorth(Transform root, SiteRefs refs, List<Facade> facades)
        {
            var pm = new ProcMesh();
            // Front row along the outer N pavement (z 65), 4 m back from it.
            Building(pm, refs, facades, -8f, 76f, 24f, 12f, 14f, Palette.Render);
            Building(pm, refs, facades, 24f, 76f, 26f, 15f, 14f, Palette.Render);
            Building(pm, refs, facades, 56f, 76f, 24f, 9f, 14f, Palette.Render);
            Building(pm, refs, facades, 78f, 76f, 12f, 12f, 14f, Palette.Render);
            // Second row: school and sports hall.
            Building(pm, refs, facades, 10f, 94f, 30f, 12f, 12f, Palette.Render);
            Building(pm, refs, facades, 50f, 94f, 30f, 15f, 12f, Palette.Render);
            // Courtyard trees in the strip between the rows.
            float[] tx = { -14f, -2f, 8f, 20f, 30f, 42f, 54f, 66f };
            foreach (float x in tx) Tree(pm, x, 85.5f);
            MatLib.Spawn("CityNorth", pm.Build("cityNorth"), root, Vector3.zero);
        }

        // ------------------------------------------------------------------
        // East: supermarket, workshop, petrol station
        // ------------------------------------------------------------------

        private static void BuildEast(Transform root, SiteRefs refs, List<Facade> facades)
        {
            var pm = new ProcMesh();

            // Supermarket with its car park between it and the road. The
            // fascia sits above the top storey's windows and below the parapet.
            Building(pm, refs, facades, 115f, 40f, 30f, 6f, 22f, Palette.Render);
            pm.Box(new Vector3(99.95f, 5.5f, 40f), new Vector3(0.12f, 0.6f, 20f), Palette.PaleBlue);
            Slab(pm, 89f, 99f, 29f, 51f, RoadTop, RoadH, Palette.Slate);
            Car(pm, refs, 96.8f, 34f, true);
            Car(pm, refs, 96.8f, 37f, true);
            Car(pm, refs, 96.8f, 40f, true);

            // Workshop: roller door on the road face, two crates outside.
            Building(pm, refs, facades, 100f, 5f, 20f, 6f, 16f, Palette.Render);
            pm.Box(new Vector3(90f, 2f, 5f), new Vector3(0.2f, 4f, 4f), Palette.Slate);
            var crate = new Vector3(1.2f, 1.2f, 1.2f);
            pm.Box(new Vector3(98f, 0.6f, -5.5f), crate, Palette.Earth);
            pm.Box(new Vector3(99.4f, 0.6f, -5.5f), crate, Palette.Earth);
            Obstacle(refs, new Vector3(98.7f, 0.6f, -5.5f), new Vector3(2.6f, 1.2f, 1.2f));

            BuildPetrolStation(pm, refs, facades);
            MatLib.Spawn("CityEast", pm.Build("cityEast"), root, Vector3.zero);
        }

        private static void BuildPetrolStation(ProcMesh pm, SiteRefs refs, List<Facade> facades)
        {
            Slab(pm, 88f, 118f, -32f, -16f, RoadTop, RoadH, Palette.Slate);
            // Canopy on four columns; the columns are sunk 10 cm into it so
            // their caps do not share its underside plane.
            pm.Box(new Vector3(103f, 4.75f, -24f), new Vector3(16f, 0.5f, 9f), Palette.Render);
            pm.Box(new Vector3(95f, 4.75f, -24f), new Vector3(0.12f, 0.6f, 9.2f), Palette.Amber);
            float[] cx = { 96.5f, 109.5f };
            float[] cz = { -27.5f, -20.5f };
            foreach (float x in cx)
                foreach (float z in cz)
                {
                    pm.Cylinder(new Vector3(x, 2.3f, z), 0.25f, 4.6f, 6, Palette.Concrete);
                    Obstacle(refs, new Vector3(x, 2.3f, z), new Vector3(0.5f, 4.6f, 0.5f));
                }
            // Two pump islands, two pumps each.
            float[] ix = { 99f, 105f };
            foreach (float x in ix)
            {
                pm.Box(new Vector3(x, 0.1f, -24f), new Vector3(1f, 0.15f, 5f), Palette.Slate);
                pm.Box(new Vector3(x, 0.875f, -25.5f), new Vector3(0.6f, 1.4f, 0.4f), Palette.Render);
                pm.Box(new Vector3(x, 0.875f, -22.5f), new Vector3(0.6f, 1.4f, 0.4f), Palette.Render);
                Obstacle(refs, new Vector3(x, 0.8f, -24f), new Vector3(1f, 1.6f, 5f));
            }
            Building(pm, refs, facades, 113f, -24f, 6f, 3.2f, 6f, Palette.Render);
        }

        // ------------------------------------------------------------------
        // South: terraces, the playground, the corner shop
        // ------------------------------------------------------------------

        private static void BuildSouth(Transform root, SiteRefs refs, List<Facade> facades)
        {
            var pm = new ProcMesh();
            // Each terrace is ONE wall box (six unit boxes side by side would
            // stack coplanar plinths and parapets), read as six units by the
            // party walls on the roof and a door per unit.
            Terrace(pm, refs, facades, 0f, -41f);
            Terrace(pm, refs, facades, 44f, -41f);
            Terrace(pm, refs, facades, 0f, -55f);

            // The alley between the two front terraces is the playground:
            // a swing frame and a slide.
            pm.Cylinder(new Vector3(38f, 1.2f, -41f), 0.08f, 2.4f, 6, Palette.Amber);
            pm.Cylinder(new Vector3(42f, 1.2f, -41f), 0.08f, 2.4f, 6, Palette.Amber);
            pm.Box(new Vector3(40f, 2.3f, -41f), new Vector3(4.2f, 0.1f, 0.1f), Palette.Amber);
            Obstacle(refs, new Vector3(40f, 1.2f, -41f), new Vector3(4.2f, 2.4f, 0.3f));
            pm.Beam(new Vector3(40f, 1.6f, -43f), new Vector3(40f, 0.25f, -45.5f), 0.6f, Palette.PaleBlue);
            Obstacle(refs, new Vector3(40f, 0.9f, -44.25f), new Vector3(0.8f, 1.8f, 3f));

            Building(pm, refs, facades, -16f, -42f, 12f, 4f, 10f, Palette.Render);

            float[] tx = { 6f, 18f, 30f, 50f, 62f, 74f };
            foreach (float x in tx) Tree(pm, x, -47.5f);
            MatLib.Spawn("CitySouth", pm.Build("citySouth"), root, Vector3.zero);
        }

        /// <summary>Six 6 m row houses from x0, 7 m high, 6 m deep, doors on
        /// the north (street) face.</summary>
        private static void Terrace(ProcMesh pm, SiteRefs refs, List<Facade> facades, float x0, float z)
        {
            Building(pm, refs, facades, x0 + 18f, z, 36f, 7f, 6f, Palette.Render);
            float face = z + 3f;
            for (int k = 0; k < 6; k++)
            {
                float x = x0 + 3f + 6f * k;
                // Doors stand 15 cm proud so the plinth (5 cm) does not swallow them.
                pm.Box(new Vector3(x, 1f, face), new Vector3(0.9f, 2f, 0.3f), Palette.Earth);
                if (k > 0)
                    pm.Box(new Vector3(x0 + 6f * k, 7.4f, z), new Vector3(0.2f, 0.6f, 6.6f), Palette.Slate);
            }
        }

        // ------------------------------------------------------------------
        // Street furniture: lamps, the bus stop, parked cars, verge trees
        // ------------------------------------------------------------------

        private static void BuildFurniture(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            var heads = new ProcMesh();
            // Everything registered so far is a block footprint; the verge
            // trees are culled against those, not against the lamps and cars
            // added below.
            int blockObstacles = refs.TownObstacles.Count;

            // Twenty lamps on the outer pavements, arms reaching over the road.
            // No Light components: the night pass lights the heads by enabling
            // the unlit "StreetLampHeads" mesh instead.
            float[] lx = { -20f, 0f, 20f, 40f, 60f, 80f };
            foreach (float x in lx)
            {
                Lamp(pm, heads, refs, x, -32.4f, 0f, 1f);
                Lamp(pm, heads, refs, x, 62.4f, 0f, -1f);
            }
            float[] lz = { -10f, 10f, 30f, 50f };
            foreach (float z in lz)
            {
                Lamp(pm, heads, refs, -24.4f, z, 1f, 0f);
                Lamp(pm, heads, refs, 82.4f, z, -1f, 0f);
            }

            BusStop(pm, refs);

            // Two cars in the visitor bays (parallel, along x). There is no
            // kerbside parking on the W and E roads: both lanes of the 7 m
            // carriageway carry the presence layer's traffic.
            Car(pm, refs, (BayAX0 + BayAX1) / 2f, BayCarZ, true);
            Car(pm, refs, (BayBX0 + BayBX1) / 2f, BayCarZ, true);

            VergeTrees(pm, refs, blockObstacles);
            MatLib.Spawn("StreetFurniture", pm.Build("furniture"), root, Vector3.zero);

            refs.StreetLampHeads = GlowMesh(root, "StreetLampHeads", heads.Build("lampHeads"));
        }

        /// <summary>Pole, arm and — in the glow mesh — the amber head at the
        /// arm's end. (dx, dz) is the unit direction toward the road.</summary>
        private static void Lamp(ProcMesh pm, ProcMesh heads, SiteRefs refs,
            float x, float z, float dx, float dz)
        {
            pm.Cylinder(new Vector3(x, 3f, z), 0.08f, 6f, 6, Palette.Slate);
            var arm = new Vector3(0.12f + 0.68f * Mathf.Abs(dx), 0.12f, 0.12f + 0.68f * Mathf.Abs(dz));
            pm.Box(new Vector3(x + dx * 0.4f, 5.9f, z + dz * 0.4f), arm, Palette.Slate);
            var head = new Vector3(0.3f + 0.2f * Mathf.Abs(dx), 0.15f, 0.3f + 0.2f * Mathf.Abs(dz));
            heads.Box(new Vector3(x + dx * 0.85f, 5.85f, z + dz * 0.85f), head, Palette.Amber);
            Obstacle(refs, new Vector3(x, 3f, z), new Vector3(0.3f, 6f, 0.3f));
        }

        /// <summary>Shelter at BusStopPos.x. The structure stands on the verge
        /// BEHIND the outer pavement with the roof reaching over it, because
        /// everything solid has to keep ObstacleClear from the standing spot
        /// — and the spot itself is at the KERB (z -32.6), beside the stop
        /// sign, because the pavement's centre line (z -33.5) is the outer
        /// loop route and the presence layer keeps standers 0.8 m off it.</summary>
        private static void BusStop(ProcMesh pm, SiteRefs refs)
        {
            float x = refs.BusStopPos.x;
            pm.Cylinder(new Vector3(x - 1.45f, 1.3f, -35.35f), 0.06f, 2.6f, 6, Palette.Slate);
            pm.Cylinder(new Vector3(x + 1.45f, 1.3f, -35.35f), 0.06f, 2.6f, 6, Palette.Slate);
            pm.Box(new Vector3(x, 2.6f, -34.4f), new Vector3(3f, 0.1f, 2f), Palette.Slate);
            pm.Box(new Vector3(x, 1.4f, -35.45f), new Vector3(3f, 2.2f, 0.05f), Palette.PaleBlue);
            pm.Box(new Vector3(x, 0.45f, -35.25f), new Vector3(2.4f, 0.08f, 0.4f), Palette.Earth);
            pm.Box(new Vector3(x - 1f, 0.22f, -35.25f), new Vector3(0.08f, 0.44f, 0.36f), Palette.Slate);
            pm.Box(new Vector3(x + 1f, 0.22f, -35.25f), new Vector3(0.08f, 0.44f, 0.36f), Palette.Slate);
            Obstacle(refs, new Vector3(x, 1.3f, -35.3f), new Vector3(3.1f, 2.6f, 0.5f));
            // Stop sign at the kerb, 3 m along.
            pm.Cylinder(new Vector3(x + 3f, 1.5f, -32.6f), 0.04f, 3f, 6, Palette.Slate);
            pm.Box(new Vector3(x + 3f, 2.85f, -32.6f), new Vector3(0.5f, 0.4f, 0.06f), Palette.ProgramBlue);
            Obstacle(refs, new Vector3(x + 3f, 1.5f, -32.6f), new Vector3(0.2f, 3f, 0.2f));
        }

        /// <summary>Trees along the outer verges every ~12 m, dropped where a
        /// block footprint is within TreeClear.</summary>
        private static void VergeTrees(ProcMesh pm, SiteRefs refs, int blockObstacles)
        {
            for (float x = -24f; x <= 84f; x += 12f)
            {
                VergeTree(pm, refs, blockObstacles, x, -36.5f);
                VergeTree(pm, refs, blockObstacles, x, 66.5f);
            }
            for (float z = -30f; z <= 60f; z += 12f)
            {
                VergeTree(pm, refs, blockObstacles, -28.5f, z);
                VergeTree(pm, refs, blockObstacles, 86.5f, z);
            }
        }

        private static void VergeTree(ProcMesh pm, SiteRefs refs, int blockObstacles, float x, float z)
        {
            var p = new Vector3(x, 0.5f, z);
            for (int i = 0; i < blockObstacles; i++)
                if (PlanDistance(p, refs.TownObstacles[i]) < TreeClear) return;
            Tree(pm, x, z);
        }

        // ------------------------------------------------------------------
        // Windows: a lit pale-blue pane on every face, and two amber glow
        // meshes the night pass switches on in two waves
        // ------------------------------------------------------------------

        private static void BuildWindows(Transform root, SiteRefs refs, List<Facade> facades)
        {
            var lit = new ProcMesh();
            var early = new ProcMesh();
            var late = new ProcMesh();
            int index = 0;
            foreach (Facade f in facades)
            {
                for (int side = 0; side < 4; side++)
                {
                    // Sides: +x, -x, +z, -z. Windows every 3 m, centred on the face.
                    bool alongZ = side < 2;
                    float len = alongZ ? f.Size.z : f.Size.x;
                    int n = (int)Mathf.Floor(len / WindowStep);
                    float sign = side % 2 == 0 ? 1f : -1f;
                    for (int k = 0; k < f.Storeys; k++)
                        for (int j = 0; j < n; j++)
                        {
                            float along = -(n - 1) * WindowStep / 2f + j * WindowStep;
                            Vector3 c = alongZ
                                ? new Vector3(f.Centre.x + sign * f.Size.x / 2f, 1.6f + 3f * k, f.Centre.z + along)
                                : new Vector3(f.Centre.x + along, 1.6f + 3f * k, f.Centre.z + sign * f.Size.z / 2f);
                            // The pane is 2 cm proud; the glow box 4 cm and a
                            // touch larger, so no edge is coplanar with it.
                            lit.Box(c, alongZ ? new Vector3(0.04f, 1.2f, 1f) : new Vector3(1f, 1.2f, 0.04f), Palette.PaleBlue);
                            var glow = index % 5 < 3 ? early : late;
                            glow.Box(c, alongZ ? new Vector3(0.08f, 1.24f, 1.04f) : new Vector3(1.04f, 1.24f, 0.08f), Palette.Amber);
                            index++;
                        }
                }
            }
            MatLib.Spawn("CityWindows", lit.Build("windows"), root, Vector3.zero, collider: false);
            refs.TownWindowsEarly = GlowMesh(root, "CityWindowsEarly", early.Build("windowsEarly"));
            refs.TownWindowsLate = GlowMesh(root, "CityWindowsLate", late.Build("windowsLate"));
        }

        /// <summary>Unlit, collider-less, and off until the night pass wants it.</summary>
        private static Renderer GlowMesh(Transform root, string name, Mesh mesh)
        {
            var go = MatLib.Spawn(name, mesh, root, Vector3.zero, collider: false, transparent: true);
            var r = go.GetComponent<Renderer>();
            r.enabled = false;
            return r;
        }

        // ------------------------------------------------------------------
        // Anchors for the presence and weather layers
        // ------------------------------------------------------------------

        private static void DefineAnchors(SiteRefs refs)
        {
            // West first: the dense residential block is the one the wind
            // sector and the night glow read.
            refs.TownBearingsDeg.Clear();
            refs.TownBearingsDeg.Add(270f);
            refs.TownBearingsDeg.Add(0f);
            refs.TownBearingsDeg.Add(90f);
            refs.TownBearingsDeg.Add(180f);

            // Outer pavement loop, ping-ponged: residents never share the
            // inner pavement with the player. It is the only route: a walked
            // zebra crossing on the x = 55 pedestrian axis would end 2 m from
            // StreetSpawn, and the presence layer drops any vertex inside
            // SpawnClear — the zebra stays paint.
            AddRoute(refs, "outer loop",
                new Vector3(-25.5f, PaveTop, -33.5f), new Vector3(83.5f, PaveTop, -33.5f),
                new Vector3(83.5f, PaveTop, 63.5f), new Vector3(-25.5f, PaveTop, 63.5f));

            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(10f, 0, -21.2f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(30f, 0, -21.2f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(45f, 0, -21.2f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(-13.2f, 0, 0f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(-13.2f, 0, 30f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", new Vector3(71.2f, 0, 10f));
            AddSpot(refs.FenceLineSpots, refs, "fence line", refs.BusStopPos);

            // The picket stands at the VEHICLE gate on the dropped kerb
            // (x -17..-14, z 13..25): deliveries are what it turns away. The
            // carriageway itself belongs to the cars — the inner lane runs at
            // x = -18.5 and the presence layer drops any stander within 1.4 m
            // of it, so the front row is on the kerb line at x = -17.
            float y = RoadTop + 0.01f;
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-15.5f, y, 17f));
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-15.5f, y, 21f));
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-16.5f, y, 14.5f));
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-16.5f, y, 23.5f));
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-17f, y, 16f));
            AddSpot(refs.GateProtestSpots, refs, "picket", new Vector3(-17f, y, 22f));
        }

        private static void AddSpot(List<Vector3> list, SiteRefs refs, string what, Vector3 p)
        {
            if (Clear(refs, what, p, true)) list.Add(p);
        }

        /// <summary>A route is all or nothing: a polyline with a vertex missing
        /// is a different path, possibly through a wall.</summary>
        private static void AddRoute(SiteRefs refs, string what, params Vector3[] pts)
        {
            foreach (Vector3 p in pts)
                if (!Clear(refs, what, p, true)) return;
            refs.ResidentRoutes.Add(pts);
        }

        /// <summary>The placement rule for anything a resident stands on:
        /// SpawnClear from the spawn, ObstacleClear from every footprint.</summary>
        private static bool Clear(SiteRefs refs, string what, Vector3 p, bool spawnClear)
        {
            if (spawnClear && Vector3.Distance(p, refs.StreetSpawn) < SpawnClear)
            {
                Debug.LogWarning("CityBuilder: " + what + " " + p + " is within " + SpawnClear + " m of the spawn; skipped.");
                return false;
            }
            foreach (Bounds b in refs.TownObstacles)
            {
                if (Distance(p, b) >= ObstacleClear) continue;
                Debug.LogWarning("CityBuilder: " + what + " " + p + " is within " + ObstacleClear + " m of an obstacle at " + b.center + "; skipped.");
                return false;
            }
            return true;
        }

        /// <summary>Distance from a point to the nearest point of a box (0 inside).</summary>
        private static float Distance(Vector3 p, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.size.x / 2f);
            float dy = Mathf.Max(0f, Mathf.Abs(p.y - b.center.y) - b.size.y / 2f);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.size.z / 2f);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Distance in plan (x/z only) from a point to the nearest
        /// point of a box footprint (0 inside). Height is ignored: a tree
        /// crown clears a house whatever storey the bounds reach.</summary>
        private static float PlanDistance(Vector3 p, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.size.x / 2f);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.size.z / 2f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
