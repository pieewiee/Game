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

        /// <summary>The frame on which a modal window consumed Escape to close
        /// itself — the player rig must not ALSO toggle the cursor that frame
        /// (script execution order would otherwise decide the outcome).</summary>
        public static int EscConsumedFrame = -1;

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

        /// <summary>True while a modal IMGUI window is open (bulletin editor,
        /// network panel). Those windows own Escape and the mouse; the player
        /// rig neither moves nor re-locks the cursor until they close.</summary>
        public static bool UiWantsCursor
        {
            get
            {
                if (BulletinEditor.Instance != null && BulletinEditor.Instance.IsOpen) return true;
                if (ContractEditor.Instance != null && ContractEditor.Instance.IsOpen) return true;
                return Net != null && Net.PanelOpen;
            }
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
            gameObject.AddComponent<ContractEditor>();
            gameObject.AddComponent<Hud>();
            Net = gameObject.AddComponent<NetSession>();
            gameObject.AddComponent<SaveSystem>();

            HazardInstaller.Install(Site, Facility);

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
