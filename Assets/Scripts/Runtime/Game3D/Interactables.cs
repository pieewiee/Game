using System;
using System.Globalization;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The physical-controls contract (docs/systems/coop-griefing.md §1): if it
    /// matters, it is an object in the world, anyone can operate it, and there
    /// is no confirmation dialog anywhere. Prompt() feeds the HUD; Interact()
    /// fires on E (or per-frame while holding for hold-style controls).
    /// </summary>
    public interface IInteractable
    {
        string Prompt(PlayerRig player);
        void Interact(PlayerRig player);
    }

    /// <summary>Optional: receives held-E every frame (diesel lever, held door).</summary>
    public interface IHoldInteractable
    {
        void InteractHold(PlayerRig player, float deltaTime);
        void InteractRelease(PlayerRig player);
    }

    /// <summary>A carryable object. While carried its collider is off and it
    /// parents to the player's carry anchor. Dropped on death.</summary>
    public class Carryable : MonoBehaviour, IInteractable
    {
        public string DisplayName = "object";
        public PlayerRig HeldBy { get; private set; }

        public virtual string Prompt(PlayerRig player)
        {
            if (player.Carried == this) return "Q: put down " + DisplayName;
            return player.Carried == null ? "E: pick up " + DisplayName : null;
        }

        public virtual void Interact(PlayerRig player)
        {
            if (player.Carried == null && HeldBy == null) player.PickUp(this);
        }

        public void OnPickedUp(PlayerRig player) { HeldBy = player; SetPhysics(false); }
        public void OnDropped() { HeldBy = null; SetPhysics(true); }

        private void SetPhysics(bool on)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = on;
        }
    }

    public sealed class WireCutters : Carryable
    {
        private void Awake() { DisplayName = "wire cutters"; }
    }

    /// <summary>A blanking panel (workplace-accidents.md §4.7): pulling it off a
    /// rack degrades airflow site-wide; putting it back restores it. Invisible
    /// cause, audible effect — the fans do the telling.</summary>
    public sealed class BlankingPanel : Carryable
    {
        public FacilityController Facility;
        public bool Mounted = true;

        private void Awake() { DisplayName = "blanking panel"; }

        public override string Prompt(PlayerRig player)
        {
            if (!Mounted) return base.Prompt(player);
            return player.Carried == null
                ? "E: pull blanking panel"
                : "blanking panel (your hands are full)";
        }

        public override void Interact(PlayerRig player)
        {
            // Slot panels never leave the rack: pulling one hides the slot and
            // hands the player a LOOSE panel object instead, so a later rack
            // rebuild cannot duplicate it and a remount cannot destroy it.
            // With full hands the panel would vanish into nothing, so refuse.
            if (Mounted && Facility != null && player.Carried == null)
                Facility.OnPanelPulled(this, player);
        }
    }

    /// <summary>The carryable panel object itself — what you hold, drop, lose
    /// behind the chillers, and eventually remount at any rack.</summary>
    public sealed class LoosePanel : Carryable
    {
        private void Awake() { DisplayName = "blanking panel"; }
    }

    /// <summary>Lives on each rack: remounting point for a carried blanking
    /// panel (workplace-accidents.md §4.7 — "putting it back restores it").</summary>
    public sealed class RackRemount : MonoBehaviour, IInteractable
    {
        public FacilityController Facility;

        public string Prompt(PlayerRig player)
        {
            if (Facility == null || !(player.Carried is LoosePanel)) return null;
            return Facility.HasMissingPanel ? "E: remount blanking panel" : null;
        }

        public void Interact(PlayerRig player)
        {
            if (Facility != null && player.Carried is LoosePanel)
                Facility.RemountPanel(player);
        }
    }

    /// <summary>A compartment door. Opens from both sides, always — and can be
    /// HELD shut with E from either side, which is the whole point
    /// (workplace-accidents.md §4.1).</summary>
    public sealed class Door : MonoBehaviour, IInteractable, IHoldInteractable
    {
        public string DoorName = "door";
        public bool IsOpen;
        public PlayerRig HeldShutBy { get; private set; }
        private float _lastHoldTime;
        private Transform _leaf;

        public void SetLeaf(Transform leaf) { _leaf = leaf; Apply(); }

        public string Prompt(PlayerRig player)
        {
            if (HeldShutBy != null && HeldShutBy != player) return DoorName + " is being held shut";
            return (IsOpen ? "E: close " : "E: open ") + DoorName + "  (hold E: hold shut)";
        }

        public void Interact(PlayerRig player)
        {
            if (HeldShutBy != null && HeldShutBy != player) return; // physically blocked
            IsOpen = !IsOpen;
            Apply();
        }

        public void InteractHold(PlayerRig player, float dt)
        {
            if (HeldShutBy == null || HeldShutBy == player)
            {
                HeldShutBy = player;
                _lastHoldTime = Time.unscaledTime;
                if (IsOpen) { IsOpen = false; Apply(); }
            }
        }

        public void InteractRelease(PlayerRig player)
        {
            if (HeldShutBy == player) HeldShutBy = null;
        }

        private void Update()
        {
            if (HeldShutBy != null && Time.unscaledTime - _lastHoldTime > 0.3f) HeldShutBy = null;
        }

        private void Apply()
        {
            if (_leaf != null) _leaf.gameObject.SetActive(!IsOpen);
        }
    }

    /// <summary>The evaporative inlet valve (workplace-accidents.md §4.8): E
    /// steps it down a quarter turn, wraps from shut back to open. Wide open
    /// drains the town, shut kills the cooling — one wheel, either disaster.</summary>
    public sealed class WaterValveWheel : MonoBehaviour, IInteractable
    {
        public double Position = 1.0;

        public string Prompt(PlayerRig player)
        {
            return "E: turn water inlet valve (now " +
                (Position * 100).ToString("0", CultureInfo.InvariantCulture) + "%)";
        }

        public void Interact(PlayerRig player)
        {
            Position -= 0.25;
            if (Position < -0.01) Position = 1.0;
            if (Position < 0) Position = 0;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetWaterValve, A = Position },
                "water valve to " + (Position * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
        }
    }

    /// <summary>The cooling setpoint dial (workplace-accidents.md §4.2): a real
    /// dial with NO safety limits. E lowers by 2 °C, Shift+E raises. Below
    /// freezing, the FreezeSystem takes over.</summary>
    public sealed class SetpointDial : MonoBehaviour, IInteractable
    {
        public double SetpointC = 18.0;

        public string Prompt(PlayerRig player)
        {
            return "E: setpoint -2°C / Shift+E: +2°C (now " +
                SetpointC.ToString("0", CultureInfo.InvariantCulture) + "°C)";
        }

        public void Interact(PlayerRig player)
        {
            SetpointC += player.ShiftHeld ? 2.0 : -2.0;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetSetpoint, A = SetpointC },
                "setpoint " + SetpointC.ToString("0", CultureInfo.InvariantCulture) + "°C");
        }
    }

    /// <summary>The diesel start lever: hold E for three seconds and it runs
    /// until pulled again. No dialog; the confirmation is the three seconds you
    /// spend holding it while someone runs across the yard shouting.</summary>
    public sealed class DieselLever : MonoBehaviour, IInteractable, IHoldInteractable
    {
        public const float HoldSeconds = 3f;
        public bool Running;
        private float _held;
        private float _lastHold;

        public string Prompt(PlayerRig player)
        {
            if (Running) return "E: stop the generator";
            return _held > 0.05f
                ? "hold E... " + (_held / HoldSeconds * 100f).ToString("0") + "%"
                : "hold E (3 s): start the generator";
        }

        public void Interact(PlayerRig player)
        {
            if (Running)
            {
                Running = false;
                GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetDieselManual, A = 0 },
                    "diesel lever off");
            }
        }

        public void InteractHold(PlayerRig player, float dt)
        {
            if (Running) return;
            _held += dt;
            _lastHold = Time.unscaledTime;
            if (_held >= HoldSeconds)
            {
                _held = 0f;
                Running = true;
                GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetDieselManual, A = 1 },
                    "diesel lever ON");
            }
        }

        public void InteractRelease(PlayerRig player) { _held = 0f; }

        private void Update()
        {
            if (_held > 0f && Time.unscaledTime - _lastHold > 0.3f) _held = 0f;
        }
    }

    /// <summary>The emergency power-off (workplace-accidents.md §4.5): legally
    /// mandated, therefore unprotected. One press kills the room and any
    /// running training job's progress since its last checkpoint.</summary>
    public sealed class EpoButton : MonoBehaviour, IInteractable
    {
        public FacilityController Facility;
        public Room Room;

        public string Prompt(PlayerRig player) { return "E: EMERGENCY POWER OFF (" + Room.Name + ")"; }

        public void Interact(PlayerRig player)
        {
            double frac = Facility != null ? Facility.RoomLoadFraction(Room) : 1.0;
            double hours = GameBootstrap.Driver != null ? GameBootstrap.Driver.Balance.EpoOutageHours : 1.0;
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.EpoTrip, A = frac, B = hours },
                "EPO pressed in " + Room.Name);
            NewsFeed.Post("Power was cut to " + Room.Name + " by the emergency stop. The button is, by law, unguarded.");
        }
    }

    /// <summary>The tool cabinet: home of the wire cutters, which exist for
    /// cable work and incidentally open every sealed pull station on site.</summary>
    public sealed class ToolCabinet : MonoBehaviour, IInteractable
    {
        public WireCutters Cutters;

        public string Prompt(PlayerRig player)
        {
            return Cutters != null && Cutters.HeldBy == null && Cutters.transform.parent == transform
                ? "E: take the wire cutters" : null;
        }

        public void Interact(PlayerRig player)
        {
            if (Cutters != null && player.Carried == null && Cutters.transform.parent == transform)
            {
                Cutters.transform.SetParent(null, true);
                player.PickUp(Cutters);
            }
        }
    }
}
