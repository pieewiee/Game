using System;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Procedural audio — no clips exist anywhere in the project. Fan noise is
    /// filtered noise whose loudness tracks the sim's actual cooling fan power
    /// (audio is not optional: noise is a core mechanic, and what you hear IS
    /// N_noise's source term). The diesel adds a low pulse; the suppression
    /// siren is a synthesized two-tone.
    /// </summary>
    public sealed class SiteAudio : MonoBehaviour
    {
        private static SiteAudio _instance;
        private AudioSource _fans;
        private AudioSource _diesel;
        private static AudioClip _noiseLoop;
        private static AudioClip _dieselLoop;
        private static AudioClip _sirenClip;
        private static AudioClip _stepClip;
        private static AudioClip _landClip;

        private void Start()
        {
            _instance = this;
            var site = GameBootstrap.Site;

            _fans = MakeSource("FanNoise", site != null
                ? site.Plant.Bounds.center : new Vector3(25, 2, 10), NoiseLoop(), 28f);
            _diesel = MakeSource("DieselNoise", site != null
                ? new Vector3(46, 1.5f, 14) : Vector3.zero, DieselLoop(), 45f);
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
                _fans.volume = fanLoad <= 0.001f ? 0f : Mathf.Clamp01(0.12f + 0.35f * fanLoad);
                _fans.pitch = 0.8f + Mathf.Clamp01(fanLoad) * 0.5f;
            }
            if (_diesel != null)
            {
                float dieselLoad = (float)(r.DieselKwh / 250.0);
                _diesel.volume = Mathf.Clamp01(dieselLoad) * 0.8f;
            }
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

        /// <summary>A boot on concrete: a short noise burst with a fast decay
        /// and a little body. Pitch-shifted per step so it never sounds like a
        /// metronome.</summary>
        public static AudioClip StepClip()
        {
            if (_stepClip != null) return _stepClip;
            const int rate = 22050;
            var data = new float[rate / 8];               // 125 ms
            uint seed = 9187;
            for (int i = 0; i < data.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = (seed >> 9) / 4194304f - 1f;
                float t = i / (float)rate;
                float env = Mathf.Clamp01(1f - t * 26f);
                env *= env;
                float body = Mathf.Sin(t * 2f * Mathf.PI * 150f) * 0.35f;
                data[i] = (white * 0.55f + body) * env;
            }
            _stepClip = AudioClip.Create("step", data.Length, 1, rate, false);
            _stepClip.SetData(data, 0);
            return _stepClip;
        }

        /// <summary>Landing: the same idea an octave down, with more thud and a
        /// longer tail. Loud landings are how a fall announces itself.</summary>
        public static AudioClip LandClip()
        {
            if (_landClip != null) return _landClip;
            const int rate = 22050;
            var data = new float[rate / 4];               // 250 ms
            uint seed = 55127;
            for (int i = 0; i < data.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                float white = (seed >> 9) / 4194304f - 1f;
                float t = i / (float)rate;
                float env = Mathf.Clamp01(1f - t * 9f);
                env *= env;
                float body = Mathf.Sin(t * 2f * Mathf.PI * 68f) * 0.8f;
                data[i] = (white * 0.3f + body) * env;
            }
            _landClip = AudioClip.Create("land", data.Length, 1, rate, false);
            _landClip.SetData(data, 0);
            return _landClip;
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
