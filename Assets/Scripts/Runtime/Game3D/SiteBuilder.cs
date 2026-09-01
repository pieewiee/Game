using System.Collections.Generic;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>A fire compartment: the unit of suppression, EPO scope and
    /// who-is-inside queries (construction-routing.md §2, simplified for the
    /// slice to three fixed compartments).</summary>
    public sealed class Room
    {
        public string Name;
        public Bounds Bounds;
        public List<Door> Doors = new List<Door>();

        public bool Contains(Vector3 p) { return Bounds.Contains(p); }
    }

    /// <summary>Everything static that the dynamic layers hang off.</summary>
    public sealed class SiteRefs
    {
        public Transform Root;
        public Room HallA, Plant, Office;
        public List<Room> Rooms = new List<Room>();
        public Vector3 CarParkSpawn = new Vector3(-14, 0.1f, -14);
        public Vector3 DieselStackTop = new Vector3(46, 4.2f, 14);
        public Vector3 TransformerPos = new Vector3(46, 0, 6);
        public List<Vector3> RackSlots = new List<Vector3>();      // Hall A
        public List<Vector3> PlantSlots = new List<Vector3>();     // Plant room
        public List<Vector3> SolarSlots = new List<Vector3>();     // south field
        public Vector3 TurbinePos = new Vector3(70, 0, 30);
        public Transform GroundTf;
        public Mesh GroundSummer, GroundWinter;
    }

    /// <summary>
    /// Builds the whole static site from code: ground, the three-compartment
    /// building (no roof — the one directional light is the only light we own,
    /// and a slice reads better lit than correct), yard, fence in Program Blue,
    /// solar field anchors, the town of small houses to the WEST — which is
    /// bearing 270°, exactly the TOWN_BEARING_DEG the wind sector uses, so the
    /// plume you see drifting over the houses is the same event the air channel
    /// is charging you for.
    /// </summary>
    public static class SiteBuilder
    {
        public const float WallH = 4f;

        public static SiteRefs Build(Transform parent)
        {
            var refs = new SiteRefs();
            var root = new GameObject("Site").transform;
            root.SetParent(parent, false);
            refs.Root = root;

            BuildGround(root, refs);
            BuildBuilding(root, refs);
            BuildYard(root, refs);
            BuildFence(root);
            BuildTown(root);
            CollectSlots(refs);
            return refs;
        }

        private static void BuildGround(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, -0.5f, 0), new Vector3(500, 1f, 500), Palette.Field);
            var ground = MatLib.Spawn("Ground", pm.Build("ground"), root, Vector3.zero);
            refs.GroundTf = ground.transform;

            var apron = new ProcMesh();
            apron.Box(new Vector3(26, 0.02f, 9), new Vector3(80, 0.05f, 40), Palette.Concrete);
            MatLib.Spawn("Apron", apron.Build("apron"), root, Vector3.zero);

            var park = new ProcMesh();
            park.Box(new Vector3(-14, 0.02f, -14), new Vector3(14, 0.05f, 10), Palette.Slate);
            MatLib.Spawn("CarPark", park.Build("carpark"), root, Vector3.zero, false);
        }

        // Building: x 0..40, z 0..20. Hall A 0..20, Plant 20..30, Office 30..40.
        private static void BuildBuilding(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            const float t = 0.3f;
            // Exterior walls with door gaps.
            WallZ(pm, 0, 40, 20, t);                                 // north
            WallZSplit(pm, 0, 40, 0, t, 34, 36);                     // south, office entrance
            WallXSplit(pm, 0, 20, 0, t, 8, 12);                      // west, delivery door
            WallX(pm, 0, 20, 40, t);                                 // east
            // Interior compartment walls with door gaps at z 9..11.
            WallXSplit(pm, 0, 20, 20, t, 9, 11);                     // HallA | Plant
            WallXSplit(pm, 0, 20, 30, t, 9, 11);                     // Plant | Office
            // Low ceiling beams for silhouette (no roof, see class comment).
            for (int x = 4; x <= 36; x += 8)
                pm.Box(new Vector3(x, WallH - 0.2f, 10), new Vector3(0.4f, 0.4f, 20), Palette.Slate);
            MatLib.Spawn("Building", pm.Build("building"), root, Vector3.zero);

            refs.HallA = MakeRoom(refs, "Hall A", new Vector3(10, WallH / 2, 10), new Vector3(20, WallH, 20));
            refs.Plant = MakeRoom(refs, "Plant room", new Vector3(25, WallH / 2, 10), new Vector3(10, WallH, 20));
            refs.Office = MakeRoom(refs, "Office", new Vector3(35, WallH / 2, 10), new Vector3(10, WallH, 20));

            MakeDoor(root, refs.HallA, refs.Plant, "Hall A door", new Vector3(20, 0, 10), 0f);
            MakeDoor(root, refs.Plant, refs.Office, "plant room door", new Vector3(30, 0, 10), 0f);
            MakeDoor(root, refs.Office, null, "office entrance", new Vector3(35, 0, 0), 90f);
            MakeDoor(root, refs.HallA, null, "delivery door", new Vector3(0, 0, 10), 0f);
        }

        private static Room MakeRoom(SiteRefs refs, string name, Vector3 centre, Vector3 size)
        {
            var r = new Room { Name = name, Bounds = new Bounds(centre, size) };
            refs.Rooms.Add(r);
            return r;
        }

        private static void MakeDoor(Transform root, Room a, Room b, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject("Door " + name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 1.3f, 0), new Vector3(0.15f, 2.6f, 2f), Palette.ProgramBlue);
            var leaf = MatLib.Spawn("Leaf", pm.Build("doorleaf"), go.transform, Vector3.zero);
            var trigger = go.AddComponent<BoxCollider>();
            trigger.size = new Vector3(1.2f, 2.6f, 2.6f);
            trigger.center = new Vector3(0, 1.3f, 0);
            trigger.isTrigger = true;
            var door = go.AddComponent<Door>();
            door.DoorName = name;
            door.SetLeaf(leaf.transform);
            if (a != null) a.Doors.Add(door);
            if (b != null) b.Doors.Add(door);
        }

        // Wall running along X at given z from x0..x1.
        private static void WallZ(ProcMesh pm, float x0, float x1, float z, float t)
        {
            pm.Box(new Vector3((x0 + x1) / 2, WallH / 2, z), new Vector3(x1 - x0, WallH, t), Palette.Render);
        }

        private static void WallZSplit(ProcMesh pm, float x0, float x1, float z, float t, float gap0, float gap1)
        {
            WallZ(pm, x0, gap0, z, t);
            WallZ(pm, gap1, x1, z, t);
            pm.Box(new Vector3((gap0 + gap1) / 2, WallH - 0.6f, z), new Vector3(gap1 - gap0, 1.2f, t), Palette.Render);
        }

        // Wall running along Z at given x from z0..z1.
        private static void WallX(ProcMesh pm, float z0, float z1, float x, float t)
        {
            pm.Box(new Vector3(x, WallH / 2, (z0 + z1) / 2), new Vector3(t, WallH, z1 - z0), Palette.Render);
        }

        private static void WallXSplit(ProcMesh pm, float z0, float z1, float x, float t, float gap0, float gap1)
        {
            WallX(pm, z0, gap0, x, t);
            WallX(pm, gap1, z1, x, t);
            pm.Box(new Vector3(x, WallH - 0.6f, (gap0 + gap1) / 2), new Vector3(t, 1.2f, gap1 - gap0), Palette.Render);
        }

        private static void BuildYard(Transform root, SiteRefs refs)
        {
            // Transformer.
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 1.1f, 0), new Vector3(2.4f, 2.2f, 2f), Palette.Slate);
            for (int i = 0; i < 4; i++)
                pm.Box(new Vector3(-1.0f + i * 0.66f, 1.2f, 1.05f), new Vector3(0.12f, 1.6f, 0.12f), Palette.Slate);
            MatLib.Spawn("Transformer", pm.Build("transformer"), root, refs.TransformerPos);

            // Diesel genset container with exhaust stack.
            var dm = new ProcMesh();
            dm.Box(new Vector3(0, 1.3f, 0), new Vector3(6f, 2.6f, 2.4f), Palette.ProgramBlue);
            dm.Cylinder(new Vector3(0, 3.4f, 0), 0.18f, 1.6f, 6, Palette.Ink);
            var genset = MatLib.Spawn("DieselGenset", dm.Build("genset"), root, new Vector3(46, 0, 14));
            var lever = new GameObject("DieselLever");
            lever.transform.SetParent(genset.transform, false);
            lever.transform.localPosition = new Vector3(-3.2f, 1.2f, 0);
            var lc = lever.AddComponent<BoxCollider>();
            lc.size = new Vector3(0.6f, 0.8f, 0.6f);
            lever.AddComponent<DieselLever>();
            var lpm = new ProcMesh();
            lpm.Box(Vector3.zero, new Vector3(0.15f, 0.7f, 0.15f), Palette.AlarmRed);
            MatLib.Spawn("LeverMesh", lpm.Build("lever"), lever.transform, Vector3.zero, false);

            // Wind turbine far east.
            var tm = new ProcMesh();
            tm.Cylinder(new Vector3(0, 12f, 0), 0.7f, 24f, 6, Palette.Render, 0.4f);
            for (int b = 0; b < 3; b++)
            {
                float a = b * 120f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a));
                tm.Box(new Vector3(0.6f, 24f, 0) + dir * 4.5f, new Vector3(0.25f, 0.6f + Mathf.Abs(dir.y) * 8f, 0.6f + Mathf.Abs(dir.z) * 8f), Palette.Render);
            }
            MatLib.Spawn("Turbine", tm.Build("turbine"), root, refs.TurbinePos);
        }

        private static void BuildFence(Transform root)
        {
            var pm = new ProcMesh();
            float x0 = -6, x1 = 62, z0 = -8, z1 = 26, h = 2f;
            for (float x = x0; x <= x1; x += 4f)
            {
                pm.Box(new Vector3(x, h / 2, z0), new Vector3(0.12f, h, 0.12f), Palette.ProgramBlue);
                pm.Box(new Vector3(x, h / 2, z1), new Vector3(0.12f, h, 0.12f), Palette.ProgramBlue);
            }
            for (float z = z0; z <= z1; z += 4f)
            {
                pm.Box(new Vector3(x0, h / 2, z), new Vector3(0.12f, h, 0.12f), Palette.ProgramBlue);
                pm.Box(new Vector3(x1, h / 2, z), new Vector3(0.12f, h, 0.12f), Palette.ProgramBlue);
            }
            // Rails (thin, no collider fidelity needed beyond blocking walks).
            pm.Box(new Vector3((x0 + x1) / 2, 1.9f, z0), new Vector3(x1 - x0, 0.06f, 0.06f), Palette.ProgramBlue);
            pm.Box(new Vector3((x0 + x1) / 2, 1.9f, z1), new Vector3(x1 - x0, 0.06f, 0.06f), Palette.ProgramBlue);
            pm.Box(new Vector3(x0, 1.9f, (z0 + z1) / 2), new Vector3(0.06f, 0.06f, z1 - z0), Palette.ProgramBlue);
            pm.Box(new Vector3(x1, 1.9f, (z0 + z1) / 2), new Vector3(0.06f, 0.06f, z1 - z0), Palette.ProgramBlue);
            MatLib.Spawn("Fence", pm.Build("fence"), root, Vector3.zero, false);

            // The perimeter actually blocks bodies and forklifts: invisible
            // collider walls (the visual posts alone let everything through).
            // One gate gap at the south-west corner faces the visitor car park.
            void Wall(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                go.AddComponent<BoxCollider>().size = size;
            }
            float midX = (x0 + x1) / 2f, midZ = (z0 + z1) / 2f;
            Wall("FenceWall S", new Vector3((x0 + 4f + x1) / 2f, h / 2, z0), new Vector3(x1 - x0 - 4f, h, 0.3f));
            Wall("FenceWall N", new Vector3(midX, h / 2, z1), new Vector3(x1 - x0, h, 0.3f));
            Wall("FenceWall W", new Vector3(x0, h / 2, midZ), new Vector3(0.3f, h, z1 - z0));
            Wall("FenceWall E", new Vector3(x1, h / 2, midZ), new Vector3(0.3f, h, z1 - z0));
        }

        private static void BuildTown(Transform root)
        {
            var pm = new ProcMesh();
            var rng = new Game.Sim.SimRandom(1234); // cosmetic only, never the sim RNG
            for (int i = 0; i < 14; i++)
            {
                float x = -90f - (float)rng.NextDouble() * 60f;
                float z = -15f + (float)rng.NextDouble() * 45f;
                float w = 6f + (float)rng.NextDouble() * 3f;
                float d = 5f + (float)rng.NextDouble() * 3f;
                float h = 3f + (float)rng.NextDouble() * 1.5f;
                pm.Box(new Vector3(x, h / 2, z), new Vector3(w, h, d), Palette.Render);
                // gable
                pm.Box(new Vector3(x, h + 0.5f, z), new Vector3(w * 0.9f, 1f, d * 0.9f), Palette.Earth);
                // a tree or two
                pm.Cylinder(new Vector3(x + w, 1.2f, z + d), 0.2f, 2.4f, 5, Palette.Earth);
                pm.Cylinder(new Vector3(x + w, 3.4f, z + d), 1.6f, 2.6f, 5, Palette.Foliage, 0.2f);
            }
            MatLib.Spawn("Town", pm.Build("town"), root, Vector3.zero, false);
        }

        private static void CollectSlots(SiteRefs refs)
        {
            // Hall A: two rack rows, 10 slots each.
            for (int i = 0; i < 10; i++)
            {
                refs.RackSlots.Add(new Vector3(2.5f + i * 1.8f, 0, 6f));
                refs.RackSlots.Add(new Vector3(2.5f + i * 1.8f, 0, 14f));
            }
            // Plant room: 8 unit slots.
            for (int i = 0; i < 8; i++)
                refs.PlantSlots.Add(new Vector3(21.5f + (i % 4) * 2.2f, 0, 4f + (i / 4) * 12f));
            // Solar field: 12 row slots south of the apron.
            for (int i = 0; i < 12; i++)
                refs.SolarSlots.Add(new Vector3(2f + (i % 6) * 8f, 0, -14f - (i / 6) * 6f));
        }
    }
}
