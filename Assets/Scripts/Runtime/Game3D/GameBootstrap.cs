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
        public static readonly List<string> Ledger = new List<string>();

        /// <summary>True while a mouse-driven UI (console with freed cursor,
        /// bulletin editor) should swallow clicks meant for placement.</summary>
        public static bool UiCapturesMouse
        {
            get { return Cursor.lockState != CursorLockMode.Locked; }
        }

        private void Awake()
        {
            Driver = GetComponent<SimDriver>();

            Site = SiteBuilder.Build(transform);
            Facility = gameObject.AddComponent<FacilityController>();
            Facility.Init(Site);

            LocalPlayer = PlayerRig.Create(Site.CarParkSpawn, "Operator");
            RespawnSystem.Init(Site);

            gameObject.AddComponent<SeasonAndVfx>();
            gameObject.AddComponent<SiteAudio>();
            gameObject.AddComponent<NewsTicker>();
            gameObject.AddComponent<BulletinEditor>();
            gameObject.AddComponent<Hud>();
            Net = gameObject.AddComponent<NetSession>();
            gameObject.AddComponent<SaveSystem>();

            HazardInstaller.Install(Site, Facility);

            NewsFeed.Post("A new operator has taken over the old barn site. " +
                "The Good Neighbor Program welcomes the community's continued partnership.");
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
            if (ledgerLine != null)
            {
                string actor = Net != null && Net.IsHost ? Net.LocalPlayerName
                             : LocalPlayer != null ? LocalPlayer.PlayerName : "operator";
                AddLedger(actor, ledgerLine);
            }
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
