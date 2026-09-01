using System.Collections.Generic;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>A fire compartment: the unit of suppression, EPO scope and
    /// who-is-inside queries (construction-routing.md §2). Bounds are 3D, so a
    /// room on another floor is a genuinely different compartment.</summary>
    public sealed class Room
    {
        public string Name;
        public Bounds Bounds;
        public List<Door> Doors = new List<Door>();
        /// <summary>True for the compute halls: racks, cold aisles, freezing.</summary>
        public bool IsComputeHall;

        public bool Contains(Vector3 p) { return Bounds.Contains(p); }
    }

    /// <summary>Where a control is mounted: a point on a wall and the direction
    /// it faces into the room. Controls floating in mid-air, facing an
    /// arbitrary axis, cannot be found and barely can be pressed.</summary>
    public struct Mount
    {
        public Vector3 Pos;
        public float Yaw;
        public Mount(Vector3 pos, float yaw) { Pos = pos; Yaw = yaw; }
    }

    /// <summary>Everything static that the dynamic layers hang off.</summary>
    public sealed class SiteRefs
    {
        public Transform Root;

        // Ground floor compartments.
        public Room HallA, Hall2, Plant, Nshv, Ups, GasRoom, MeetMe, Workshop, Water, Office, GoodsIn, StairCore;
        // Basement compartments.
        public Room CableBasement, DieselTankRoom, WaterTanks, StairCoreB;
        // Upper floor compartments.
        public Room HallC, Ahu, StairCoreU;

        public List<Room> Rooms = new List<Room>();
        /// <summary>Every compartment that holds racks — the suppression, freeze
        /// and node-count queries iterate these instead of naming one hall.</summary>
        public List<Room> ComputeHalls = new List<Room>();

        public Vector3 CarParkSpawn = new Vector3(-22, 0.1f, -20);
        public Vector3 GensetPos = new Vector3(58, 0, -11);
        public Vector3 DieselStackTop = new Vector3(60.2f, 4.6f, -11);
        public Vector3 TransformerPos = new Vector3(64, 0, -11);
        public Vector3 TurbinePos = new Vector3(84, 0, 34);
        public Vector3 DockPos = new Vector3(-4.5f, 0, 19f);
        public Vector3 ForkliftSpawn = new Vector3(-4.5f, 0, 13f);
        public Vector3 SkipPos = new Vector3(-4.5f, 0, 26f);

        public List<Vector3> RackSlots = new List<Vector3>();      // both halls
        public List<Vector3> PlantSlots = new List<Vector3>();     // outdoor cooling yard
        public List<Vector3> SolarSlots = new List<Vector3>();     // south field

        // Wall mounts for the physical controls.
        public Mount PullHallA, PullHall2, PullPlant, PullGoodsIn;
        public Mount EpoHallA, EpoHall2, EpoPlant, EpoUps;
        public Mount Valve, Dial, Breaker, VentFan, Eyewash;
        public Vector3 CabinetPos, BulletinDesk, ContractDesk, GasBottles;

        public Transform GroundTf;
    }

    /// <summary>
    /// Builds the whole site from code: terrain, a THREE-LEVEL datacentre with
    /// the compartment programme the design documents actually call for, the
    /// outdoor plant yard, two fence perimeters with gates, the solar field and
    /// the town to the WEST — bearing 270°, exactly the TOWN_BEARING_DEG the
    /// wind sector uses, so the plume drifting over the houses is the same
    /// event the air channel charges for.
    ///
    /// The rooms are not set dressing. Each is a suppression and EPO scope
    /// (construction-routing.md §2), and each hazard the docs specify lives
    /// where a real site puts it: batteries and their hydrogen vent in the UPS
    /// room, the main breaker in the LV switch room, suppression cylinders in
    /// their own room, packaging and pallets in goods-in, cable routes and
    /// tanks in the basement. Cooling plant is OUTDOORS, because evaporative
    /// towers and chillers are (cooling-water.md).
    ///
    /// Deviation on record: the basement is an addition. The documents specify
    /// a raised-floor plenum, not a storey; a cable basement with the fuel and
    /// water plant in it is the German-practice reading of the same need.
    /// </summary>
    public static class SiteBuilder
    {
        // Level datums.
        public const float BasementY = -4.4f, GroundY = 0f, UpperY = 4.8f;
        public const float RoomH = 4.6f, BasementH = 4.2f;
        public const float T = 0.3f;          // wall thickness
        public const float SlabT = 0.4f;      // floor slab thickness

        // Footprint and the internal grid.
        public const float X0 = 0f, X1 = 60f, Z0 = 0f, Z1 = 28f;
        public const float XCore = 10f, XHall = 34f, XSpine = 44f, XEast = 52f;
        public const float ZCore = 10f, ZHallSplit = 14f, ZNshv = 19f;

        public static SiteRefs Build(Transform parent)
        {
            var refs = new SiteRefs();
            var root = new GameObject("Site").transform;
            root.SetParent(parent, false);
            refs.Root = root;

            BuildTerrain(root, refs);
            BuildBasement(root, refs);
            BuildGroundFloor(root, refs);
            BuildUpperFloor(root, refs);
            BuildStairs(root, refs);
            BuildYard(root, refs);
            BuildFences(root, refs);
            BuildTown(root);
            CollectSlots(refs);
            DefineMounts(refs);
            return refs;
        }

        // ------------------------------------------------------------------
        // Terrain — carved, because the basement has to fit inside it
        // ------------------------------------------------------------------

        private static void BuildTerrain(Transform root, SiteRefs refs)
        {
            // The ground is built as four slabs around the excavation instead of
            // one solid block: a single 500 m box with a MeshCollider makes a
            // basement physically impossible to enter.
            const float ex0 = X0 - 0.6f, ex1 = XSpine + 0.6f, ez0 = Z0 - 0.6f, ez1 = Z1 + 0.6f;
            var pm = new ProcMesh();
            GroundStrip(pm, -260f, ex0, -260f, 260f);
            GroundStrip(pm, ex1, 260f, -260f, 260f);
            GroundStrip(pm, ex0, ex1, -260f, ez0);
            GroundStrip(pm, ex0, ex1, ez1, 260f);
            var ground = MatLib.Spawn("Ground", pm.Build("ground"), root, Vector3.zero);
            refs.GroundTf = ground.transform;

            // Excavation walls, so the hole reads as a hole.
            var pit = new ProcMesh();
            pit.Box(new Vector3((ex0 + ex1) / 2f, BasementY - 0.4f, ez0 - 0.15f),
                new Vector3(ex1 - ex0, 5f, 0.3f), Palette.Concrete);
            pit.Box(new Vector3((ex0 + ex1) / 2f, BasementY - 0.4f, ez1 + 0.15f),
                new Vector3(ex1 - ex0, 5f, 0.3f), Palette.Concrete);
            pit.Box(new Vector3(ex0 - 0.15f, BasementY - 0.4f, (ez0 + ez1) / 2f),
                new Vector3(0.3f, 5f, ez1 - ez0), Palette.Concrete);
            pit.Box(new Vector3(ex1 + 0.15f, BasementY - 0.4f, (ez0 + ez1) / 2f),
                new Vector3(0.3f, 5f, ez1 - ez0), Palette.Concrete);
            MatLib.Spawn("Excavation", pit.Build("pit"), root, Vector3.zero);

            var apron = new ProcMesh();
            apron.Box(new Vector3(30, 0.02f, 4), new Vector3(104, 0.05f, 60), Palette.Concrete);
            MatLib.Spawn("Apron", apron.Build("apron"), root, Vector3.zero, false);

            var park = new ProcMesh();
            park.Box(new Vector3(-22, 0.02f, -20), new Vector3(16, 0.05f, 12), Palette.Slate);
            MatLib.Spawn("CarPark", park.Build("carpark"), root, Vector3.zero, false);
        }

        private static void GroundStrip(ProcMesh pm, float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 <= 0.01f || z1 - z0 <= 0.01f) return;
            pm.Box(new Vector3((x0 + x1) / 2f, -0.5f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, 1f, z1 - z0), Palette.Field);
        }

        // ------------------------------------------------------------------
        // Basement: cable routes, fuel and water
        // ------------------------------------------------------------------

        private static void BuildBasement(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            float y = BasementY, h = BasementH;

            SlabStrips(pm, y, X0, XSpine);                        // basement floor
            WallAlongX(pm, y, h, X0, XSpine, Z0);
            WallAlongX(pm, y, h, X0, XSpine, Z1);
            WallAlongZ(pm, y, h, Z0, Z1, X0);
            WallAlongZ(pm, y, h, Z0, Z1, XSpine);
            WallAlongXGap(pm, y, h, X0, XCore, ZCore, 4f, 6f);
            WallAlongZGap(pm, y, h, Z0, Z1, XCore, 12f, 14f);
            WallAlongZGap(pm, y, h, Z0, Z1, XHall, 6f, 8f);
            WallAlongXGap(pm, y, h, XHall, XSpine, ZHallSplit, 38f, 40f);
            MatLib.Spawn("Basement", pm.Build("basement"), root, Vector3.zero);

            refs.StairCoreB = MakeRoom(refs, "stair core (basement)", Box(X0, XCore, Z0, ZCore, y, h));
            refs.CableBasement = MakeRoom(refs, "cable basement", Box(XCore, XHall, Z0, Z1, y, h));
            refs.DieselTankRoom = MakeRoom(refs, "diesel tank room", Box(XHall, XSpine, Z0, ZHallSplit, y, h));
            refs.WaterTanks = MakeRoom(refs, "water treatment", Box(XHall, XSpine, ZHallSplit, Z1, y, h));

            MakeDoor(root, refs.StairCoreB, refs.CableBasement, "cable basement door", new Vector3(5f, y, ZCore), 90f);
            MakeDoor(root, refs.CableBasement, refs.DieselTankRoom, "tank room door", new Vector3(XHall, y, 7f), 0f);
            MakeDoor(root, refs.DieselTankRoom, refs.WaterTanks, "water plant door", new Vector3(39f, y, ZHallSplit), 90f);

            var kit = new ProcMesh();
            for (int i = 0; i < 2; i++)
                kit.Cylinder(new Vector3(37.5f + i * 4f, y + 1.5f, 6f), 1.6f, 3.0f, 10, Palette.Ink);
            kit.Box(new Vector3(41.5f, y + 0.7f, 11f), new Vector3(2f, 1.4f, 1.2f), Palette.Slate);
            kit.Cylinder(new Vector3(38f, y + 1.7f, 22f), 2.0f, 3.4f, 12, Palette.PaleBlue);
            kit.Box(new Vector3(42f, y + 0.9f, 19f), new Vector3(1.6f, 1.8f, 3f), Palette.Slate);
            for (int i = 0; i < 4; i++)
                kit.Box(new Vector3(XCore + 3.5f + i * 5.5f, y + 2.8f, (Z0 + Z1) / 2f),
                    new Vector3(0.7f, 0.12f, Z1 - Z0 - 2f), Palette.Slate);
            MatLib.Spawn("BasementPlant", kit.Build("bplant"), root, Vector3.zero);

            CeilingLight(root, new Vector3(5f, y + h - 0.5f, 5f), 13f);
            CeilingLight(root, new Vector3(16f, y + h - 0.5f, 8f), 18f);
            CeilingLight(root, new Vector3(28f, y + h - 0.5f, 20f), 18f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 7f), 14f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 22f), 14f);
        }

        // ------------------------------------------------------------------
        // Ground floor: the compartment programme
        // ------------------------------------------------------------------

        private static void BuildGroundFloor(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            float y = GroundY, h = RoomH;

            SlabStrips(pm, y, X0, X1);
            WallAlongXGap(pm, y, h, X0, X1, Z0, 54f, 56f);        // south: main entrance
            WallAlongX(pm, y, h, X0, X1, Z1);
            WallAlongZGap(pm, y, h, Z0, Z1, X0, 17f, 21f);        // west: goods door
            WallAlongZ(pm, y, h, Z0, Z1, X1);

            WallAlongXGap(pm, y, h, X0, XCore, ZCore, 4f, 6f);
            WallAlongZGap(pm, y, h, Z0, ZCore, XCore, 4f, 6f);
            WallAlongZGap(pm, y, h, ZCore, ZHallSplit, XCore, 11f, 13f);
            WallAlongZGap(pm, y, h, ZHallSplit, Z1, XCore, 21f, 23f);
            WallAlongXGap(pm, y, h, XCore, XHall, ZHallSplit, 30f, 32f);   // Hall A | Hall 2
            WallAlongZGap(pm, y, h, Z0, ZCore, XHall, 4f, 6f);             // Hall A | plant
            WallAlongZGap(pm, y, h, ZCore, ZNshv, XHall, 14f, 16f);        // Hall 2 | NSHV
            WallAlongZGap(pm, y, h, ZNshv, Z1, XHall, 23f, 25f);           // Hall 2 | UPS
            WallAlongXGap(pm, y, h, XHall, XSpine, ZCore, 38f, 40f);       // plant | NSHV
            WallAlongXGap(pm, y, h, XHall, XSpine, ZNshv, 38f, 40f);       // NSHV | UPS
            WallAlongZGap(pm, y, h, Z0, ZCore, XSpine, 4f, 6f);            // plant | workshop
            WallAlongZGap(pm, y, h, ZCore, ZNshv, XSpine, 14f, 16f);       // NSHV | meet-me
            WallAlongZGap(pm, y, h, ZNshv, Z1, XSpine, 23f, 25f);          // UPS | gas room
            WallAlongXGap(pm, y, h, XSpine, XEast, ZCore, 47f, 49f);
            WallAlongXGap(pm, y, h, XSpine, XEast, ZNshv, 47f, 49f);
            WallAlongZGap(pm, y, h, Z0, Z1, XEast, 6f, 8f);                // east block | office
            MatLib.Spawn("GroundFloor", pm.Build("ground_floor"), root, Vector3.zero);

            refs.StairCore = MakeRoom(refs, "stair core", Box(X0, XCore, Z0, ZCore, y, h));
            refs.GoodsIn = MakeRoom(refs, "goods receiving", Box(X0, XCore, ZCore, Z1, y, h));
            refs.HallA = MakeRoom(refs, "Hall A", Box(XCore, XHall, Z0, ZHallSplit, y, h), true);
            refs.Hall2 = MakeRoom(refs, "Hall 2", Box(XCore, XHall, ZHallSplit, Z1, y, h), true);
            refs.Plant = MakeRoom(refs, "plant room", Box(XHall, XSpine, Z0, ZCore, y, h));
            refs.Nshv = MakeRoom(refs, "LV switch room", Box(XHall, XSpine, ZCore, ZNshv, y, h));
            refs.Ups = MakeRoom(refs, "UPS and battery room", Box(XHall, XSpine, ZNshv, Z1, y, h));
            refs.Workshop = MakeRoom(refs, "workshop", Box(XSpine, XEast, Z0, ZCore, y, h));
            refs.MeetMe = MakeRoom(refs, "meet-me room", Box(XSpine, XEast, ZCore, ZNshv, y, h));
            refs.GasRoom = MakeRoom(refs, "suppression cylinder room", Box(XSpine, XEast, ZNshv, Z1, y, h));
            refs.Office = MakeRoom(refs, "office", Box(XEast, X1, Z0, Z1, y, h));

            MakeDoor(root, refs.StairCore, refs.GoodsIn, "stair core door", new Vector3(5f, y, ZCore), 90f);
            MakeDoor(root, refs.StairCore, refs.HallA, "Hall A stair door", new Vector3(XCore, y, 5f), 0f);
            MakeDoor(root, refs.GoodsIn, refs.HallA, "Hall A goods door", new Vector3(XCore, y, 12f), 0f);
            MakeDoor(root, refs.GoodsIn, refs.Hall2, "Hall 2 goods door", new Vector3(XCore, y, 22f), 0f);
            MakeDoor(root, refs.HallA, refs.Hall2, "hall link door", new Vector3(31f, y, ZHallSplit), 90f);
            MakeDoor(root, refs.HallA, refs.Plant, "plant room door", new Vector3(XHall, y, 5f), 0f);
            MakeDoor(root, refs.Hall2, refs.Nshv, "LV switch room door", new Vector3(XHall, y, 15f), 0f);
            MakeDoor(root, refs.Hall2, refs.Ups, "UPS room door", new Vector3(XHall, y, 24f), 0f);
            MakeDoor(root, refs.Plant, refs.Nshv, "switch room link", new Vector3(39f, y, ZCore), 90f);
            MakeDoor(root, refs.Nshv, refs.Ups, "battery room link", new Vector3(39f, y, ZNshv), 90f);
            MakeDoor(root, refs.Plant, refs.Workshop, "workshop door", new Vector3(XSpine, y, 5f), 0f);
            MakeDoor(root, refs.Nshv, refs.MeetMe, "meet-me door", new Vector3(XSpine, y, 15f), 0f);
            MakeDoor(root, refs.Ups, refs.GasRoom, "cylinder room door", new Vector3(XSpine, y, 24f), 0f);
            MakeDoor(root, refs.Workshop, refs.Office, "office door", new Vector3(XEast, y, 7f), 0f);
            MakeDoor(root, refs.Office, null, "main entrance", new Vector3(55f, y, Z0), 90f);
            MakeDoor(root, refs.GoodsIn, null, "goods door", new Vector3(X0, y, 19f), 0f);

            var kit = new ProcMesh();
            for (int i = 0; i < 3; i++)   // UPS cabinets
                kit.Box(new Vector3(36f + i * 2.4f, y + 1.05f, 22f), new Vector3(1.5f, 2.1f, 1.1f), Palette.Slate);
            for (int i = 0; i < 5; i++)   // battery strings: the hydrogen source
                kit.Box(new Vector3(36f + i * 1.6f, y + 0.85f, 26f), new Vector3(1.3f, 1.7f, 0.9f), Palette.Ink);
            for (int i = 0; i < 5; i++)   // switchgear line-up
                kit.Box(new Vector3(36f + i * 1.7f, y + 1.15f, 13f), new Vector3(1.5f, 2.3f, 1.1f), Palette.Amber);
            for (int i = 0; i < 6; i++)   // suppression cylinders
                kit.Cylinder(new Vector3(46f + i * 0.7f, y + 0.85f, 26f), 0.26f, 1.7f, 8, Palette.AlarmRed);
            kit.Box(new Vector3(47.7f, y + 1.8f, 26f), new Vector3(4.8f, 0.1f, 0.9f), Palette.Slate);
            for (int i = 0; i < 3; i++)   // meet-me carrier racks
                kit.Box(new Vector3(46f + i * 1.5f, y + 1.05f, 15f), new Vector3(0.9f, 2.1f, 1.1f), Palette.ProgramBlue);
            kit.Box(new Vector3(47f, y + 0.5f, 4f), new Vector3(3f, 1.0f, 0.9f), Palette.Earth);   // workbench
            MatLib.Spawn("RoomFittings", kit.Build("fittings"), root, Vector3.zero);

            CeilingLight(root, new Vector3(5f, y + h - 0.5f, 5f), 13f);
            CeilingLight(root, new Vector3(5f, y + h - 0.5f, 19f), 17f);
            CeilingLight(root, new Vector3(16f, y + h - 0.5f, 7f), 18f);
            CeilingLight(root, new Vector3(28f, y + h - 0.5f, 7f), 18f);
            CeilingLight(root, new Vector3(16f, y + h - 0.5f, 21f), 18f);
            CeilingLight(root, new Vector3(28f, y + h - 0.5f, 21f), 18f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 5f), 13f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 14f), 13f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 24f), 13f);
            CeilingLight(root, new Vector3(48f, y + h - 0.5f, 5f), 12f);
            CeilingLight(root, new Vector3(48f, y + h - 0.5f, 14f), 12f);
            CeilingLight(root, new Vector3(48f, y + h - 0.5f, 24f), 12f);
            CeilingLight(root, new Vector3(56f, y + h - 0.5f, 8f), 14f);
            CeilingLight(root, new Vector3(56f, y + h - 0.5f, 20f), 14f);
        }

        // ------------------------------------------------------------------
        // Upper floor: expansion shell and the air handling deck
        // ------------------------------------------------------------------

        private static void BuildUpperFloor(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            float y = UpperY, h = RoomH;

            SlabStrips(pm, y, X0, XSpine);
            WallAlongX(pm, y, h, X0, XSpine, Z0);
            WallAlongX(pm, y, h, X0, XSpine, Z1);
            WallAlongZ(pm, y, h, Z0, Z1, X0);
            WallAlongZ(pm, y, h, Z0, Z1, XSpine);
            WallAlongXGap(pm, y, h, X0, XCore, ZCore, 4f, 6f);
            WallAlongZGap(pm, y, h, Z0, Z1, XCore, 12f, 14f);
            WallAlongZGap(pm, y, h, Z0, Z1, XHall, 12f, 14f);
            MatLib.Spawn("UpperFloor", pm.Build("upper_floor"), root, Vector3.zero);

            refs.StairCoreU = MakeRoom(refs, "stair core (first floor)", Box(X0, XCore, Z0, ZCore, y, h));
            refs.HallC = MakeRoom(refs, "Hall 3 (shell)", Box(XCore, XHall, Z0, Z1, y, h));
            refs.Ahu = MakeRoom(refs, "air handling", Box(XHall, XSpine, Z0, Z1, y, h));

            MakeDoor(root, refs.StairCoreU, refs.HallC, "Hall 3 door", new Vector3(XCore, y, 13f), 0f);
            MakeDoor(root, refs.HallC, refs.Ahu, "air handling door", new Vector3(XHall, y, 13f), 0f);

            var ahu = new ProcMesh();
            for (int i = 0; i < 4; i++)
                ahu.Box(new Vector3(37f + i * 1.8f, y + 1.4f, 8f), new Vector3(1.6f, 2.8f, 4.0f), Palette.Slate);
            ahu.Box(new Vector3(39f, y + 3.4f, 20f), new Vector3(9f, 1.2f, 1.2f), Palette.PaleBlue);
            MatLib.Spawn("AirHandling", ahu.Build("ahu"), root, Vector3.zero);

            CeilingLight(root, new Vector3(5f, y + h - 0.5f, 5f), 13f);
            CeilingLight(root, new Vector3(20f, y + h - 0.5f, 8f), 20f);
            CeilingLight(root, new Vector3(20f, y + h - 0.5f, 21f), 20f);
            CeilingLight(root, new Vector3(39f, y + h - 0.5f, 14f), 18f);
        }

        /// <summary>A floor slab with the two stair voids left open.</summary>
        private static void SlabStrips(ProcMesh pm, float y, float xEnd0, float xEnd1)
        {
            SlabStrip(pm, y, xEnd0, 1.0f, Z0, Z1);
            SlabStrip(pm, y, 1.0f, 4.4f, Z0, 0.6f);
            SlabStrip(pm, y, 1.0f, 4.4f, 9.4f, Z1);
            SlabStrip(pm, y, 4.4f, 5.6f, Z0, Z1);
            SlabStrip(pm, y, 5.6f, 9.0f, Z0, 0.6f);
            SlabStrip(pm, y, 5.6f, 9.0f, 9.4f, Z1);
            SlabStrip(pm, y, 9.0f, xEnd1, Z0, Z1);
        }

        private static void SlabStrip(ProcMesh pm, float y, float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 <= 0.01f || z1 - z0 <= 0.01f) return;
            pm.Box(new Vector3((x0 + x1) / 2f, y - SlabT / 2f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, SlabT, z1 - z0), Palette.Concrete);
        }

        // ------------------------------------------------------------------
        // Stairs
        // ------------------------------------------------------------------

        private static void BuildStairs(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            // Down to the basement, descending north in the west bay.
            StairRun(pm, 1.2f, 4.2f, 9.0f, -0.42f, GroundY - 0.21f, -0.21f, 21);
            // Up to the first floor, ascending north in the east bay.
            StairRun(pm, 5.8f, 8.8f, 1.0f, 0.42f, GroundY + 0.24f, 0.24f, 20);   // 20 x 0.24 = 4.80 = UpperY
            pm.Box(new Vector3(1.2f, GroundY - 2.3f, 5f), new Vector3(0.08f, 1.0f, 8.6f), Palette.Amber);
            pm.Box(new Vector3(8.8f, GroundY + 2.5f, 5f), new Vector3(0.08f, 1.0f, 8.6f), Palette.Amber);
            MatLib.Spawn("Stairs", pm.Build("stairs"), root, Vector3.zero);
        }

        private static void StairRun(ProcMesh pm, float x0, float x1, float zStart, float dz,
            float yStart, float dy, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                float y = yStart + dy * i;
                float z = zStart + dz * i;
                // Solid treads: a capsule must never fall between two steps.
                pm.Box(new Vector3((x0 + x1) / 2f, y - 0.12f, z),
                    new Vector3(x1 - x0, 0.26f, Mathf.Abs(dz) + 0.04f), Palette.Slate);
            }
        }

        // ------------------------------------------------------------------
        // Yard: the plant that belongs outdoors, and the rest of the site
        // ------------------------------------------------------------------

        private static void BuildYard(Transform root, SiteRefs refs)
        {
            var pm = new ProcMesh();
            pm.Box(new Vector3(refs.TransformerPos.x, 1.1f, refs.TransformerPos.z),
                new Vector3(3.4f, 2.2f, 2.6f), Palette.Slate);
            pm.Box(new Vector3(refs.TransformerPos.x, 2.5f, refs.TransformerPos.z),
                new Vector3(2.8f, 0.4f, 2.0f), Palette.Ink);
            MatLib.Spawn("Transformer", pm.Build("transformer"), root, Vector3.zero);

            var dm = new ProcMesh();
            dm.Box(new Vector3(0, 1.3f, 0), new Vector3(6.4f, 2.6f, 2.8f), Palette.Amber);
            dm.Box(new Vector3(0, 2.7f, 0), new Vector3(5.2f, 0.2f, 2.2f), Palette.Ink);
            dm.Cylinder(new Vector3(2.2f, 3.6f, 0), 0.18f, 1.9f, 6, Palette.Ink);
            MatLib.Spawn("Genset", dm.Build("genset"), root, refs.GensetPos);

            var tm = new ProcMesh();
            tm.Cylinder(new Vector3(0, 12f, 0), 0.7f, 24f, 6, Palette.Render, 0.4f);
            tm.Box(new Vector3(0, 24.2f, 0), new Vector3(1.2f, 1.0f, 2.2f), Palette.Render);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                tm.Box(new Vector3(Mathf.Sin(a) * 5f, 24.2f + Mathf.Cos(a) * 5f, 1.4f),
                    new Vector3(0.5f, 9f, 0.2f), Palette.Render);
            }
            MatLib.Spawn("Turbine", tm.Build("turbine"), root, refs.TurbinePos);

            // The loading dock and the skip that the packaging is supposed to
            // end up in (certifications-audits.md: combustible loading, −8).
            var dock = new ProcMesh();
            Vector3 c = refs.DockPos;
            dock.Box(new Vector3(c.x, 0.03f, c.z), new Vector3(5.4f, 0.05f, 5.4f), Palette.Slate);
            dock.Box(new Vector3(c.x, 0.04f, c.z), new Vector3(4.6f, 0.05f, 4.6f), Palette.Earth);
            for (int i = -2; i <= 2; i++)
                dock.Box(new Vector3(c.x + i * 1.1f, 0.05f, c.z - 2.8f),
                    new Vector3(0.55f, 0.05f, 0.4f), Palette.ProgramBlue);
            MatLib.Spawn("LoadingDock", dock.Build("dock"), root, Vector3.zero, false);

            var skip = new ProcMesh();
            skip.Box(new Vector3(0, 0.9f, 0), new Vector3(2.4f, 1.8f, 5.0f), Palette.Amber);
            skip.Box(new Vector3(0, 1.75f, 0), new Vector3(2.0f, 0.1f, 4.6f), Palette.Ink);
            MatLib.Spawn("Skip", skip.Build("skip"), root, refs.SkipPos);

            // Guard post at the compound gate.
            var guard = new ProcMesh();
            guard.Box(new Vector3(0, 1.3f, 0), new Vector3(2.4f, 2.6f, 2.4f), Palette.Render);
            guard.Box(new Vector3(0, 2.7f, 0), new Vector3(2.8f, 0.2f, 2.8f), Palette.Ink);
            guard.Box(new Vector3(0, 1.5f, -1.25f), new Vector3(1.6f, 1.0f, 0.06f), Palette.PaleBlue);
            MatLib.Spawn("GuardPost", guard.Build("guard"), root, new Vector3(24f, 0, -16f));
        }

        // ------------------------------------------------------------------
        // Fences
        // ------------------------------------------------------------------

        private static void BuildFences(Transform root, SiteRefs refs)
        {
            // Outer site boundary: solar field and turbine inside, visitor car
            // park outside, one gate on the west facing the car park.
            Fence(root, "Outer", -12f, 92f, -34f, 42f, 2.2f, 'W', -24f, -16f, Palette.Slate);
            // Inner compound: the datacentre itself, gated from the yard.
            Fence(root, "Compound", -10f, 68f, -20f, 34f, 2.6f, 'S', 20f, 28f, Palette.ProgramBlue);
        }

        private static void Fence(Transform root, string name, float x0, float x1,
            float z0, float z1, float h, char gateSide, float gate0, float gate1, Color colour)
        {
            var posts = new ProcMesh();
            var mesh = new ProcMesh();
            Color m = colour; m.a = 0.28f;
            const float span = 4f;

            bool IsGate(char side, float a0, float a1)
            {
                return gateSide == side && a1 > gate0 + 0.01f && a0 < gate1 - 0.01f;
            }

            void Panel(bool alongX, float a0, float a1, float fixedCoord, char side)
            {
                if (IsGate(side, a0, a1)) return;
                float mid = (a0 + a1) / 2f;
                if (alongX)
                {
                    posts.Box(new Vector3(mid, h - 0.06f, fixedCoord), new Vector3(a1 - a0, 0.08f, 0.08f), colour);
                    mesh.Box(new Vector3(mid, h / 2f, fixedCoord), new Vector3(a1 - a0 - 0.1f, h - 0.25f, 0.04f), m);
                    FenceWall(root, name, new Vector3(mid, h / 2f, fixedCoord), new Vector3(a1 - a0, h, 0.3f));
                }
                else
                {
                    posts.Box(new Vector3(fixedCoord, h - 0.06f, mid), new Vector3(0.08f, 0.08f, a1 - a0), colour);
                    mesh.Box(new Vector3(fixedCoord, h / 2f, mid), new Vector3(0.04f, h - 0.25f, a1 - a0 - 0.1f), m);
                    FenceWall(root, name, new Vector3(fixedCoord, h / 2f, mid), new Vector3(0.3f, h, a1 - a0));
                }
            }

            for (float x = x0; x < x1 - 0.01f; x += span)
            {
                float xe = Mathf.Min(x + span, x1);
                Panel(true, x, xe, z0, 'S');
                Panel(true, x, xe, z1, 'N');
            }
            for (float z = z0; z < z1 - 0.01f; z += span)
            {
                float ze = Mathf.Min(z + span, z1);
                Panel(false, z, ze, x0, 'W');
                Panel(false, z, ze, x1, 'E');
            }
            for (float x = x0; x <= x1 + 0.01f; x += span)
            {
                posts.Box(new Vector3(x, h / 2f, z0), new Vector3(0.12f, h, 0.12f), colour);
                posts.Box(new Vector3(x, h / 2f, z1), new Vector3(0.12f, h, 0.12f), colour);
            }
            for (float z = z0; z <= z1 + 0.01f; z += span)
            {
                posts.Box(new Vector3(x0, h / 2f, z), new Vector3(0.12f, h, 0.12f), colour);
                posts.Box(new Vector3(x1, h / 2f, z), new Vector3(0.12f, h, 0.12f), colour);
            }

            // Amber caps on the gate posts: this is the way through.
            Vector3 ga, gb;
            switch (gateSide)
            {
                case 'W': ga = new Vector3(x0, h + 0.15f, gate0); gb = new Vector3(x0, h + 0.15f, gate1); break;
                case 'E': ga = new Vector3(x1, h + 0.15f, gate0); gb = new Vector3(x1, h + 0.15f, gate1); break;
                case 'N': ga = new Vector3(gate0, h + 0.15f, z1); gb = new Vector3(gate1, h + 0.15f, z1); break;
                default: ga = new Vector3(gate0, h + 0.15f, z0); gb = new Vector3(gate1, h + 0.15f, z0); break;
            }
            posts.Box(ga, new Vector3(0.24f, 0.3f, 0.24f), Palette.Amber);
            posts.Box(gb, new Vector3(0.24f, 0.3f, 0.24f), Palette.Amber);

            MatLib.Spawn("Fence" + name, posts.Build("fence"), root, Vector3.zero, false);
            MatLib.Spawn("Fence" + name + "Mesh", mesh.Build("fenceMesh"), root, Vector3.zero, false, true);
        }

        private static void FenceWall(Transform root, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject("Fence" + name + "Section");
            go.transform.SetParent(root, false);
            go.transform.localPosition = centre;
            go.AddComponent<BoxCollider>().size = size;
            go.AddComponent<FenceSection>();
        }

        // ------------------------------------------------------------------
        // Town, slots, mounts
        // ------------------------------------------------------------------

        private static void BuildTown(Transform root)
        {
            var pm = new ProcMesh();
            var rnd = new System.Random(1234);
            for (int i = 0; i < 14; i++)
            {
                float x = -46f - (i % 4) * 11f;
                float z = -26f + (i / 4) * 14f + (float)rnd.NextDouble() * 4f;
                float w = 6f + (float)rnd.NextDouble() * 3f;
                float d = 5f + (float)rnd.NextDouble() * 3f;
                pm.Box(new Vector3(x, 1.6f, z), new Vector3(w, 3.2f, d), Palette.Render);
                pm.Box(new Vector3(x, 3.6f, z), new Vector3(w + 0.4f, 0.9f, d + 0.4f), Palette.Earth);
                pm.Cylinder(new Vector3(x + w, 1.2f, z + d), 0.2f, 2.4f, 5, Palette.Earth);
                pm.Cylinder(new Vector3(x + w, 3.4f, z + d), 1.6f, 2.6f, 5, Palette.Foliage, 0.2f);
            }
            MatLib.Spawn("Town", pm.Build("town"), root, Vector3.zero);
        }

        private static void CollectSlots(SiteRefs refs)
        {
            // Hall A then Hall 2, filled in order: the delivery flow addresses
            // slots by installed node count, so the order IS the fill order.
            for (int i = 0; i < 8; i++)
            {
                refs.RackSlots.Add(new Vector3(13f + i * 2.4f, 0, 5f));
                refs.RackSlots.Add(new Vector3(13f + i * 2.4f, 0, 11f));
            }
            for (int i = 0; i < 8; i++)
            {
                refs.RackSlots.Add(new Vector3(13f + i * 2.4f, 0, 18f));
                refs.RackSlots.Add(new Vector3(13f + i * 2.4f, 0, 24f));
            }
            // Cooling plant is OUTDOOR plant (cooling-water.md): towers and
            // chillers stand in the yard south of the building, not in a room.
            for (int i = 0; i < 8; i++)
                refs.PlantSlots.Add(new Vector3(8f + (i % 4) * 7f, 0, -8f - (i / 4) * 7f));
            for (int i = 0; i < 12; i++)
                refs.SolarSlots.Add(new Vector3(4f + (i % 6) * 9f, 0, -26f - (i / 6) * 6f));
        }

        /// <summary>Every control mounted ON a wall, facing into its room.</summary>
        private static void DefineMounts(SiteRefs refs)
        {
            float y = GroundY;
            const float d = 0.18f;   // how far the box stands off the wall face

            refs.PullHallA = new Mount(new Vector3(XHall - T / 2f - d, y + 1.4f, 9f), 270f);
            refs.PullHall2 = new Mount(new Vector3(XHall - T / 2f - d, y + 1.4f, 21f), 270f);
            refs.PullPlant = new Mount(new Vector3(XHall + T / 2f + d, y + 1.4f, 3f), 90f);
            refs.PullGoodsIn = new Mount(new Vector3(XCore - T / 2f - d, y + 1.4f, 25f), 270f);

            refs.EpoHallA = new Mount(new Vector3(XHall - T / 2f - d, y + 1.25f, 7f), 270f);
            refs.EpoHall2 = new Mount(new Vector3(XHall - T / 2f - d, y + 1.25f, 17f), 270f);
            refs.EpoPlant = new Mount(new Vector3(XHall + T / 2f + d, y + 1.25f, 7.5f), 90f);
            refs.EpoUps = new Mount(new Vector3(XHall + T / 2f + d, y + 1.25f, 21f), 90f);

            // Cooling controls in the plant room, on the south wall.
            refs.Valve = new Mount(new Vector3(37f, y + 1.15f, Z0 + T / 2f + d), 0f);
            refs.Dial = new Mount(new Vector3(40f, y + 1.4f, Z0 + T / 2f + d), 0f);
            // Main breaker in the LV switch room; hydrogen vent switch in the
            // battery room; eyewash beside it, as the docs specify.
            refs.Breaker = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.4f, 14f), 90f);
            refs.VentFan = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.4f, 22f), 90f);
            refs.Eyewash = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.0f, 26f), 90f);

            refs.CabinetPos = new Vector3(2.0f, y, 24f);      // goods receiving
            refs.GasBottles = new Vector3(47.7f, y, 26f);     // cylinder room
            refs.BulletinDesk = new Vector3(56f, y, 5f);      // office
            refs.ContractDesk = new Vector3(56f, y, 10f);
        }

        // ------------------------------------------------------------------
        // Geometry helpers
        // ------------------------------------------------------------------

        private static Bounds Box(float x0, float x1, float z0, float z1, float y, float h)
        {
            return new Bounds(new Vector3((x0 + x1) / 2f, y + h / 2f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, h, z1 - z0));
        }

        private static Room MakeRoom(SiteRefs refs, string name, Bounds b, bool computeHall = false)
        {
            var r = new Room { Name = name, Bounds = b, IsComputeHall = computeHall };
            refs.Rooms.Add(r);
            if (computeHall) refs.ComputeHalls.Add(r);
            return r;
        }

        private static void MakeDoor(Transform root, Room a, Room b, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject("Door " + name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 1.25f, 0), new Vector3(0.14f, 2.5f, 1.9f), Palette.ProgramBlue);
            var leaf = MatLib.Spawn("Leaf", pm.Build("doorleaf"), go.transform, Vector3.zero);
            // A TIGHT trigger. The old 1.2 x 2.6 x 2.6 volume protruded 0.6 m
            // into both rooms and intercepted the interaction probe for every
            // control anywhere near a doorway.
            var trigger = go.AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.45f, 2.4f, 1.9f);
            trigger.center = new Vector3(0, 1.25f, 0);
            trigger.isTrigger = true;
            var door = go.AddComponent<Door>();
            door.DoorName = name;
            door.SetLeaf(leaf.transform);
            if (a != null) a.Doors.Add(door);
            if (b != null) b.Doors.Add(door);
        }

        private static void CeilingLight(Transform root, Vector3 pos, float range)
        {
            var go = new GameObject("CeilingLight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = 1.1f;
            l.color = new Color(0.95f, 0.96f, 1f);
        }

        private static void WallAlongX(ProcMesh pm, float y, float h, float x0, float x1, float z)
        {
            if (x1 - x0 <= 0.01f) return;
            pm.Box(new Vector3((x0 + x1) / 2f, y + h / 2f, z), new Vector3(x1 - x0, h, T), Palette.Render);
        }

        private static void WallAlongXGap(ProcMesh pm, float y, float h, float x0, float x1, float z,
            float gap0, float gap1)
        {
            WallAlongX(pm, y, h, x0, gap0, z);
            WallAlongX(pm, y, h, gap1, x1, z);
            pm.Box(new Vector3((gap0 + gap1) / 2f, y + h - 0.75f, z),
                new Vector3(gap1 - gap0, 1.5f, T), Palette.Render);
        }

        private static void WallAlongZ(ProcMesh pm, float y, float h, float z0, float z1, float x)
        {
            if (z1 - z0 <= 0.01f) return;
            pm.Box(new Vector3(x, y + h / 2f, (z0 + z1) / 2f), new Vector3(T, h, z1 - z0), Palette.Render);
        }

        private static void WallAlongZGap(ProcMesh pm, float y, float h, float z0, float z1, float x,
            float gap0, float gap1)
        {
            WallAlongZ(pm, y, h, z0, gap0, x);
            WallAlongZ(pm, y, h, gap1, z1, x);
            pm.Box(new Vector3(x, y + h - 0.75f, (gap0 + gap1) / 2f),
                new Vector3(T, 1.5f, gap1 - gap0), Palette.Render);
        }
    }
}
