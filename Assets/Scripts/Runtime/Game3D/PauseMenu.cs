using System;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The Escape menu: resume, options, network, save/load, quit. It is the
    /// base of a small window stack — options (F3) and the network panel
    /// (F2) open on top of it and closing them returns here.
    ///
    /// Opening it pauses the simulation clock unless a network session is
    /// running: the host cannot stop time for everyone by tabbing out, and a
    /// client's local driver is paused anyway.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour, IUiWindow
    {
        public static PauseMenu Instance { get; private set; }

        public static bool IsOpen
        {
            get { return Instance != null && UiWindows.IsOpen(Instance); }
        }

        /// <summary>Debug shortcut wired by whoever owns the full build-out
        /// (editor / development builds only). Left null the button shows
        /// "(not wired)" and does nothing.</summary>
        public static Action FullBuildRequested;

        private const float QuitConfirmSeconds = 3f;

        // The menu only restores what it paused itself.
        private bool _ownsPause;
        private bool _pausedBefore;
        private float _quitArmedUntil = -1f;
        private string _status = "";
        private float _statusUntil;
        // Net state sampled at Layout: Join in the network window stacked on
        // top flips IsClient inside the same IMGUI event, and the extra
        // "host only" label would desync this window's layout.
        private bool _inSession, _isClient, _isHost, _canFullBuild;
        private bool _hasSave;

        public int Id { get { return 900; } }
        public string Title { get { return "GOOD NEIGHBOR PROGRAM — paused"; } }
        public UiWindowFlags Flags { get { return UiWindowFlags.DimBackdrop | UiWindowFlags.NoDrag; } }

        public Rect DefaultRect(float w, float h)
        {
            return new Rect(w / 2f - 180f, h / 2f - 210f, 360f, 420f);
        }

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

        public void OnOpened()
        {
            _quitArmedUntil = -1f;
            _status = "";
            _hasSave = System.IO.File.Exists(SaveSystem.SavePath);
            var driver = GameBootstrap.Driver;
            var net = GameBootstrap.Net;
            bool inSession = net != null && net.Active;
            _ownsPause = !inSession && driver != null;
            if (_ownsPause)
            {
                _pausedBefore = driver.Paused;
                driver.Paused = true;
            }
        }

        public void OnClosed()
        {
            var driver = GameBootstrap.Driver;
            var net = GameBootstrap.Net;
            // A session joined from the stacked network panel owns the clock
            // now: a client's local driver stays paused. A host started from
            // there gets its clock back like any solo game.
            bool client = net != null && net.IsClient;
            if (_ownsPause && driver != null && !client) driver.Paused = _pausedBefore;
            _ownsPause = false;
        }

        public void DrawContents(int id)
        {
            var net = GameBootstrap.Net;
            var saves = GameBootstrap.Saves;
            if (Event.current.type == EventType.Layout)
            {
                _inSession = net != null && net.Active;
                _isClient = net != null && net.IsClient;
                _isHost = net != null && net.IsHost;
                _canFullBuild = FullBuildRequested != null && !_isClient && FullBuildActions.CanApply();
            }
            bool prev = GUI.enabled;

            GUILayout.Label(_inSession
                ? (_isHost ? "hosting — session keeps running" : "client — session keeps running")
                : "simulation paused");
            GUILayout.Space(6);

            if (GUILayout.Button("Resume", GUILayout.Height(30))) UiWindows.Close(this);
            GUILayout.Space(4);
            if (GUILayout.Button("Options (F3)", GUILayout.Height(30)) && PlayerOptions.Instance != null)
                UiWindows.Open(PlayerOptions.Instance);
            if (GUILayout.Button("Network (F2)", GUILayout.Height(30)) && net != null)
                UiWindows.Open(net);
            GUILayout.Space(4);

            // Save/load are host-only: a client has no authoritative sim to
            // write, and loading one would fork it from the host's.
            bool canSave = !_isClient && saves != null;
            GUI.enabled = prev && canSave;
            if (GUILayout.Button("Save (F5)", GUILayout.Height(30)) && canSave)
            {
                if (saves.Save())
                {
                    _hasSave = true;
                    Flash("saved to " + SaveSystem.SavePath);
                }
                else Flash("save failed — see the news ticker");
            }
            // The reason lives in the button text: a conditional label here
            // would change the control count in the Save click's own pass.
            bool canLoad = canSave && _hasSave;
            GUI.enabled = prev && canLoad;
            if (GUILayout.Button(_hasSave ? "Load (F9)" : "Load (F9) — no save file yet",
                    GUILayout.Height(30)) && canLoad)
            {
                // Load leaves the driver paused so the state can be inspected;
                // resuming the menu must not undo that. A failed load (the
                // ticker says why) never reached that pause, so the clock
                // keeps what it had.
                if (saves.Load())
                {
                    _pausedBefore = true;
                    Flash("loaded — clock stays paused, unpause in the console");
                }
                else Flash("load failed — see the news ticker");
            }
            GUI.enabled = prev;
            if (_isClient) GUILayout.Label("save/load: host only");

            if (Application.isEditor || Debug.isDebugBuild)
            {
                GUILayout.Space(4);
                bool wired = FullBuildRequested != null;
                GUI.enabled = prev && _canFullBuild;
                if (GUILayout.Button(wired ? "Full build-out (debug)" : "Full build-out (debug) (not wired)",
                        GUILayout.Height(30)) && _canFullBuild)
                {
                    FullBuildRequested();
                    UiWindows.Close(this);
                }
                GUI.enabled = prev;
                GUILayout.Label("host only · fills every rack, plant and solar slot at once");
            }

            GUILayout.FlexibleSpace();

            // Two-step quit: the first click arms the button for a few
            // seconds instead of opening yet another window.
            bool armed = Time.unscaledTime < _quitArmedUntil;
            if (GUILayout.Button(armed ? "Really quit?" : "Quit", GUILayout.Height(30)))
            {
                if (armed) Quit();
                else _quitArmedUntil = Time.unscaledTime + QuitConfirmSeconds;
            }

            // Always drawn (possibly empty) so the control count is the same
            // in the Layout and Repaint passes of one frame.
            GUILayout.Label(Time.unscaledTime < _statusUntil ? _status : "");
            GUILayout.Label("F1 console · F2 net · F3 options · F5/F9 save/load · Esc resume");
        }

        private void Flash(string text)
        {
            _status = text;
            _statusUntil = Time.unscaledTime + 4f;
        }

        private void Quit()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.Active) net.Disconnect();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
