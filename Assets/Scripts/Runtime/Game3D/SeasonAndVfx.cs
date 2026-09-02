using System;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The diesel plume — the hero VFX, advected by the SIMULATED wind vector,
    /// scaled by actual generator output. When the arrow points at the town,
    /// the particles drift over the houses, because they are the same event the
    /// air channel is charging for. (Sun, sky, fog and season grade live in
    /// Weather; this keeps only the plume.)
    /// </summary>
    public sealed class SeasonAndVfx : MonoBehaviour
    {
        private ParticleSystem _plume;
        private ParticleSystem.MainModule _plumeMain;
        private ParticleSystem.EmissionModule _plumeEmission;
        private ParticleSystem.VelocityOverLifetimeModule _plumeVelocity;

        private void Start()
        {
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
            if (_plume == null) return;
            TickReport r = GameBootstrap.CurrentReport;
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
}
