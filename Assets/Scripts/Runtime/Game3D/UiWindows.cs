using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    [Flags]
    public enum UiWindowFlags
    {
        None = 0,
        /// <summary>Corner grip; the window keeps its size across close/open.</summary>
        Resizable = 1,
        /// <summary>Only the title bar drags (windows full of sliders and
        /// text fields must not move when a control is grabbed).</summary>
        TitleDragOnly = 2,
        /// <summary>Background tinted by PlayerOptions.ConsoleOpacity.</summary>
        Tinted = 4,
        NoDrag = 8,
        /// <summary>Dim the whole screen behind the window stack.</summary>
        DimBackdrop = 16,
    }

    /// <summary>
    /// A movable IMGUI window. Implementations register with UiWindows in
    /// Awake/OnEnable and unregister in OnDestroy/OnDisable; they never call
    /// GUILayout.Window or touch the cursor themselves.
    /// </summary>
    public interface IUiWindow
    {
        int Id { get; }
        string Title { get; }
        UiWindowFlags Flags { get; }
        /// <summary>Position on first open, in virtual (UiScaler) pixels. A
        /// height of 0 lets the window size itself.</summary>
        Rect DefaultRect(float w, float h);
        void DrawContents(int id);
        void OnOpened();
        void OnClosed();
    }

    /// <summary>
    /// The one OnGUI that draws windows. Five components each calling
    /// GUILayout.Window from their own OnGUI have no defined z-order, so the
    /// window drawn last ate the clicks meant for the one on top and buttons
    /// "could partly not be pressed". Here every window goes through a single
    /// back-to-front pass, the clicked window is raised, and the cursor is
    /// freed exactly when something is open.
    ///
    /// The registry is static so components can register from Awake in any
    /// order, before the host component exists.
    /// </summary>
    public sealed class UiWindows : MonoBehaviour
    {
        private sealed class Entry
        {
            public IUiWindow W;
            public bool Open;
            public bool Placed;
            public Rect Rect;
        }

        private static readonly Dictionary<int, Entry> _byId = new Dictionary<int, Entry>();
        /// <summary>Back-to-front: the last open entry is the front window.</summary>
        private static readonly List<Entry> _z = new List<Entry>();
        private static int _raise = -1;

        public static void Register(IUiWindow w)
        {
            if (w == null) return;
            Entry e;
            if (_byId.TryGetValue(w.Id, out e))
            {
                if (ReferenceEquals(e.W, w)) return;
                // A stale entry from a destroyed component of the same id.
                Unregister(e.W);
            }
            e = new Entry { W = w };
            _byId[w.Id] = e;
            _z.Add(e);
        }

        public static void Unregister(IUiWindow w)
        {
            if (w == null) return;
            Entry e;
            if (!_byId.TryGetValue(w.Id, out e) || !ReferenceEquals(e.W, w)) return;
            if (e.Open)
            {
                e.Open = false;
                try { w.OnClosed(); } catch (Exception ex) { Debug.LogError(ex); }
            }
            _byId.Remove(w.Id);
            _z.Remove(e);
            ApplyCursor();
        }

        private static Entry Find(IUiWindow w)
        {
            if (w == null) return null;
            Entry e;
            return _byId.TryGetValue(w.Id, out e) && ReferenceEquals(e.W, w) ? e : null;
        }

        /// <summary>A registered component that Unity has destroyed without
        /// unregistering compares equal to null through the Object overload.</summary>
        private static bool Dead(Entry e)
        {
            var o = e.W as UnityEngine.Object;
            return !ReferenceEquals(o, null) && o == null;
        }

        public static void Open(IUiWindow w)
        {
            Entry e = Find(w);
            if (e == null || Dead(e)) return;
            if (!e.Open)
            {
                // The pause menu is the base of a stack: its own sub-windows
                // (options, network) sit on top of it and closing them returns
                // to the menu; anything else replaces it.
                if (!(w is PauseMenu) && !(w is PlayerOptions) && !(w is Game.Runtime.Net.NetSession))
                {
                    if (PauseMenu.Instance != null) Close(PauseMenu.Instance);
                }
                e.Open = true;
                try { w.OnOpened(); } catch (Exception ex) { Debug.LogError(ex); }
            }
            SetFront(e);
            ApplyCursor();
        }

        public static void Close(IUiWindow w)
        {
            Entry e = Find(w);
            if (e == null || !e.Open) return;
            e.Open = false;
            if (!Dead(e))
            {
                try { w.OnClosed(); } catch (Exception ex) { Debug.LogError(ex); }
            }
            ApplyCursor();
        }

        public static void Toggle(IUiWindow w)
        {
            if (IsOpen(w)) Close(w); else Open(w);
        }

        public static void CloseAll()
        {
            // Front to back so a stack unwinds in the order Escape would.
            for (int i = _z.Count - 1; i >= 0; i--)
            {
                if (_z[i].Open) Close(_z[i].W);
            }
            ApplyCursor();
        }

        public static bool IsOpen(IUiWindow w)
        {
            Entry e = Find(w);
            return e != null && e.Open && !Dead(e);
        }

        public static bool AnyOpen
        {
            get
            {
                for (int i = 0; i < _z.Count; i++)
                {
                    if (_z[i].Open && !Dead(_z[i])) return true;
                }
                return false;
            }
        }

        /// <summary>The open window on top, or null.</summary>
        public static IUiWindow Front
        {
            get
            {
                for (int i = _z.Count - 1; i >= 0; i--)
                {
                    if (_z[i].Open && !Dead(_z[i])) return _z[i].W;
                }
                return null;
            }
        }

        /// <summary>Current (clamped) rect of a window; its default rect if it
        /// has never been shown. Virtual pixels.</summary>
        public static Rect RectOf(IUiWindow w)
        {
            Entry e = Find(w);
            if (e == null) return new Rect(0, 0, 0, 0);
            if (!e.Placed) return w.DefaultRect(UiScaler.W, UiScaler.H);
            return e.Rect;
        }

        private static void SetFront(Entry e)
        {
            if (_z.Count > 0 && ReferenceEquals(_z[_z.Count - 1], e))
            {
                _raise = e.W.Id;
                return;
            }
            _z.Remove(e);
            _z.Add(e);
            _raise = e.W.Id;
        }

        /// <summary>The only writer of Cursor.lockState/visible in the game:
        /// free while any window is open, locked and hidden otherwise.</summary>
        public static void ApplyCursor()
        {
            bool free = AnyOpen;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        private void Start()
        {
            ApplyCursor();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                var pm = PauseMenu.Instance;
                if (pm != null && IsOpen(pm))
                {
                    // Everything stacked above the menu belongs to it.
                    Close(pm);
                    for (int i = _z.Count - 1; i >= 0; i--)
                    {
                        if (_z[i].Open) Close(_z[i].W);
                    }
                }
                else
                {
                    IUiWindow front = Front;
                    if (front != null) Close(front);
                    else if (pm != null) Open(pm);
                }
                ApplyCursor();
            }
        }

        private void LateUpdate()
        {
            // Alt-tab or an OS dialog can hand the cursor back while nothing
            // is open; the first click in the world takes it again. After the
            // Updates, so placement and the rig — which read the same press —
            // have already seen the cursor as free and ignored it.
            var mouse = Mouse.current;
            if (!AnyOpen && Cursor.lockState != CursorLockMode.Locked &&
                mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                ApplyCursor();
            }
        }

        private void OnGUI()
        {
            GUI.depth = 0;
            UiScaler.Begin();
            try
            {
                bool dim = false;
                for (int i = 0; i < _z.Count; i++)
                {
                    Entry e = _z[i];
                    if (e.Open && !Dead(e) && (e.W.Flags & UiWindowFlags.DimBackdrop) != 0) { dim = true; break; }
                }
                if (dim)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.45f);
                    GUI.DrawTexture(new Rect(0, 0, UiScaler.W, UiScaler.H), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                // Iterate a copy: DrawContents may open or close windows,
                // which reorders _z.
                Entry[] order = _z.ToArray();
                for (int i = 0; i < order.Length; i++)
                {
                    Entry e = order[i];
                    if (!e.Open || Dead(e)) continue;
                    if (!e.Placed)
                    {
                        e.Rect = UiScaler.Clamp(e.W.DefaultRect(UiScaler.W, UiScaler.H));
                        e.Placed = true;
                    }
                    Color bg = GUI.backgroundColor;
                    if ((e.W.Flags & UiWindowFlags.Tinted) != 0)
                        GUI.backgroundColor = new Color(1f, 1f, 1f, PlayerOptions.ConsoleOpacity);
                    e.Rect = UiScaler.Clamp(GUILayout.Window(e.W.Id, e.Rect, WindowFn, e.W.Title));
                    GUI.backgroundColor = bg;
                }

                if (_raise >= 0)
                {
                    GUI.BringWindowToFront(_raise);
                    GUI.FocusWindow(_raise);
                    _raise = -1;
                }
            }
            finally
            {
                UiScaler.End();
            }
        }

        private void WindowFn(int id)
        {
            Entry e;
            if (!_byId.TryGetValue(id, out e) || Dead(e)) return;
            // IMGUI hands a real MouseDown only to the topmost window under
            // the pointer; the (window-local) rect check keeps a stray one
            // from raising a window the click did not land in.
            var down = Event.current;
            if (down.type == EventType.MouseDown &&
                new Rect(0, 0, e.Rect.width, e.Rect.height).Contains(down.mousePosition))
                SetFront(e);

            // Not wrapped in try/catch: IMGUI aborts a pass with its own
            // ExitGUIException, which must propagate.
            bool enabled = GUI.enabled;
            e.W.DrawContents(id);
            GUI.enabled = enabled;

            UiWindowFlags flags = e.W.Flags;
            if ((flags & UiWindowFlags.Resizable) != 0)
            {
                // Resize grip: drag the bottom-right corner.
                var grip = new Rect(e.Rect.width - 18, e.Rect.height - 18, 16, 16);
                GUI.Box(grip, "◢");
                var ev = Event.current;
                if (ev.type == EventType.MouseDrag && grip.Contains(ev.mousePosition))
                {
                    e.Rect.width = Mathf.Max(520f, e.Rect.width + ev.delta.x);
                    e.Rect.height = Mathf.Max(320f, e.Rect.height + ev.delta.y);
                    ev.Use();
                }
            }

            if ((flags & UiWindowFlags.NoDrag) == 0)
            {
                GUI.DragWindow((flags & UiWindowFlags.TitleDragOnly) != 0
                    ? new Rect(0, 0, e.Rect.width, 20)
                    : new Rect(0, 0, 100000, 100000));
            }
        }
    }
}
