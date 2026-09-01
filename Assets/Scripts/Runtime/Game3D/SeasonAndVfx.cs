using System;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Seasons as palette and light (art-bible.md: a grade, not new assets) and
    /// the diesel plume — the hero VFX, advected by the SIMULATED wind vector,
    /// scaled by actual generator output. When the arrow points at the town,
    /// the particles drift over the houses, because they are the same event the
    /// air channel is charging for.
    /// </summary>
    public sealed class SeasonAndVfx : MonoBehaviour
    {
        private Light _sun;
        private ParticleSystem _plume;
        private ParticleSystem.MainModule _plumeMain;
        private ParticleSystem.EmissionModule _plumeEmission;
        private ParticleSystem.VelocityOverLifetimeModule _plumeVelocity;

        private void Start()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;

            var site = GameBootstrap.Site;
            var plumeGo = new GameObject("DieselPlume");
            plumeGo.transform.position = site != null ? site.DieselStackTop : new Vector3(46, 4.2f, 14);
            _plume = plumeGo.AddComponent<ParticleSystem>();
            var rend = plumeGo.GetComponent<ParticleSystemRenderer>();
            rend.material = MatLib.Transparent;

            _plumeMain = _plume.main;
            _plumeMain.startLifetime = 14f;
            _plumeMain.startSize = 2.2f;
            _plumeMain.startSpeed = 0.6f;
            _plumeMain.maxParticles = 4000;
            _plumeMain.startColor = new Color(0.12f, 0.12f, 0.13f, 0.45f);
            _plumeMain.simulationSpace = ParticleSystemSimulationSpace.World;

            _plumeEmission = _plume.emission;
            _plumeEmission.rateOverTime = 0f;

            _plumeVelocity = _plume.velocityOverLifetime;
            _plumeVelocity.enabled = true;
        }

        private void Update()
        {
            TickReport r = CurrentReport();

            // --- season + daylight grade -----------------------------------
            int month = SimClock.Month(CurrentTick());
            int hour = SimClock.HourOfDay(CurrentTick());
            float summer = 0.5f - 0.5f * Mathf.Cos((month - 1) / 12f * Mathf.PI * 2f); // 0 winter → 1 July-ish

            float dayFrac = Mathf.InverseLerp(5f, 15f, hour) - Mathf.InverseLerp(15f, 23f, hour);
            float sunUp = Mathf.Clamp01(0.15f + dayFrac);
            _sun.transform.rotation = Quaternion.Euler(20f + sunUp * 45f + summer * 12f, 160f, 0);
            _sun.intensity = 0.25f + sunUp * (0.7f + 0.3f * summer);
            _sun.color = Color.Lerp(new Color(0.85f, 0.88f, 1f), new Color(1f, 0.96f, 0.85f), summer);

            Color sky = Color.Lerp(new Color(0.62f, 0.68f, 0.75f), new Color(0.75f, 0.83f, 0.88f), summer);
            sky = Color.Lerp(Color.Lerp(sky, Palette.Ink, 0.85f), sky, Mathf.Clamp01(sunUp + 0.15f));
            RenderSettings.ambientLight = Color.Lerp(sky * 0.7f, sky, 0.5f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = sky;
            RenderSettings.fogDensity = Mathf.Lerp(0.004f, 0.0015f, summer);
            var cam = GameBootstrap.LocalPlayer != null ? GameBootstrap.LocalPlayer.Cam : null;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = sky;
            }

            // --- the plume --------------------------------------------------
            if (_plume != null)
            {
                double dieselKw = r.DieselKwh; // 1 tick = 1 h, so kWh ≡ average kW
                _plumeEmission.rateOverTime = (float)(dieselKw * 0.5);
                // Wind blows TOWARD WindTowardDeg (0° = +z). The town sits west
                // (bearing 270° = −x), so a 270° wind drives particles at it.
                float rad = (float)r.WindTowardDeg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(rad), 0.25f, Mathf.Cos(rad));
                float speed = (float)r.WindSpeedMs * 0.8f;
                _plumeVelocity.x = dir.x * speed;
                _plumeVelocity.y = 0.8f;
                _plumeVelocity.z = dir.z * speed;
            }
        }

        private static TickReport CurrentReport()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return net.RemoteReport;
            var d = GameBootstrap.Driver;
            return d != null ? d.Latest : default(TickReport);
        }

        private static long CurrentTick()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return net.RemoteTick;
            var d = GameBootstrap.Driver;
            return d != null && d.Sim != null ? d.Sim.State.Tick : 0;
        }
    }
}
