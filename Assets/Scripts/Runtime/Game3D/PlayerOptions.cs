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
        public static float BobScale = 1f;
        public static float FoleyVolume = 0.8f;
        public static float MasterVolume = 1f;

        public bool IsOpen { get; private set; }
        private Rect _win = new Rect(60, 60, 340, 250);

        private void Awake()
        {
            Instance = this;
            Sensitivity = PlayerPrefs.GetFloat("gnp.sens", 0.08f);
            InvertY = PlayerPrefs.GetInt("gnp.invertY", 0) == 1;
            Fov = PlayerPrefs.GetFloat("gnp.fov", 68f);
            BobScale = PlayerPrefs.GetFloat("gnp.bob", 1f);
            FoleyVolume = PlayerPrefs.GetFloat("gnp.foley", 0.8f);
            MasterVolume = PlayerPrefs.GetFloat("gnp.volume", 1f);
            AudioListener.volume = MasterVolume;
        }

        private void Save()
        {
            PlayerPrefs.SetFloat("gnp.sens", Sensitivity);
            PlayerPrefs.SetInt("gnp.invertY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("gnp.fov", Fov);
            PlayerPrefs.SetFloat("gnp.bob", BobScale);
            PlayerPrefs.SetFloat("gnp.foley", FoleyVolume);
            PlayerPrefs.SetFloat("gnp.volume", MasterVolume);
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
            _win = GUILayout.Window(915, _win, DrawWindow, "OPTIONS (F3)");
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
            FoleyVolume = Row("Footsteps", FoleyVolume, 0f, 1f, "0.00");
            MasterVolume = Row("Master volume", MasterVolume, 0f, 1f, "0.00");

            GUILayout.Space(8);
            GUILayout.Label("Comfort settings are per machine and are NOT part of\n" +
                            "the shared simulation — they never affect a session.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("close", GUILayout.Width(90))) Toggle(false);
            if (GUILayout.Button("defaults", GUILayout.Width(90)))
            {
                Sensitivity = 0.08f; InvertY = false; Fov = 68f;
                BobScale = 1f; FoleyVolume = 0.8f; MasterVolume = 1f;
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }
    }
}
