using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Sim
{
    /// <summary>
    /// The deterministic tick orchestrator. One call = one simulated hour, in
    /// the fixed order from docs/GDD.md §4:
    ///
    ///   0. apply queued commands          5. power dispatch (θ_power)
    ///   1. day-boundary state (drought)   6. deliver to contracts by priority
    ///   2. climate sample                 7. nuisance channels
    ///   3. contract demand                8. GNI memory + escalation
    ///   4. cooling pass (θ_cool)          9. money, settlement, permits
    ///
    /// The cooling/power feedback is resolved in a single pass, not iterated
    /// (docs/open-questions.md Q6): surplus cooling in a throttled hour is
    /// simply unused. Presentation layers never call anything but Tick() and
    /// Enqueue() — they read SimState/TickReport and must not write either.
    /// </summary>
    public sealed partial class Simulation
    {
        public readonly Balance B;
        public readonly ClimateModel Climate;
        public readonly SimState State;

        private readonly List<SimCommand> _scheduled;
        private int _nextScheduled;
        private readonly List<SimCommand> _live = new List<SimCommand>();

        public Simulation(Balance balance, Scenario scenario)
        {
            B = balance;
            Climate = new ClimateModel(balance, scenario.Seed);
            _scheduled = new List<SimCommand>(scenario.Commands);

            var s = new SimState();
            s.Rng = new SimRandom(scenario.Seed);
            s.NodesInstalled = scenario.Nodes;
            s.FreecoolKwTh = scenario.FreecoolKwTh;
            s.EvapKwTh = scenario.EvapKwTh;
            s.ChillerKwTh = scenario.ChillerKwTh;
            s.GridTier = scenario.GridTier;
            s.SolarKwp = scenario.SolarKwp;
            s.WindKw = scenario.WindKw;
            s.BatteryKwhCap = scenario.BatteryKwh;
            s.BatteryKw = scenario.BatteryKw;
            s.BatterySocKwh = scenario.BatteryKwh * 0.5;
            s.DieselKw = scenario.DieselKw;
            s.Diesel = scenario.Diesel;
            s.LocalFte = scenario.LocalFte;
            s.VisualPoints = scenario.VisualPoints;
            s.CashEur = scenario.CashEur;
            s.SpotEnabled = scenario.SpotEnabled;
            s.SpotDepthKw = balance.SpotDepthMeanKw;
            s.Reputation = scenario.ReputationStart >= 0 ? scenario.ReputationStart : balance.ReputationStart;
            s.Gni = balance.GniStart;
            // The town's remembered price starts at today's undisturbed resident price.
            s.PriceBaselineEurKwh = balance.GridPriceSeason.Evaluate(0)
                                  * balance.GridPriceDiurnal.Evaluate(12)
                                  * balance.RetailMarkup;
            State = s;
        }

        /// <summary>Queue a live command (debug UI / players). Applied at the next tick start.</summary>
        public void Enqueue(SimCommand cmd)
        {
            _live.Add(cmd);
        }

        public TickReport Tick()
        {
            var s = State;
            var r = new TickReport { Tick = s.Tick, TrainingProgressFrac = -1.0 };

            // 0. commands
            ApplyDueCommands(ref r);

            // 1. day boundary
            long day = SimClock.DayIndex(s.Tick);
            if (day != s.LastProcessedDay)
            {
                s.LastProcessedDay = day;
                UpdateDrought(day);
                UpdateSpotDepth();
            }

            // 2. climate
            ClimateSample c = Climate.Sample(s.Tick, s.DroughtActive);
            r.TdbC = c.TdbC; r.TwbC = c.TwbC; r.WindSpeedMs = c.WindSpeedMs;
            r.WindTowardDeg = c.WindTowardDeg; r.WindTowardTown = c.WindTowardTown;
            r.IrradianceFrac = c.IrradianceFrac; r.WindCf = c.WindCf;
            r.DroughtActive = c.DroughtActive; r.HeatwaveActive = c.HeatwaveActive;
            r.DunkelflauteActive = c.DunkelflauteActive; r.ScarcityMult = c.ScarcityMult;

            if (s.RunOver)
            {
                // The run has ended; the world keeps ticking so the CSV shows the
                // aftermath, but the site is dark.
                r.Gni = s.Gni; r.Stage = s.Stage; r.Reputation = s.Reputation; r.CashEur = s.CashEur;
                s.Tick++;
                return r;
            }

            // 3–6. physical loop
            StepLoadsAndPlant(in c, ref r);

            // 7–8. community
            StepCommunity(in c, ref r);

            // 9. money and pipelines
            StepEconomyAndPermits(ref r);

            r.CashEur = s.CashEur;
            r.Reputation = s.Reputation;
            r.ActiveContracts = CountActive();
            s.Tick++;
            return r;
        }

        private int CountActive()
        {
            int n = 0;
            for (int i = 0; i < State.Contracts.Count; i++)
                if (State.Contracts[i].Status == ContractStatus.Active) n++;
            return n;
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        private void ApplyDueCommands(ref TickReport r)
        {
            while (_nextScheduled < _scheduled.Count && _scheduled[_nextScheduled].Tick <= State.Tick)
            {
                ApplyCommand(_scheduled[_nextScheduled], ref r);
                _nextScheduled++;
            }
            if (_live.Count > 0)
            {
                for (int i = 0; i < _live.Count; i++) ApplyCommand(_live[i], ref r);
                _live.Clear();
            }
        }

        private void ApplyCommand(SimCommand cmd, ref TickReport r)
        {
            var s = State;
            switch (cmd.Kind)
            {
                case CommandKind.SignInference:
                {
                    if (s.Reputation < B.RepGateInference)
                    {
                        s.Log("contract", "Inference offer refused: Reputation " +
                            s.Reputation.ToString("0.0", CultureInfo.InvariantCulture) +
                            " below gate " + B.RepGateInference.ToString("0", CultureInfo.InvariantCulture));
                        return;
                    }
                    var ct = new Contract
                    {
                        Id = s.NextContractId++,
                        Type = ContractType.Inference,
                        StartTick = s.Tick,
                        EndTick = s.Tick + (long)(cmd.B * SimClock.TicksPerDay),
                        BaseKw = cmd.A,
                        RateEurPerKwhIt = B.RateInference,
                        SlaTarget = B.SlaInference
                    };
                    s.Contracts.Add(ct);
                    s.Log("contract", "Signed inference #" + ct.Id + ": " + cmd.A + " kW for " + cmd.B + " days");
                    break;
                }
                case CommandKind.SignTraining:
                {
                    if (s.Reputation < B.RepGateTrainingMed)
                    {
                        s.Log("contract", "Training offer refused: Reputation below gate " +
                            B.RepGateTrainingMed.ToString("0", CultureInfo.InvariantCulture));
                        return;
                    }
                    long deadline = s.Tick + (long)(cmd.B * SimClock.TicksPerDay);
                    var ct = new Contract
                    {
                        Id = s.NextContractId++,
                        Type = ContractType.Training,
                        StartTick = s.Tick,
                        EndTick = deadline,
                        BaseKw = cmd.A,
                        RateEurPerKwhIt = B.RateTraining,
                        // compute-contracts.md 2.2 writes required = block x term,
                        // but with CHECKPOINT_OVERHEAD = 3% max attainable progress
                        // is 0.97 x block x term: the doc formula is uncompletable
                        // as written (flagged in the M1 report). The frac leaves
                        // headroom for the overhead plus brief throttles.
                        RequiredKwh = cmd.A * (cmd.B * SimClock.TicksPerDay) * B.TrainingRequiredFrac,
                        LastCheckpointTick = s.Tick
                    };
                    s.Contracts.Add(ct);
                    s.Log("contract", "Signed training #" + ct.Id + ": " + cmd.A + " kW block, deadline day " +
                        SimClock.DayIndex(deadline));
                    break;
                }
                case CommandKind.SetSpot:
                    s.SpotEnabled = cmd.A >= 0.5;
                    break;
                case CommandKind.ApplyTier:
                    ApplyForTier((int)cmd.A);
                    break;
                case CommandKind.SetDieselPolicy:
                    s.Diesel = (DieselPolicy)(int)cmd.A;
                    s.Log("power", "Diesel policy set to " + s.Diesel);
                    break;
                case CommandKind.AddNodes:
                    if (s.Stage >= EscalationStage.Protest)
                    {
                        s.Log("escalation", "Delivery of " + (int)cmd.A +
                            " nodes turned away at the gate (unscheduled gate activity)");
                        return;
                    }
                    s.NodesInstalled += (int)cmd.A;
                    s.Log("contract", "Installed " + (int)cmd.A + " nodes, fleet now " + s.NodesInstalled);
                    break;
                case CommandKind.SetLocalFte:
                    s.LocalFte = cmd.A;
                    break;
                case CommandKind.SetVisualPoints:
                    s.VisualPoints = cmd.A;
                    break;

                // --- Milestones 3-5 -----------------------------------------
                case CommandKind.AddPlant:
                {
                    // DELTA, not absolute: concurrent placements from several
                    // players (or several clicks in one paused tick) must
                    // compose without anyone reading current state first.
                    var kind = (PlantKind)(int)cmd.A;
                    double d = cmd.B;
                    double v;
                    switch (kind)
                    {
                        case PlantKind.FreecoolKwTh: v = s.FreecoolKwTh = Math.Max(0.0, s.FreecoolKwTh + d); break;
                        case PlantKind.EvapKwTh: v = s.EvapKwTh = Math.Max(0.0, s.EvapKwTh + d); break;
                        case PlantKind.ChillerKwTh: v = s.ChillerKwTh = Math.Max(0.0, s.ChillerKwTh + d); break;
                        case PlantKind.SolarKwp: v = s.SolarKwp = Math.Max(0.0, s.SolarKwp + d); break;
                        case PlantKind.WindKw: v = s.WindKw = Math.Max(0.0, s.WindKw + d); break;
                        case PlantKind.BatteryKwh: v = s.BatteryKwhCap = Math.Max(0.0, s.BatteryKwhCap + d); break;
                        case PlantKind.BatteryKw: v = s.BatteryKw = Math.Max(0.0, s.BatteryKw + d); break;
                        case PlantKind.DieselKw: v = s.DieselKw = Math.Max(0.0, s.DieselKw + d); break;
                        default: v = 0; break;
                    }
                    s.Log("facility", kind + " now " + v.ToString("0", CultureInfo.InvariantCulture));
                    break;
                }
                case CommandKind.AddRouteLossKw:
                    // Delta per finished run; the total never goes negative.
                    s.RouteLossKw = Math.Max(0.0, s.RouteLossKw + cmd.A);
                    break;
                case CommandKind.DestroyNodes:
                {
                    int destroy = Math.Min((int)cmd.A, s.NodesInstalled);
                    if (destroy <= 0) break;
                    s.NodesInstalled -= destroy;
                    s.Log("facility", destroy + " nodes destroyed, fleet now " + s.NodesInstalled);
                    break;
                }
                case CommandKind.EpoTrip:
                {
                    s.OutageFrac = Math.Max(0.0, Math.Min(1.0, cmd.A));
                    s.OutageUntilTick = s.Tick + Math.Max(1L, (long)cmd.B);
                    s.Log("facility", "EPO: " + (s.OutageFrac * 100).ToString("0", CultureInfo.InvariantCulture) +
                        "% of load de-energised for " + Math.Max(1L, (long)cmd.B) + " h");
                    break;
                }
                case CommandKind.SetCoolingDerate:
                    s.CoolingDerateMult = Math.Max(0.0, Math.Min(1.0, cmd.A));
                    break;
                case CommandKind.SetWaterValve:
                    s.WaterValveFrac = Math.Max(0.0, Math.Min(1.0, cmd.A));
                    break;
                case CommandKind.SetSetpoint:
                    s.SetpointC = cmd.A;
                    break;
                case CommandKind.SetDieselManual:
                    s.DieselManualOn = cmd.A >= 0.5;
                    s.Log("power", s.DieselManualOn ? "Diesel lever: MANUAL RUN" : "Diesel lever released");
                    break;
                case CommandKind.IssueBulletin:
                {
                    bool named = cmd.A >= 0.5;
                    double effect;
                    if (s.Credibility < B.CredibilityMockeryThreshold)
                    {
                        // Below the mockery line, bulletins COST goodwill.
                        effect = B.BulletinBackfire;
                    }
                    else
                    {
                        effect = B.BulletinBaseEffect * s.Credibility;
                        if (named)
                        {
                            effect += B.NamingGniEffect * Math.Pow(B.NamingDecay, s.NamingCount);
                            s.NamingCount++;
                        }
                    }
                    s.Gni = Math.Max(0.0, Math.Min(100.0, s.Gni + effect));
                    s.Credibility = Math.Max(0.0, s.Credibility - B.CredibilityLossPerUse);
                    s.BulletinCount++;
                    s.Log("media", "Program bulletin #" + s.BulletinCount +
                        (named ? " (responsible employee named)" : "") +
                        ": GNI " + (effect >= 0 ? "+" : "") + effect.ToString("0.0", CultureInfo.InvariantCulture) +
                        ", credibility now " + s.Credibility.ToString("0.00", CultureInfo.InvariantCulture));
                    break;
                }
                case CommandKind.ReportAccident:
                    s.TotalAccidents++;
                    s.AccidentScore += B.AccidentGniPenalty;
                    s.Log("media", "Recordable incident #" + s.TotalAccidents +
                        " — the public accident statistics have been updated");
                    break;
            }
        }

        private void ApplyForTier(int target)
        {
            var s = State;
            var ci = CultureInfo.InvariantCulture;
            if (target != s.GridTier + 1 || target > 4)
            {
                s.Log("permit", "Tier application rejected: can only apply for tier " + (s.GridTier + 1));
                return;
            }
            if (s.Permit != null)
            {
                s.Log("permit", "Tier application rejected: one already in flight");
                return;
            }
            double capex = B.TierCapexEur[target];
            double gate = B.TierSentimentGate[target];
            if (s.CashEur < capex)
            {
                s.Log("permit", "Tier " + target + " application rejected: needs EUR " + capex.ToString("0", ci));
                return;
            }
            if (s.Gni < gate)
            {
                s.Log("permit", "Tier " + target + " application rejected at submission: GNI " +
                    s.Gni.ToString("0.0", ci) + " below gate " + gate.ToString("0", ci));
                return;
            }
            if (target == 4 && s.ReferendumsWon < 1)
            {
                // power.md 2 tier table: T4 is "75 + referendum" - the utility
                // will not connect 50 MW to a town that has not voted for it.
                s.Log("permit", "Tier 4 application rejected: requires a WON community consultation event");
                return;
            }
            s.CashEur -= capex;
            _capexThisTick += capex;
            s.Permit = new PendingPermit
            {
                TargetTier = target,
                RemainingLeadTicks = B.TierLeadDays[target] * SimClock.TicksPerDay,
                CapexPaidEur = capex
            };
            s.Log("permit", "Applied for grid tier " + target + " (EUR " + capex.ToString("0", ci) +
                " committed, " + B.TierLeadDays[target].ToString("0", ci) + " day lead time)");
        }

        // ------------------------------------------------------------------
        // Economy, settlement, permit clock
        // ------------------------------------------------------------------

        private double _capexThisTick;

        private void StepEconomyAndPermits(ref TickReport r)
        {
            var s = State;
            var ci = CultureInfo.InvariantCulture;

            r.CapexEur = _capexThisTick;
            _capexThisTick = 0.0;

            // Permit clock — paused during an administrative pause (injunction).
            if (s.Permit != null)
            {
                if (s.Stage != EscalationStage.Injunction && s.Stage != EscalationStage.Sabotage)
                    s.Permit.RemainingLeadTicks -= 1.0;
                if (s.Permit.RemainingLeadTicks <= 0.0)
                {
                    double gate = B.TierSentimentGate[s.Permit.TargetTier];
                    bool referendumOk = s.Permit.TargetTier != 4 || s.ReferendumsWon >= 1;
                    if (s.Gni >= gate && referendumOk)
                    {
                        s.GridTier = s.Permit.TargetTier;
                        s.Log("permit", "Grid tier " + s.GridTier + " GRANTED (GNI " +
                            s.Gni.ToString("0.0", ci) + " ≥ gate " + gate.ToString("0", ci) + ")");
                    }
                    else
                    {
                        s.Log("permit", "Grid tier " + s.Permit.TargetTier + " REFUSED at completion re-check: GNI " +
                            s.Gni.ToString("0.0", ci) + " below gate " + gate.ToString("0", ci) +
                            ". EUR " + s.Permit.CapexPaidEur.ToString("0", ci) + " is not refunded.");
                    }
                    s.Permit = null;
                }
            }

            // Monthly settlement on the last tick of each 730-hour month.
            if (s.Tick % (SimClock.TicksPerYear / 12) == (SimClock.TicksPerYear / 12) - 1)
                SettleMonth(ref r);
        }

        private void SettleMonth(ref TickReport r)
        {
            var s = State;
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < s.Contracts.Count; i++)
            {
                Contract ct = s.Contracts[i];
                if (ct.Type != ContractType.Inference || ct.Status != ContractStatus.Active) continue;
                if (ct.MonthRequestedKwh <= 0.0) continue;

                double uptime = ct.MonthDeliveredKwh / ct.MonthRequestedKwh;
                double shortfall = ct.SlaTarget - uptime;
                if (shortfall > 0.0)
                {
                    double penalty = B.SlaPenaltyK * shortfall * ct.MonthRevenueEur;
                    double cap = B.SlaPenaltyCap * ct.MonthRevenueEur;
                    if (penalty > cap) penalty = cap;
                    s.CashEur -= penalty;
                    ct.TotalPenaltyEur += penalty;
                    r.PenaltyEur += penalty;
                    double severity = shortfall / (1.0 - ct.SlaTarget);
                    if (severity > 3.0) severity = 3.0;
                    s.Reputation -= B.ReputationLossBreach * severity;
                    if (s.Reputation < 0) s.Reputation = 0;
                    s.Log("contract", "SLA breach on inference #" + ct.Id + ": uptime " +
                        (uptime * 100.0).ToString("0.00", ci) + "% vs " +
                        (ct.SlaTarget * 100.0).ToString("0.0", ci) + "%, penalty EUR " + penalty.ToString("0", ci));
                }
                else
                {
                    s.Reputation += B.ReputationGainMonth;
                    if (s.Reputation > 100) s.Reputation = 100;
                }
                ct.MonthRequestedKwh = 0.0;
                ct.MonthDeliveredKwh = 0.0;
                ct.MonthRevenueEur = 0.0;

                if (s.Tick >= ct.EndTick)
                {
                    ct.Status = ContractStatus.Completed;
                    s.Log("contract", "Inference #" + ct.Id + " ran to term");
                }
            }
        }
    }
}
