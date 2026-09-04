using UnityEngine;
using Game.Sim;

namespace Game.Runtime.World
{
    /// <summary>
    /// Physical, purchasable defenses against an incursion (Npcs.cs,
    /// Game.Sim.Simulation.Community.StepIncursion). Never a stat slider —
    /// docs/systems/incidents.md §3: each buy places or spawns a real object.
    /// This window is the one place the prices are shown; the purchase
    /// itself is CommandKind.BuySecurity, the same command a scripted
    /// scenario could issue, so a client's request travels to the host
    /// exactly like signing a contract does.
    /// </summary>
    public sealed class SecurityWindow : MonoBehaviour, IUiWindow
    {
        public static SecurityWindow Instance { get; private set; }

        private static readonly SecurityKind[] Kinds =
            { SecurityKind.Camera, SecurityKind.Floodlight, SecurityKind.Alarm, SecurityKind.Taser };
        private static readonly string[] Names = { "Camera", "Floodlight", "Alarm", "Taser" };
        // Camera/Floodlight/Alarm are sited hardware and each adds a small,
        // fixed amount to N_visual on purchase (sentiment.md §3's fence trap,
        // on a shorter lever) — said here so it is seen, not hidden. The
        // carried taser has no footprint of its own and is exempt.
        private static readonly string[] Blurbs =
        {
            "Earlier, more specific warning. Stops nothing by itself. Visible on the fence line.",
            "The only passive deterrent: draws site power, some incidents just abort. Visible.",
            "A cue on the HUD while an incident is active. No auto-resolve. Visible.",
            "Carried. Aim it in person at the fence line. No ammunition, no footprint of its own.",
        };

        private GameObject _taserProp;

        private void Awake()
        {
            Instance = this;
            UiWindows.Register(this);
        }

        private void OnDestroy()
        {
            UiWindows.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            SiteRefs site = GameBootstrap.Site;
            if (site == null) return;
            var root = new GameObject("Security").transform;
            root.SetParent(transform, false);
            SecurityLocker.Create(root, site);
        }

        /// <summary>The taser prop is spawned once, the first frame it is
        /// owned — lazily, because it does not exist in the world until
        /// bought (docs/systems/incidents.md §3: defenses have a footprint).</summary>
        private void Update()
        {
            if (_taserProp != null) return;
            SiteRefs site = GameBootstrap.Site;
            if (site == null || !GameBootstrap.CurrentReport.HasTaser) return;
            Transform root = transform.Find("Security");
            _taserProp = TaserTool.Create(root != null ? root : transform,
                site.GuardPostPos + new Vector3(1.6f, 0.85f, -0.5f)).gameObject;
        }

        public void Open() { UiWindows.Open(this); }
        public void Close() { UiWindows.Close(this); }

        // --- IUiWindow ------------------------------------------------------
        public int Id { get { return 916; } }
        public string Title { get { return "SECURITY"; } }
        public UiWindowFlags Flags { get { return UiWindowFlags.None; } }

        public Rect DefaultRect(float w, float h)
        {
            return new Rect(w * 0.5f - 220f, h * 0.5f - 150f, 440f, 0f);
        }

        public void OnOpened() { }
        public void OnClosed() { }

        public void DrawContents(int id)
        {
            var driver = GameBootstrap.Driver;
            Balance b = driver != null ? driver.Balance : null;
            TickReport r = GameBootstrap.CurrentReport;

            GUILayout.Label("Cash on hand: EUR " + r.CashEur.ToString("N0"));
            GUILayout.Space(4);
            for (int i = 0; i < Kinds.Length; i++)
            {
                bool owned = Owned(r, Kinds[i]);
                double cost = b != null ? Cost(b, Kinds[i]) : 0.0;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label(Names[i] + (owned ? " — installed" : " — EUR " + cost.ToString("N0")));
                GUILayout.Label(Blurbs[i], GUILayout.Width(300));
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                bool prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && !owned && r.CashEur >= cost;
                if (GUILayout.Button("Buy", GUILayout.Width(50), GUILayout.Height(36)))
                {
                    GameBootstrap.SendCommand(
                        new SimCommand { Kind = CommandKind.BuySecurity, A = (int)Kinds[i] },
                        Names[i].ToLowerInvariant() + " installed");
                }
                GUI.enabled = prevEnabled;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Close")) Close();
        }

        private static bool Owned(in TickReport r, SecurityKind kind)
        {
            switch (kind)
            {
                case SecurityKind.Camera: return r.HasCamera;
                case SecurityKind.Floodlight: return r.HasFloodlight;
                case SecurityKind.Alarm: return r.HasAlarm;
                default: return r.HasTaser;
            }
        }

        private static double Cost(Balance b, SecurityKind kind)
        {
            switch (kind)
            {
                case SecurityKind.Camera: return b.CameraCostEur;
                case SecurityKind.Floodlight: return b.FloodlightCostEur;
                case SecurityKind.Alarm: return b.AlarmCostEur;
                default: return b.TaserCostEur;
            }
        }
    }

    /// <summary>A small board beside the guard post. E opens the purchase
    /// window; the board itself never touches the sim.</summary>
    public sealed class SecurityLocker : MonoBehaviour, IInteractable
    {
        public static SecurityLocker Create(Transform root, SiteRefs site)
        {
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.7f, 0.9f, 0.16f), Palette.Slate);
            pm.Box(new Vector3(0f, 0.2f, 0.09f), new Vector3(0.5f, 0.45f, 0.02f), Palette.ProgramBlue);
            var pos = site.GuardPostPos + new Vector3(1.25f, 0.95f, -0.9f);
            var go = MatLib.Spawn("SecurityLocker", pm.Build("securityLocker"), root, pos, true);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            return go.AddComponent<SecurityLocker>();
        }

        public string Prompt(PlayerRig player) { return "E: security"; }

        public void Interact(PlayerRig player)
        {
            if (SecurityWindow.Instance != null) SecurityWindow.Instance.Open();
        }
    }

    /// <summary>The one weapon: a carried tool, no ammunition, no cooldown —
    /// the scarcity the design asks for is attention, not charge. Using it is
    /// Resident.Interact recognising a carried TaserTool while the resident
    /// is the active incursion actor (IsActiveSaboteur, Npcs.cs); this class
    /// only carries and displays it.</summary>
    public sealed class TaserTool : Carryable
    {
        private void Awake() { DisplayName = "taser"; }

        public static TaserTool Create(Transform root, Vector3 pos)
        {
            var pm = new ProcMesh();
            pm.Box(Vector3.zero, new Vector3(0.07f, 0.2f, 0.045f), Palette.Slate);
            pm.Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.025f, 0.03f, 0.09f), Palette.Amber);
            var go = MatLib.Spawn("Taser", pm.Build("taser"), root, pos, true);
            return go.AddComponent<TaserTool>();
        }
    }
}
