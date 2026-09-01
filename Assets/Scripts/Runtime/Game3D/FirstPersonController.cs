using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    /// <summary>
    /// The local player: CharacterController walking, mouse look, E to
    /// interact (held E for hold-controls), Q to drop, F enters the forklift.
    /// Capsule with a hard hat, per art-bible.md §3. Remote players are drawn
    /// by NetSession from replicated transforms.
    /// </summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        public const float InteractRange = 3.2f;

        public Camera Cam { get; private set; }
        public Carryable Carried { get; private set; }
        public bool ShiftHeld { get; private set; }
        public bool IsDead { get; private set; }
        public string PlayerName = "Operator";
        public int TempWorkerCount;
        public Forklift Driving;

        private CharacterController _cc;
        private Transform _carryAnchor;
        private float _yaw, _pitch;
        private float _fallSpeed;
        private IHoldInteractable _holding;
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
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.35f;
            _cc.center = new Vector3(0, 0.9f, 0);

            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = 0.08f;
            Cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();

            _carryAnchor = new GameObject("Carry").transform;
            _carryAnchor.SetParent(camGo.transform, false);
            _carryAnchor.localPosition = new Vector3(0.35f, -0.35f, 0.7f);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null) return;
            ShiftHeld = kb.leftShiftKey.isPressed;

            // Escape frees the cursor for the debug console; click re-locks.
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                Cursor.lockState != CursorLockMode.Locked && !GameBootstrap.UiCapturesMouse)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (IsDead) return;
            if (Driving != null) { Driving.Drive(this, kb); return; }

            // --- look ---
            if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * 0.08f;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                transform.rotation = Quaternion.Euler(0, _yaw, 0);
                Cam.transform.localRotation = Quaternion.Euler(_pitch, 0, 0);
            }

            // --- move ---
            Vector3 wish = Vector3.zero;
            if (kb.wKey.isPressed) wish += transform.forward;
            if (kb.sKey.isPressed) wish -= transform.forward;
            if (kb.dKey.isPressed) wish += transform.right;
            if (kb.aKey.isPressed) wish -= transform.right;
            float speed = ShiftHeld ? 6.5f : 3.6f;
            Vector3 vel = wish.normalized * speed;
            if (_cc.isGrounded) _fallSpeed = -0.5f;
            else _fallSpeed -= 14f * Time.deltaTime;
            vel.y = _fallSpeed;
            _cc.Move(vel * Time.deltaTime);

            // --- interact ---
            IInteractable target = CurrentTarget(out _);
            if (kb.eKey.isPressed && target is IHoldInteractable hold)
            {
                _holding = hold;
                hold.InteractHold(this, Time.deltaTime);
            }
            if (kb.eKey.wasReleasedThisFrame && _holding != null)
            {
                _holding.InteractRelease(this);
                _holding = null;
            }
            if (kb.eKey.wasPressedThisFrame && target != null && !(target is IHoldInteractable))
                target.Interact(this);
            if (kb.eKey.wasPressedThisFrame && target is DieselLever lever && lever.Running)
                lever.Interact(this); // stop is a tap, start is the hold
            if (kb.qKey.wasPressedThisFrame && Carried != null) Drop();
        }

        public IInteractable CurrentTarget(out string prompt)
        {
            prompt = null;
            if (Cam == null) return null;
            var ray = new Ray(Cam.transform.position, Cam.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, InteractRange))
            {
                var it = hit.collider.GetComponentInParent<MonoBehaviour>() as IInteractable;
                if (it == null && hit.collider.attachedRigidbody != null)
                    it = hit.collider.attachedRigidbody.GetComponent<IInteractable>();
                if (it == null)
                {
                    foreach (var mb in hit.collider.GetComponentsInParent<MonoBehaviour>())
                        if (mb is IInteractable cand) { it = cand; break; }
                }
                if (it != null) prompt = it.Prompt(this);
                return it;
            }
            return null;
        }

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
            _cc.enabled = false;
            transform.position = carPark;
            _cc.enabled = true;
            _exposure = 0f;
            IsDead = false;
        }
    }
}
