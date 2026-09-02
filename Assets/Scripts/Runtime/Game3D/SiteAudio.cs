using System;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The site's sound. The plant is procedural: fan noise is filtered noise
    /// whose loudness tracks the sim's actual cooling fan power (audio is not
    /// optional: noise is a core mechanic, and what you hear IS N_noise's
    /// source term), the diesel a low pulse, the suppression siren a
    /// synthesized two-tone. Three things come from Assets/ThirdParty via the
    /// AssetKit, each with a synthesized fallback: the protest chant (a real
    /// crowd, low-passed until no word survives), window clicks and the
    /// clunk of a control being operated.
    /// </summary>
    public sealed class SiteAudio : MonoBehaviour
    {
        private const string ChantKey = "freesound/485621-protest-crowd";
        private const string OpenKey = "kenney/interface-sounds/click_002";
        private const string CloseKey = "kenney/interface-sounds/close_001";
        private const string ClunkKey = "kenney/impact-sounds/impactmetal_light_00";
        /// <summary>art-bible.md, sound: the chant is muffled, never intelligible.</summary>
        private const float ChantLowPassHz = 350f;

        private static SiteAudio _instance;
        public static SiteAudio Instance { get { return _instance; } }
        private AudioSource _fans;
        private AudioSource _diesel;
        private AudioSource _chant;
        private AudioSource _ui;
        private int _clunkIndex;
        // The listening-post perspective: the site's own plant stepped back.
        private float _outdoorDuck = 1f;
        private static AudioClip _noiseLoop;
        private static AudioClip _dieselLoop;
        private static AudioClip _chantLoop;
        private static AudioClip _sirenClip;
        private static AudioClip _engineClip;
        private static AudioClip _beepClip;
        private static AudioClip _hornClip;

        private void Start()
        {
            _instance = this;
            var site = GameBootstrap.Site;

            // The towers and chillers stand in the yard, so that is where the
            // noise the town hears comes from.
            Vector3 coolYard = site != null && site.PlantSlots.Count > 0
                ? site.PlantSlots[site.PlantSlots.Count / 2] + Vector3.up * 2f
                : new Vector3(20, 2, -10);
            _fans = MakeSource("FanNoise", coolYard, NoiseLoop(), 40f);
            _diesel = MakeSource("DieselNoise", site != null
                ? site.GensetPos + Vector3.up * 1.5f : Vector3.zero, DieselLoop(), 55f);
        }

        private static AudioSource MakeSource(string name, Vector3 pos, AudioClip clip, float maxDist)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 1f;
            src.maxDistance = maxDist;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = 0f;
            src.Play();
            return src;
        }

        private void Update()
        {
            var driver = GameBootstrap.Driver;
            TickReport r = GameBootstrap.Net != null && GameBootstrap.Net.IsClient
                ? GameBootstrap.Net.RemoteReport
                : driver != null ? driver.Latest : default(TickReport);
            double refKw = driver != null ? driver.Balance.NoiseFanRefKw : 60.0;

            // Fans: cooling electrical power against the same reference the
            // noise channel uses — you hear roughly what the town hears.
            float fanLoad = (float)(r.PCoolKw / Math.Max(1.0, refKw));
            if (_fans != null)
            {
                // Zero cooling plant is SILENT — the fans-off contrast after an
                // EPO is half the fun; the floor applies only while running.
                _fans.volume = (fanLoad <= 0.001f ? 0f : Mathf.Clamp01(0.12f + 0.35f * fanLoad)) * _outdoorDuck;
                _fans.pitch = 0.8f + Mathf.Clamp01(fanLoad) * 0.5f;
            }
            if (_diesel != null)
            {
                float dieselLoad = (float)(r.DieselKwh / 250.0);
                _diesel.volume = Mathf.Clamp01(dieselLoad) * 0.8f * _outdoorDuck;
            }
        }

        /// <summary>The gate crowd: a low murmur, muffled and never intelligible
        /// (art-bible.md §5). intensity01 scales the volume; 0 stops it.</summary>
        public void SetProtestChant(Vector3 pos, float intensity01)
        {
            if (intensity01 <= 0.001f)
            {
                if (_chant != null && _chant.isPlaying) _chant.Stop();
                return;
            }
            if (_chant == null)
            {
                _chant = MakeSource("ProtestChant", pos, ChantLoop(), 60f);
                // The recording is a crowd of several hundred at a climate
                // strike; through the fence and the low-pass it is a swell
                // with a rhythm and no words, which is all the operator gets.
                _chant.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = ChantLowPassHz;
            }
            _chant.transform.position = pos;
            _chant.volume = Mathf.Clamp01(intensity01) * 0.5f;
            if (!_chant.isPlaying) _chant.Play();
        }

        /// <summary>A window opening or closing: a soft interface click, 2D.</summary>
        public static void PlayUiClick(bool open)
        {
            SiteAudio a = _instance;
            if (a == null || !AssetKit.TryGetClip(open ? OpenKey : CloseKey, out AudioClip clip)) return;
            if (a._ui == null)
            {
                a._ui = a.gameObject.AddComponent<AudioSource>();
                a._ui.spatialBlend = 0f;
                a._ui.playOnAwake = false;
            }
            a._ui.PlayOneShot(clip, 0.45f);
        }

        /// <summary>A control operated by hand: a light metal clunk where it
        /// stands. One of five takes, round-robin, so a row of switches does
        /// not sound like one sample.</summary>
        public static void PlayClunk(Vector3 pos)
        {
            SiteAudio a = _instance;
            if (a == null) return;
            a._clunkIndex = (a._clunkIndex + 1) % 5;
            if (!AssetKit.TryGetClip(ClunkKey + a._clunkIndex, out AudioClip clip)) return;
            AudioSource.PlayClipAtPoint(clip, pos, 0.6f);
        }

        /// <summary>The listening post: outdoor plant loops ducked by 30 % so the
        /// town's side of the fence comes forward. One mixer state, no sim effect.</summary>
        public void SetFenceMix(bool near)
        {
            _outdoorDuck = near ? 0.7f : 1f;
        }

        public static void PlaySiren(Vector3 pos, float seconds)
        {
            var go = new GameObject("Siren");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = Siren();
            src.loop = true;
            src.spatialBlend = 0.6f;
            src.maxDistance = 80f;
            src.volume = 0.9f;
            src.Play();
            Destroy(go, seconds);
        }

        // --- synthesized clips ---------------------------------------------

        private static AudioClip NoiseLoop()
        {
            if (_noiseLoop != null) return _noiseLoop;
            const int rate = 22050;
            var data = new float[rate * 2];
            float brown = 0f;
            uint seed = 22222;
            for (int i = 0; i < data.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                // 23 random bits mapped onto [-1, 1) — halving the divisor of
                // the naive version, whose output was always negative and
                // integrated straight into a silent DC rail.
                float white = (seed >> 9) / 4194304f - 1f;
                brown = Mathf.Clamp(brown + white * 0.04f, -1f, 1f) * 0.998f;
                data[i] = brown * 0.8f;
            }
            // Kill the loop-seam click: tilt the whole buffer so the last
            // sample lands where the first one starts.
            float offset = data[data.Length - 1] - data[0];
            for (int i = 0; i < data.Length; i++)
                data[i] -= offset * i / (data.Length - 1);
            _noiseLoop = AudioClip.Create("fanNoise", data.Length, 1, rate, false);
            _noiseLoop.SetData(data, 0);
            return _noiseLoop;
        }

        private static AudioClip DieselLoop()
        {
            if (_dieselLoop != null) return _dieselLoop;
            const int rate = 22050;
            var data = new float[rate];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float thump = Mathf.Sin(t * 2f * Mathf.PI * 28f);
                float rattle = Mathf.Sin(t * 2f * Mathf.PI * 97f) * 0.2f;
                data[i] = Mathf.Clamp(thump * Mathf.Abs(thump) + rattle, -1f, 1f) * 0.7f;
            }
            _dieselLoop = AudioClip.Create("diesel", data.Length, 1, rate, false);
            _dieselLoop.SetData(data, 0);
            return _dieselLoop;
        }

        /// <summary>A crowd heard through a fence: low-passed brown noise that
        /// swells every 1.5 s under two soft drone tones. No words — the point
        /// of the murmur is that the operator cannot make them out.</summary>
        private static AudioClip ChantLoop()
        {
            if (_chantLoop != null) return _chantLoop;
            if (AssetKit.TryGetClip(ChantKey, out AudioClip real)) { _chantLoop = real; return real; }
            const int rate = 22050;
            var data = new float[rate * 3];
            float brown = 0f, lp = 0f;
            uint seed = 7331;
            for (int i = 0; i < data.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = (seed >> 9) / 4194304f - 1f;
                brown = Mathf.Clamp(brown + white * 0.05f, -1f, 1f) * 0.997f;
                lp += (brown - lp) * 0.05f;
                float t = i / (float)rate;
                float swell = Mathf.Sin(t * Mathf.PI / 1.5f);
                float env = 0.55f + 0.45f * swell * swell;
                float drone = Mathf.Sin(t * 2f * Mathf.PI * 98f) * 0.15f
                            + Mathf.Sin(t * 2f * Mathf.PI * 147f) * 0.10f;
                data[i] = Mathf.Clamp(lp * env * 3f + drone, -1f, 1f) * 0.6f;
            }
            float offset = data[data.Length - 1] - data[0];
            for (int i = 0; i < data.Length; i++)
                data[i] -= offset * i / (data.Length - 1);
            _chantLoop = AudioClip.Create("protestMurmur", data.Length, 1, rate, false);
            _chantLoop.SetData(data, 0);
            return _chantLoop;
        }

        /// <summary>A small diesel engine at idle: a low pulse train with a
        /// rough harmonic. Pitched by speed at the source.</summary>
        public static AudioClip EngineClip()
        {
            if (_engineClip != null) return _engineClip;
            const int rate = 22050;
            var data = new float[rate];            // 1 s, loops seamlessly at 18 Hz
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float fire = Mathf.Sin(t * 2f * Mathf.PI * 18f);
                float rough = Mathf.Sin(t * 2f * Mathf.PI * 54f) * 0.3f;
                float whine = Mathf.Sin(t * 2f * Mathf.PI * 210f) * 0.08f;
                data[i] = Mathf.Clamp(fire * Mathf.Abs(fire) + rough + whine, -1f, 1f) * 0.6f;
            }
            _engineClip = AudioClip.Create("engine", data.Length, 1, rate, false);
            _engineClip.SetData(data, 0);
            return _engineClip;
        }

        /// <summary>The reverse alarm. Every site has one, every neighbour
        /// knows it, and it carries much further than the operator thinks.</summary>
        public static AudioClip BeepClip()
        {
            if (_beepClip != null) return _beepClip;
            const int rate = 22050;
            var data = new float[rate];            // 1 s: 0.35 on, 0.65 off
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                data[i] = t < 0.35f ? Mathf.Sin(t * 2f * Mathf.PI * 1100f) * 0.5f : 0f;
            }
            _beepClip = AudioClip.Create("reverseAlarm", data.Length, 1, rate, false);
            _beepClip.SetData(data, 0);
            return _beepClip;
        }

        private static AudioClip HornClip()
        {
            if (_hornClip != null) return _hornClip;
            const int rate = 22050;
            var data = new float[rate / 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(1f - t * 2.2f);
                data[i] = (Mathf.Sin(t * 2f * Mathf.PI * 420f) +
                           Mathf.Sin(t * 2f * Mathf.PI * 525f) * 0.7f) * 0.35f * env;
            }
            _hornClip = AudioClip.Create("horn", data.Length, 1, rate, false);
            _hornClip.SetData(data, 0);
            return _hornClip;
        }

        public static void PlayHorn(Vector3 pos)
        {
            var go = new GameObject("Horn");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = HornClip();
            src.spatialBlend = 1f;
            src.maxDistance = 90f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = 0.8f * PlayerOptions.MachineryVolume;
            src.Play();
            Destroy(go, 1.2f);
        }

        private static AudioClip Siren()
        {
            if (_sirenClip != null) return _sirenClip;
            const int rate = 22050;
            var data = new float[rate * 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float f = t % 2f < 1f ? 880f : 660f;
                data[i] = Mathf.Sin(t * 2f * Mathf.PI * f) * 0.6f;
            }
            _sirenClip = AudioClip.Create("siren", data.Length, 1, rate, false);
            _sirenClip.SetData(data, 0);
            return _sirenClip;
        }
    }
}
