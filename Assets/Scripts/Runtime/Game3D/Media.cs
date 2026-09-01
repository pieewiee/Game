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
                _offset = Screen.width;
            }
            if (_current.Length > 0)
            {
                _offset -= Time.unscaledDeltaTime * 120f;
                if (_offset < -_current.Length * 9f) _current = "";
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
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            GUI.color = Color.black;
            GUI.Label(new Rect(0, Screen.height - 30, 4000, 26), "", GUI.skin.box);
            GUI.color = Palette();
            GUI.Label(new Rect(_offset, Screen.height - 28, 4000, 26), _current, style);
            GUI.color = Color.white;
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                IsOpen = false;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            // GUILayout.Window, not GUI.Window: the window body uses GUILayout
            // controls, which need the layouting window variant.
            _win = GUILayout.Window(913, _win, DrawWindow, "PROGRAM BULLETIN — draft");
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
                IsOpen = false;
            }
            if (GUILayout.Button("discard", GUILayout.Width(90))) IsOpen = false;
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
}
