using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    /// <summary>
    /// The local player. A datacenter is a place you WALK, for hours, carrying
    /// things and stopping at objects, so the body has to feel trustworthy:
    /// acceleration and friction rather than instant velocity, reliable ground
    /// contact on stairs and thresholds, coyote time and jump buffering so an
    /// input is never silently eaten, a crouch that cannot stand up into a
    /// desk, and feedback (head bob, footsteps, landing) that tells you how
    /// fast you are actually moving.
    ///
    /// Falls are the one movement mistake with a consequence, because falls
    /// from height are the classic site fatality (workplace-accidents.md):
    /// above FatalFallM the rig dies and files an accident like any other.
    ///
    /// Capsule with a hard hat, per art-bible.md §3. Remote players are drawn
    /// by NetSession from replicated transforms.
    /// </summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        // ---- movement tuning (presentation only — no sim constants here) ----
        public const float WalkSpeed = 3.4f, RunSpeed = 6.3f, CrouchSpeed = 1.7f;
        public const float GroundAccel = 60f, AirAccel = 16f, GroundFriction = 12f;
        public const float JumpHeight = 1.05f, GravityMss = 20f, TerminalVelocity = 45f;
        public const float StepOffset = 0.42f;   // kerbs, thresholds, the car park lip
        public const float CoyoteTime = 0.12f, JumpBufferTime = 0.15f;
        public const float StandHeight = 1.8f, CrouchHeight = 1.05f;
        public const float StandEye = 1.62f, CrouchEye = 0.86f, SeatEye = 0.72f;
        public const float InteractRange = 3.2f, InteractRadius = 0.16f;
        public const float HurtFallM = 3.5f, FatalFallM = 6f;
        public const float KillPlaneY = -40f;   // insurance, not a feature

        /// <summary>Own layer, so ground probes cannot hit the player's own
        /// capsule. Numeric — no project layer setup required.</summary>
        public const int PlayerLayer = 8;
        private const int WorldMask = ~(1 << PlayerLayer);

        public Camera Cam { get; private set; }
        public Carryable Carried { get; private set; }
        public bool ShiftHeld { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsCrouched { get; private set; }
        public bool IsGrounded { get; private set; }
        /// <summary>Horizontal speed in m/s that the body ACTUALLY covered —
        /// not what it intended. Blocked against a wall this is zero, so the
        /// camera stops pretending you are running.</summary>
        public float Speed { get { return _achieved.magnitude; } }
        public string PlayerName = "Operator";
        public int TempWorkerCount;
        public Forklift Driving;

        private const float HoldThreshold = 0.35f;
        private const float HoldGrace = 0.4f;      // aim may wander this long
        private const float UseBufferTime = 0.15f; // same courtesy as the jump

        private CharacterController _cc;
        private Transform _carryAnchor;
        private float _yaw, _pitch, _driveYaw;

        // body state
        private Vector3 _vel;                 // full velocity, world space
        private float _curHeight = StandHeight;
        private float _coyote, _jumpBuffer;
        private bool _jumping;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _achieved;            // measured horizontal movement
        private Transform _platform;          // what we stand on (forklift!)
        private Vector3 _platformLastPos;
        private float _apexY;                 // highest point of the current fall

        // camera feel
        private float _eye = StandEye;
        private float _bobPhase, _bobAmount, _viewDip, _viewDipVel, _fovKick;

        // interaction
        private IHoldInteractable _holding;
        private IHoldInteractable _pendingHold;
        private float _pendingTime;
        private float _holdLost, _usePress;
        private int _targetFrame = -1;
        private IInteractable _cachedTarget;
        private string _cachedPrompt;
        private float _exposure; // freezing, 0..1 (FreezeSystem drives it)

        public float Exposure { get { return _exposure; } set { _exposure = value; } }

        public static PlayerRig Create(Vector3 pos, string name)
        {
            var go = new GameObject("Player " + name);
            go.transform.position = pos;
            var rig = go.AddComponent<PlayerRig>();
            rig.PlayerName = name;
            return rig;
        }

        private void Awake()
        {
            gameObject.layer = PlayerLayer;

            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = StandHeight;
            _cc.radius = 0.32f;
            _cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            _cc.slopeLimit = 50f;      // ramps yes, stacked crates no
            _cc.stepOffset = StepOffset;
            _cc.skinWidth = 0.03f;
            _cc.minMoveDistance = 0f;  // default 0.001 eats slow crouch-walking

            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, StandEye, 0);
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = 0.08f;
            Cam.farClipPlane = 600f;
            Cam.fieldOfView = PlayerOptions.Fov;
            camGo.AddComponent<AudioListener>();

            _carryAnchor = new GameObject("Carry").transform;
            _carryAnchor.SetParent(camGo.transform, false);
            _carryAnchor.localPosition = new Vector3(0.35f, -0.35f, 0.7f);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Spawning is not falling.
            _apexY = transform.position.y;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null) return;
            float dt = Time.deltaTime;
            ShiftHeld = kb.leftShiftKey.isPressed;

            // No floor should be missing; a body that does get under the site
            // must still not fall for ever with the respawn never firing.
            if (!IsDead && transform.position.y < KillPlaneY) Die("fell out of the world");

            // Escape TOGGLES the cursor (free it for the debug console, press
            // again to play on). Never re-lock by click: clicking a console
            // slider must not yank the camera. Modal windows own their own
            // Escape, so it is ignored while one is open.
            if (kb.escapeKey.wasPressedThisFrame && !GameBootstrap.UiWantsCursor &&
                GameBootstrap.EscConsumedFrame != Time.frameCount)
            {
                bool locking = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = locking ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !locking;
            }

            // While any UI owns the input (or the cursor is free), the body
            // stands still — but it still FALLS: freezing mid-air because
            // someone opened the console would be a physics lie.
            bool uiOwnsInput = GameBootstrap.UiWantsCursor ||
                               Cursor.lockState != CursorLockMode.Locked;
            if (uiOwnsInput)
            {
                if (_holding != null) { _holding.InteractRelease(this); _holding = null; }
                _pendingHold = null;
            }

            if (IsDead) return;
            if (Driving != null)
            {
                // Seated, but not blinkered: you can look around the cab,
                // over your shoulder while reversing, and down at the forks.
                if (!uiOwnsInput) DriveLook(mouse);
                CameraFeel(dt);
                if (!uiOwnsInput) Driving.Drive(this, kb);
                return;
            }

            if (!uiOwnsInput) Look(mouse);
            Crouch(kb, uiOwnsInput);
            Move(kb, uiOwnsInput, dt);
            CameraFeel(dt);
            if (!uiOwnsInput) Interact(kb, dt);
        }

        // ------------------------------------------------------------------
        // Look
        // ------------------------------------------------------------------

        private void Look(Mouse mouse)
        {
            if (mouse == null) return;
            // Mouse delta is already per-frame movement: multiplying by
            // deltaTime here would make sensitivity depend on frame rate.
            Vector2 d = mouse.delta.ReadValue() * PlayerOptions.Sensitivity;
            _yaw += d.x;
            _pitch = Mathf.Clamp(_pitch + (PlayerOptions.InvertY ? d.y : -d.y), -88f, 88f);
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
        }

        /// <summary>Look while seated: yaw is relative to the vehicle and
        /// clamped to what a neck allows, so the forklift still points where
        /// it drives.</summary>
        private void DriveLook(Mouse mouse)
        {
            if (mouse == null) return;
            Vector2 d = mouse.delta.ReadValue() * PlayerOptions.Sensitivity;
            _driveYaw = Mathf.Clamp(_driveYaw + d.x, -150f, 150f);
            _pitch = Mathf.Clamp(_pitch + (PlayerOptions.InvertY ? d.y : -d.y), -80f, 80f);
            transform.localRotation = Quaternion.Euler(0, _driveYaw, 0);
        }

        /// <summary>Called on mounting: face forward, and forget the walking
        /// yaw so the seat does not start twisted.</summary>
        public void BeginDriving()
        {
            _driveYaw = 0f;
            _pitch = 0f;
            _bobAmount = 0f;
            _viewDip = 0f;
            _vel = Vector3.zero;          // no walking momentum in the seat
            _achieved = Vector3.zero;
            _fovKick = 0f;
            transform.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------
        // Crouch
        // ------------------------------------------------------------------

        private void Crouch(Keyboard kb, bool uiOwnsInput)
        {
            bool want = !uiOwnsInput && kb.leftCtrlKey.isPressed;
            if (!want && IsCrouched && !HasHeadroom()) want = true; // pinned under a desk
            IsCrouched = want;

            float target = want ? CrouchHeight : StandHeight;
            if (!Mathf.Approximately(_curHeight, target))
            {
                _curHeight = Mathf.MoveTowards(_curHeight, target, 7f * Time.deltaTime);
                _cc.height = _curHeight;
                _cc.center = new Vector3(0, _curHeight * 0.5f, 0);
            }
        }

        private bool HasHeadroom()
        {
            // Sweep the stand-up envelope, not a thin line: a rack overhang
            // beside the head must block standing up too.
            Vector3 origin = transform.position + Vector3.up * (CrouchHeight - _cc.radius);
            float dist = StandHeight - CrouchHeight + 0.05f;
            return !Physics.SphereCast(origin, _cc.radius * 0.95f, Vector3.up,
                out _, dist, WorldMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------
        // Move
        // ------------------------------------------------------------------

        private void Move(Keyboard kb, bool uiOwnsInput, float dt)
        {
            bool wasGrounded = IsGrounded;
            ProbeGround();

            // --- landing ---
            if (IsGrounded && !wasGrounded && OnLanded()) return;   // fatal fall: body replaced
            if (!IsGrounded) _apexY = Mathf.Max(_apexY, transform.position.y);
            else _apexY = transform.position.y;

            // --- timers: an input near the edge should still count ---
            _coyote = IsGrounded ? CoyoteTime : Mathf.Max(0f, _coyote - dt);
            if (!uiOwnsInput && kb.spaceKey.wasPressedThisFrame) _jumpBuffer = JumpBufferTime;
            else _jumpBuffer = Mathf.Max(0f, _jumpBuffer - dt);

            // --- wish direction ---
            Vector3 wish = Vector3.zero;
            if (!uiOwnsInput)
            {
                if (kb.wKey.isPressed) wish += transform.forward;
                if (kb.sKey.isPressed) wish -= transform.forward;
                if (kb.dKey.isPressed) wish += transform.right;
                if (kb.aKey.isPressed) wish -= transform.right;
            }
            wish = wish.normalized;

            float targetSpeed = IsCrouched ? CrouchSpeed
                              : ShiftHeld && !IsCrouched ? RunSpeed : WalkSpeed;
            // Backpedalling is slower; nobody sprints backwards convincingly.
            if (Vector3.Dot(wish, transform.forward) < -0.5f) targetSpeed *= 0.72f;

            var horizontal = new Vector3(_vel.x, 0f, _vel.z);
            if (IsGrounded && !_jumping)
            {
                // Friction first, then acceleration toward the wish velocity:
                // stops are crisp, starts have weight.
                float drop = GroundFriction * dt;
                float mag = horizontal.magnitude;
                if (wish.sqrMagnitude < 0.01f && mag > 0f)
                    horizontal = mag > drop ? horizontal * ((mag - drop) / mag) : Vector3.zero;
                horizontal = Vector3.MoveTowards(horizontal, wish * targetSpeed, GroundAccel * dt);

                // On a slope steeper than the controller will hold, gravity wins.
                if (Vector3.Angle(_groundNormal, Vector3.up) > _cc.slopeLimit)
                {
                    Vector3 down = Vector3.ProjectOnPlane(Vector3.down, _groundNormal).normalized;
                    horizontal += down * (GravityMss * 0.55f * dt);
                }
            }
            else if (wish.sqrMagnitude > 0.01f)
            {
                // Air control STEERS: it may redirect momentum but never spend
                // it. Releasing the keys mid-jump used to brake to a standstill
                // in mid-air, which is the opposite of how a jump reads.
                float keep = horizontal.magnitude;
                Vector3 steered = Vector3.MoveTowards(horizontal, wish * targetSpeed, AirAccel * dt);
                horizontal = steered.magnitude < keep && keep > 0.01f
                    ? steered.normalized * keep : steered;
            }

            // --- vertical ---
            if (_jumpBuffer > 0f && _coyote > 0f && !IsCrouched)
            {
                _vel.y = Mathf.Sqrt(2f * GravityMss * JumpHeight);
                _jumpBuffer = 0f;
                _coyote = 0f;
                _jumping = true;
                IsGrounded = false;
            }
            else if (IsGrounded && _vel.y < 0f)
            {
                _vel.y = -2f; // keep the capsule pressed onto the ground
                _jumping = false;
            }
            else
            {
                _vel.y = Mathf.Max(_vel.y - GravityMss * dt, -TerminalVelocity);
            }

            _vel.x = horizontal.x;
            _vel.z = horizontal.z;

            // --- moving ground (ride the forklift) ---
            Vector3 platformDelta = Vector3.zero;
            if (_platform != null)
            {
                platformDelta = _platform.position - _platformLastPos;
                _platformLastPos = _platform.position;
            }

            // Steps are for feet on the ground. The controller auto-steps in
            // the air too, which turned a jump into a vault over any rail whose
            // top sat within the step height of the jump apex.
            _cc.stepOffset = IsGrounded ? StepOffset : 0f;
            Vector3 posBefore = transform.position;
            _cc.Move(_vel * dt + platformDelta);

            // Reconcile with what actually happened: pressed into a wall, the
            // open-loop velocity keeps claiming full speed, which drives the
            // bob and the sprint FOV — and launches the player sideways the
            // instant the wall ends. Only ever REDUCE, so slopes and steps
            // (where the controller achieves less for a frame) do not stall us.
            if (dt > 0f)
            {
                Vector3 achieved = (transform.position - posBefore - platformDelta) / dt;
                _achieved = new Vector3(achieved.x, 0f, achieved.z);
                var wanted = new Vector3(_vel.x, 0f, _vel.z);
                if (_achieved.magnitude < wanted.magnitude)
                {
                    _vel.x = _achieved.x;
                    _vel.z = _achieved.z;
                }
            }

            // Ceiling: stop climbing the moment the hard hat hits something.
            if ((_cc.collisionFlags & CollisionFlags.Above) != 0 && _vel.y > 0f) _vel.y = 0f;

            // Walking off a step should not become a hop: if we were grounded,
            // are not jumping and the ground is a hair below, snap onto it.
            if (wasGrounded && !_jumping && !_cc.isGrounded)
            {
                Vector3 origin = transform.position + Vector3.up * _cc.radius;
                if (Physics.SphereCast(origin, _cc.radius * 0.95f, Vector3.down,
                        out RaycastHit snap, StepOffset + 0.1f, WorldMask,
                        QueryTriggerInteraction.Ignore))
                {
                    _cc.Move(Vector3.down * (snap.distance - 0.02f));
                    IsGrounded = true;
                    _groundNormal = snap.normal;
                    _apexY = transform.position.y;
                }
            }

            Stride(dt);
        }

        /// <summary>Ground truth, independent of the controller's own flag:
        /// a sweep that also yields the surface normal and what we stand on.</summary>
        private void ProbeGround()
        {
            Vector3 origin = transform.position + Vector3.up * _cc.radius;
            if (Physics.SphereCast(origin, _cc.radius * 0.95f, Vector3.down,
                    out RaycastHit hit, 0.12f, WorldMask, QueryTriggerInteraction.Ignore))
            {
                IsGrounded = _vel.y <= 0.01f;
                _groundNormal = hit.normal;
                // Only a VEHICLE carries its passengers. Treating any collider
                // as a platform makes network-teleported avatars and rebuilt
                // racks fling the player across the site.
                Transform platform = hit.collider.GetComponentInParent<Forklift>() != null
                    ? hit.collider.GetComponentInParent<Forklift>().transform : null;
                if (_platform != platform)
                {
                    _platform = platform;
                    if (platform != null) _platformLastPos = platform.position;
                }
                return;
            }
            IsGrounded = _cc.isGrounded && _vel.y <= 0.01f;
            _groundNormal = Vector3.up;
            _platform = null;
        }

        /// <summary>Returns true when the landing killed the player, so the
        /// caller stops moving a body that has already been replaced.</summary>
        private bool OnLanded()
        {
            float drop = _apexY - transform.position.y;
            _jumping = false;
            // A landing is felt, not heard: the dip is the whole cue.
            _viewDip += Mathf.Clamp(drop * 0.03f, 0.004f, 0.18f);

            if (drop >= FatalFallM)
            {
                Die("fell from height (" + drop.ToString("0.0",
                    System.Globalization.CultureInfo.InvariantCulture) + " m)");
                return true;
            }
            if (drop >= HurtFallM)
            {
                // Survivable, and it stays in the body for a moment: the site
                // teaches through the walk back, not through a damage number.
                _vel.x *= 0.2f;
                _vel.z *= 0.2f;
            }
            return false;
        }

        /// <summary>Walk cadence for the camera only — silent. _bobPhase counts
        /// STEPS: stride length grows with speed, which puts a brisk walk near
        /// 2.8 steps/s and a sprint near 4.3 — human, instead of the 10-19
        /// steps/s a naive speed multiplier produces.</summary>
        private void Stride(float dt)
        {
            float speed = Speed;
            if (!IsGrounded || speed < 0.6f)
            {
                _bobAmount = Mathf.MoveTowards(_bobAmount, 0f, 4f * dt);
                return;
            }
            float stride = 0.9f + speed * 0.09f;
            _bobPhase += speed * dt / stride;
            float amp = IsCrouched ? 0.008f : ShiftHeld ? 0.024f : 0.014f;
            _bobAmount = Mathf.MoveTowards(_bobAmount, amp, 0.12f * dt);
        }

        // ------------------------------------------------------------------
        // Camera feel: eye height, bob, landing dip, sprint FOV
        // ------------------------------------------------------------------

        private void CameraFeel(float dt)
        {
            // Seated in the cab the head is low — otherwise the camera floats
            // above the safety cage and the forks vanish off the bottom.
            float targetEye = Driving != null ? SeatEye : IsCrouched ? CrouchEye : StandEye;
            _eye = Mathf.MoveTowards(_eye, targetEye, 6f * dt);

            _viewDip = Mathf.SmoothDamp(_viewDip, 0f, ref _viewDipVel, 0.16f);

            // One vertical dip per step, one lateral sway per stride.
            float bob = _bobAmount * PlayerOptions.BobScale;
            float bobY = Mathf.Cos(_bobPhase * 2f * Mathf.PI) * bob;
            float bobX = Mathf.Sin(_bobPhase * Mathf.PI) * bob * 0.35f;
            Cam.transform.localPosition = new Vector3(bobX, _eye + bobY - _viewDip, 0f);

            // Sprinting widens the view a little — cheap, and the only speed
            // cue that reads at a glance while carrying something.
            float wantKick = Driving == null && ShiftHeld && IsGrounded && Speed > RunSpeed * 0.6f ? 4f : 0f;
            _fovKick = Mathf.MoveTowards(_fovKick, wantKick, 22f * dt);
            Cam.fieldOfView = PlayerOptions.Fov + _fovKick;

            // Pitch on the camera; yaw stays on the body so the capsule turns.
            Cam.transform.localRotation = Quaternion.Euler(_pitch + _viewDip * 9f, 0f, 0f);
        }

        // ------------------------------------------------------------------
        // Interaction
        // ------------------------------------------------------------------

        private void Interact(Keyboard kb, float dt)
        {
            // Plain interactables fire on the E press. Objects that are ALSO
            // hold-controls (door, diesel lever) disambiguate on release: a
            // tap (< 0.35 s) is Interact, anything longer was a hold.
            IInteractable target = CurrentTarget(out _);
            // Looking away (or walking out of range) breaks a hold: a door is
            // held shut by a BODY at the door, not by a keypress from across
            // the yard. The physical-controls contract depends on this.
            // Losing the target for a FRAME must not end a 30 s door hold — a
            // mouse twitch is not letting go. Aim has to be off for a while.
            if (_pendingHold != null)
            {
                if (ReferenceEquals(target, _pendingHold)) _holdLost = 0f;
                else _holdLost += dt;
                if (_holdLost > HoldGrace)
                {
                    if (_holding != null) _holding.InteractRelease(this);
                    _holding = null;
                    _pendingHold = null;
                    _holdLost = 0f;
                }
            }
            // E is buffered like the jump: a press one frame early on approach
            // should not be thrown away.
            if (kb.eKey.wasPressedThisFrame) _usePress = UseBufferTime;
            else _usePress = Mathf.Max(0f, _usePress - dt);

            if (_usePress > 0f && target != null && _pendingHold == null)
            {
                _usePress = 0f;
                if (target is IHoldInteractable armed)
                {
                    _pendingHold = armed;
                    _pendingTime = 0f;
                    _holdLost = 0f;
                }
                else { target.Interact(this); _targetFrame = -1; }
            }
            if (kb.eKey.isPressed && _pendingHold != null)
            {
                _pendingTime += dt;
                if (_pendingTime >= HoldThreshold)
                {
                    _holding = _pendingHold;
                    _holding.InteractHold(this, dt);
                }
            }
            if (kb.eKey.wasReleasedThisFrame)
            {
                if (_holding != null)
                {
                    _holding.InteractRelease(this);
                }
                else if (_pendingHold is IInteractable tapped && _pendingTime < HoldThreshold)
                {
                    tapped.Interact(this);
                    _targetFrame = -1;
                }
                _holding = null;
                _pendingHold = null;
            }
            if (kb.qKey.wasPressedThisFrame && Carried != null) Drop();
        }

        /// <summary>A forgiving aim: a sphere, not a needle. Small controls
        /// (the EPO, a panel edge) must not demand pixel precision.
        /// Cached per frame — OnGUI asks for the prompt several times a frame
        /// and each ask is two physics sweeps.</summary>
        public IInteractable CurrentTarget(out string prompt)
        {
            if (_targetFrame == Time.frameCount)
            {
                prompt = _cachedPrompt;
                return _cachedTarget;
            }
            _targetFrame = Time.frameCount;
            _cachedTarget = Probe(out _cachedPrompt);
            prompt = _cachedPrompt;
            return _cachedTarget;
        }

        /// <summary>The NEAREST thing under the cursor that actually has
        /// something to say. Stopping at the first collider meant a rack face,
        /// an emptied cabinet or a doorway trigger became a dead target that
        /// ate the press and hid the control behind it.</summary>
        private IInteractable Probe(out string prompt)
        {
            prompt = null;
            if (Cam == null) return null;
            Vector3 origin = Cam.transform.position;
            Vector3 dir = Cam.transform.forward;

            IInteractable best = null;
            string bestPrompt = null;
            float bestDist = float.MaxValue;

            void Consider(RaycastHit[] hits)
            {
                if (hits == null) return;
                foreach (RaycastHit h in hits)
                {
                    if (h.distance >= bestDist) continue;
                    IInteractable cand = FromCollider(h.collider);
                    if (cand == null) continue;
                    string p = cand.Prompt(this);
                    if (string.IsNullOrEmpty(p)) continue;   // nothing to do here
                    best = cand;
                    bestPrompt = p;
                    bestDist = h.distance;
                }
            }

            Consider(Physics.RaycastAll(origin, dir, InteractRange, WorldMask,
                QueryTriggerInteraction.Collide));
            // The forgiving pass runs regardless, so a small control beside a
            // big one is still reachable without pixel-perfect aim.
            Consider(Physics.SphereCastAll(origin, InteractRadius, dir, InteractRange,
                WorldMask, QueryTriggerInteraction.Collide));

            prompt = bestPrompt;
            return best;
        }

        private static IInteractable FromCollider(Collider col)
        {
            if (col == null) return null;
            foreach (var mb in col.GetComponentsInParent<MonoBehaviour>())
                if (mb is IInteractable cand) return cand;
            return null;
        }

        // ------------------------------------------------------------------
        // Carrying, driving, dying
        // ------------------------------------------------------------------

        public void PickUp(Carryable c)
        {
            if (Carried != null) return;
            Carried = c;
            c.OnPickedUp(this);
            c.transform.SetParent(_carryAnchor, false);
            c.transform.localPosition = Vector3.zero;
            c.transform.localRotation = Quaternion.identity;
        }

        public void Drop()
        {
            if (Carried == null) return;
            Carryable c = Carried;
            Carried = null;
            c.transform.SetParent(null, true);
            c.transform.position = Cam.transform.position + Cam.transform.forward * 1.0f;
            c.OnDropped();
        }

        /// <summary>Destroys the carried object (a panel remounted into a rack
        /// slot lives on as the slot's own panel, not as this world object).</summary>
        public void ConsumeCarried()
        {
            if (Carried == null) return;
            Carryable c = Carried;
            Carried = null;
            Destroy(c.gameObject);
        }

        /// <summary>The forklift disables the walking capsule while driving —
        /// two enabled CharacterControllers in one hierarchy fight.</summary>
        public void SetBodyEnabled(bool on)
        {
            if (_cc == null) return;
            _cc.enabled = on;
            if (on)
            {
                // Keep looking where the cab was looking.
                _yaw = transform.eulerAngles.y;
                // Stepping off a moving forklift must not read as a fall.
                _vel = Vector3.zero;
                _apexY = transform.position.y;
                _platform = null;
            }
        }

        /// <summary>Death is never the punishment; the statistic is. The rig is
        /// respawned by RespawnSystem as a fresh temp worker.</summary>
        public void Die(string cause)
        {
            if (IsDead) return;
            IsDead = true;
            Drop();
            RespawnSystem.OnPlayerDied(this, cause);
        }

        public void FinishRespawn(Vector3 carPark)
        {
            TempWorkerCount++;
            PlayerName = "Temp worker #" + TempWorkerCount;

            // A dead driver leaves the seat. Without this the new body stays
            // PARENTED to the forklift with both CharacterControllers enabled:
            // it is dragged around by the machine, can never walk again, and
            // is immune to being run over.
            if (Driving != null)
            {
                Driving.ForceDismount(this);
                Driving = null;
            }
            if (transform.parent != null) transform.SetParent(null, true);

            _cc.enabled = false;
            transform.position = carPark;
            _cc.enabled = true;
            // A teleport is not a fall, and the new body starts upright.
            _vel = Vector3.zero;
            _achieved = Vector3.zero;
            _apexY = carPark.y;
            _platform = null;
            _jumping = false;
            _viewDip = 0f;
            IsCrouched = false;
            _curHeight = StandHeight;
            _cc.height = StandHeight;
            _cc.center = new Vector3(0, StandHeight * 0.5f, 0);
            _eye = StandEye;
            _exposure = 0f;
            IsDead = false;
        }
    }
}
