using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// Players do not die; they become incidents (workplace-accidents.md §1).
    /// Death drops everything carried, respawns the rig at the visitor car park
    /// as a fresh temp worker, and adds one line to the public accident
    /// statistics — which feed the Good Neighbor Index through the sim's
    /// decaying accident score. The punishment is not the walk back; it is the
    /// press release about it.
    /// </summary>
    public static class RespawnSystem
    {
        private static SiteRefs _site;

        public static void Init(SiteRefs site) { _site = site; }

        public static void OnPlayerDied(PlayerRig rig, string cause)
        {
            GameBootstrap.SendCommand(new SimCommand { Kind = CommandKind.ReportAccident, A = 1 },
                rig.PlayerName + " " + cause);
            NewsFeed.Post(rig.PlayerName + " " + cause + ". The public accident statistics have " +
                "been updated; the Program reaffirms that safety culture remains a cornerstone.");

            if (_site != null) rig.FinishRespawn(_site.CarParkSpawn);
        }
    }
}
