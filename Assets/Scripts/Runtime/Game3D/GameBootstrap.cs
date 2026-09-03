using System;
using System.Collections.Generic;
using Game.Runtime.DebugTools;
using Game.Runtime.Media;
using Game.Runtime.Net;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Assembles the playable world on top of the SimDriver: site, facility,
    /// player, HUD, media, seasons/VFX, audio, networking, saves. Added by the
    /// SimDriver bootstrap so entering Play mode in any scene produces the full
    /// game — there is still no authored scene content anywhere.
    ///
    /// Also the single funnel for player-caused sim commands: SendCommand
    /// routes locally (recorded for the save system and stamped into the
    /// ledger) or via the network when this instance is a client.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static SimDriver Driver { get; private set; }
        public static PlayerRig LocalPlayer { get; private set; }
        public static FacilityController Facility { get; private set; }
        public static SiteRefs Site { get; private set; }
        public static NetSession Net { get; private set; }
        public static SaveSystem Saves { get; private set; }
        public static readonly List<string> Ledger = new List<string>();

        /// <summary>The report every presentation system reads: the replicated
        /// snapshot on a client (whose own sim is paused and stale), the live
        /// sim's latest tick otherwise. HUD, audio, VFX, freeze, media — all
        /// of it keys off this so clients see the host's reality.</summary>
        public static TickReport CurrentReport
        {
            get
            {
                if (Net != null && Net.IsClient) return Net.RemoteReport;
                return Driver != null ? Driver.Latest : default;
            }
        }

        /// <summary>True while any IMGUI window is open (console, editors,
        /// options, network panel, pause menu). Windows own the mouse; the
        /// player rig neither moves nor aims until they close.</summary>
        public static bool UiWantsCursor
        {
            get { return UiWindows.AnyOpen; }
        }

        /// <summary>Who the ledger blames for a local act: the host's chosen
        /// name in a session, the rig's name otherwise.</summary>
        public static string LocalActorName()
        {
            if (Net != null && Net.IsHost) return Net.LocalPlayerName;
            return LocalPlayer != null ? LocalPlayer.PlayerName : "operator";
        }

        private void Awake()
        {
            Driver = GetComponent<SimDriver>();

            // The site is lit by two dozen point lights and one sun. The
            // project's quality levels allow four pixel lights and no shadows
            // on the lower tiers, which renders every ceiling light as
            // nothing and lets the sun through the slabs into the basement.
            // 23 ceiling lights + sun + the truck's beacon and headlights all
            // touch a whole-storey mesh; a light demoted past the cap is gone
            // (there is no vertex-light path), and the pulsing beacon would
            // demote a different ceiling light every half second.
            QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 40);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 140f);

            // The code assumes an empty scene; a scene Unity creates is not
            // (Main Camera, Directional Light). A second directional light
            // shines through every slab unshadowed, a second camera renders
            // the whole site twice, its listener doubles the audio.
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.gameObject.SetActive(false);
            foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                c.gameObject.SetActive(false);

            Site = SiteBuilder.Build(transform);
            Facility = gameObject.AddComponent<FacilityController>();
            Facility.Init(Site);

            LocalPlayer = PlayerRig.Create(Site.StreetSpawn, "Operator");
            RespawnSystem.Init(Site);

            // Weather before the season/VFX pass and the figures: both read
            // its sky state and the shared IsDark helper the same frame.
            gameObject.AddComponent<Weather>();
            gameObject.AddComponent<SeasonAndVfx>();
            gameObject.AddComponent<Presence>();
            gameObject.AddComponent<SiteAudio>();
            // The window host first: it owns every window's OnGUI, Escape
            // and the cursor. Windows register themselves from Awake.
            gameObject.AddComponent<UiWindows>();
            gameObject.AddComponent<NewsTicker>();
            gameObject.AddComponent<BulletinEditor>();
            gameObject.AddComponent<ContractEditor>();
            gameObject.AddComponent<Hud>();
            gameObject.AddComponent<PlayerOptions>();
            Net = gameObject.AddComponent<NetSession>();
            Saves = gameObject.AddComponent<SaveSystem>();
            gameObject.AddComponent<PauseMenu>();

            HazardInstaller.Install(Site, Facility);

            // The clock runs from the first frame at the driver's walking
            // pace (one sim hour per ten seconds); a net session pauses and
            // resumes it itself, and F1 still holds the faster presets.
            if (Driver != null) Driver.Paused = false;

            NewsFeed.Post("A new operator has taken over the old barn site. " +
                "The Good Neighbor Program welcomes the community's continued partnership.");
            NewsFeed.Post("ORIENTATION: the operations terminal is F1 — contracts, money and " +
                "the town's mood live there. The desk in the office publishes press releases; " +
                "the Program recommends restraint.");
            NewsFeed.Post("SITE NOTE: every control on this site can be operated by anyone " +
                "at any time, an arrangement the Program describes as \"empowerment\".");
        }

        /// <summary>Every player-caused sim mutation goes through here — the
        /// ledger records the name and the act, never the intent.</summary>
        public static void SendCommand(SimCommand cmd, string ledgerLine)
        {
            if (Net != null && Net.IsClient)
            {
                Net.SendCommandToHost(cmd, ledgerLine);
                return;
            }
            if (Driver == null || Driver.Sim == null) return;
            Driver.EnqueueRecorded(cmd);
            if (ledgerLine != null) AddLedger(LocalActorName(), ledgerLine);
        }

        public static void AddLedger(string actor, string line)
        {
            long tick = Driver != null && Driver.Sim != null ? Driver.Sim.State.Tick : 0;
            string entry = "d" + SimClock.DayIndex(tick).ToString("000") +
                " h" + SimClock.HourOfDay(tick).ToString("00") + "  " + actor + ": " + line;
            Ledger.Add(entry);
            if (Ledger.Count > 400) Ledger.RemoveAt(0);
            if (Net != null && Net.IsHost) Net.BroadcastLedger(entry);
        }
    }
}
