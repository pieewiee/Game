using System;
using System.Globalization;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    /// <summary>
    /// The forklift (workplace-accidents.md §4.4): the only way pallets move,
    /// and a pure physics object. F enters and exits; WASD drives. Hitting a
    /// player is fatal; ramming a rack at speed destroys nodes; everything is
    /// deniable because driving the forklift is the job.
    /// </summary>
    public sealed class Forklift : MonoBehaviour, IInteractable
    {
        private const float MaxSpeed = 6f;
        private const float Accel = 8f;
        private const float TurnDegPerSec = 90f;
        private const float HarmSpeed = 2.2f;

        public PlayerRig DriverRig { get; private set; }
        private CharacterController _cc;
        private float _speed;
        private float _lastRackHit;

        public static Forklift Create(SiteRefs site, Vector3 pos)
        {
            var go = new GameObject("Forklift");
            go.transform.SetParent(site.Root, false);
            go.transform.position = pos;
            var pm = new ProcMesh();
            pm.Box(new Vector3(0, 0.7f, 0), new Vector3(1.2f, 1.1f, 1.9f), Palette.Amber);
            pm.Box(new Vector3(0, 1.7f, -0.4f), new Vector3(1.0f, 1.0f, 0.9f), Palette.Slate);   // cage
            pm.Box(new Vector3(-0.35f, 0.12f, 1.6f), new Vector3(0.14f, 0.1f, 1.2f), Palette.Ink); // forks
            pm.Box(new Vector3(0.35f, 0.12f, 1.6f), new Vector3(0.14f, 0.1f, 1.2f), Palette.Ink);
            MatLib.Spawn("ForkliftMesh", pm.Build("forklift"), go.transform, Vector3.zero, false);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.9f;
            cc.center = new Vector3(0, 1.0f, 0);
            return go.AddComponent<Forklift>();
        }

        private void Awake() { _cc = GetComponent<CharacterController>(); }

        public string Prompt(PlayerRig player)
        {
            return DriverRig == null ? "E: drive the forklift" : null;
        }

        public void Interact(PlayerRig player)
        {
            if (DriverRig == null && player.Driving == null)
            {
                DriverRig = player;
                player.Driving = this;
                // Two enabled CharacterControllers in one hierarchy fight each
                // other — the walking capsule sleeps while seated.
                player.SetBodyEnabled(false);
                player.transform.SetParent(transform, false);
                player.transform.localPosition = new Vector3(0, 1.1f, -0.4f);
            }
        }

        /// <summary>Called by the seated player's Update.</summary>
        public void Drive(PlayerRig player, Keyboard kb)
        {
            if (kb.eKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame)
            {
                // dismount
                DriverRig = null;
                player.Driving = null;
                player.transform.SetParent(null, true);
                player.transform.position = transform.position + transform.right * 1.6f;
                player.SetBodyEnabled(true);
                return;
            }
            float thrust = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float turn = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            _speed = Mathf.MoveTowards(_speed, thrust * MaxSpeed, Accel * Time.deltaTime);
            transform.Rotate(0, turn * TurnDegPerSec * Time.deltaTime, 0);
            Vector3 vel = transform.forward * _speed;
            vel.y = -2f;
            _cc.Move(vel * Time.deltaTime);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(_speed) < HarmSpeed) return;

            // The local player in the way is an incident.
            var rig = hit.collider.GetComponentInParent<PlayerRig>();
            if (rig != null && rig != DriverRig && !rig.IsDead)
            {
                rig.Die("was struck by the forklift");
                _speed = 0f;
                return;
            }

            // A remote player's avatar (host side): the kill goes over the wire.
            var netSession = GameBootstrap.Net;
            if (netSession != null && netSession.IsHost &&
                hit.collider.gameObject.name.StartsWith("Avatar ", StringComparison.Ordinal) &&
                ulong.TryParse(hit.collider.gameObject.name.Substring(7),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong clientId))
            {
                netSession.HostKillClient(clientId, "was struck by the forklift");
                _speed = 0f;
                return;
            }

            // A rack takes real damage — that is a rack's worth of hardware,
            // per workplace-accidents.md §4.4. Panels are rack children, so
            // the type check catches every face of the cabinet.
            if (hit.collider.GetComponentInParent<RackRemount>() != null &&
                Time.time - _lastRackHit > 2f)
            {
                _lastRackHit = Time.time;
                GameBootstrap.SendCommand(
                    new SimCommand { Kind = CommandKind.DestroyNodes, A = FacilityController.NodesPerRack },
                    "forklift into a rack (" + FacilityController.NodesPerRack + " nodes destroyed)");
                NewsFeed.Post("Materials-handling equipment made contact with live hardware. " +
                    "The Program calls it \"an unplanned rack-adjacent logistics event\".");
                _speed = 0f;
            }
        }
    }
}
