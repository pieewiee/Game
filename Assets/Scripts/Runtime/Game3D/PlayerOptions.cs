using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runtime.World
{
    /// <summary>
    /// Feel is taste, not design: mouse sensitivity, inverted look, field of
    /// view, head bob and volume belong to whoever is holding the mouse. F3
    /// opens the panel; everything persists in PlayerPrefs, so a five-player
    /// session does not begin with five people fighting my constants.
    ///
    /// Deliberately NOT in the tuning file: balance.tuning is simulation
    /// truth that must stay identical for everyone in a session, and these
    /// are per-machine comfort settings that must not.
    /// </summary>
    public sealed class PlayerOptions : MonoBehaviour
    {
        public static PlayerOptions Instance { get; private set; }

        public static float Sensitivity = 0.08f;
        public static bool InvertY;
        public static float Fov = 68f;
        public static float BobScale = 1f;   // 1 = the calm default
        public static float MachineryVolume = 0.8f;
        public static float MasterVolume = 1f;
        public static float UiScale = 1f;
        public static float ConsoleOpacity = 0.92f;
        public static bool HudVisible = true;

        public bool IsOpen { get; private set; }
        private Rect _win = new Rect(60, 60, 360, 330);

        private void Awake()
        {
            Instance = this;
            Sensitivity = PlayerPrefs.GetFloat("gnp.sens", 0.08f);
            InvertY = PlayerPrefs.GetInt("gnp.invertY", 0) == 1;
            Fov = PlayerPrefs.GetFloat("gnp.fov", 68f);
            BobScale = PlayerPrefs.GetFloat("gnp.bob", 1f);
            MachineryVolume = PlayerPrefs.GetFloat("gnp.machinery", 0.8f);
            MasterVolume = PlayerPrefs.GetFloat("gnp.volume", 1f);
            UiScale = PlayerPrefs.GetFloat("gnp.uiscale", 1f);
            ConsoleOpacity = PlayerPrefs.GetFloat("gnp.consoleop", 0.92f);
            HudVisible = PlayerPrefs.GetInt("gnp.hud", 1) == 1;
            AudioListener.volume = MasterVolume;
        }

        private void Save()
        {
            PlayerPrefs.SetFloat("gnp.sens", Sensitivity);
            PlayerPrefs.SetInt("gnp.invertY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("gnp.fov", Fov);
            PlayerPrefs.SetFloat("gnp.bob", BobScale);
            PlayerPrefs.SetFloat("gnp.machinery", MachineryVolume);
            PlayerPrefs.SetFloat("gnp.volume", MasterVolume);
            PlayerPrefs.SetFloat("gnp.uiscale", UiScale);
            PlayerPrefs.SetFloat("gnp.consoleop", ConsoleOpacity);
            PlayerPrefs.SetInt("gnp.hud", HudVisible ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f3Key.wasPressedThisFrame) Toggle(!IsOpen);
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame)
            {
                Toggle(false);
                GameBootstrap.EscConsumedFrame = Time.frameCount;
            }
            AudioListener.volume = MasterVolume;
        }

        private void Toggle(bool open)
        {
            IsOpen = open;
            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Save();
                // Only take the cursor back if no other modal still wants it.
                if (!GameBootstrap.UiWantsCursor)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            UiScaler.Begin();
            _win = GUILayout.Window(915, _win, DrawWindow, "OPTIONS (F3)");
            UiScaler.End();
        }

        private static float Row(string label, float value, float lo, float hi, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130));
            float v = GUILayout.HorizontalSlider(value, lo, hi, GUILayout.Width(120));
            GUILayout.Label(v.ToString(fmt, CultureInfo.InvariantCulture), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return v;
        }

        private void DrawWindow(int id)
        {
            Sensitivity = Row("Mouse sensitivity", Sensitivity, 0.01f, 0.30f, "0.000");
            InvertY = GUILayout.Toggle(InvertY, " invert vertical look");
            Fov = Row("Field of view", Fov, 55f, 100f, "0");
            BobScale = Row("Head bob", BobScale, 0f, 2f, "0.00");
            MachineryVolume = Row("Machinery", MachineryVolume, 0f, 1f, "0.00");
            MasterVolume = Row("Master volume", MasterVolume, 0f, 1f, "0.00");

            GUILayout.Space(6);
            GUILayout.Label("— interface —");
            UiScale = Row("UI scale", UiScale, 0.7f, 2.0f, "0.00");
            ConsoleOpacity = Row("Console opacity", ConsoleOpacity, 0.3f, 1f, "0.00");
            HudVisible = GUILayout.Toggle(HudVisible, " show the site HUD (clock, GNI, wind)");

            GUILayout.Space(8);
            GUILayout.Label("Comfort settings are per machine and are NOT part of\n" +
                            "the shared simulation — they never affect a session.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("close", GUILayout.Width(90))) Toggle(false);
            if (GUILayout.Button("defaults", GUILayout.Width(90)))
            {
                Sensitivity = 0.08f; InvertY = false; Fov = 68f;
                BobScale = 1f; MachineryVolume = 0.8f; MasterVolume = 1f;
                UiScale = 1f; ConsoleOpacity = 0.92f; HudVisible = true;
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }
    }
}
