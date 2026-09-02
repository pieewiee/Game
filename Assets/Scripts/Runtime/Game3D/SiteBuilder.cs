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
        public Room HallA, Hall2, Plant, Nshv, Ups, GasRoom, MeetMe, Workshop, Office, GoodsIn, StairCore;
        // Basement compartments.
        public Room CableBasement, DieselTankRoom, WaterTanks, StairCoreB, BasementStore;
        // Upper floor compartments.
        public Room HallC, Ahu, StairCoreU, WestBayU;

        public List<Room> Rooms = new List<Room>();
        /// <summary>Every compartment that holds racks — the suppression, freeze
        /// and node-count queries iterate these instead of naming one hall.</summary>
        public List<Room> ComputeHalls = new List<Room>();

        /// <summary>Where a body starts and respawns: on the public pavement
        /// outside the pedestrian gate, facing the site (yaw 0 = +z).</summary>
        public Vector3 StreetSpawn = new Vector3(55f, 0.3f, -24f);
        // Genset and transformer stand in the east strip, off the line from
        // the pedestrian gate to the door; the stack top follows the genset.
        public Vector3 GensetPos = new Vector3(65, 0, -6);
        public Vector3 DieselStackTop { get { return GensetPos + new Vector3(2.2f, 4.6f, 0); } }
        public Vector3 TransformerPos = new Vector3(65, 0, 6);
        public Vector3 TurbinePos = new Vector3(60, 0, 42);

        // ---- Outside-the-fence anchors ------------------------------------
        // Filled by SiteBuilder (fence, gates, turbine) and CityBuilder (roads,
        // blocks, routes); read by the spawn, the NPC presence layer and the
        // weather pass. Positions are surface heights unless stated.
        public Vector3 PedestrianGate = new Vector3(55f, 0, -20f);   // gap centre on the fence line
        public Vector3 VehicleGate = new Vector3(-12f, 0, 19f);       // dock axis, west line
        public Vector3 SiteCentre = new Vector3(30f, 0, 15f);
        // Kerb edge of the outer pavement (z -35..-32): the pavement's centre
        // line is the residents' walking route and a stander keeps 0.8 m off it.
        public Vector3 BusStopPos = new Vector3(30f, 0.15f, -32.6f);
        /// <summary>The single perimeter fence rectangle (x0..x1, z0..z1).</summary>
        public float FenceX0 = -12f, FenceX1 = 70f, FenceZ0 = -20f, FenceZ1 = 50f;
        /// <summary>No silhouette stands or walks within this of a player rig.</summary>
        public const float ResidentClearRadius = 6f;
        /// <summary>Bearings (0 = +z, 90 = +x) of the residential blocks the
        /// night glow and the wind read; the dense west block is first.</summary>
        public List<float> TownBearingsDeg = new List<float>();
        /// <summary>Open polylines on the OUTER pavements (never inside the
        /// fence, never on the player's pavement) that distant figures ping-pong along.</summary>
        public List<Vector3[]> ResidentRoutes = new List<Vector3[]>();
        /// <summary>Standing spots on the verge outside the fence, facing SiteCentre.</summary>
        public List<Vector3> FenceLineSpots = new List<Vector3>();
        /// <summary>Picket arc outside the VEHICLE gate (deliveries are turned away there).</summary>
        public List<Vector3> GateProtestSpots = new List<Vector3>();
        /// <summary>Footprints of city blocks and furniture, for route validation.</summary>
        public List<Bounds> TownObstacles = new List<Bounds>();
        /// <summary>Unlit amber glow meshes; the weather/night pass toggles .enabled.</summary>
        public Renderer StreetLampHeads;
        public Renderer TownWindowsEarly, TownWindowsLate;
        /// <summary>Rotor spins about its LOCAL z; the nacelle (its parent) yaws into the wind.</summary>
        public Transform TurbineRotor, TurbineNacelle;

        public bool InsideFence(Vector3 p)
        {
            return p.x >= FenceX0 && p.x <= FenceX1 && p.z >= FenceZ0 && p.z <= FenceZ1;
        }
        public Vector3 DockPos = new Vector3(-4.5f, 0, 19f);
        public Vector3 ForkliftSpawn = new Vector3(-4.5f, 0.1f, 13f);   // drops onto the apron
        public Vector3 SkipPos = new Vector3(-4.5f, 0, 26f);

        public List<Vector3> RackSlots = new List<Vector3>();      // both halls
        public List<Vector3> PlantSlots = new List<Vector3>();     // outdoor cooling yard
        public List<Vector3> SolarSlots = new List<Vector3>();     // north field

        // Wall mounts for the physical controls.
        public Mount PullHallA, PullHall2, PullPlant, PullGoodsIn;
        public Mount EpoHallA, EpoHall2, EpoPlant, EpoUps;
        public Mount Valve, Dial, Breaker, VentFan, Eyewash, DieselStart;
        public Vector3 CabinetPos, BulletinDesk, ContractDesk, GasBottles;

        public Transform GroundTf;
    }

    /// <summary>
    /// Builds the whole site from code: terrain, a THREE-LEVEL datacentre with
    /// the compartment programme the design documents actually call for, the
    /// outdoor plant yard, the solar field and the turbine, all inside ONE blue
    /// perimeter fence with a pedestrian gate on the entrance axis and a
    /// vehicle gate on the dock axis. The datacentre stands in the MIDDLE of
    /// town: the ring road and the four city blocks around the fence are
    /// CityBuilder's, with the dense residential block to the WEST — bearing
    /// 270°, exactly the TOWN_BEARING_DEG the wind sector uses, so the plume
    /// drifting over the houses is the same event the air channel charges for.
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
        public const float RoofY = UpperY + RoomH;   // the envelope closes here
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
            CityBuilder.Build(root, refs);
            CollectSlots(refs);
            DefineMounts(refs);
            return refs;
        }

        // ------------------------------------------------------------------
        // Terrain — carved, because the basement has to fit inside it
        // ------------------------------------------------------------------

        private static void BuildTerrain(Transform root, SiteRefs refs)
        {
            // The ground is built as four slabs around the building footprint
            // instead of one solid block: a single 500 m box with a MeshCollider
            // makes a basement physically impossible to enter. The strips butt
            // FLUSH against the slab edge — any daylight between the two is a
            // bottomless slot, and the goods doorway sat right on top of one.
            var pm = new ProcMesh();
            GroundStrip(pm, -260f, X0, -260f, 260f);
            GroundStrip(pm, X1, 260f, -260f, 260f);
            GroundStrip(pm, X0, X1, -260f, Z0);
            GroundStrip(pm, X0, X1, Z1, 260f);
            var ground = MatLib.Spawn("Ground", pm.Build("ground"), root, Vector3.zero);
            refs.GroundTf = ground.transform;

            // Excavation lining around the basement footprint, so the hole
            // reads as a hole from below the terrain skin.
            const float px0 = X0, px1 = XSpine, pz0 = Z0, pz1 = Z1;
            var pit = new ProcMesh();
            pit.Box(new Vector3((px0 + px1) / 2f, BasementY - 0.4f, pz0 - 0.15f),
                new Vector3(px1 - px0 + 0.6f, 5f, 0.3f), Palette.Concrete);
            pit.Box(new Vector3((px0 + px1) / 2f, BasementY - 0.4f, pz1 + 0.15f),
                new Vector3(px1 - px0 + 0.6f, 5f, 0.3f), Palette.Concrete);
            pit.Box(new Vector3(px0 - 0.15f, BasementY - 0.4f, (pz0 + pz1) / 2f),
                new Vector3(0.3f, 5f, pz1 - pz0), Palette.Concrete);
            pit.Box(new Vector3(px1 + 0.15f, BasementY - 0.4f, (pz0 + pz1) / 2f),
                new Vector3(0.3f, 5f, pz1 - pz0), Palette.Concrete);
            MatLib.Spawn("Excavation", pit.Build("pit"), root, Vector3.zero);

            // The apron is four strips AROUND the building. One sheet under the
            // whole site sat 4.5 cm above every interior floor and painted over
            // the stair voids.
            // It stops at the fence line: outside it is the city's verge and
            // pavement, inside north of z 34 the solar field stands on grass.
            var apron = new ProcMesh();
            ApronStrip(apron, -12f, X0, -20f, 34f);    // west: dock, forklift bay
            ApronStrip(apron, X1, 70f, -20f, 34f);     // east: genset and transformer
            ApronStrip(apron, X0, X1, -20f, Z0);       // south: the cooling yard
            ApronStrip(apron, X0, X1, Z1, 34f);        // north
            // It carries a collider: it stands 4.5 cm proud of the terrain
            // datum, and a body walking on the terrain collider sank into it.
            MatLib.Spawn("Apron", apron.Build("apron"), root, Vector3.zero);
        }

        private static void ApronStrip(ProcMesh pm, float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 <= 0.01f || z1 - z0 <= 0.01f) return;
            pm.Box(new Vector3((x0 + x1) / 2f, 0.02f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, 0.05f, z1 - z0), Palette.Concrete);
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

            // The lowest slab is SOLID. Nothing descends below it, and a stair
            // void cut here is a shaft with no bottom.
            SlabStrip(pm, y, X0, XSpine, Z0, Z1);
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
            refs.BasementStore = MakeRoom(refs, "basement store", Box(X0, XCore, ZCore, Z1, y, h));
            refs.CableBasement = MakeRoom(refs, "cable basement", Box(XCore, XHall, Z0, Z1, y, h));
            refs.DieselTankRoom = MakeRoom(refs, "diesel tank room", Box(XHall, XSpine, Z0, ZHallSplit, y, h));
            refs.WaterTanks = MakeRoom(refs, "water treatment", Box(XHall, XSpine, ZHallSplit, Z1, y, h));

            MakeDoor(root, refs.StairCoreB, refs.BasementStore, "basement store door", new Vector3(5f, y, ZCore), 90f);
            MakeDoor(root, refs.BasementStore, refs.CableBasement, "cable basement door", new Vector3(XCore, y, 13f), 0f);
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

            SlabStrips(pm, y, X0, X1, cutDownBay: true, cutUpBay: false);
            WallAlongXGap(pm, y, h, X0, X1, Z0, 54f, 56f);        // south: main entrance
            WallAlongX(pm, y, h, X0, X1, Z1);
            WallAlongZGap(pm, y, h, Z0, Z1, X0, 17f, 21f);        // west: goods door
            WallAlongZ(pm, y, h, Z0, Z1, X1);

            WallAlongXGap(pm, y, h, X0, XCore, ZCore, 4f, 6f);
            WallAlongZGap(pm, y, h, Z0, ZCore, XCore, 5f, 7f);    // north of the up flight's low treads
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
            MakeDoor(root, refs.StairCore, refs.HallA, "Hall A stair door", new Vector3(XCore, y, 6f), 0f);
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
            MakeDoor(root, refs.Workshop, refs.MeetMe, "meet-me service door", new Vector3(48f, y, ZCore), 90f);
            MakeDoor(root, refs.MeetMe, refs.GasRoom, "cylinder room service door", new Vector3(48f, y, ZNshv), 90f);
            MakeDoor(root, refs.Workshop, refs.Office, "office door", new Vector3(XEast, y, 7f), 0f);
            MakeDoor(root, refs.Office, null, "main entrance", new Vector3(55f, y, Z0), 90f);
            MakeDoor(root, refs.GoodsIn, null, "goods door", new Vector3(X0, y, 19f), 0f, 4.0f, true);

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

            SlabStrips(pm, y, X0, XSpine, cutDownBay: false, cutUpBay: true);
            // The ceiling over the single-storey east block and the roof over
            // the upper storey. The building was an open-topped box without
            // these two, with the sun on the switchgear at noon.
            SlabStrip(pm, UpperY, XSpine, X1, Z0, Z1);
            SlabStrip(pm, RoofY, X0, XSpine, Z0, Z1);
            WallAlongX(pm, y, h, X0, XSpine, Z0);
            WallAlongX(pm, y, h, X0, XSpine, Z1);
            WallAlongZ(pm, y, h, Z0, Z1, X0);
            WallAlongZ(pm, y, h, Z0, Z1, XSpine);
            WallAlongXGap(pm, y, h, X0, XCore, ZCore, 4f, 6f);
            WallAlongZGap(pm, y, h, Z0, Z1, XCore, 12f, 14f);
            WallAlongZGap(pm, y, h, Z0, Z1, XHall, 12f, 14f);
            MatLib.Spawn("UpperFloor", pm.Build("upper_floor"), root, Vector3.zero);

            refs.StairCoreU = MakeRoom(refs, "stair core (first floor)", Box(X0, XCore, Z0, ZCore, y, h));
            refs.WestBayU = MakeRoom(refs, "west bay (first floor)", Box(X0, XCore, ZCore, Z1, y, h));
            refs.HallC = MakeRoom(refs, "Hall 3 (shell)", Box(XCore, XHall, Z0, Z1, y, h));
            refs.Ahu = MakeRoom(refs, "air handling", Box(XHall, XSpine, Z0, Z1, y, h));

            MakeDoor(root, refs.StairCoreU, refs.WestBayU, "west bay door", new Vector3(5f, y, ZCore), 90f);
            MakeDoor(root, refs.WestBayU, refs.HallC, "Hall 3 door", new Vector3(XCore, y, 13f), 0f);
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

        // The stair bays: the down flight in the west bay, the up flight in
        // the east bay, both voids running z VoidZ0..VoidZ1 so the walkways at
        // either end are 0.95 m — wider than the 0.64 m capsule, which the old
        // 0.45 m ledges were not.
        public const float DownBayX0 = 1.0f, DownBayX1 = 4.4f, UpBayX0 = 5.6f, UpBayX1 = 9.0f;
        public const float VoidZ0 = 1.1f, VoidZ1 = 8.9f;

        /// <summary>A floor slab, with a stair void cut only where a flight
        /// actually passes through THIS slab. A void in a slab nothing descends
        /// through is a hole in the floor.</summary>
        private static void SlabStrips(ProcMesh pm, float y, float xEnd0, float xEnd1,
            bool cutDownBay, bool cutUpBay)
        {
            SlabStrip(pm, y, xEnd0, DownBayX0, Z0, Z1);
            if (cutDownBay)
            {
                SlabStrip(pm, y, DownBayX0, DownBayX1, Z0, VoidZ0);
                SlabStrip(pm, y, DownBayX0, DownBayX1, VoidZ1, Z1);
            }
            else SlabStrip(pm, y, DownBayX0, DownBayX1, Z0, Z1);
            SlabStrip(pm, y, DownBayX1, UpBayX0, Z0, Z1);
            if (cutUpBay)
            {
                SlabStrip(pm, y, UpBayX0, UpBayX1, Z0, VoidZ0);
                SlabStrip(pm, y, UpBayX0, UpBayX1, VoidZ1, Z1);
            }
            else SlabStrip(pm, y, UpBayX0, UpBayX1, Z0, Z1);
            SlabStrip(pm, y, UpBayX1, xEnd1, Z0, Z1);
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
            // Down to the basement, descending southward in the west bay:
            // 21 risers of 0.21 = 4.41, the last tread flush with the basement
            // slab; a 0.36 going keeps the whole run inside the 7.8 m void.
            StairRun(pm, 1.2f, 4.2f, 8.65f, -0.36f, GroundY - 0.21f, -0.21f, 21);
            // Up to the first floor, ascending northward in the east bay:
            // 20 risers of 0.24 = 4.80 = UpperY; a 0.39 going lands the top
            // tread on the slab edge at VoidZ1.
            StairRun(pm, 5.8f, 8.8f, 1.36f, 0.39f, GroundY + 0.24f, 0.24f, 20);

            // Balustrades on the three closed sides of each void, on the floor
            // the void is cut in. The open side is the one the flight arrives at.
            Balustrade(pm, GroundY, DownBayX0, DownBayX0, VoidZ0, VoidZ1);
            Balustrade(pm, GroundY, DownBayX1, DownBayX1, VoidZ0, VoidZ1);
            Balustrade(pm, GroundY, DownBayX0, DownBayX1, VoidZ0, VoidZ0);
            Balustrade(pm, UpperY, UpBayX0, UpBayX0, VoidZ0, VoidZ1);
            Balustrade(pm, UpperY, UpBayX1, UpBayX1, VoidZ0, VoidZ1);
            Balustrade(pm, UpperY, UpBayX0, UpBayX1, VoidZ0, VoidZ0);
            MatLib.Spawn("Stairs", pm.Build("stairs"), root, Vector3.zero);
        }

        /// <summary>Two rails and posts along one edge of a void. The gaps are
        /// under 0.5 m, so no capsule fits through; the mesh carries the
        /// collider, so nothing walks off the edge either.</summary>
        private static void Balustrade(ProcMesh pm, float floorY, float x0, float x1, float z0, float z1)
        {
            const float railH = 1.1f, t = 0.08f;
            bool alongX = x1 - x0 > z1 - z0;
            float len = alongX ? x1 - x0 : z1 - z0;
            var c = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            Vector3 rail = alongX ? new Vector3(len, 0.06f, t) : new Vector3(t, 0.06f, len);
            pm.Box(c + Vector3.up * (floorY + railH), rail, Palette.Amber);
            pm.Box(c + Vector3.up * (floorY + railH * 0.5f), rail, Palette.Amber);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 1.6f) + 1);
            for (int i = 0; i < posts; i++)
            {
                float a = -len / 2f + len * i / (posts - 1);
                Vector3 p = c + (alongX ? new Vector3(a, 0f, 0f) : new Vector3(0f, 0f, a));
                pm.Box(p + Vector3.up * (floorY + railH / 2f), new Vector3(t, railH, t), Palette.Amber);
            }
        }

        /// <summary>A flight of solid treads with a handrail on both edges —
        /// both sides of a flight through a void are a drop. The rails follow
        /// the pitch of the flight, nosing to nosing, at hand height: a level
        /// bar across a flight that climbs 4.8 m cut through the middle treads
        /// and floated at both ends.</summary>
        private static void StairRun(ProcMesh pm, float x0, float x1, float zStart, float dz,
            float yStart, float dy, int steps)
        {
            const float railH = 0.9f, t = 0.06f;
            float[] rails = { x0 + t, x1 - t };
            for (int i = 0; i < steps; i++)
            {
                float y = yStart + dy * i;
                float z = zStart + dz * i;
                // Solid treads: a capsule must never fall between two steps.
                pm.Box(new Vector3((x0 + x1) / 2f, y - 0.12f, z),
                    new Vector3(x1 - x0, 0.26f, Mathf.Abs(dz) + 0.04f), Palette.Slate);
                if (i % 4 == 0 || i == steps - 1)
                    foreach (float railX in rails)
                        pm.Box(new Vector3(railX, y + railH / 2f, z), new Vector3(t, railH, t), Palette.Amber);
            }
            float yEnd = yStart + dy * (steps - 1), zEnd = zStart + dz * (steps - 1);
            float over = Mathf.Sign(dz) * 0.2f;
            foreach (float railX in rails)
                pm.Beam(new Vector3(railX, yStart + railH, zStart - over),
                    new Vector3(railX, yEnd + railH, zEnd + over), t, Palette.Amber);
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

            BuildTurbine(root, refs);

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

            // Guard post BESIDE the pedestrian gate, west of it, window on the
            // gateway. In the middle of it, it split the gate into two lanes;
            // its east face (52.5) stays clear of the gate post at 53.7.
            var guard = new ProcMesh();
            guard.Box(new Vector3(0, 1.3f, 0), new Vector3(2.4f, 2.6f, 2.4f), Palette.Render);
            guard.Box(new Vector3(0, 2.7f, 0), new Vector3(2.8f, 0.2f, 2.8f), Palette.Ink);
            guard.Box(new Vector3(-1.25f, 1.5f, 0), new Vector3(0.06f, 1.0f, 1.6f), Palette.PaleBlue);
            var hut = MatLib.Spawn("GuardPost", guard.Build("guard"), root, new Vector3(51.3f, 0, -18.3f));
            hut.transform.rotation = Quaternion.Euler(0, 180f, 0);   // window (local -x) faces the gate

            BuildGateKit(root, refs);
        }

        /// <summary>The walk from the pavement to the door, made obvious: a
        /// painted strip from the pedestrian gate to the main entrance and the
        /// gate leaf standing open against the east post. Neither collides —
        /// the strip is paint, and a leaf with a collider is a gate.</summary>
        private static void BuildGateKit(Transform root, SiteRefs refs)
        {
            var kit = new ProcMesh();
            float x = refs.PedestrianGate.x, z0 = refs.FenceZ0;
            // Bottom face 5 mm INTO the apron (top 0.045): coplanar it z-fights.
            kit.Box(new Vector3(x, 0.05f, (z0 + Z0) / 2f), new Vector3(2.6f, 0.02f, Z0 - z0 - 0.3f), Palette.Render);
            MatLib.Spawn("Walkway", kit.Build("walkway"), root, Vector3.zero, false);

            // The leaf's east face sits 1 cm inside the east gate post (face
            // 56.24): a leaf with air between it and its post is a loose panel,
            // and this is the first thing the spawn looks at.
            var leaf = new ProcMesh();
            leaf.Box(new Vector3(x + 1.22f, 0.6f, z0 + 0.6f), new Vector3(0.06f, 1.2f, 1.2f), Palette.ProgramBlue);
            MatLib.Spawn("GateLeaf", leaf.Build("gateleaf"), root, Vector3.zero, false);
        }

        /// <summary>Tower → Nacelle → Rotor → three Blades, as a transform
        /// chain: the rotor plane is the rotor's local x-y and it spins about
        /// its local z, the nacelle yaws about its y. Boxes are axis-aligned, so
        /// each blade is its own child, rotated by its transform. The blades
        /// used to be three loose boxes floating beside the tower.</summary>
        private static void BuildTurbine(Transform root, SiteRefs refs)
        {
            var tower = new ProcMesh();
            tower.Cylinder(new Vector3(0, 12f, 0), 0.7f, 24f, 6, Palette.Render, 0.4f);
            var turbine = MatLib.Spawn("Turbine", tower.Build("turbine"), root, refs.TurbinePos);

            var nm = new ProcMesh();
            nm.Box(Vector3.zero, new Vector3(1.2f, 1.0f, 2.2f), Palette.Slate);
            var nacelle = MatLib.Spawn("Nacelle", nm.Build("nacelle"), turbine.transform, new Vector3(0, 24.2f, 0), false);

            // The hub starts 0.1 m proud of the nacelle face (1.1): coplanar
            // there, its corners would poke through once it turns. A shaft
            // bridges the gap, buried 0.1 m in both parts (nacelle-local z
            // 1.0..1.3); its corners at r 0.35 stay inside the 1.0 m nacelle
            // section while it spins.
            var hm = new ProcMesh();
            hm.Box(new Vector3(0, 0, -0.3f), new Vector3(0.5f, 0.5f, 0.3f), Palette.Slate);
            hm.Box(new Vector3(0, 0, 0.05f), new Vector3(0.9f, 0.9f, 0.6f), Palette.Render);
            var rotor = MatLib.Spawn("Rotor", hm.Build("hub"), nacelle.transform, new Vector3(0, 0, 1.45f), false);

            // Blade root at y 0.35 sits inside the hub (half-height 0.45).
            for (int i = 0; i < 3; i++)
            {
                var bm = new ProcMesh();
                bm.Box(new Vector3(0, 4.675f, 0), new Vector3(0.5f, 8.65f, 0.2f), Palette.Render);
                var blade = MatLib.Spawn("Blade" + i, bm.Build("blade"), rotor.transform, Vector3.zero, false);
                blade.transform.localRotation = Quaternion.Euler(0, 0, -120f * i);
            }

            refs.TurbineRotor = rotor.transform;
            refs.TurbineNacelle = nacelle.transform;
        }

        // ------------------------------------------------------------------
        // Fences
        // ------------------------------------------------------------------

        /// <summary>An opening in one side of a fence: Side 'N','S','E' or 'W',
        /// A0..A1 along that side's axis (x for N/S, z for E/W).</summary>
        public struct FenceGate
        {
            public char Side;
            public float A0, A1;
        }

        private static void BuildFences(Transform root, SiteRefs refs)
        {
            // One perimeter: the pedestrian gate on the entrance axis (x 55,
            // the main door), the vehicle gate on the dock axis (z 19). Two
            // nested fences meant two gates between any outside spawn and the
            // door, whatever else the plan did.
            Fence(root, "Perimeter", refs.FenceX0, refs.FenceX1, refs.FenceZ0, refs.FenceZ1, 2.6f, Palette.ProgramBlue,
                new FenceGate { Side = 'S', A0 = 53.7f, A1 = 56.3f },
                new FenceGate { Side = 'W', A0 = 14f, A1 = 24f });
        }

        private static void Fence(Transform root, string name, float x0, float x1,
            float z0, float z1, float h, Color colour, params FenceGate[] gates)
        {
            var posts = new ProcMesh();
            var mesh = new ProcMesh();
            var wire = new ProcMesh();
            const float span = 4f;
            // The wire above the top rail leans OUT (art-bible mesh #6: a
            // fence that keeps people out, not in): each post carries a 45°
            // arm and three strands of barbed wire run along the arms.
            const float ArmOut = 0.32f, ArmUp = 0.32f;

            void Panel(bool alongX, float a0, float a1, float fixedCoord, char side)
            {
                float mid = (a0 + a1) / 2f;
                float outSign = side == 'S' || side == 'W' ? -1f : 1f;
                if (alongX)
                {
                    posts.Box(new Vector3(mid, h - 0.06f, fixedCoord), new Vector3(a1 - a0, 0.08f, 0.08f), colour);
                    FenceWall(root, name, new Vector3(mid, h / 2f, fixedCoord), new Vector3(a1 - a0, h, 0.3f));
                }
                else
                {
                    posts.Box(new Vector3(fixedCoord, h - 0.06f, mid), new Vector3(0.08f, 0.08f, a1 - a0), colour);
                    FenceWall(root, name, new Vector3(fixedCoord, h / 2f, mid), new Vector3(0.3f, h, a1 - a0));
                }
                ChainLink(mesh, alongX, a0 + 0.06f, a1 - 0.06f, fixedCoord, 0.12f, h - 0.1f, colour);
                BarbedWire(wire, alongX, a0, a1, fixedCoord, h, outSign, ArmOut, ArmUp, Palette.Slate);
            }

            void Post(bool alongX, float a, float fixedCoord, float outSign)
            {
                // Corner posts belong to the X sides; the Z sides would put a
                // second, coincident post there.
                if (!alongX && (Mathf.Abs(a - z0) < 0.01f || Mathf.Abs(a - z1) < 0.01f)) return;
                Vector3 p = alongX ? new Vector3(a, h / 2f, fixedCoord) : new Vector3(fixedCoord, h / 2f, a);
                posts.Box(p, new Vector3(0.12f, h, 0.12f), colour);
                Vector3 top = alongX ? new Vector3(a, h, fixedCoord) : new Vector3(fixedCoord, h, a);
                Vector3 outward = alongX ? new Vector3(0f, 0f, outSign) : new Vector3(outSign, 0f, 0f);
                posts.Beam(top, top + outward * ArmOut + Vector3.up * ArmUp, 0.05f, colour);
            }

            // A run is divided into EVEN panels, so a post lands on both ends
            // of it: the corners and the gate edges. Striding 4 m from the
            // corner left posts standing in the gateway and none on the far
            // corner whenever the side was not a multiple of four.
            void Run(bool alongX, float a0, float a1, float fixedCoord, char side)
            {
                float len = a1 - a0;
                if (len <= 0.01f) return;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / span - 0.01f));
                float step = len / n;
                float outSign = side == 'S' || side == 'W' ? -1f : 1f;
                for (int i = 0; i <= n; i++) Post(alongX, a0 + i * step, fixedCoord, outSign);
                for (int i = 0; i < n; i++) Panel(alongX, a0 + i * step, a0 + (i + 1) * step, fixedCoord, side);
            }

            // A side breaks into one run per stretch between its gates; each
            // run ends on a post, so every gate edge gets one and the gap none.
            void Side(bool alongX, float a0, float a1, float fixedCoord, char side)
            {
                var cuts = new List<FenceGate>();
                foreach (FenceGate g in gates) if (g.Side == side) cuts.Add(g);
                cuts.Sort((p, q) => p.A0.CompareTo(q.A0));
                float a = a0;
                foreach (FenceGate g in cuts)
                {
                    Run(alongX, a, g.A0, fixedCoord, side);
                    a = g.A1;
                }
                Run(alongX, a, a1, fixedCoord, side);
            }

            Side(true, x0, x1, z0, 'S');
            Side(true, x0, x1, z1, 'N');
            Side(false, z0, z1, x0, 'W');
            Side(false, z0, z1, x1, 'E');

            // Amber caps on the gate posts: this is the way through.
            foreach (FenceGate g in gates)
            {
                Vector3 ga, gb;
                switch (g.Side)
                {
                    case 'W': ga = new Vector3(x0, h + 0.15f, g.A0); gb = new Vector3(x0, h + 0.15f, g.A1); break;
                    case 'E': ga = new Vector3(x1, h + 0.15f, g.A0); gb = new Vector3(x1, h + 0.15f, g.A1); break;
                    case 'N': ga = new Vector3(g.A0, h + 0.15f, z1); gb = new Vector3(g.A1, h + 0.15f, z1); break;
                    default: ga = new Vector3(g.A0, h + 0.15f, z0); gb = new Vector3(g.A1, h + 0.15f, z0); break;
                }
                posts.Box(ga, new Vector3(0.24f, 0.3f, 0.24f), Palette.Amber);
                posts.Box(gb, new Vector3(0.24f, 0.3f, 0.24f), Palette.Amber);
            }

            MatLib.Spawn("Fence" + name, posts.Build("fence"), root, Vector3.zero, false);
            MatLib.Spawn("Fence" + name + "Mesh", mesh.Build("fenceMesh"), root, Vector3.zero, false);
            MatLib.Spawn("Fence" + name + "Wire", wire.Build("fenceWire"), root, Vector3.zero, false);
        }

        /// <summary>Chain-link: two families of diagonal wires 20 cm apart,
        /// each a 3 cm strip seen from both sides, clipped to the panel. At
        /// the distances the fence is looked at it reads as a mesh; up close
        /// it is a lattice you can see the yard through.</summary>
        private static void ChainLink(ProcMesh pm, bool alongX, float a0, float a1, float fixedCoord,
            float y0, float y1, Color colour)
        {
            const float pitch = 0.2f, width = 0.03f;
            float hgt = y1 - y0;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (float k = a0 - hgt; k < a1 + hgt; k += pitch)
                {
                    // The wire is a = k + dir·t, y = y0 + t for t in [0, hgt],
                    // cut to the panel's a range.
                    float tMin = 0f, tMax = hgt;
                    if (dir > 0) { tMin = Mathf.Max(tMin, a0 - k); tMax = Mathf.Min(tMax, a1 - k); }
                    else { tMin = Mathf.Max(tMin, k - a1); tMax = Mathf.Min(tMax, k - a0); }
                    if (tMax - tMin < 0.05f) continue;
                    WireStrip(pm, alongX, k + dir * tMin, y0 + tMin, k + dir * tMax, y0 + tMax, fixedCoord, width, colour);
                }
            }
        }

        /// <summary>A flat strip in the fence plane from (aA, yA) to (aB, yB),
        /// both faces.</summary>
        private static void WireStrip(ProcMesh pm, bool alongX, float aA, float yA, float aB, float yB,
            float f, float width, Color c)
        {
            float da = aB - aA, dy = yB - yA;
            float len = Mathf.Sqrt(da * da + dy * dy);
            float na = -dy / len * width / 2f, ny = da / len * width / 2f;
            Vector3 p0 = FencePoint(alongX, aA - na, yA - ny, f), p1 = FencePoint(alongX, aA + na, yA + ny, f);
            Vector3 p2 = FencePoint(alongX, aB + na, yB + ny, f), p3 = FencePoint(alongX, aB - na, yB - ny, f);
            pm.Quad(p0, p1, p2, p3, c);
            pm.Quad(p1, p0, p3, p2, c);
        }

        private static Vector3 FencePoint(bool alongX, float a, float y, float f, float o = 0f)
        {
            return alongX ? new Vector3(a, y, f + o) : new Vector3(f + o, y, a);
        }

        /// <summary>Three strands along the leaning arms, a barb every 40 cm
        /// (one short diagonal, alternating its lean, which reads as barbs
        /// from the pavement without costing a cross each).</summary>
        private static void BarbedWire(ProcMesh pm, bool alongX, float a0, float a1, float f, float h,
            float outSign, float armOut, float armUp, Color colour)
        {
            float[] along = { 0.4f, 0.7f, 1f };
            foreach (float fr in along)
            {
                float up = armUp * fr, o = armOut * fr * outSign;
                pm.Beam(FencePoint(alongX, a0, h + up, f, o), FencePoint(alongX, a1, h + up, f, o), 0.015f, colour);
                int barbs = Mathf.FloorToInt((a1 - a0) / 0.4f);
                for (int i = 0; i < barbs; i++)
                {
                    float a = a0 + 0.2f + i * 0.4f;
                    float lean = i % 2 == 0 ? 0.04f : -0.04f;
                    pm.Beam(FencePoint(alongX, a, h + up - 0.04f, f, o - lean),
                        FencePoint(alongX, a, h + up + 0.04f, f, o + lean), 0.012f, colour);
                }
            }
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
        // Slots, mounts (the town around the site lives in CityBuilder)
        // ------------------------------------------------------------------

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
            // Solar rows on the grass north of the apron, inside the fence:
            // the north field, clear of the walking line from the gate.
            for (int i = 0; i < 12; i++)
                refs.SolarSlots.Add(new Vector3(4f + (i % 6) * 9f, 0, 38f + (i / 6) * 6f));
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
            // All three hang on the WEST face of the x = 44 spine wall, so they
            // face -X (yaw 270) into their rooms. The breaker sits on the solid
            // z 16..19 segment, clear of the meet-me doorway at 14..16.
            refs.Breaker = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.4f, 17.5f), 270f);
            refs.VentFan = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.4f, 22f), 270f);
            refs.Eyewash = new Mount(new Vector3(XSpine - T / 2f - d, y + 1.0f, 26f), 270f);
            // The start lever on the genset's north face (body half-depth
            // 1.4, so 0.2 m proud), facing the open apron.
            refs.DieselStart = new Mount(new Vector3(refs.GensetPos.x, refs.GensetPos.y + 1.2f, refs.GensetPos.z + 1.6f), 0f);

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

        /// <summary>A door in a wall opening. The leaf hangs off a carrier the
        /// Door component moves: sideways into the wall pocket on its local +z
        /// side (every opening has at least a leaf's width of wall there), or
        /// straight up into the lintel for the goods shutter. An open leaf that
        /// stays in the opening is an obstacle; one that swings is a trap for
        /// whatever is under it.</summary>
        private static void MakeDoor(Transform root, Room a, Room b, string name, Vector3 pos, float yaw,
            float leafW = 2.0f, bool shutter = false)
        {
            var go = new GameObject("Door " + name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var carrier = new GameObject("Carrier");
            carrier.transform.SetParent(go.transform, false);
            var pm = new ProcMesh();
            float lh = Door.LeafH;
            pm.Box(new Vector3(0, lh / 2f, 0), new Vector3(0.14f, lh, leafW), Palette.ProgramBlue);
            MatLib.Spawn("Leaf", pm.Build("doorleaf"), carrier.transform, Vector3.zero);
            if (shutter)
            {
                // The raised shutter stands a storey above its lintel; the
                // hood on the outside hides the part the upper wall does not.
                var hood = new ProcMesh();
                hood.Box(new Vector3(-0.2f, lh + 1.6f, 0), new Vector3(0.4f, 0.6f, leafW + 0.5f), Palette.Slate);
                MatLib.Spawn("Shutter hood", hood.Build("hood"), go.transform, Vector3.zero);
            }
            // A TIGHT trigger. The old 1.2 x 2.6 x 2.6 volume protruded 0.6 m
            // into both rooms and intercepted the interaction probe for every
            // control anywhere near a doorway.
            var trigger = go.AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.45f, lh - 0.1f, leafW);
            trigger.center = new Vector3(0, lh / 2f, 0);
            trigger.isTrigger = true;
            var door = go.AddComponent<Door>();
            door.DoorName = name;
            door.SetCarrier(carrier.transform, leafW, shutter);
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
            l.intensity = 1.6f;
            l.color = new Color(0.95f, 0.96f, 1f);
            // A point light ignores walls: without shadows every room lit the
            // yard through the envelope at night. Lowest resolution — the
            // walls are flat boxes, the penumbra does not matter.
            l.shadows = LightShadows.Hard;
            l.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Low;
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
