using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// The sky: sun and moon geometry, cloud cover, precipitation, fog and the
    /// directional light, all driven by the sim's climate meters (TickReport
    /// CloudFrac / RainMmH / TdbC / wind) and the sim clock. Pure
    /// presentation — nothing here writes into the simulation.
    ///
    /// The sun geometry is a static, pure function of the tick so every
    /// layer that needs "is it dark" (window lights, lamp heads, the figures
    /// with their phones) asks the same question and gets the same answer.
    ///
    /// Everything is a function of (visual tick, report), so a host and its
    /// clients see the same sky. Only cloud drift, the rotor angle and the
    /// particles integrate real time — cosmetic, like the plume.
    /// </summary>
    public sealed class Weather : MonoBehaviour
    {
        public static Weather Instance { get; private set; }

        /// <summary>Site latitude; solar noon sits at 13:00 sim time (summer time).</summary>
        public const float LatitudeDeg = 51f;
        public const float SolarNoonHour = 13f;

        /// <summary>What the rest of the presentation reads once per frame.</summary>
        public struct SkyState
        {
            public double Vt;          // visual tick: report tick + sub-tick fraction
            public float HourF;        // fractional hour of Vt, 0..24
            public float SunElevationDeg, SunAzimuthDeg;   // azimuth 0 = +z (north), 90 = +x (east)
            public float SunUp;        // smoothstep(-8°, +10°) of the elevation: 0 night .. 1 day
            public float Dusk;         // bell around the horizon crossing, 0..1
            public float Cloud;        // 0..1 from the sim
            public float RainMmH;      // 0 when dry
            public bool Snow;          // precipitation falls as snow (TdbC < 0.5)
            public float Summer;       // 0 midwinter .. 1 midsummer
            public float Autumn;       // 1 in October, 0 outside Aug..Dec
            public bool Dark;          // IsDark(floor(Vt)): the one "is it night" for lamps and the HUD
            public bool Indoors;       // a roof over the camera: no rain
            public float FogDensity;   // exp² fog, <= 0.018
            public Vector3 ToSun, ToMoon;
            public Color Zenith, Horizon, Fog, Ambient, CloudTint, SunColor;
        }

        public SkyState Sky;
        /// <summary>One line for the HUD, rebuilt once per tick.</summary>
        public string HudLine = "";

        // ---- palette anchors (art-bible §1 + §4 rows 7-8) --------------------
        private static readonly Color DuskTint = new Color(0.93f, 0.62f, 0.40f);
        private static readonly Color NightLight = new Color(0.6f, 0.7f, 0.95f);
        private static readonly Color AmbientFloor = new Color(0.13f, 0.14f, 0.16f);
        private static readonly Color TownGlow = new Color(0.18f, 0.13f, 0.07f);

        private const float DomeRadius = 480f;      // < camera far clip (600)
        private const int DomeSegments = 24, DomeRings = 9;
        private const float DomeSkirtDeg = -12f;    // below-horizon skirt hides the ground edge
        private const float DiscDistance = 470f;
        private const int CloudCount = 28;
        private const float CloudSpread = 450f;     // blobs live in ±450 m around the camera
        private const ulong CloudSeed = 0xC10DUL;
        private const float RecolourMinInterval = 1f / 30f;
        private const float RecolourSimHours = 0.25f;
        private const float FogHudThreshold = 0.010f;
        private const float WindyMs = 8.5f;         // top ~10 % of the sim's wind (caps near 10 m/s)

        private Light _sun;

        private Transform _dome;
        private Mesh _domeMesh;
        private Vector3[] _domeDirs;
        private readonly List<Color> _domeColors = new List<Color>();

        private Material _discMat;
        private Disc _sunDisc, _sunHalo, _moonDisc;

        private Transform _cloudRoot;
        private readonly Blob[] _clouds = new Blob[CloudCount];
        private Vector2 _drift;
        private Vector3 _lastCloudCam;
        private Vector2 _lastCloudDrift;

        private ParticleSystem _precip;
        private ParticleSystemRenderer _precipRenderer;
        private bool _precipSnow;
        private float _nextIndoorCheck;

        private double _vt;
        private long _lastRemoteTick = -1;
        private bool _lastRemotePaused;
        private float _lastSnapTime;
        private long _lastTick = -1;
        private double _lastRecolourVt = double.NegativeInfinity;
        private float _nextRecolourTime;
        private SkyState _last;
        private bool _built;

        private float _rotorOmega;
        private readonly StringBuilder _sb = new StringBuilder(96);

        /// <summary>A billboarded polygon in the sky (sun, halo, moon): a fan of
        /// degenerate quads so the rim can fade independently of the centre.</summary>
        private sealed class Disc
        {
            public Transform Tf;
            public Renderer Rend;
            public Mesh Mesh;
            public float[] Shade;      // per-vertex alpha factor (halo: 1 centre, 0 rim)
            public List<Color> Colors = new List<Color>();
        }

        /// <summary>One cloud: 2-4 overlapping flat quads, recoloured together.</summary>
        private sealed class Blob
        {
            public Transform Tf;
            public Renderer Rend;
            public Mesh Mesh;
            public Vector3 Base;       // hashed world position before drift; y is the altitude
            public float Threshold;    // visible once CloudFrac exceeds this
            public float[] Shade;      // per-vertex brightness (lobe-to-lobe variation)
            public float[] Rim;        // per-vertex alpha factor: 1 at a lobe's centre, 0 on its rim
            public List<Color> Colors = new List<Color>();
        }

        // ---- stars -----------------------------------------------------------
        private const int StarCount = 600;
        private const float StarDistance = 474f;
        /// <summary>Celestial pole for 51° N: up the +z (north) axis, 51° above the horizon.</summary>
        private static readonly Vector3 PoleAxis = new Vector3(0f, Mathf.Sin(51f * Mathf.Deg2Rad), Mathf.Cos(51f * Mathf.Deg2Rad));
        private Transform _stars;
        private Mesh _starMesh;
        private Vector3[] _starDirs;          // unit direction of each star in the sky's own frame
        private float[] _starBright;          // 0.35..1 per star
        private Color[] _starTint;
        private readonly List<Color> _starColors = new List<Color>();
        private Renderer _starRend;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            // ambientLight only feeds the Flat mode; in the scene's default
            // Skybox mode the probe is baked once and 2 a.m. looks like noon.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            _discMat = new Material(MatLib.Transparent) { renderQueue = 1001 };
            BuildDome();
            _sunDisc = BuildDisc("SunDisc", 12f, false);
            _sunHalo = BuildDisc("SunHalo", 30f, true);
            _moonDisc = BuildDisc("Moon", 10f, false);
            BuildStars();
            BuildClouds();
            _built = true;
        }

        // ---- stars -----------------------------------------------------------

        /// <summary>600 hashed points on the sky sphere, each a small quad
        /// facing the centre (1-2 px at the dome's distance), in one mesh
        /// under a root that turns with the hour about the celestial pole.
        /// Alpha is set per star on recolour: darkness, cloud, haze and a
        /// fade-in above the horizon.</summary>
        private void BuildStars()
        {
            var pm = new ProcMesh();
            _starDirs = new Vector3[StarCount];
            _starBright = new float[StarCount];
            _starTint = new Color[StarCount];
            for (int i = 0; i < StarCount; i++)
            {
                // Uniform on the sphere: z in [-1, 1], azimuth in [0, 2π).
                float z = 2f * Hash(1000 + i, 0) - 1f;
                float az = Mathf.PI * 2f * Hash(1000 + i, 1);
                float rxy = Mathf.Sqrt(1f - z * z);
                var dir = new Vector3(rxy * Mathf.Cos(az), z, rxy * Mathf.Sin(az));
                float bright = Mathf.Pow(Hash(1000 + i, 2), 2f);          // few bright, many faint
                float size = 0.9f + 1.4f * bright;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized * size * 0.5f;
                Vector3 v = Vector3.Cross(dir, u).normalized * size * 0.5f;
                Vector3 c = dir * StarDistance;
                pm.Quad(c - u - v, c + u - v, c + u + v, c - u + v, Color.white);
                pm.Quad(c + u - v, c - u - v, c - u + v, c + u + v, Color.white);
                _starDirs[i] = dir;
                _starBright[i] = 0.35f + 0.65f * bright;
                float hue = Hash(1000 + i, 3);
                _starTint[i] = hue < 0.15f ? Color.Lerp(Color.white, Palette.Amber, 0.35f)
                             : hue < 0.45f ? Color.Lerp(Color.white, Palette.PaleBlue, 0.35f) : Color.white;
            }
            _starMesh = pm.Build("Stars");
            var go = MatLib.Spawn("Stars", _starMesh, null, Vector3.zero, collider: false, transparent: true);
            _starRend = go.GetComponent<MeshRenderer>();
            _starRend.sharedMaterial = _discMat;
            _starRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _starRend.receiveShadows = false;
            _starRend.enabled = false;
            _stars = go.transform;
            for (int k = 0; k < StarCount * 8; k++) _starColors.Add(Color.white);
        }

        /// <summary>The sky turns 15° per hour about the pole (plus the slow
        /// seasonal drift), so the same stars rise in the east.</summary>
        private void PlaceStars(in SkyState st, Vector3 camPos)
        {
            _stars.position = camPos;
            float doy = SimClock.DayOfYear((long)Math.Floor(st.Vt));
            _stars.rotation = Quaternion.AngleAxis(-(15f * st.HourF + 0.9856f * doy), PoleAxis);
        }

        private void RecolourStars(in SkyState st)
        {
            float dark = Mathf.Clamp01((-st.SunElevationDeg - 6f) / 8f);
            float haze = Mathf.Exp(-(150f * st.FogDensity) * (150f * st.FogDensity));
            float clear = Mathf.Pow(1f - st.Cloud, 2f);
            float global = dark * clear * haze;
            bool visible = global > 0.02f;
            _starRend.enabled = visible;
            if (!visible) return;
            Quaternion rot = _stars.rotation;
            for (int i = 0; i < StarCount; i++)
            {
                float y = (rot * _starDirs[i]).y;
                float a = global * _starBright[i] * Mathf.Clamp01((y - 0.02f) / 0.12f);
                Color c = _starTint[i];
                c.a = a;
                for (int k = 0; k < 8; k++) _starColors[i * 8 + k] = c;
            }
            _starMesh.SetColors(_starColors);
        }

        // ---- pure sun geometry ---------------------------------------------

        /// <summary>0 midwinter .. 1 midsummer, peaking around day 201 (20 July).</summary>
        public static float SummerFrac(long tick)
        {
            int doy = SimClock.DayOfYear(tick);
            return 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (doy - 19) / 365f);
        }

        /// <summary>Solar declination in degrees for the tick's day.</summary>
        public static float DeclinationDeg(long tick)
        {
            return -23.44f + 46.88f * SummerFrac(tick);
        }

        /// <summary>Sun elevation above the horizon in degrees at a fractional
        /// tick (tick + sub-tick fraction), negative below it.</summary>
        public static float SunElevationDeg(double fracTick)
        {
            long tick = (long)Math.Floor(fracTick);
            float hour = SimClock.HourOfDay(tick) + (float)(fracTick - tick);
            float lat = LatitudeDeg * Mathf.Deg2Rad;
            float dec = DeclinationDeg(tick) * Mathf.Deg2Rad;
            float ha = (hour - SolarNoonHour) * 15f * Mathf.Deg2Rad;
            float sinEl = Mathf.Sin(lat) * Mathf.Sin(dec) + Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(ha);
            return Mathf.Asin(Mathf.Clamp(sinEl, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Sun azimuth in degrees, 0 = +z (north), 90 = +x (east),
        /// 180 = south — the site's own bearing convention.</summary>
        public static float SunAzimuthDeg(double fracTick)
        {
            long tick = (long)Math.Floor(fracTick);
            float hour = SimClock.HourOfDay(tick) + (float)(fracTick - tick);
            float lat = LatitudeDeg * Mathf.Deg2Rad;
            float dec = DeclinationDeg(tick) * Mathf.Deg2Rad;
            float ha = (hour - SolarNoonHour) * 15f * Mathf.Deg2Rad;
            // 00:00-01:00 is 11-12 h AFTER noon, not before: wrap into (-π, π]
            // or the moon jumps across north at midnight.
            if (ha <= -Mathf.PI) ha += 2f * Mathf.PI;
            if (ha > Mathf.PI) ha -= 2f * Mathf.PI;
            float el = SunElevationDeg(fracTick) * Mathf.Deg2Rad;
            float cosAz = (Mathf.Sin(dec) - Mathf.Sin(el) * Mathf.Sin(lat)) / Mathf.Max(1e-4f, Mathf.Cos(el) * Mathf.Cos(lat));
            float az = Mathf.Acos(Mathf.Clamp(cosAz, -1f, 1f)) * Mathf.Rad2Deg;
            // Before solar noon the sun is in the east, after it in the west.
            return ha < 0f ? az : 360f - az;
        }

        /// <summary>Direction TO the sun in world space for a fractional tick.</summary>
        public static Vector3 SunDirection(double fracTick)
        {
            float el = SunElevationDeg(fracTick) * Mathf.Deg2Rad;
            float az = SunAzimuthDeg(fracTick) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }

        /// <summary>Civil darkness: the sun is more than 6° below the horizon.
        /// Window lights, lamp heads and the figures' phones key off this.</summary>
        public static bool IsDark(long tick)
        {
            return SunElevationDeg(tick + 0.5) < -6f;
        }

        public static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The authoritative tick: the replicated one on a client.</summary>
        public static long CurrentTick()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return net.RemoteTick;
            var d = GameBootstrap.Driver;
            return d != null && d.Sim != null ? d.Sim.State.Tick : 0;
        }

        // ---- visual tick ---------------------------------------------------

        /// <summary>The fractional tick the sky is drawn at: the tick of the
        /// report being rendered (State.Tick - 1 == Latest.Tick) plus the
        /// driver's sub-tick, so rain, cloud and the clock describe the same
        /// hour. A client integrates the host's rate and snaps when it drifts.
        ///
        /// The host broadcasts RemoteTick only every 0.2 s (NetSession), i.e.
        /// ~5 ticks per snapshot at the default 24 t/s, so the target is
        /// extrapolated from the last change and the snap threshold scales
        /// with the rate — a fixed 2-tick threshold snaps on every snapshot.</summary>
        private double VisualTick(float dt)
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient)
            {
                float now = Time.unscaledTime;
                if (net.RemoteTick != _lastRemoteTick || net.RemoteTimePaused != _lastRemotePaused)
                {
                    _lastRemoteTick = net.RemoteTick;
                    _lastRemotePaused = net.RemoteTimePaused;
                    _lastSnapTime = now;
                }
                double target = Math.Max(0, net.RemoteTick - 1);
                if (!net.RemoteTimePaused) target += net.RemoteTps * (now - _lastSnapTime);
                double delta = _vt - target;
                double snap = Math.Max(2.0, 0.5 * net.RemoteTps);     // >= half a second of drift
                double step = net.RemoteTps * dt;
                if (Math.Abs(delta) > snap) _vt = target;
                else if (net.RemoteTimePaused)
                    // Settle on the host's paused hour instead of freezing the offset.
                    _vt = delta > 0 ? Math.Max(target, _vt - step) : Math.Min(target, _vt + step);
                else _vt += step * (delta < -0.5 ? 1.5f : delta > 0.5 ? 0.75f : 1f);
                return _vt;
            }
            _lastRemoteTick = -1;
            var d = GameBootstrap.Driver;
            if (d == null || d.Sim == null) { _vt = 0; return 0; }
            double vt = (d.Sim.State.Tick - 1) + d.SubTick;
            _vt = vt < 0 ? 0 : vt;
            return _vt;
        }

        // ---- sky state: pure function of (vt, report) -----------------------

        private static SkyState ComputeSky(double vt, TickReport r)
        {
            SkyState st = default;
            long tick = (long)Math.Floor(vt);
            st.Vt = vt;
            st.HourF = SimClock.HourOfDay(tick) + (float)(vt - tick);
            st.Summer = SummerFrac(tick);
            int month = SimClock.Month(tick);
            st.Autumn = Mathf.Clamp01(1f - Mathf.Abs(month - 10) / 2.5f);
            st.SunElevationDeg = SunElevationDeg(vt);
            st.SunAzimuthDeg = SunAzimuthDeg(vt);
            st.ToSun = SunDirection(vt);
            st.ToMoon = -st.ToSun;                     // always-full anti-sun moon
            st.SunUp = Smoothstep(-8f, 10f, st.SunElevationDeg);
            st.Dusk = 1f - Mathf.Abs(Mathf.Clamp(st.SunElevationDeg, -8f, 8f)) / 8f;
            st.Cloud = Mathf.Clamp01((float)r.CloudFrac);
            st.RainMmH = Mathf.Max(0f, (float)r.RainMmH);
            st.Snow = st.RainMmH > 0f && r.TdbC < 0.5;
            st.Dark = IsDark(tick);

            float cloud = st.Cloud, sunUp = st.SunUp;
            Color zenithClear = Color.Lerp(new Color(0.55f, 0.65f, 0.78f), new Color(0.45f, 0.62f, 0.85f), st.Summer);
            Color horizonClear = Color.Lerp(new Color(0.72f, 0.76f, 0.80f), Palette.Sky, st.Summer);
            Color dayZ = Color.Lerp(zenithClear, new Color(0.58f, 0.61f, 0.64f), cloud);
            Color dayH = Color.Lerp(horizonClear, new Color(0.70f, 0.71f, 0.72f), cloud);
            Color nightZ = Color.Lerp(new Color(0.05f, 0.06f, 0.09f), new Color(0.08f, 0.08f, 0.10f), cloud);
            Color nightH = Color.Lerp(new Color(0.12f, 0.14f, 0.18f), new Color(0.14f, 0.14f, 0.16f), cloud);
            st.Zenith = Color.Lerp(nightZ, dayZ, sunUp);
            st.Horizon = Color.Lerp(nightH, dayH, sunUp);
            // The art-bible skybox table wants autumn amber-tinted. Per the
            // design critique the blend sits on the horizon, not on the Sun
            // light's colour (which stays the dusk→noon ramp below).
            Color amberH = Palette.Amber * 0.6f + st.Horizon * 0.4f;
            st.Horizon = Color.Lerp(st.Horizon, amberH, 0.35f * st.Autumn * sunUp);
            st.Horizon.a = 1f; st.Zenith.a = 1f;
            st.Fog = st.Horizon;

            Color amb = Color.Lerp(st.Zenith, st.Horizon, 0.5f) * (0.9f - 0.15f * cloud);
            amb.r = Mathf.Max(amb.r, AmbientFloor.r);
            amb.g = Mathf.Max(amb.g, AmbientFloor.g);
            amb.b = Mathf.Max(amb.b, AmbientFloor.b);
            amb.a = 1f;
            st.Ambient = amb;

            Color noon = Color.Lerp(new Color(0.85f, 0.88f, 1f), new Color(1f, 0.96f, 0.85f), st.Summer);
            st.SunColor = Color.Lerp(new Color(1f, 0.62f, 0.35f), noon, Mathf.Clamp01(st.SunElevationDeg / 25f));

            // Clouds: Render lit by day, towards Slate-shaded Sky when overcast, Ink at night.
            Color lit = Color.Lerp(Palette.Render, Color.white, 0.3f);
            Color shaded = Color.Lerp(Palette.Sky, Palette.Slate, 0.35f);
            Color tint = Color.Lerp(Palette.Ink, Color.Lerp(lit, shaded, cloud), sunUp);
            st.CloudTint = Color.Lerp(tint, DuskTint, 0.5f * st.Dusk * (1f - 0.5f * cloud));
            st.CloudTint.a = 1f;

            st.FogDensity = FogDensityFor(st, (float)r.WindSpeedMs);
            return st;
        }

        /// <summary>Radiation fog at dawn (clear autumn nights), wet air,
        /// cloud, snow; wind clears it. 0.018 exp² ≈ 150 m visibility.</summary>
        private static float FogDensityFor(in SkyState st, float windMs)
        {
            float morning = 1f - Smoothstep(0f, 3f, Mathf.Abs(st.HourF - 6.5f));
            float radiation = morning * (1f - 0.5f * st.Cloud) * (0.5f + 0.5f * st.Autumn);
            float wet = st.RainMmH > 0f ? 0.35f + 0.1f * Mathf.Clamp01(st.RainMmH / 6f) : 0f;
            float d = Mathf.Lerp(0.004f, 0.0015f, st.Summer)
                      + 0.006f * radiation + 0.004f * wet + 0.0015f * st.Cloud + (st.Snow ? 0.003f : 0f);
            d *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((windMs - 4f) / 8f));
            return Mathf.Min(d, 0.018f);
        }

        /// <summary>Sky colour just above the horizon in a given direction: the
        /// base, warmed towards the sun at dusk, plus the town's window glow
        /// summed over every residential bearing (residents as light).</summary>
        private static Color HorizonAt(in SkyState st, Vector3 dir, SiteRefs refs)
        {
            float dx = dir.x, dz = dir.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len > 1e-4f) { dx /= len; dz /= len; }
            float sx = st.ToSun.x, sz = st.ToSun.z;
            float slen = Mathf.Sqrt(sx * sx + sz * sz);
            if (slen > 1e-4f) { sx /= slen; sz /= slen; }
            float toSun = Mathf.Max(0f, dx * sx + dz * sz);
            Color c = Color.Lerp(st.Horizon, DuskTint, st.Dusk * toSun * toSun * (1f - 0.7f * st.Cloud));
            if (refs != null && st.SunUp < 1f)
            {
                float glow = 0f;
                var bearings = refs.TownBearingsDeg;
                for (int i = 0; i < bearings.Count; i++)
                {
                    float b = bearings[i] * Mathf.Deg2Rad;
                    float d = Mathf.Max(0f, dx * Mathf.Sin(b) + dz * Mathf.Cos(b));
                    glow += d * d * d;
                }
                c = c + TownGlow * (glow * (1f - st.SunUp));
            }
            c.a = 1f;
            return c;
        }

        // ---- the one directional light ------------------------------------

        /// <summary>Never lower than 10° so shadows never go horizontal.</summary>
        private static Vector3 ClampElevation(Vector3 dir, float minDeg)
        {
            float minY = Mathf.Sin(minDeg * Mathf.Deg2Rad);
            if (dir.y >= minY) return dir.normalized;
            float hx = dir.x, hz = dir.z;
            float len = Mathf.Sqrt(hx * hx + hz * hz);
            if (len < 1e-4f) return Vector3.up;
            float cosE = Mathf.Cos(minDeg * Mathf.Deg2Rad);
            return new Vector3(hx / len * cosE, minY, hz / len * cosE);
        }

        /// <summary>Sun by day, moon by night, blended across the crossover
        /// while the shadow strength dips to zero — so the 180° flip of the
        /// only shadow caster happens while shadows are invisible.</summary>
        private void ApplyLight(in SkyState st)
        {
            float w = Smoothstep(-8f, 2f, st.SunElevationDeg);
            Vector3 sunC = ClampElevation(st.ToSun, 10f);
            Vector3 moonC = ClampElevation(st.ToMoon, 10f);
            Vector3 lightDir = Vector3.Slerp(moonC, sunC, w).normalized;
            _sun.transform.rotation = Quaternion.LookRotation(-lightDir);

            float dayI = (0.05f + st.SunUp * (0.85f + 0.3f * st.Summer)) * (1f - 0.55f * st.Cloud);
            _sun.intensity = Mathf.Lerp(0.06f, dayI, w);
            _sun.color = Color.Lerp(NightLight, st.SunColor, w);
            _sun.shadowStrength = Mathf.Lerp(0.85f, 0.25f, st.Cloud) * (1f - 4f * w * (1f - w));
            RenderSettings.ambientLight = st.Ambient;
        }

        // ---- sky dome ------------------------------------------------------

        /// <summary>Lat/long dome from a below-horizon skirt to the zenith,
        /// drawn first (queue 1000, unlit, no fog, Cull Off so it is visible
        /// from inside) and carried with the camera. Only its vertex colours
        /// ever change.</summary>
        private void BuildDome()
        {
            var pm = new ProcMesh();
            var dirs = new List<Vector3>(DomeSegments * DomeRings * 4);
            for (int j = 0; j < DomeRings; j++)
            {
                float lat0 = Mathf.Lerp(DomeSkirtDeg, 90f, (float)j / DomeRings) * Mathf.Deg2Rad;
                float lat1 = Mathf.Lerp(DomeSkirtDeg, 90f, (float)(j + 1) / DomeRings) * Mathf.Deg2Rad;
                for (int i = 0; i < DomeSegments; i++)
                {
                    float az0 = Mathf.PI * 2f * i / DomeSegments;
                    float az1 = Mathf.PI * 2f * (i + 1) / DomeSegments;
                    Vector3 a = DomeDir(lat0, az0), b = DomeDir(lat0, az1);
                    Vector3 c = DomeDir(lat1, az1), d = DomeDir(lat1, az0);
                    pm.Quad(a * DomeRadius, b * DomeRadius, c * DomeRadius, d * DomeRadius, Color.white);
                    dirs.Add(a); dirs.Add(b); dirs.Add(c); dirs.Add(d);
                }
            }
            _domeDirs = dirs.ToArray();
            _domeMesh = pm.Build("SkyDome");
            var go = MatLib.Spawn("SkyDome", _domeMesh, null, Vector3.zero, collider: false, transparent: true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(MatLib.Transparent) { renderQueue = 1000 };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _dome = go.transform;
            for (int k = 0; k < _domeDirs.Length; k++) _domeColors.Add(Color.white);
        }

        private static Vector3 DomeDir(float lat, float az)
        {
            float c = Mathf.Cos(lat);
            return new Vector3(c * Mathf.Sin(az), Mathf.Sin(lat), c * Mathf.Cos(az));
        }

        private void RecolourDome(in SkyState st, SiteRefs refs)
        {
            for (int k = 0; k < _domeDirs.Length; k++)
            {
                Vector3 dir = _domeDirs[k];
                float t = Mathf.Pow(Mathf.Max(0f, dir.y), 0.6f);
                _domeColors[k] = Color.Lerp(HorizonAt(st, dir, refs), st.Zenith, t);
            }
            _domeMesh.SetColors(_domeColors);
        }

        // ---- sun, halo, moon -------------------------------------------------

        /// <summary>A 12-gon in the local XY plane (normal along local z), so
        /// LookRotation(dir) billboards it. The fan's centre vertex is the
        /// first of each degenerate quad; a soft disc fades to its rim.</summary>
        private Disc BuildDisc(string name, float radius, bool soft)
        {
            const int sides = 12;
            var pm = new ProcMesh();
            var shade = new float[sides * 4];
            for (int s = 0; s < sides; s++)
            {
                float a0 = Mathf.PI * 2f * s / sides, a1 = Mathf.PI * 2f * (s + 1) / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0);
                pm.Quad(Vector3.zero, p0, p1, p1, Color.white);
                shade[s * 4] = 1f;
                float rim = soft ? 0f : 1f;
                shade[s * 4 + 1] = rim; shade[s * 4 + 2] = rim; shade[s * 4 + 3] = rim;
            }
            var disc = new Disc();
            disc.Mesh = pm.Build(name);
            disc.Shade = shade;
            var go = MatLib.Spawn(name, disc.Mesh, null, Vector3.zero, collider: false, transparent: true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _discMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            disc.Rend = mr;
            disc.Tf = go.transform;
            for (int k = 0; k < shade.Length; k++) disc.Colors.Add(Color.white);
            return disc;
        }

        /// <summary>Queue 1001 sorts front-to-back, so the halo sits a little
        /// nearer than the disc and the disc draws over it.</summary>
        private static void PlaceDisc(Disc d, Vector3 camPos, Vector3 dir, float distance, bool visible)
        {
            d.Rend.enabled = visible;
            if (!visible) return;
            d.Tf.position = camPos + dir * distance;
            d.Tf.rotation = Quaternion.LookRotation(dir);
        }

        private static void Tint(Disc d, Color c, float alpha)
        {
            for (int k = 0; k < d.Shade.Length; k++)
            {
                Color v = c;
                v.a = alpha * d.Shade[k];
                d.Colors[k] = v;
            }
            d.Mesh.SetColors(d.Colors);
        }

        /// <summary>Alpha from cloud and a mild haze term, so a clear winter
        /// sun (base fog 0.004) still shows while thick dawn fog eats it.</summary>
        private void RecolourDiscs(in SkyState st)
        {
            float haze = Mathf.Exp(-(150f * st.FogDensity) * (150f * st.FogDensity));
            float clear = Mathf.Pow(1f - st.Cloud, 1.5f);
            Color sun = Color.Lerp(Palette.Amber, new Color(1f, 0.97f, 0.85f), Mathf.Clamp01(st.SunElevationDeg / 30f));
            Tint(_sunDisc, sun, clear * haze);
            Tint(_sunHalo, sun, 0.15f * (1f - 0.5f * st.Cloud) * haze);
            Color moon = Color.Lerp(Palette.Sky, Color.white, 0.4f);
            Tint(_moonDisc, moon, (1f - st.SunUp) * (1f - 0.9f * st.Cloud) * 0.9f * haze);
        }

        // ---- clouds --------------------------------------------------------

        private static float Hash(int i, int k)
        {
            return (float)SimRandom.Hash01(CloudSeed, (ulong)i, (ulong)k);
        }

        /// <summary>28 flat blobs, every dimension from the stateless hash so a
        /// host and its clients build the same sky. Blob i appears once the
        /// cloud fraction passes 0.9·i/28: a third of the sky dotted at 0.3, a
        /// lid at 0.9. A blob is three to five overlapping elliptical lobes,
        /// each a fan whose rim alpha is 0: the edges are soft and no
        /// rectangle is ever seen against the sky.</summary>
        private void BuildClouds()
        {
            _cloudRoot = new GameObject("Clouds").transform;
            const int sides = 14;
            for (int i = 0; i < CloudCount; i++)
            {
                var pm = new ProcMesh();
                int lobes = 3 + (int)(Hash(i, 3) * 3f);          // 3..5
                var shade = new List<float>();
                var rim = new List<float>();
                for (int q = 0; q < lobes; q++)
                {
                    int h = 10 + q * 5;
                    float rx = 25f + 30f * Hash(i, h), rz = 18f + 22f * Hash(i, h + 1);
                    float ox = q == 0 ? 0f : (Hash(i, h + 2) - 0.5f) * 60f;
                    float oz = q == 0 ? 0f : (Hash(i, h + 3) - 0.5f) * 40f;
                    float oy = q * 1.5f;                             // overlapping layers, never z-fighting
                    float s = q == 0 ? 1f : 0.9f + 0.15f * Hash(i, h + 4);
                    // A firm body out to 65 % of the radius, then a ring that
                    // fades to nothing: the lobe has weight and a soft edge,
                    // instead of being one flat gradient with no cloud in it.
                    const float core = 0.65f;
                    var centre = new Vector3(ox, oy, oz);
                    for (int k = 0; k < sides; k++)
                    {
                        float a0 = Mathf.PI * 2f * k / sides, a1 = Mathf.PI * 2f * (k + 1) / sides;
                        float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                        var i0 = new Vector3(ox + c0 * rx * core, oy, oz + s0 * rz * core);
                        var i1 = new Vector3(ox + c1 * rx * core, oy, oz + s1 * rz * core);
                        var o0 = new Vector3(ox + c0 * rx, oy, oz + s0 * rz);
                        var o1 = new Vector3(ox + c1 * rx, oy, oz + s1 * rz);
                        pm.Triangle(centre, i0, i1, Vector3.up, Color.white);
                        shade.Add(s); shade.Add(s); shade.Add(s);
                        rim.Add(1f); rim.Add(1f); rim.Add(1f);
                        pm.Triangle(i0, o0, o1, Vector3.up, Color.white);
                        shade.Add(s); shade.Add(s); shade.Add(s);
                        rim.Add(1f); rim.Add(0f); rim.Add(0f);
                        pm.Triangle(i0, o1, i1, Vector3.up, Color.white);
                        shade.Add(s); shade.Add(s); shade.Add(s);
                        rim.Add(1f); rim.Add(0f); rim.Add(1f);
                    }
                }
                var b = new Blob();
                b.Mesh = pm.Build("Cloud" + i);
                b.Shade = shade.ToArray();
                b.Rim = rim.ToArray();
                b.Base = new Vector3((Hash(i, 0) - 0.5f) * 2f * CloudSpread, 140f + 25f * Hash(i, 2),
                                     (Hash(i, 1) - 0.5f) * 2f * CloudSpread);
                b.Threshold = 0.9f * i / CloudCount;
                var go = MatLib.Spawn("Cloud" + i, b.Mesh, _cloudRoot, b.Base, collider: false, transparent: true);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
                b.Rend = mr;
                b.Tf = go.transform;
                for (int k = 0; k < b.Shade.Length; k++) b.Colors.Add(Color.white);
                _clouds[i] = b;
            }
        }

        private static float Wrap(float v)
        {
            return Mathf.Repeat(v + CloudSpread, 2f * CloudSpread) - CloudSpread;
        }

        /// <summary>World-fixed blob positions (base + wind drift), tiled into
        /// the ±450 m window around the camera; a blob re-enters on the far
        /// side where the distance fade has already made it invisible.</summary>
        private Vector3 CloudOffset(Blob b, Vector3 camPos)
        {
            return new Vector3(Wrap(b.Base.x + _drift.x - camPos.x), b.Base.y, Wrap(b.Base.z + _drift.y - camPos.z));
        }

        private void UpdateClouds(TickReport r, Vector3 camPos, float dt)
        {
            float rad = (float)r.WindTowardDeg * Mathf.Deg2Rad;
            float v = (float)r.WindSpeedMs * 0.5f * dt;   // same advection rule as the plume
            _drift.x += Mathf.Sin(rad) * v;
            _drift.y += Mathf.Cos(rad) * v;
            for (int i = 0; i < CloudCount; i++)
            {
                var b = _clouds[i];
                Vector3 off = CloudOffset(b, camPos);
                b.Tf.position = new Vector3(camPos.x + off.x, off.y, camPos.z + off.z);
            }
        }

        private void RecolourClouds(in SkyState st, Vector3 camPos)
        {
            float fog = st.FogDensity;
            for (int i = 0; i < CloudCount; i++)
            {
                var b = _clouds[i];
                Vector3 off = CloudOffset(b, camPos);
                float distXZ = Mathf.Sqrt(off.x * off.x + off.z * off.z);
                float dy = off.y - camPos.y;
                float dist = Mathf.Sqrt(distXZ * distXZ + dy * dy);
                float a = Mathf.Clamp01((st.Cloud - b.Threshold) / 0.25f) * 0.92f
                          * Mathf.Clamp01((430f - distXZ) / 80f)
                          * Mathf.Exp(-(dist * fog) * (dist * fog));
                bool visible = b.Threshold < st.Cloud && a > 0.004f;
                b.Rend.enabled = visible;
                if (!visible) continue;
                for (int k = 0; k < b.Shade.Length; k++)
                {
                    Color c = st.CloudTint * b.Shade[k];
                    c.a = a * b.Rim[k];
                    b.Colors[k] = c;
                }
                b.Mesh.SetColors(b.Colors);
            }
            _lastCloudCam = camPos;
            _lastCloudDrift = _drift;
        }

        // ---- rain / snow ---------------------------------------------------

        /// <summary>A 30×30 m box emitter 14 m above and 6 m ahead of the rig
        /// root (yaw-only, so it stays world-up whatever the camera's pitch).
        /// The seed can only be set while stopped; AddComponent starts playing.</summary>
        private void EnsurePrecip()
        {
            if (_precip != null) return;
            var rig = GameBootstrap.LocalPlayer;
            if (rig == null) return;
            var go = new GameObject("Precipitation");
            go.transform.SetParent(rig.transform, false);
            go.transform.localPosition = new Vector3(0, 14f, 6f);
            _precip = go.AddComponent<ParticleSystem>();
            _precip.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _precip.useAutoRandomSeed = false;
            _precip.randomSeed = 0xC10Du;

            var main = _precip.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var shape = _precip.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // The box emits along its local +z; pitched 90° that is straight
            // down and local y becomes world z, so the 30×30 sheet is (x, y).
            shape.scale = new Vector3(30f, 30f, 1f);
            shape.rotation = new Vector3(90f, 0, 0);
            var emission = _precip.emission;
            emission.rateOverTime = 0f;
            var vel = _precip.velocityOverLifetime;
            vel.enabled = true;
            // The module defaults to Local: the wind push would yaw with the rig.
            vel.space = ParticleSystemSimulationSpace.World;

            _precipRenderer = go.GetComponent<ParticleSystemRenderer>();
            _precipRenderer.material = MatLib.Transparent;
            ConfigurePrecip(false);
            _precip.Play();
        }

        /// <summary>Rain: stretched streaks under gravity. Snow: fine flakes
        /// that fall at their terminal speed and drift on a noise field.
        /// Both are camera-facing quads — a mesh particle does not receive
        /// the particle colour through this unlit vertex-colour shader and
        /// came out a muddy lavender. Switching clears the system so no
        /// streak turns into a flake mid-air.</summary>
        private void ConfigurePrecip(bool snow)
        {
            _precipSnow = snow;
            _precip.Clear();
            var main = _precip.main;
            var noise = _precip.noise;
            var shape = _precip.shape;
            if (snow)
            {
                _precipRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                // A flake falls at its terminal speed, about 1.1 m/s, and
                // never accelerates: under gravity it was 180 m below the
                // ground by the end of its life and the air looked empty.
                // 16 s carries it the 12 m from the emitter to the ground,
                // and the emitter sits over the player instead of ahead of
                // them, so the snow is all round rather than in one wall.
                _precip.transform.localPosition = new Vector3(0f, 12f, 0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
                main.startLifetime = 16f;
                main.startSpeed = 1.1f;
                main.gravityModifier = 0f;
                main.startColor = Color.white;
                main.maxParticles = 12000;
                // A 30 m sheet left the far half of the street bare; 44 m
                // reaches past the fence, and the rate rises with the area
                // so the air is no thinner.
                shape.scale = new Vector3(44f, 44f, 1f);
                noise.enabled = true;
                noise.strength = 0.4f;
                noise.frequency = 0.2f;
                noise.scrollSpeed = 0.15f;
                noise.damping = true;
            }
            else
            {
                // Rain falls fast enough to be read ahead of the player.
                _precip.transform.localPosition = new Vector3(0f, 14f, 6f);
                _precipRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                _precipRenderer.lengthScale = 5f;
                main.startSize = 0.035f;
                main.startLifetime = 2f;
                main.startSpeed = 14f;
                main.gravityModifier = 1f;
                main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.35f);
                main.maxParticles = 4000;
                shape.scale = new Vector3(30f, 30f, 1f);
                noise.enabled = false;
            }
        }

        private void UpdatePrecip(in SkyState st, TickReport r)
        {
            EnsurePrecip();
            if (_precip == null) return;
            // Snow only flips with the report (TdbC / RainMmH are per-tick values), so
            // this reconfigures at most once per tick and also on the first wet frame
            // after the lazy creation.
            if (st.RainMmH > 0f && st.Snow != _precipSnow) ConfigurePrecip(st.Snow);

            float rate = st.RainMmH <= 0f || st.Indoors ? 0f : st.RainMmH * (st.Snow ? 700f : 250f);
            var emission = _precip.emission;
            emission.rateOverTime = rate;
            if (rate > 0f)
            {
                float rad = (float)r.WindTowardDeg * Mathf.Deg2Rad;
                float w = (float)r.WindSpeedMs * (st.Snow ? 0.4f : 0.3f);
                var vel = _precip.velocityOverLifetime;
                vel.x = Mathf.Sin(rad) * w;
                vel.y = 0f;
                vel.z = Mathf.Cos(rad) * w;
            }
        }

        /// <summary>A roof over the camera, sampled at 4 Hz: any Room bound, or
        /// anything an upward ray hits within 40 m (canopies without a Room).</summary>
        private bool IndoorsNow(Vector3 camPos, bool previous)
        {
            if (Time.time < _nextIndoorCheck) return previous;
            _nextIndoorCheck = Time.time + 0.25f;
            var site = GameBootstrap.Site;
            if (site != null)
                for (int i = 0; i < site.Rooms.Count; i++)
                    if (site.Rooms[i].Contains(camPos)) return true;
            RaycastHit hit;
            return Physics.Raycast(new Ray(camPos, Vector3.up), out hit, 40f);
        }

        // ---- the site: lamp heads, the turbine -----------------------------

        /// <summary>Null-checked every frame: the site pass rebuilds the
        /// turbine and the lamp row while this runs. The lamps follow the
        /// rendered hour (Sky.Dark), not the authoritative tick, so they agree
        /// with the sky and the HUD line at dusk and dawn.</summary>
        private void UpdateSite(in SkyState st, TickReport r, float dt)
        {
            var refs = GameBootstrap.Site;
            if (refs == null) return;
            if (refs.StreetLampHeads != null) refs.StreetLampHeads.enabled = st.Dark;

            if (refs.TurbineRotor == null || refs.TurbineNacelle == null) return;
            // The sim's wind tops out near 10 m/s (WIND_SPEED_MONTH × 1.4), so
            // the curve runs cut-in 2 → 14 rpm at 10; no storm cut-out exists.
            float v = (float)r.WindSpeedMs;
            float rpm = v < 2f ? 0f : Mathf.Lerp(4f, 14f, Mathf.Clamp01((v - 2f) / 8f));
            _rotorOmega = Mathf.MoveTowards(_rotorOmega, rpm * 6f, 20f * dt);
            refs.TurbineRotor.Rotate(0, 0, -_rotorOmega * dt, Space.Self);

            // The rotor faces INTO the wind: nacelle +z points at where it comes from.
            float yawTarget = (float)r.WindTowardDeg + 180f;
            Vector3 e = refs.TurbineNacelle.localEulerAngles;
            e.y = Mathf.MoveTowardsAngle(e.y, yawTarget, 10f * dt);
            refs.TurbineNacelle.localEulerAngles = e;
        }

        // ---- HUD line ------------------------------------------------------

        private static readonly string[] Compass16 =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        /// <summary>"{T} °C · {sky} · {precip} · wind {v} m/s → {toward}" plus
        /// the report's flags. The arrow points where the wind blows TOWARD,
        /// like the HUD's own wind arrow. Rebuilt once per tick change.</summary>
        private string BuildHudLine(in SkyState st, TickReport r)
        {
            var ci = CultureInfo.InvariantCulture;
            float v = (float)r.WindSpeedMs;
            string sky = st.Cloud < 0.25f ? (st.Dark ? "clear night" : "clear")
                       : st.Cloud < 0.5f ? "few clouds"
                       : st.Cloud < 0.75f ? "cloudy" : "overcast";
            string precip = st.RainMmH <= 0f ? "dry"
                          : st.Snow ? (v >= WindyMs ? "blizzard" : st.RainMmH < 1f ? "light snow" : "snow")
                          : st.RainMmH < 1f ? "drizzle" : st.RainMmH < 4f ? "rain" : "heavy rain";
            int dir = ((int)Mathf.Round((float)r.WindTowardDeg / 22.5f) % 16 + 16) % 16;

            _sb.Length = 0;
            _sb.Append(((int)Math.Round(r.TdbC)).ToString(ci)).Append(" °C · ")
               .Append(sky).Append(" · ").Append(precip)
               .Append(" · wind ").Append(((int)Math.Round(v)).ToString(ci)).Append(" m/s → ")
               .Append(Compass16[dir]);
            if (r.HeatwaveActive) _sb.Append(" · HEATWAVE");
            if (r.DroughtActive) _sb.Append(" · drought");
            if (r.DunkelflauteActive) _sb.Append(" · dunkelflaute");
            if (v >= WindyMs) _sb.Append(" · windy");
            if (st.FogDensity >= FogHudThreshold) _sb.Append(" · fog");
            return _sb.ToString();
        }

        // ---- frame update --------------------------------------------------

        private void Update()
        {
            if (!_built) return;
            float dt = Time.deltaTime;
            TickReport r = GameBootstrap.CurrentReport;
            double vt = VisualTick(dt);
            long tick = (long)Math.Floor(vt);
            bool tickChanged = tick != _lastTick;

            SkyState st = ComputeSky(vt, r);
            var rig = GameBootstrap.LocalPlayer;
            var cam = rig != null ? rig.Cam : null;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            st.Indoors = cam != null && IndoorsNow(camPos, _last.Indoors);

            ApplyLight(st);
            RenderSettings.fogDensity = st.FogDensity;
            RenderSettings.fogColor = st.Fog;

            if (cam != null)
            {
                // Solid clear = the horizon: the fallback beyond the dome's skirt.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = st.Horizon;
                _dome.position = camPos;
                bool sunVisible = st.SunElevationDeg > -4f;
                PlaceDisc(_sunDisc, camPos, st.ToSun, DiscDistance, sunVisible);
                PlaceDisc(_sunHalo, camPos, st.ToSun, DiscDistance - 2f, sunVisible);
                PlaceDisc(_moonDisc, camPos, st.ToMoon, DiscDistance, st.SunElevationDeg < 4f && st.ToMoon.y > -0.05f);
                PlaceStars(st, camPos);
                UpdateClouds(r, camPos, dt);
                if (NeedsRecolour(st, vt, camPos))
                {
                    var refs = GameBootstrap.Site;
                    RecolourDome(st, refs);
                    RecolourDiscs(st);
                    RecolourStars(st);
                    RecolourClouds(st, camPos);
                    _lastRecolourVt = vt;
                    _nextRecolourTime = Time.time + RecolourMinInterval;
                }
                UpdatePrecip(st, r);
            }

            UpdateSite(st, r, dt);
            if (tickChanged || HudLine.Length == 0)
            {
                HudLine = BuildHudLine(st, r);
                _lastTick = tick;
            }
            Sky = st;
            _last = st;
        }

        /// <summary>Recolour on a visible change of state, every quarter sim
        /// hour, or when the cloud layer moved relative to the camera; never
        /// more than 30 times a second (a time-lapse at 24 t/s stays smooth,
        /// a paused sim costs nothing).</summary>
        private bool NeedsRecolour(in SkyState st, double vt, Vector3 camPos)
        {
            if (Time.time < _nextRecolourTime) return false;
            if (double.IsNegativeInfinity(_lastRecolourVt)) return true;
            if (vt - _lastRecolourVt >= RecolourSimHours || vt < _lastRecolourVt) return true;
            if (Mathf.Abs(st.SunUp - _last.SunUp) > 0.01f || Mathf.Abs(st.Cloud - _last.Cloud) > 0.01f ||
                Mathf.Abs(st.Dusk - _last.Dusk) > 0.01f || Mathf.Abs(st.Summer - _last.Summer) > 0.01f ||
                Mathf.Abs(st.Autumn - _last.Autumn) > 0.01f ||
                Mathf.Abs(st.FogDensity - _last.FogDensity) > 0.0005f) return true;
            float cx = camPos.x - _lastCloudCam.x, cz = camPos.z - _lastCloudCam.z;
            float dx = _drift.x - _lastCloudDrift.x, dz = _drift.y - _lastCloudDrift.y;
            return cx * cx + cz * cz > 100f || dx * dx + dz * dz > 100f;
        }
    }
}
