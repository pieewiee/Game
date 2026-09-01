using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.Media
{
    /// <summary>The local paper's inbox. Everything the town saw ends up here,
    /// and the ticker at the bottom of the screen reads it out.</summary>
    public static class NewsFeed
    {
        public static readonly List<string> Items = new List<string>();
        public static event Action<string> OnPosted;

        public static void Post(string item)
        {
            Items.Add(item);
            if (Items.Count > 200) Items.RemoveAt(0);
            OnPosted?.Invoke(item);
        }
    }

    /// <summary>
    /// The scrolling local-news ticker (M5). Feeds on NewsFeed posts plus
    /// generated items from sim events — stage changes, droughts, permits — in
    /// the tone rules' register: residents factually correct, only the
    /// operator's language absurd.
    /// </summary>
    public sealed class NewsTicker : MonoBehaviour
    {
        private readonly Queue<string> _queue = new Queue<string>();
        private string _current = "";
        private float _offset;
        private float _textW;      // measured on first draw; 0 = not yet
        private GUIStyle _style;
        private int _lastEventCount;
        private bool _wasDiesel;

        private void OnEnable() { NewsFeed.OnPosted += Enqueue; }
        private void OnDisable() { NewsFeed.OnPosted -= Enqueue; }

        private void Enqueue(string s) { _queue.Enqueue(s); }

        private void Update()
        {
            var driver = World.GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return;

            // A client generates nothing: the host's copies of these stories
            // arrive over the wire (its own sim is paused and stale). It only
            // scrolls what lands in the feed.
            var net = World.GameBootstrap.Net;
            bool generate = net == null || !net.IsClient;

            if (generate)
            {
                // Turn notable sim events into news as it appears. Generated
                // items go through NewsFeed.Post so they reach the archive —
                // and, when hosting, every client.
                var events = driver.Sim.State.Events;
                for (; _lastEventCount < events.Count; _lastEventCount++)
                {
                    SimEvent e = events[_lastEventCount];
                    string item = Translate(e);
                    if (item != null) NewsFeed.Post(item);
                }
                if (_lastEventCount > events.Count) _lastEventCount = events.Count; // restart

                // The plume story writes itself from the wind.
                TickReport r = driver.Latest;
                bool diesel = r.DieselKwh > 1e-6;
                if (diesel && !_wasDiesel)
                {
                    NewsFeed.Post(r.WindTowardTown
                        ? "The generator is running and the wind is from the east. Residents on the lane are photographing the plume over their gardens."
                        : "The site's generator is running. Today's wind carries the plume away from town; nobody has said thank you.");
                }
                _wasDiesel = diesel;
            }

            // Scroll.
            if (_current.Length == 0 && _queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _offset = World.UiScaler.W;
                _textW = 0f;
            }
            if (_current.Length > 0)
            {
                _offset -= Time.unscaledDeltaTime * 120f;
                // The item is done once its measured width has scrolled off
                // the left edge; the character estimate only covers the
                // frames before the first draw measured it.
                float end = _textW > 0f ? _textW + 20f : _current.Length * 9f;
                if (_offset < -end) _current = "";
            }
        }

        private static string Translate(SimEvent e)
        {
            switch (e.Category)
            {
                case "escalation": return "LOCAL NEWS: " + e.Message;
                case "referendum": return "LOCAL NEWS: " + e.Message;
                case "water": return "LOCAL NEWS: " + e.Message;
                case "permit":
                    return e.Message.Contains("REFUSED")
                        ? "LOCAL NEWS: the utility has refused the site's grid application. Neighbours call the decision \"overdue\"."
                        : e.Message.Contains("GRANTED")
                            ? "LOCAL NEWS: the site's grid upgrade was approved. The operator thanks the community for \"its continued partnership\"."
                            : null;
                default: return null;
            }
        }

        private void OnGUI()
        {
            if (_current.Length == 0) return;
            World.UiScaler.Begin();
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = false };
            if (_textW <= 0f) _textW = _style.CalcSize(new GUIContent(_current)).x;
            GUI.color = Color.black;
            GUI.Label(new Rect(0, World.UiScaler.H - 30, World.UiScaler.W, 26), "", GUI.skin.box);
            GUI.color = Palette();
            GUI.Label(new Rect(_offset, World.UiScaler.H - 28, _textW + 8f, 26), _current, _style);
            GUI.color = Color.white;
            World.UiScaler.End();
        }

        private static Color Palette() { return new Color(0.98f, 0.92f, 0.75f); }
    }

    /// <summary>
    /// The Program bulletin editor (M5): a free-text field and the
    /// responsible-employee dropdown, at a terminal in the office. Publishing
    /// is one click with no confirmation; the mechanism (credibility decay,
    /// mockery backfire, diminishing naming returns) lives in the sim, and the
    /// standing cost of a naming lands on the named player via the net session.
    /// </summary>
    public sealed class BulletinEditor : MonoBehaviour
    {
        public static BulletinEditor Instance { get; private set; }
        public bool IsOpen { get; private set; }
        private string _text = "The Good Neighbor Program continues to deliver measurable benefit to the region.";
        private int _namedIndex; // 0 = nobody
        private Rect _win = new Rect(200, 120, 560, 260);

        private void Awake() { Instance = this; }

        public void Open()
        {
            IsOpen = true;
            // One desk at a time: two editors stacked on each other is how a
            // click meant for "discard" lands on "PUBLISH".
            if (ContractEditor.Instance != null) ContractEditor.Instance.Close();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Closes the editor and hands the mouse back to the player
        /// unless another modal still owns it. Leaving the cursor free after
        /// PUBLISH meant the next click did nothing and Escape did the
        /// opposite of what the player expected.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (!World.GameBootstrap.UiWantsCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                // This Escape belongs to the editor — the player rig must not
                // also toggle the cursor with it this frame.
                World.GameBootstrap.EscConsumedFrame = Time.frameCount;
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            // GUILayout.Window, not GUI.Window: the window body uses GUILayout
            // controls, which need the layouting window variant.
            World.UiScaler.Begin();
            _win = World.UiScaler.Clamp(GUILayout.Window(913, _win, DrawWindow, "PROGRAM BULLETIN — draft"));
            World.UiScaler.End();
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("Bulletin text (the town reads exactly what you publish):");
            _text = GUILayout.TextArea(_text, GUILayout.Height(90));

            string[] names = RosterNames();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Responsible employee:", GUILayout.Width(150));
            _namedIndex = GUILayout.Toolbar(Mathf.Clamp(_namedIndex, 0, names.Length - 1), names);
            GUILayout.EndHorizontal();

            // The replicated report, so a client sees the credibility its
            // bulletin will actually be judged against — not its stale sim.
            string cred = World.GameBootstrap.CurrentReport.Credibility
                .ToString("0.00", CultureInfo.InvariantCulture);
            GUILayout.Label("Current credibility: " + cred + "  (each bulletin spends some; below 0.20 they backfire)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PUBLISH", GUILayout.Width(120)))
            {
                bool named = _namedIndex > 0;
                string who = named ? names[_namedIndex] : null;
                World.GameBootstrap.SendCommand(
                    new SimCommand { Kind = CommandKind.IssueBulletin, A = named ? 1 : 0 },
                    "published a bulletin" + (named ? " naming " + who : ""));
                NewsFeed.Post("PRESS RELEASE: \"" + _text + "\"" +
                    (named ? " A responsible employee (" + who + ") has been identified." : ""));
                if (named && World.GameBootstrap.Net != null)
                    World.GameBootstrap.Net.ApplyNamingPenalty(who);
                Close();
            }
            if (GUILayout.Button("discard", GUILayout.Width(90))) Close();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private static string[] RosterNames()
        {
            var net = World.GameBootstrap.Net;
            var local = World.GameBootstrap.LocalPlayer;
            if (net != null && net.Active)
            {
                var roster = net.RosterSnapshot();
                var arr = new string[roster.Count + 1];
                arr[0] = "(nobody)";
                for (int i = 0; i < roster.Count; i++) arr[i + 1] = roster[i];
                return arr;
            }
            return new[] { "(nobody)", local != null ? local.PlayerName : "Operator" };
        }
    }

    /// <summary>The physical terminal in the office that opens the editor.</summary>
    public sealed class BulletinTerminal : MonoBehaviour, World.IInteractable
    {
        public string Prompt(World.PlayerRig player) { return "E: draft a Program bulletin"; }
        public void Interact(World.PlayerRig player)
        {
            if (BulletinEditor.Instance != null) BulletinEditor.Instance.Open();
        }
    }

    /// <summary>
    /// The contract desk (compute-contracts.md, at slice depth): signing moves
    /// from the debug console into the world, because "the group signs a
    /// training deadline and then the summer happens" IS the core loop and
    /// deserves a physical place. Standard terms only — no offer market yet.
    /// Anyone can sign anything; the ledger records who did.
    /// </summary>
    public sealed class ContractEditor : MonoBehaviour
    {
        public static ContractEditor Instance { get; private set; }
        public bool IsOpen { get; private set; }
        private Rect _win = new Rect(240, 100, 520, 300);

        private void Awake() { Instance = this; }

        public void Open()
        {
            IsOpen = true;
            if (BulletinEditor.Instance != null) BulletinEditor.Instance.Close();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (!World.GameBootstrap.UiWantsCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                World.GameBootstrap.EscConsumedFrame = Time.frameCount;
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            World.UiScaler.Begin();
            _win = World.UiScaler.Clamp(GUILayout.Window(914, _win, DrawWindow, "COMPUTE CONTRACTS — standard terms"));
            World.UiScaler.End();
        }

        private void Sign(CommandKind kind, double kw, double days, string desc)
        {
            World.GameBootstrap.SendCommand(new SimCommand { Kind = kind, A = kw, B = days }, desc);
            NewsFeed.Post("The Program has signed " + desc.Substring(desc.IndexOf(' ') + 1) +
                ". Capacity planning is described as \"an evolving conversation\".");
        }

        private void DrawWindow(int id)
        {
            var ci = CultureInfo.InvariantCulture;
            TickReport r = World.GameBootstrap.CurrentReport;
            GUILayout.Label("Site: " + r.NodesInstalled + " nodes (" +
                (r.NodesInstalled * 10).ToString(ci) + " kW ceiling)   contracts active: " + r.ActiveContracts +
                "   requested now: " + r.RequestedBillableKw.ToString("0", ci) + " kW" +
                "   reputation: " + r.Reputation.ToString("0.0", ci));
            GUILayout.Space(6);

            GUILayout.Label("Inference floors — steady pay, modest rate:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("100 kW · 6 months"))
                Sign(CommandKind.SignInference, 100, 180, "signed a 100 kW inference floor (180 days)");
            if (GUILayout.Button("200 kW · 6 months"))
                Sign(CommandKind.SignInference, 200, 180, "signed a 200 kW inference floor (180 days)");
            if (GUILayout.Button("300 kW · 1 year"))
                Sign(CommandKind.SignInference, 300, 350, "signed a 300 kW inference floor (350 days)");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.Label("Training blocks — deadline pay, SLA teeth:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("200 kW · 14 days"))
                Sign(CommandKind.SignTraining, 200, 14, "signed a 200 kW training block (14-day deadline)");
            if (GUILayout.Button("400 kW · 18 days"))
                Sign(CommandKind.SignTraining, 400, 18, "signed a 400 kW training block (18-day deadline)");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            GUILayout.Label("Nothing on this desk checks whether the site can deliver.\nThat is your job. The penalty clause is theirs.");
            if (GUILayout.Button("close", GUILayout.Width(90))) Close();
            GUI.DragWindow();
        }
    }

    /// <summary>The physical contract desk in the office.</summary>
    public sealed class ContractTerminal : MonoBehaviour, World.IInteractable
    {
        public string Prompt(World.PlayerRig player) { return "E: review compute contracts"; }
        public void Interact(World.PlayerRig player)
        {
            if (ContractEditor.Instance != null) ContractEditor.Instance.Open();
        }
    }
}
