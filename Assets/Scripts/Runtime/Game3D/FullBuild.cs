using System;
using System.Collections.Generic;
using Game.Runtime.Media;
using Game.Sim;
using UnityEngine;

namespace Game.Runtime.World
{
    /// <summary>
    /// "Vollausbau": the debug action that shows and, on the host, applies a
    /// full build-out of the site (DebugPresets). Two halves:
    ///   preview — FacilityController ghosts, local only, any machine;
    ///   apply   — the plan's delta commands through the SAME recorded, netted
    ///             path as the console's ADD_NODES, plus one ledger line.
    /// The plan is computed from the authoritative inventory (RemoteReport on
    /// a client, the live state plus what is already queued otherwise), so
    /// what the button says matches what lands. Nothing here owns state the
    /// sim does not own.
    /// </summary>
    public static class FullBuildActions
    {
        // Double-click guard: commands land at the NEXT tick start, so within
        // one paused tick the inventory does not move and a second click
        // would queue the whole plan twice. Reset when the history moves on.
        private static long _queuedAtTick = -1;
        private static long _queuedVersion = -1;

        /// <summary>The pause menu's debug button fires PauseMenu.FullBuildRequested
        /// and leaves the wiring to the owner of the build-out. Unsubscribe
        /// first: with domain reload off the static survives a play-mode
        /// restart and would otherwise stack a second handler.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WirePauseMenu()
        {
            PauseMenu.FullBuildRequested -= ApplyReal;
            PauseMenu.FullBuildRequested += ApplyReal;
        }

        public static bool PreviewOn
        {
            get { return GameBootstrap.Facility != null && GameBootstrap.Facility.PreviewFullBuild; }
            set { if (GameBootstrap.Facility != null) GameBootstrap.Facility.PreviewFullBuild = value; }
        }

        /// <summary>Live slot counts and unit sizes (never Default once the
        /// site exists — the plan must agree with the visual mirror).</summary>
        public static SiteLimits LiveLimits()
        {
            return FacilityController.LimitsFor(GameBootstrap.Site);
        }

        /// <summary>The authoritative inventory: replicated report on a
        /// client (whose local sim is paused and stale), live state plus the
        /// deltas already queued for this tick otherwise. A client before its
        /// first snapshot reports an empty site — the button is disabled
        /// there anyway (host only).</summary>
        public static SiteInventory LiveInventory()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return SiteInventory.From(net.RemoteReport);
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return default;
            SiteInventory inv = SiteInventory.From(driver.Sim.State);
            AddPendingThisTick(ref inv);
            return inv;
        }

        /// <summary>Deltas already queued for the current tick land BEFORE
        /// anything sent now, so the plan counts them as owned — otherwise a
        /// same-tick ADD_NODES (console, forklift, a client's delivery) plus
        /// a build-out overshoots the rack cap. Host/solo only: every path
        /// there records through SimDriver.EnqueueRecorded with its tick, in
        /// tick order (a load re-enqueues the save-tick tail the same way);
        /// the sim's own queue is private. Mirrors Simulation.ApplyCommand:
        /// clamps at zero per command, and a queued node delivery is left
        /// out while the gate is closed because the sim will refuse it,
        /// exactly like the plan's own.</summary>
        private static void AddPendingThisTick(ref SiteInventory inv)
        {
            var driver = GameBootstrap.Driver;
            var log = driver.CommandLog;
            long tick = driver.Sim.State.Tick;
            int first = log.Count;
            while (first > 0 && log[first - 1].tick == tick) first--;
            if (first == log.Count) return;
            bool gateClosed = driver.Sim.State.Stage >= EscalationStage.Protest;
            for (int i = first; i < log.Count; i++)
            {
                var rc = log[i];
                switch ((CommandKind)rc.kind)
                {
                    case CommandKind.AddNodes:
                        if (!gateClosed) inv.Nodes += (int)rc.a;
                        break;
                    case CommandKind.DestroyNodes:
                        inv.Nodes -= Math.Min((int)rc.a, inv.Nodes);
                        break;
                    case CommandKind.AddPlant:
                        switch ((PlantKind)(int)rc.a)
                        {
                            case PlantKind.FreecoolKwTh: inv.FreecoolKwTh = Math.Max(0.0, inv.FreecoolKwTh + rc.b); break;
                            case PlantKind.EvapKwTh: inv.EvapKwTh = Math.Max(0.0, inv.EvapKwTh + rc.b); break;
                            case PlantKind.ChillerKwTh: inv.ChillerKwTh = Math.Max(0.0, inv.ChillerKwTh + rc.b); break;
                            case PlantKind.SolarKwp: inv.SolarKwp = Math.Max(0.0, inv.SolarKwp + rc.b); break;
                            case PlantKind.BatteryKwh: inv.BatteryKwhCap = Math.Max(0.0, inv.BatteryKwhCap + rc.b); break;
                            case PlantKind.DieselKw: inv.DieselKw = Math.Max(0.0, inv.DieselKw + rc.b); break;
                        }
                        break;
                }
            }
        }

        public static FullBuildPlan CurrentPlan()
        {
            return DebugPresets.PlanFullBuild(LiveInventory(), LiveLimits());
        }

        /// <summary>Whether ApplyReal would send anything right now: the
        /// console button's enabled state, and what the pause menu's button
        /// should disable on.</summary>
        public static bool CanApply()
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return false;
            return CanApply(CurrentPlan(), GateClosed());
        }

        private static bool CanApply(in FullBuildPlan plan, bool gateClosed)
        {
            return IsHost() && !plan.IsEmpty && !AlreadyQueuedThisTick()
                   && !(gateClosed && plan.IsEmptyExceptNodes);
        }

        private static bool IsHost()
        {
            var net = GameBootstrap.Net;
            return net == null || !net.IsClient;
        }

        private static bool GateClosed()
        {
            var net = GameBootstrap.Net;
            if (net != null && net.IsClient) return net.RemoteReport.Stage >= EscalationStage.Protest;
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return false;
            return driver.Sim.State.Stage >= EscalationStage.Protest;
        }

        private static bool AlreadyQueuedThisTick()
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null) return false;
            if (driver.HistoryVersion != _queuedVersion) { _queuedAtTick = -1; return false; }
            return driver.Sim.State.Tick == _queuedAtTick;
        }

        private static void ArmGuard()
        {
            var driver = GameBootstrap.Driver;
            _queuedAtTick = driver.Sim.State.Tick;
            _queuedVersion = driver.HistoryVersion;
        }

        /// <summary>The ledger's usual actor (GameBootstrap.LocalActorName:
        /// the session name when hosting, the rig's name in solo), "host"
        /// when that comes back empty.</summary>
        private static string ActorName()
        {
            string name = GameBootstrap.LocalActorName();
            return string.IsNullOrEmpty(name) ? "host" : name;
        }

        /// <summary>One row for the debug console's actions block: the preview
        /// toggle, the apply button, and what the plan would add.</summary>
        public static void DrawGui()
        {
            var driver = GameBootstrap.Driver;
            bool haveSim = driver != null && driver.Sim != null;
            FullBuildPlan plan = haveSim ? CurrentPlan() : default;
            bool gateClosed = haveSim && GateClosed();
            bool onlyNodes = !plan.IsEmpty && plan.IsEmptyExceptNodes;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vollausbau", GUILayout.Width(80));
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && GameBootstrap.Facility != null;
            PreviewOn = GUILayout.Toggle(PreviewOn, "preview", GUILayout.Width(80));
            bool canApply = haveSim && CanApply(plan, gateClosed);
            GUI.enabled = prevEnabled && canApply;
            if (GUILayout.Button("Apply full build-out", GUILayout.Width(150))) ApplyReal();
            GUI.enabled = prevEnabled;

            string caption;
            if (!haveSim) caption = "no simulation";
            else if (!IsHost()) caption = "host only — " + plan.Describe();
            else if (gateClosed && onlyNodes)
                caption = "only the node delivery is outstanding — gate closed (unscheduled gate activity)";
            else if (gateClosed && plan.AddNodes > 0)
                caption = plan.Describe() + " (nodes will be turned away: gate closed)";
            else caption = plan.Describe();
            GUILayout.Label(caption);
            GUILayout.EndHorizontal();
        }

        /// <summary>Host only. Sends every delta the plan holds through the
        /// recorded command path, then one ledger line. Under Protest the gate
        /// refuses racks (Simulation.AddNodes), so the node delivery is
        /// dropped here with a visible reason instead of a silent log line.
        /// BatteryKw is added with the pack but is not visible through
        /// TickReport — a client's plan can only be trusted for the rest.
        /// Scenario-scheduled commands (AT n ADD_NODES …) due this tick are
        /// invisible from here and compose on top; a debug preset lives with
        /// that.</summary>
        public static void ApplyReal()
        {
            var driver = GameBootstrap.Driver;
            if (driver == null || driver.Sim == null || !IsHost()) return;
            if (AlreadyQueuedThisTick()) return;

            SiteInventory inv = LiveInventory();
            SiteLimits lim = LiveLimits();
            FullBuildPlan plan = DebugPresets.PlanFullBuild(inv, lim);
            List<SimCommand> cmds = DebugPresets.FullBuild(inv, lim);

            bool blocked = GateClosed();
            int droppedNodes = 0;
            if (blocked)
            {
                for (int i = cmds.Count - 1; i >= 0; i--)
                {
                    if (cmds[i].Kind != CommandKind.AddNodes) continue;
                    droppedNodes += (int)cmds[i].A;
                    cmds.RemoveAt(i);
                }
                plan.AddNodes = 0;
            }
            if (cmds.Count == 0)
            {
                if (droppedNodes > 0)
                    NewsFeed.Post("GATE: delivery of " + droppedNodes + " nodes not scheduled — unscheduled gate activity.");
                // The pause menu's button is not gated on CanApply: one news
                // line per tick, not one per click.
                ArmGuard();
                return;
            }

            // Same path as the console's ADD_NODES: recorded for the save's
            // replay, netted on a client (which cannot reach this point).
            foreach (var cmd in cmds) GameBootstrap.SendCommand(cmd, null);
            string line = "full build-out applied — " + plan.Describe();
            if (droppedNodes > 0)
                line += "; " + droppedNodes + " nodes NOT delivered: gate closed (unscheduled gate activity)";
            GameBootstrap.AddLedger(ActorName(), line);

            ArmGuard();
            // Commands apply at tick start; paused, the site would not change
            // until someone presses play.
            if (driver.Paused) driver.Step(1);
        }
    }
}
