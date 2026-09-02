using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.DebugTools
{
    /// <summary>
    /// The Milestone 2 debug interface. Ugly on purpose: IMGUI, raw numbers,
    /// graphs, a slider for every constant, speed controls and a button for
    /// every player action. No 3D, no art, no scene wiring — it builds itself.
    /// It stays in the project permanently; this is where the game is balanced.
    /// F1 toggles visibility.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class DebugConsole : MonoBehaviour, Game.Runtime.World.IUiWindow
    {
        private static readonly string[] TabNames =
            { "Meters", "Graphs", "Tuning", "Actions", "Contracts", "Events", "Scenario" };

        public static DebugConsole Instance { get; private set; }

        private SimDriver _driver;
        private TuningPanel _tuning;
        private GraphPanel _graphs;
        private int _tab;
        // A tab click lands in a Repaint pass; switching the drawn controls
        // there desyncs IMGUI's layout, so the switch waits for the next
        // Layout event. Same for anything else that changes how many lines
        // are drawn inside the pass that triggered it: a manual step (new
        // events, contracts), a folder rescan, a shortened event filter.
        private int _pendingTab = -1;
        private int _pendingStepHours;
        private bool _pendingRescan;
        private string _pendingEventFilter;
        private Vector2 _scroll;
        // Enqueue rejected a number: which button, and until when it glows.
        private string _badInput;
        private float _badInputUntil;

        // Actions tab input buffers (strings so half-typed numbers survive).
        private string _infKw = "300", _infDays = "180";
        private string _trainKw = "400", _trainDays = "18";
        private string _addNodes = "12", _localFte = "8", _visualPts = "10";
        private string _eventFilter = "";
        private string _seedText = "42";

        private static FieldInfo[] _reportFields;

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _tuning = new TuningPanel();
            _graphs = new GraphPanel();
            if (_reportFields == null)
                _reportFields = typeof(TickReport).GetFields(BindingFlags.Public | BindingFlags.Instance);
            _seedText = _driver.Seed.ToString(CultureInfo.InvariantCulture);
            Instance = this;
            Game.Runtime.World.UiWindows.Register(this);
        }

        private void OnDestroy()
        {
            Game.Runtime.World.UiWindows.Unregister(this);
            if (Instance == this) Instance = null;
        }

        /// <summary>The console is modal: it owns the cursor while it is open,
        /// because its whole point is clicking sliders and buttons.</summary>
        public static bool IsOpen
        {
            get { return Instance != null && Game.Runtime.World.UiWindows.IsOpen(Instance); }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) Game.Runtime.World.UiWindows.Toggle(this);
        }

        // --- IUiWindow ----------------------------------------------------
        public int Id { get { return 910; } }
        public string Title { get { return "GNP debug console — F1 closes, drag to move, corner to resize"; } }

        // Opacity tints the window and control BACKGROUNDS only: a
        // see-through console still has to be readable, and text at 40 %
        // alpha over the site is not.
        public Game.Runtime.World.UiWindowFlags Flags
        {
            get
            {
                return Game.Runtime.World.UiWindowFlags.Resizable |
                       Game.Runtime.World.UiWindowFlags.TitleDragOnly |
                       Game.Runtime.World.UiWindowFlags.Tinted;
            }
        }

        public Rect DefaultRect(float w, float h)
        {
            // A large but not total window, inside the view.
            return new Rect(20, 20, Mathf.Min(1040f, w - 340f),
                Mathf.Min(620f, h - 20f - Game.Runtime.World.UiScaler.BottomReserve));
        }

        public void OnOpened() { }
        public void OnClosed() { }

        public void DrawContents(int id)
        {
            if (_driver.Sim == null)
            {
                GUILayout.Label("no simulation — balance.tuning or the scenario failed to load (see Console log)");
                return;
            }
            if (Event.current.type == EventType.Layout)
            {
                if (_pendingTab >= 0) { _tab = _pendingTab; _pendingTab = -1; }
                if (_pendingStepHours > 0) { _driver.Step(_pendingStepHours); _pendingStepHours = 0; }
                if (_pendingRescan) { _driver.RefreshScenarioList(); _pendingRescan = false; }
                if (_pendingEventFilter != null) { _eventFilter = _pendingEventFilter; _pendingEventFilter = null; }
            }
            DrawTopBar();
            int clicked = GUILayout.Toolbar(_tab, TabNames, GUILayout.Height(26));
            if (clicked != _tab) _pendingTab = clicked;
            GUILayout.Space(4);
            switch (_tab)
            {
                case 0: DrawMeters(); break;
                case 1: _graphs.Draw(_driver); break;
                case 2: _tuning.Draw(_driver); break;
                case 3: DrawActions(); break;
                case 4: DrawContracts(); break;
                case 5: DrawEvents(); break;
                case 6: DrawScenario(); break;
            }
        }

        // ------------------------------------------------------------------
        private void DrawTopBar()
        {
            var s = _driver.Sim.State;
            TickReport r = _driver.Latest;
            var ci = CultureInfo.InvariantCulture;

            GUILayout.BeginHorizontal();
            long tick = s.Tick;
            GUILayout.Label(
                "Y" + (tick / SimClock.TicksPerYear) +
                " D" + SimClock.DayOfYear(tick).ToString("000", ci) +
                " " + SimClock.HourOfDay(tick).ToString("00", ci) + ":00" +
                "  M" + SimClock.Month(tick),
                GUILayout.Width(150));

            // On a client the LOCAL driver is architecturally frozen — the
            // button label and the intent both come from the HOST's replicated
            // time state, or pausing would instead stomp the host's speed.
            var netForTime = Game.Runtime.World.GameBootstrap.Net;
            bool isNetClient = netForTime != null && netForTime.IsClient;
            bool shownPaused = isNetClient ? netForTime.RemoteTimePaused : _driver.Paused;
            float shownTps = isNetClient ? netForTime.RemoteTps : _driver.TicksPerSecond;
            if (GUILayout.Button(shownPaused ? "▶ run" : "▮▮ pause", GUILayout.Width(70)))
                SetTime(!shownPaused, shownTps);
            if (!isNetClient)
            {
                if (GUILayout.Button("+1h", GUILayout.Width(40))) _pendingStepHours += 1;
                if (GUILayout.Button("+1d", GUILayout.Width(40))) _pendingStepHours += 24;
                if (GUILayout.Button("+30d", GUILayout.Width(48))) _pendingStepHours += 24 * 30;
            }

            GUILayout.Label("speed:", GUILayout.Width(44));
            DrawSpeedButton("1 h/10s", 0.1f);
            DrawSpeedButton("1 h/s", 1f);
            DrawSpeedButton("1 d/s", 24f);
            DrawSpeedButton("1 w/s", 168f);
            DrawSpeedButton("1 mo/s", 730f);
            DrawSpeedButton("6 mo/s", 4380f);

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "GNI " + r.Gni.ToString("0.0", ci) +
                " (" + r.Stage + ")   Rep " + r.Reputation.ToString("0.0", ci) +
                "   Cash " + r.CashEur.ToString("N0", ci) +
                "   Tier " + s.GridTier);
            GUILayout.EndHorizontal();

            if (s.RunOver)
            {
                Color prev = GUI.color;
                GUI.color = Color.red;
                GUILayout.Label("RUN OVER: " + s.RunOverReason);
                GUI.color = prev;
            }
        }

        private void DrawSpeedButton(string label, float tps)
        {
            var net = Game.Runtime.World.GameBootstrap.Net;
            bool client = net != null && net.IsClient;
            bool paused = client ? net.RemoteTimePaused : _driver.Paused;
            float cur = client ? net.RemoteTps : _driver.TicksPerSecond;
            bool active = !paused && Mathf.Approximately(cur, tps);
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(56)) && !active)
                SetTime(false, tps);
        }

        /// <summary>Time control is an intent: on a client it travels to the
        /// host (whose ledger names the sender) and the LOCAL sim stays
        /// paused; everywhere else it applies directly.</summary>
        private void SetTime(bool paused, float tps)
        {
            var net = Game.Runtime.World.GameBootstrap.Net;
            if (net != null && net.IsClient)
            {
                net.SendTimeControl(paused, tps);
                return;
            }
            _driver.TicksPerSecond = tps;
            _driver.Paused = paused;
            Game.Runtime.World.GameBootstrap.AddLedger(
                net != null && net.IsHost ? net.LocalPlayerName : "operator",
                paused ? "paused the clock" : "set speed " + tps.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " t/s");
        }

        // ------------------------------------------------------------------
        private void DrawMeters()
        {
            var ci = CultureInfo.InvariantCulture;
            TickReport r = _driver.Latest;
            object boxed = r;
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("— TickReport (every meter, last tick) —");
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (FieldInfo f in _reportFields)
            {
                object v = f.GetValue(boxed);
                string text;
                if (v is double d) text = d.ToString("0.###", ci);
                else text = v != null ? v.ToString() : "-";
                GUILayout.Label(f.Name + " = " + text, GUILayout.Width(280));
                if (++col == 4) { col = 0; GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();

            var s = _driver.Sim.State;
            GUILayout.Space(8);
            GUILayout.Label("— SimState extras —");
            GUILayout.Label("nodes " + s.NodesInstalled +
                "   spot " + (s.SpotEnabled ? "on" : "off") +
                " depth " + s.SpotDepthKw.ToString("0", ci) + " kW" +
                "   localFTE " + s.LocalFte.ToString("0", ci) +
                "   visualPts " + s.VisualPoints.ToString("0", ci) +
                "   battCycles " + s.BatteryCycles.ToString("0.0", ci));
            GUILayout.Label("drought " + (s.DroughtActive ? "ACTIVE" : "off") +
                "   dryStreak " + s.DryStreakDays + "d   wetStreak " + s.WetStreakDays + "d" +
                "   sabotageExposure " + s.SabotageExposureTicks.ToString("0", ci) + "h" +
                "   fibreCutUntil " + (s.FibreCutUntilTick > s.Tick ? "t" + s.FibreCutUntilTick : "-"));
            // Effective battery capacity mirrors Simulation.EffectiveBatteryCap
            // (linear fade, power.md §4.3); duplicated here read-only for display.
            double effBattFrac = Math.Max(0.0, 1.0 - 0.2 * s.BatteryCycles / Math.Max(1e-9, _driver.Balance.BatteryCycleLife));
            GUILayout.Label("diesel policy " + s.Diesel +
                "   battEffCap " + (s.BatteryKwhCap * effBattFrac).ToString("0", ci) + "/" + s.BatteryKwhCap.ToString("0", ci) + " kWh");
            GUILayout.Label("petition " + s.PetitionSignatures.ToString("0", ci) +
                "   gniAtTrigger " + (s.PetitionSignatures > 0 ? s.GniAtPetitionTrigger.ToString("0.0", ci) : "-") +
                "   referendumVote " + (s.ReferendumVoteTick >= 0 ? "day " + SimClock.DayIndex(s.ReferendumVoteTick) : "-") +
                "   won " + s.ReferendumsWon);
            if (s.Permit != null)
                GUILayout.Label("permit in flight: tier " + s.Permit.TargetTier +
                    ", " + (s.Permit.RemainingLeadTicks / 24.0).ToString("0.0", ci) + " days left, EUR " +
                    s.Permit.CapexPaidEur.ToString("N0", ci) + " committed");
            else
                GUILayout.Label("permit: none in flight");
            GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        private void DrawActions()
        {
            GUILayout.Label("Every player action, applied at the START of the next tick (step or run to see the effect).");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Inference: base kW", GUILayout.Width(120));
            _infKw = GUILayout.TextField(_infKw, GUILayout.Width(60));
            GUILayout.Label("days", GUILayout.Width(36));
            _infDays = GUILayout.TextField(_infDays, GUILayout.Width(60));
            ActionButton("SIGN_INFERENCE", 140, CommandKind.SignInference, _infKw, _infDays);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Training: block kW", GUILayout.Width(120));
            _trainKw = GUILayout.TextField(_trainKw, GUILayout.Width(60));
            GUILayout.Label("days", GUILayout.Width(36));
            _trainDays = GUILayout.TextField(_trainDays, GUILayout.Width(60));
            ActionButton("SIGN_TRAINING", 140, CommandKind.SignTraining, _trainKw, _trainDays);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SPOT on", GUILayout.Width(90)))
                Game.Runtime.World.GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetSpot, A = 1 }, null);
            if (GUILayout.Button("SPOT off", GUILayout.Width(90)))
                Game.Runtime.World.GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetSpot, A = 0 }, null);
            // The tier button computes "next" from the AUTHORITATIVE tier — on
            // a client the local sim is frozen at the join-time value, which
            // would make the netted ApplyTier a permanent no-op.
            var tierNet = Game.Runtime.World.GameBootstrap.Net;
            int curTier = tierNet != null && tierNet.IsClient
                ? Game.Runtime.World.GameBootstrap.CurrentReport.GridTier
                : _driver.Sim.State.GridTier;
            int nextTier = curTier + 1;
            if (nextTier <= 4 && GUILayout.Button("APPLY_TIER " + nextTier, GUILayout.Width(110)))
                Game.Runtime.World.GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.ApplyTier, A = nextTier }, null);
            GUILayout.Label("diesel:", GUILayout.Width(44));
            DrawDieselButton("Never", DieselPolicy.Never);
            DrawDieselButton("ProtectSla", DieselPolicy.ProtectSla);
            DrawDieselButton("Always", DieselPolicy.Always);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ADD_NODES", GUILayout.Width(80));
            _addNodes = GUILayout.TextField(_addNodes, GUILayout.Width(60));
            ActionButton("install", 70, CommandKind.AddNodes, _addNodes, null);
            GUILayout.Label("SET_LOCAL_FTE", GUILayout.Width(100));
            _localFte = GUILayout.TextField(_localFte, GUILayout.Width(60));
            ActionButton("set", 50, CommandKind.SetLocalFte, _localFte, null);
            GUILayout.Label("SET_VISUAL", GUILayout.Width(80));
            _visualPts = GUILayout.TextField(_visualPts, GUILayout.Width(60));
            ActionButton("set ", 50, CommandKind.SetVisualPoints, _visualPts, null);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            Game.Runtime.World.FullBuildActions.DrawGui();
        }

        /// <summary>A command button whose number fields may be half-typed:
        /// a rejected value turns the button red for two seconds instead of
        /// silently doing nothing.</summary>
        private void ActionButton(string label, float width, CommandKind kind, string aText, string bText)
        {
            bool bad = _badInput == label && Time.unscaledTime < _badInputUntil;
            Color prev = GUI.backgroundColor;
            if (bad) GUI.backgroundColor = Color.red;
            bool clicked = GUILayout.Button(bad ? "not a number" : label, GUILayout.Width(width));
            GUI.backgroundColor = prev;
            if (clicked && !Enqueue(kind, aText, bText))
            {
                _badInput = label;
                _badInputUntil = Time.unscaledTime + 2f;
            }
        }

        private void DrawDieselButton(string label, DieselPolicy policy)
        {
            bool active = _driver.Sim.State.Diesel == policy;
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(80)) && !active)
                Game.Runtime.World.GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.SetDieselPolicy, A = (int)policy }, null);
        }

        /// <summary>False when a field is not a number. Accepts a decimal
        /// comma: the game is played on German keyboards.</summary>
        private bool Enqueue(CommandKind kind, string aText, string bText)
        {
            double a = 0, b = 0;
            if (aText != null && !TryParseNumber(aText, out a)) return false;
            if (bText != null && !TryParseNumber(bText, out b)) return false;
            // Recorded (and net-routed on a client): console actions are real
            // history — the save's replay must reproduce them.
            Game.Runtime.World.GameBootstrap.SendCommand(new SimCommand { Kind = kind, A = a, B = b }, null);
            return true;
        }

        public static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        // ------------------------------------------------------------------
        private void DrawContracts()
        {
            var ci = CultureInfo.InvariantCulture;
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(Pad("id", 5) + Pad("type", 11) + Pad("status", 16) + Pad("kW", 8) +
                Pad("uptime-mo", 11) + Pad("progress", 10) + Pad("revenue", 12) + Pad("penalties", 12));
            foreach (Contract c in _driver.Sim.State.Contracts)
            {
                string uptime = c.MonthRequestedKwh > 1e-6
                    ? (100.0 * c.MonthDeliveredKwh / c.MonthRequestedKwh).ToString("0.0", ci) + "%"
                    : "-";
                string progress = c.Type == ContractType.Training && c.RequiredKwh > 0
                    ? (100.0 * c.ProgressKwh / c.RequiredKwh).ToString("0.0", ci) + "%"
                    : "-";
                GUILayout.Label(
                    Pad("#" + c.Id, 5) + Pad(c.Type.ToString(), 11) + Pad(c.Status.ToString(), 16) +
                    Pad(c.BaseKw.ToString("0", ci), 8) + Pad(uptime, 11) + Pad(progress, 10) +
                    Pad(c.TotalRevenueEur.ToString("N0", ci), 12) +
                    Pad(c.TotalPenaltyEur.ToString("N0", ci), 12));
            }
            GUILayout.EndScrollView();
        }

        private static string Pad(string s, int w)
        {
            return s.Length >= w ? s + " " : s.PadRight(w);
        }

        // ------------------------------------------------------------------
        private void DrawEvents()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("filter:", GUILayout.Width(40));
            string typed = GUILayout.TextField(_eventFilter, GUILayout.Width(200));
            if (typed != _eventFilter) _pendingEventFilter = typed;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);
            var events = _driver.Sim.State.Events;
            int shown = 0;
            for (int i = events.Count - 1; i >= 0 && shown < 300; i--)
            {
                SimEvent e = events[i];
                string line = "d" + SimClock.DayIndex(e.Tick).ToString("000") +
                    " h" + SimClock.HourOfDay(e.Tick).ToString("00") +
                    " [" + e.Category + "] " + e.Message;
                if (_eventFilter.Length > 0 &&
                    line.IndexOf(_eventFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                GUILayout.Label(line);
                shown++;
            }
            GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        private void DrawScenario()
        {
            var ci = CultureInfo.InvariantCulture;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scenario (restart keeps the LIVE-TUNED balance — that is the balancing loop):");
            if (GUILayout.Button("rescan folder", GUILayout.Width(110)))
                _pendingRescan = true;
            GUILayout.EndHorizontal();
            foreach (string name in _driver.ListScenarios())
            {
                GUILayout.BeginHorizontal();
                bool current = name == _driver.ScenarioName;
                GUILayout.Label(current ? "▶" : " ", GUILayout.Width(16));
                if (GUILayout.Button(name, GUILayout.Width(320)))
                {
                    if (ulong.TryParse(_seedText, NumberStyles.Integer, ci, out ulong seed))
                        _driver.Restart(name, seed);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("seed:", GUILayout.Width(40));
            _seedText = GUILayout.TextField(_seedText, GUILayout.Width(100));
            if (GUILayout.Button("Restart current", GUILayout.Width(130)))
            {
                if (ulong.TryParse(_seedText, NumberStyles.Integer, ci, out ulong seed))
                    _driver.Restart(_driver.ScenarioName, seed);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("balance file: " + _driver.BalancePath);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save tuned values to file", GUILayout.Width(200)))
                _driver.SaveBalanceToFile();
            if (GUILayout.Button("Reload file + restart", GUILayout.Width(180)))
                _driver.ReloadBalanceFromFileAndRestart();
            GUILayout.Label(_driver.LastFileOpMessage);
            GUILayout.EndHorizontal();
        }
    }
}
