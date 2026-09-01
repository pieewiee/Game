using System;
using System.Globalization;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    /// <summary>
    /// A delivery pallet. Hardware arrives on these and cannot be installed any
    /// other way (workplace-accidents.md §4.4). Exactly one exists at a time —
    /// a slice simplification that also makes it trivial to replicate.
    /// </summary>
    public sealed class Pallet : MonoBehaviour
    {
        public static Pallet Current;

        public string Kind = "rack";     // what installing it delivers
        public float BaseY;              // height of the fork pockets above ground

        private bool _carried;
        public bool Carried
        {
            get { return _carried; }
            set
            {
                _carried = value;
                // On the forks it is cargo, not an obstacle: a live collider
                // would let the load block the machine carrying it.
                foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = !value;
            }
        }

        public static Pallet Spawn(Vector3 pos, string kind)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Pallet " + kind);
            go.transform.position = pos;

            var pm = new ProcMesh();
            // Deck + stringers: the pockets between them are what the forks enter.
            pm.Box(new Vector3(0, 0.14f, 0), new Vector3(1.2f, 0.06f, 1.0f), Palette.Earth);
            pm.Box(new Vector3(-0.5f, 0.07f, 0), new Vector3(0.14f, 0.14f, 1.0f), Palette.Earth);
            pm.Box(new Vector3(0.5f, 0.07f, 0), new Vector3(0.14f, 0.14f, 1.0f), Palette.Earth);
            // The crate on top: cardboard, exactly the fire load the docs warn about.
            pm.Box(new Vector3(0, 0.62f, 0), new Vector3(1.0f, 0.9f, 0.86f), Palette.Render);
            pm.Box(new Vector3(0, 1.08f, 0), new Vector3(1.02f, 0.04f, 0.88f), Palette.Ink);
            MatLib.Spawn("PalletMesh", pm.Build("pallet"), go.transform, Vector3.zero);

            var p = go.AddComponent<Pallet>();
            p.Kind = kind;
            p.BaseY = 0.16f;
            Current = p;
            return p;
        }
    }

    /// <summary>
    /// The forklift (workplace-accidents.md §4.4). Its function is not comedy:
    /// hardware arrives on pallets and a rack cannot be installed without it.
    /// The comedy is a consequence of the function.
    ///
    /// Real behaviour, because the hazards depend on it:
    /// - REAR-wheel steering. Turn rate scales with speed (a forklift cannot
    ///   pivot standing still) and the body swings about the front axle, so
    ///   the back end sweeps out — which is how you take out a chiller you
    ///   thought you had cleared.
    /// - A mast that lifts. Fork height decides whether a pallet is engaged,
    ///   and height plus speed plus steering decide whether you tip over.
    /// - Tipping is fatal to the driver. Turning fast with a raised load is
    ///   the classic forklift fatality and the docs call for it explicitly.
    ///
    /// Authority: the machine whose player is driving owns the vehicle and
    /// broadcasts its transform; when nobody drives, the host owns it.
    /// Consequences (nodes destroyed, plant damaged, fence wrecked) travel as
    /// ordinary recorded commands, so the host's ledger names the driver.
    /// </summary>
    public sealed class Forklift : MonoBehaviour, IInteractable
    {
        public static Forklift Instance;

        // --- vehicle constants ---
        public const float MaxForward = 4.2f, MaxReverse = 3.0f;
        public const float Accel = 3.6f, BrakeDecel = 7.5f, EngineBrake = 2.4f;
        public const float MaxSteerDeg = 38f, SteerRate = 110f, SteerReturn = 150f;
        public const float TurnGain = 1.15f;
        public const float LiftSpeed = 0.85f, MaxLift = 2.6f;
        public const float HarmSpeed = 2.0f;
        // Tip envelope: fast + hard steering + raised forks.
        public const float TipSpeed = 2.2f, TipForkH = 1.2f, TipSteerDeg = 26f;

        public PlayerRig DriverRig { get; private set; }
        public float Speed { get; private set; }          // signed, m/s
        public float SteerAngle { get; private set; }     // degrees
        public float ForkHeight { get; private set; }     // metres
        public bool Tipped { get; private set; }
        public bool HeadlightsOn { get; private set; }
        public Pallet Load { get; private set; }

        private CharacterController _cc;
        private Transform _forks;
        private Transform _mast;
        private GameObject _beaconGo;
        private Light _beacon;
        private Light _headL, _headR;
        private AudioSource _engine, _beeper;
        private float _lastImpact;
        private string _lastImpactName = "";

        // replicated state when this machine is not the authority
        private Vector3 _netPos;
        private float _netYaw, _netRoll, _netFork;
        private bool _netValid;

        public static Forklift Create(SiteRefs site, Vector3 pos)
        {
            var go = new GameObject("Forklift");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;

            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 0.62f, -0.2f), new Vector3(1.2f, 0.95f, 1.6f), Palette.Amber);  // body
            pm.Box(new Vector3(0, 1.22f, -0.55f), new Vector3(1.0f, 0.3f, 0.7f), Palette.Ink);    // seat
            pm.Box(new Vector3(-0.5f, 1.75f, -0.55f), new Vector3(0.08f, 1.0f, 0.08f), Palette.Slate); // cage
            pm.Box(new Vector3(0.5f, 1.75f, -0.55f), new Vector3(0.08f, 1.0f, 0.08f), Palette.Slate);
            pm.Box(new Vector3(0, 2.28f, -0.55f), new Vector3(1.1f, 0.08f, 0.8f), Palette.Slate); // roof
            pm.Box(new Vector3(0, 0.28f, 0.55f), new Vector3(1.1f, 0.5f, 0.35f), Palette.Ink);    // counterweight side
            MatLib.Spawn("ForkliftBody", pm.Build("forklift"), go.transform, Vector3.zero, false);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.75f;
            cc.center = new Vector3(0, 0.8f, 0);
            cc.slopeLimit = 30f;
            cc.stepOffset = 0.25f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;

            var fl = go.AddComponent<Forklift>();
            fl.Build(go.transform);
            Instance = fl;
            Pallet.Current = null;   // a fresh site has no deliveries pending
            return fl;
        }

        private void Build(Transform root)
        {
            _cc = GetComponent<CharacterController>();

            // Mast at the front, forks that slide up it.
            var mastGo = new GameObject("Mast");
            mastGo.transform.SetParent(root, false);
            mastGo.transform.localPosition = new Vector3(0, 0, 0.85f);
            _mast = mastGo.transform;
            var mm = new ProcMesh();
            mm.Box(new Vector3(-0.42f, 1.45f, 0), new Vector3(0.12f, 2.9f, 0.12f), Palette.Slate);
            mm.Box(new Vector3(0.42f, 1.45f, 0), new Vector3(0.12f, 2.9f, 0.12f), Palette.Slate);
            mm.Box(new Vector3(0, 2.88f, 0), new Vector3(0.96f, 0.1f, 0.12f), Palette.Slate);
            MatLib.Spawn("MastMesh", mm.Build("mast"), mastGo.transform, Vector3.zero, false);

            var forkGo = new GameObject("Forks");
            forkGo.transform.SetParent(mastGo.transform, false);
            _forks = forkGo.transform;
            var fm = new ProcMesh();
            fm.Box(new Vector3(0, 0.16f, 0.02f), new Vector3(0.94f, 0.3f, 0.08f), Palette.Ink); // carriage
            fm.Box(new Vector3(-0.35f, 0.05f, 0.6f), new Vector3(0.13f, 0.06f, 1.2f), Palette.Ink);
            fm.Box(new Vector3(0.35f, 0.05f, 0.6f), new Vector3(0.13f, 0.06f, 1.2f), Palette.Ink);
            MatLib.Spawn("ForkMesh", fm.Build("forks"), forkGo.transform, Vector3.zero, false);

            // Amber beacon: the legally required "I am about to ruin your day".
            _beaconGo = new GameObject("Beacon");
            _beaconGo.transform.SetParent(root, false);
            _beaconGo.transform.localPosition = new Vector3(0, 2.42f, -0.55f);
            var bm = new ProcMesh();
            bm.Cylinder(Vector3.zero, 0.11f, 0.16f, 8, Palette.Amber);
            MatLib.Spawn("BeaconMesh", bm.Build("beacon"), _beaconGo.transform, Vector3.zero, false);
            _beacon = _beaconGo.AddComponent<Light>();
            _beacon.type = LightType.Point;
            _beacon.color = new Color(1f, 0.6f, 0.1f);
            _beacon.range = 14f;
            _beacon.intensity = 0f;

            _headL = MakeHeadlight(root, new Vector3(-0.42f, 0.95f, 0.75f));
            _headR = MakeHeadlight(root, new Vector3(0.42f, 0.95f, 0.75f));

            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = SiteAudio.EngineClip();
            _engine.loop = true;
            _engine.spatialBlend = 1f;
            _engine.maxDistance = 40f;
            _engine.rolloffMode = AudioRolloffMode.Linear;
            _engine.volume = 0f;
            _engine.Play();

            var beepGo = new GameObject("Beeper");
            beepGo.transform.SetParent(root, false);
            _beeper = beepGo.AddComponent<AudioSource>();
            _beeper.clip = SiteAudio.BeepClip();
            _beeper.loop = true;
            _beeper.spatialBlend = 1f;
            _beeper.maxDistance = 60f;   // the town can hear it, which is the joke
            _beeper.rolloffMode = AudioRolloffMode.Linear;
            _beeper.volume = 0f;
            _beeper.Play();
        }

        private static Light MakeHeadlight(Transform root, Vector3 local)
        {
            var go = new GameObject("Headlight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 55f;
            l.range = 30f;
            l.intensity = 0f;
            l.color = new Color(1f, 0.97f, 0.88f);
            return l;
        }

        // ------------------------------------------------------------------
        // Authority
        // ------------------------------------------------------------------

        /// <summary>The driver's machine owns the vehicle; with nobody aboard,
        /// the host does. Everyone else renders what they are told.</summary>
        public bool IsAuthority()
        {
            var net = GameBootstrap.Net;
            if (net == null || !net.Active) return true;
            if (DriverRig != null) return true;               // I am driving it
            return net.IsHost && !net.SomeoneElseDriving;
        }

        // ------------------------------------------------------------------
        // Deliveries
        // ------------------------------------------------------------------

        /// <summary>Order a pallet of rack hardware onto the dock. Routed to
        /// whoever owns the vehicle, so the pallet exists exactly once.
        /// Returns the hint to show the player.</summary>
        public static string OrderDelivery()
        {
            if (Pallet.Current != null)
                return "a delivery is already waiting at the dock";
            var net = GameBootstrap.Net;
            if (net != null && net.Active && (Instance == null || !Instance.IsAuthority()))
            {
                net.RequestDelivery();
                return "delivery ordered";
            }
            SpawnDelivery();
            GameBootstrap.AddLedger(
                GameBootstrap.LocalPlayer != null ? GameBootstrap.LocalPlayer.PlayerName : "operator",
                "ordered a pallet of rack hardware");
            return "delivery ordered — it is on the dock";
        }

        public static void SpawnDelivery()
        {
            var site = GameBootstrap.Site;
            if (site == null || Pallet.Current != null) return;
            Pallet.Spawn(site.DockPos, "rack");
            NewsFeed.Post("A lorry has left a pallet of hardware on the dock. It will not " +
                "install itself, and the Program has costed exactly one forklift.");
        }

        // ------------------------------------------------------------------
        // Mount / dismount
        // ------------------------------------------------------------------

        public string Prompt(PlayerRig player)
        {
            if (Tipped) return "E: heave the forklift back onto its wheels";
            if (DriverRig != null) return null;
            var net = GameBootstrap.Net;
            if (net != null && net.Active && net.SomeoneElseDriving) return "someone is driving this";
            return "E: drive the forklift";
        }

        public void Interact(PlayerRig player)
        {
            if (Tipped)
            {
                Tipped = false;
                transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
                GameBootstrap.AddLedger(player.PlayerName, "righted the overturned forklift");
                NewsFeed.Post("Site staff have returned a forklift to its wheels. The Program " +
                    "notes that the equipment \"performed as designed throughout\".");
                return;
            }
            if (DriverRig != null) return;
            var net = GameBootstrap.Net;
            if (net != null && net.Active && net.SomeoneElseDriving) return;

            DriverRig = player;
            player.Driving = this;
            player.SetBodyEnabled(false);
            player.transform.SetParent(transform, false);
            player.transform.localPosition = new Vector3(0, 1.35f, -0.55f);
            player.transform.localRotation = Quaternion.identity;
            player.BeginDriving();
            if (net != null && net.Active) net.ClaimVehicle(true);
            GameBootstrap.AddLedger(player.PlayerName, "took the forklift");
        }

        private void Dismount(PlayerRig player)
        {
            DriverRig = null;
            player.Driving = null;
            player.transform.SetParent(null, true);
            // Step out on whichever side is clear, and above the deck rather
            // than 10 cm inside it.
            Vector3 side = transform.right * 1.6f;
            if (Physics.SphereCast(transform.position + Vector3.up * 1f, 0.4f,
                    transform.right, out _, 1.8f, ~(1 << PlayerRig.PlayerLayer),
                    QueryTriggerInteraction.Ignore))
                side = -side;
            player.transform.position = transform.position + side + Vector3.up * 0.6f;
            player.SetBodyEnabled(true);
            Speed = 0f;
            var net = GameBootstrap.Net;
            if (net != null && net.Active) net.ClaimVehicle(false);
        }

        /// <summary>The seat is vacated without the dismount teleport: the
        /// driver died and RespawnSystem owns where the new body appears.</summary>
        public void ForceDismount(PlayerRig rig)
        {
            if (DriverRig != rig) return;
            DriverRig = null;
            Speed = 0f;
            var net = GameBootstrap.Net;
            if (net != null && net.Active) net.ClaimVehicle(false);
        }

        // ------------------------------------------------------------------
        // Driving — called from the seated player's Update
        // ------------------------------------------------------------------

        public void Drive(PlayerRig player, Keyboard kb)
        {
            float dt = Time.deltaTime;
            if (kb.eKey.wasPressedThisFrame) { Dismount(player); return; }
            if (Tipped) { Speed = 0f; return; }

            // --- throttle: W/S, with real braking before reversing ---
            float throttle = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float target = throttle > 0 ? throttle * MaxForward : throttle * MaxReverse;
            bool braking = throttle != 0f && Mathf.Sign(throttle) != Mathf.Sign(Speed) && Mathf.Abs(Speed) > 0.1f;
            float rate = braking ? BrakeDecel : throttle == 0f ? EngineBrake : Accel;
            Speed = Mathf.MoveTowards(Speed, target, rate * dt);

            // --- steering: builds up, self-centres, and only bites with speed ---
            float steerIn = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            SteerAngle = steerIn != 0f
                ? Mathf.MoveTowards(SteerAngle, steerIn * MaxSteerDeg, SteerRate * dt)
                : Mathf.MoveTowards(SteerAngle, 0f, SteerReturn * dt);

            // --- mast: Space/R up, Ctrl/F down ---
            float lift = (kb.spaceKey.isPressed || kb.rKey.isPressed ? 1f : 0f)
                       - (kb.leftCtrlKey.isPressed || kb.fKey.isPressed ? 1f : 0f);
            if (lift != 0f)
                ForkHeight = Mathf.Clamp(ForkHeight + lift * LiftSpeed * dt, 0f, MaxLift);

            if (kb.lKey.wasPressedThisFrame) HeadlightsOn = !HeadlightsOn;
            if (kb.hKey.wasPressedThisFrame) SiteAudio.PlayHorn(transform.position);

            ApplyMotion(dt);
            HandleLoad();
            CheckTipOver();
        }

        private void ApplyMotion(float dt)
        {
            // Rear-wheel steering: the body rotates about the FRONT axle, so
            // the counterweight end swings wide. Turn rate scales with speed.
            float speedFrac = Speed / MaxForward;
            float yawDelta = SteerAngle * speedFrac * TurnGain * dt;
            Vector3 frontAxle = transform.position + transform.forward * 0.85f;
            transform.RotateAround(frontAxle, Vector3.up, yawDelta);

            Vector3 motion = transform.forward * Speed;
            motion.y = -6f;                       // stay on the ground
            _cc.Move(motion * dt);
        }

        // ------------------------------------------------------------------
        // Pallet handling
        // ------------------------------------------------------------------

        private void HandleLoad()
        {
            Pallet p = Pallet.Current;
            _forks.localPosition = new Vector3(0, ForkHeight, 0);

            if (Load == null)
            {
                if (p == null || p.Carried) return;
                // Engage when the forks are in the pockets and rise past them.
                Vector3 local = transform.InverseTransformPoint(p.transform.position);
                bool inPockets = local.z > 0.6f && local.z < 2.4f && Mathf.Abs(local.x) < 0.7f;
                if (inPockets && ForkHeight > p.BaseY && ForkHeight < p.BaseY + 0.45f)
                {
                    Load = p;
                    p.Carried = true;
                    p.transform.SetParent(_forks, true);
                }
                return;
            }

            // Set down when the forks come back to the deck.
            if (ForkHeight <= 0.05f)
            {
                Pallet load = Load;
                Load = null;
                load.Carried = false;
                load.transform.SetParent(null, true);
                Vector3 pos = load.transform.position;
                pos.y = 0f;
                load.transform.position = pos;
                load.transform.rotation = Quaternion.Euler(0, load.transform.eulerAngles.y, 0);
                TryInstall(load);
            }
        }

        /// <summary>Setting a rack pallet down on a prepared slot installs it —
        /// the only way nodes ever enter the site (docs §4.4).</summary>
        private void TryInstall(Pallet pallet)
        {
            var site = GameBootstrap.Site;
            var fac = GameBootstrap.Facility;
            if (site == null || fac == null || pallet.Kind != "rack") return;

            int installed = fac.CurrentNodes();
            int usedSlots = (installed + FacilityController.NodesPerRack - 1) / FacilityController.NodesPerRack;
            if (usedSlots >= site.RackSlots.Count)
            {
                NewsFeed.Post("There is nowhere left in Hall A to put this pallet.");
                return;
            }

            Vector3 slot = site.RackSlots[usedSlots];
            float d = Vector3.Distance(new Vector3(slot.x, 0, slot.z),
                                       new Vector3(pallet.transform.position.x, 0, pallet.transform.position.z));
            if (d > 2.5f) return;   // dropped somewhere else: it just sits there

            GameBootstrap.SendCommand(
                new SimCommand { Kind = CommandKind.AddNodes, A = FacilityController.NodesPerRack },
                "installed a rack from the delivery pallet");
            Destroy(pallet.gameObject);
            if (Pallet.Current == pallet) Pallet.Current = null;
        }

        // ------------------------------------------------------------------
        // Tipping over
        // ------------------------------------------------------------------

        private void CheckTipOver()
        {
            if (Tipped) return;
            bool fast = Mathf.Abs(Speed) > TipSpeed;
            bool hard = Mathf.Abs(SteerAngle) > TipSteerDeg;
            bool high = ForkHeight > TipForkH;
            if (!(fast && hard && high)) return;

            Tipped = true;
            Speed = 0f;
            float roll = SteerAngle > 0 ? -72f : 72f;   // it goes over the outside wheel
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, roll);

            if (Load != null)
            {
                Pallet load = Load;
                Load = null;
                load.Carried = false;
                load.transform.SetParent(null, true);
                if (load.Kind == "rack")
                    NewsFeed.Post("A rack was destroyed on the yard before it was ever installed. " +
                        "The Program calls the loss \"an opportunity to review lifting practice\".");
                Destroy(load.gameObject);
                if (Pallet.Current == load) Pallet.Current = null;
            }

            var driver = DriverRig;
            if (driver != null)
            {
                Dismount(driver);
                driver.Die("was crushed by an overturning forklift");
            }
            NewsFeed.Post("A forklift has overturned on site. The Program confirms the equipment " +
                "was \"being operated at the time\".");
        }

        // ------------------------------------------------------------------
        // Impacts — the docs' table, one entry at a time
        // ------------------------------------------------------------------

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(Speed) < HarmSpeed || hit.collider == null) return;

            // The local player in the way is an incident.
            var rig = hit.collider.GetComponentInParent<PlayerRig>();
            if (rig != null && rig != DriverRig && !rig.IsDead)
            {
                rig.Die("was struck by the forklift");
                Speed = 0f;
                return;
            }

            var net = GameBootstrap.Net;
            string n = hit.collider.gameObject.name;

            // A remote player's avatar (host side): the kill goes over the wire.
            if (net != null && net.IsHost && n.StartsWith("Avatar ", StringComparison.Ordinal) &&
                ulong.TryParse(n.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out ulong clientId))
            {
                net.HostKillClient(clientId, "was struck by the forklift");
                Speed = 0f;
                return;
            }

            // Property damage: one target per two seconds, so a scrape along a
            // fence is one finding rather than forty.
            if (Time.time - _lastImpact < 2f && _lastImpactName == n) return;

            if (hit.collider.GetComponentInParent<RackRemount>() != null)
            {
                Hit(n, new SimCommand { Kind = CommandKind.DestroyNodes, A = FacilityController.NodesPerRack },
                    "forklift into a rack (" + FacilityController.NodesPerRack + " nodes destroyed)",
                    "Materials-handling equipment made contact with live hardware. The Program calls it " +
                    "\"an unplanned rack-adjacent logistics event\".");
                return;
            }
            if (n.StartsWith("EvapTower", StringComparison.Ordinal))
                Hit(n, new SimCommand { Kind = CommandKind.AddPlant, A = (int)PlantKind.EvapKwTh, B = -FacilityController.EvapUnitKwTh },
                    "forklift into an evaporative tower (capacity lost)",
                    "A cooling tower on the site was struck by a works vehicle.");
            else if (n.StartsWith("Chiller", StringComparison.Ordinal))
                Hit(n, new SimCommand { Kind = CommandKind.AddPlant, A = (int)PlantKind.ChillerKwTh, B = -FacilityController.ChillerUnitKwTh },
                    "forklift into a chiller (capacity lost)",
                    "A chiller unit was struck by a works vehicle.");
            else if (n.StartsWith("FreecoolUnit", StringComparison.Ordinal))
                Hit(n, new SimCommand { Kind = CommandKind.AddPlant, A = (int)PlantKind.FreecoolKwTh, B = -FacilityController.FreecoolUnitKwTh },
                    "forklift into a free-cooling unit (capacity lost)",
                    "A cooling unit was struck by a works vehicle.");
            else if (n.StartsWith("Transformer", StringComparison.Ordinal))
                Hit(n, new SimCommand { Kind = CommandKind.EpoTrip, A = 0.75, B = 4 },
                    "forklift into the transformer (site de-energised)",
                    "The site's transformer was struck by its own forklift. Power to the site — and " +
                    "to part of the lane — is out while it is inspected.");
            else if (n.StartsWith("Fence", StringComparison.Ordinal))
                Hit(n, new SimCommand { Kind = CommandKind.AddVisualPoints, A = 5 },
                    "forklift through the perimeter fence",
                    "A section of the site's blue perimeter fence is flat. Residents describe the " +
                    "view as \"improved, actually\".");
        }

        private void Hit(string name, SimCommand cmd, string ledger, string news)
        {
            _lastImpact = Time.time;
            _lastImpactName = name;
            GameBootstrap.SendCommand(cmd, ledger);
            NewsFeed.Post(news);
            Speed = 0f;
        }

        // ------------------------------------------------------------------
        // Per-frame: puppet mode, lights, audio
        // ------------------------------------------------------------------

        private void Update()
        {
            bool authority = IsAuthority();

            if (!authority && _netValid)
            {
                // Puppet: follow the replicated transform without physics.
                transform.position = Vector3.Lerp(transform.position, _netPos, 12f * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0, _netYaw, _netRoll);
                ForkHeight = Mathf.MoveTowards(ForkHeight, _netFork, 3f * Time.deltaTime);
                _forks.localPosition = new Vector3(0, ForkHeight, 0);
                Tipped = Mathf.Abs(_netRoll) > 20f;
            }
            else if (DriverRig == null && !Tipped)
            {
                // Parked: settle onto the ground and bleed off any speed.
                Speed = Mathf.MoveTowards(Speed, 0f, EngineBrake * Time.deltaTime);
                _cc.Move(new Vector3(0, -6f, 0) * Time.deltaTime);
                _forks.localPosition = new Vector3(0, ForkHeight, 0);
            }

            bool occupied = DriverRig != null || (!authority && _netValid && GameBootstrap.Net != null
                                                  && GameBootstrap.Net.SomeoneElseDriving);

            // Beacon: flashes whenever the machine is live. Lights on demand.
            _beacon.intensity = occupied && !Tipped
                ? (Mathf.Sin(Time.time * 9f) > 0f ? 3.2f : 0.15f) : 0f;
            float head = HeadlightsOn && occupied && !Tipped ? 2.4f : 0f;
            _headL.intensity = head;
            _headR.intensity = head;

            // Audio: engine tracks speed, reverse alarm is honest about direction.
            float vol = PlayerOptions.MachineryVolume;
            if (_engine != null)
            {
                _engine.volume = occupied && !Tipped ? (0.18f + Mathf.Abs(Speed) / MaxForward * 0.3f) * vol : 0f;
                _engine.pitch = 0.75f + Mathf.Abs(Speed) / MaxForward * 0.7f;
            }
            if (_beeper != null)
                _beeper.volume = occupied && !Tipped && Speed < -0.25f ? 0.5f * vol : 0f;
        }

        // ------------------------------------------------------------------
        // Replication
        // ------------------------------------------------------------------

        public void WriteState(out Vector3 pos, out float yaw, out float roll, out float fork)
        {
            pos = transform.position;
            yaw = transform.eulerAngles.y;
            roll = transform.eulerAngles.z;
            fork = ForkHeight;
        }

        public void ApplyState(Vector3 pos, float yaw, float roll, float fork)
        {
            _netPos = pos; _netYaw = yaw; _netRoll = roll; _netFork = fork;
            _netValid = true;
        }

        public void ApplyPalletState(bool exists, Vector3 pos, float yaw, bool carried)
        {
            if (!exists)
            {
                if (Pallet.Current != null && Load == null) { Destroy(Pallet.Current.gameObject); Pallet.Current = null; }
                return;
            }
            if (Pallet.Current == null) Pallet.Spawn(pos, "rack");
            if (Load != null) return;                  // I am carrying it myself
            Pallet p = Pallet.Current;
            if (p == null) return;
            if (carried)
            {
                p.transform.position = pos;
                p.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
            else
            {
                p.transform.position = Vector3.Lerp(p.transform.position, pos, 10f * Time.deltaTime);
                p.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
        }
    }
}
