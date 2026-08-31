using System;
using System.Globalization;

namespace Game.Sim
{
    public sealed partial class Simulation
    {
        /// <summary>
        /// Steps 3–6 of the tick: contract demand → cooling (θ_cool) → power
        /// dispatch (θ_power) → delivery by priority. Single pass; the energy
        /// ledger in the report closes exactly by construction.
        /// </summary>
        private void StepLoadsAndPlant(in ClimateSample c, ref TickReport r)
        {
            var s = State;

            // ---- 3. Demand (billable kW_IT) --------------------------------
            double capacityKw = s.MaxBillableKw;
            double trainingReq = 0.0, inferenceReq = 0.0;
            for (int i = 0; i < s.Contracts.Count; i++)
            {
                Contract ct = s.Contracts[i];
                if (ct.Status != ContractStatus.Active) continue;
                if (ct.Type == ContractType.Training && s.Tick < ct.EndTick)
                    trainingReq += ct.BaseKw;
                else if (ct.Type == ContractType.Inference && s.Tick < ct.EndTick)
                    inferenceReq += InferenceDemandKw(ct);
            }
            // Spot soaks up idle capacity only (docs/systems/compute-contracts.md §2.3).
            double idle = capacityKw - trainingReq - inferenceReq;
            if (idle < 0) idle = 0;
            double spotReq = s.SpotEnabled ? Math.Min(idle, s.SpotDepthKw) : 0.0;

            // Demand can exceed the fleet; clip by priority before anything else.
            double clippedTraining = Math.Min(trainingReq, capacityKw);
            double clippedInference = Math.Min(inferenceReq, capacityKw - clippedTraining);
            double requested = clippedTraining + clippedInference + spotReq;
            r.RequestedBillableKw = trainingReq + inferenceReq + spotReq;

            bool fibreCut = s.FibreCutUntilTick > s.Tick;
            if (fibreCut) requested = 0.0;

            // ---- 4. Cooling pass A: capacity against requested heat --------
            double peak = B.GpuNodePeakKw, idleKw = B.GpuNodeIdleKw;
            int n = s.NodesInstalled;
            double uReq = capacityKw > 0 ? requested / capacityKw : 0.0;
            double pItReq = n * (idleKw + (peak - idleKw) * uReq);
            double qReq = pItReq * B.HeatFraction;

            double freecoolCap = c.TdbC <= B.FreecoolThresholdC ? s.FreecoolKwTh : 0.0;
            double evapCop = B.EvapCop.Evaluate(c.TwbC);
            double chillerCop = B.ChillerCop.Evaluate(c.TdbC);
            double wue = B.EvapWue.Evaluate(c.TwbC);   // L per kWh_IT-equivalent of heat

            // Water-limited evaporative capacity during a drought.
            double evapCap = s.EvapKwTh;
            if (c.DroughtActive && wue > 0.0)
            {
                double allowanceLh = B.TownWaterDemandM3Day * 1000.0 * B.DroughtAllowanceFrac / 24.0;
                double evapByWater = allowanceLh / wue; // kW_th removable within the allowance
                if (evapByWater < evapCap) evapCap = evapByWater;
            }

            double qCap = freecoolCap + evapCap + s.ChillerKwTh;
            double thetaCool = qReq > 1e-9 ? Math.Min(1.0, qCap / qReq) : 1.0;
            // θ scales *utilisation*; idle draw and its heat remain. If even idle
            // heat exceeds capacity, utilisation floors at 0 and hardware damage
            // would begin — M1 logs it as unremoved heat (damage arrives with M5).
            r.ThetaCool = thetaCool;

            // ---- 5. Power: supply ceiling → θ_power ------------------------
            double u1 = uReq * thetaCool;
            double pIt1 = n * (idleKw + (peak - idleKw) * u1);
            double q1 = pIt1 * B.HeatFraction;
            double pCool1 = CoolingPower(q1, freecoolCap, evapCap, evapCop, chillerCop, out _, out _, out _, out _);
            double pAux = B.AuxBaseKw + B.AuxFracOfIt * pIt1;

            double solarPotential = s.SolarKwp * c.IrradianceFrac;
            double windPotential = s.WindKw * c.WindCf;
            double gridCap = B.TierCapKw[s.GridTier];
            double battDischargeMax = Math.Min(s.BatteryKw, s.BatterySocKwh * Math.Sqrt(B.BatteryRte));
            double dieselMax = s.Diesel == DieselPolicy.Never ? 0.0 : s.DieselKw;

            double demand1 = pIt1 + pCool1 + pAux;
            double supplyMax = solarPotential + windPotential + gridCap + battDischargeMax + dieselMax;

            double thetaPower = 1.0;
            double uFinal = u1;
            if (demand1 > supplyMax + 1e-9)
            {
                // Scale utilisation so demand fits. Fixed load = idle draw + aux
                // base; cooling is approximated as scaling with u (documented
                // single-pass simplification, docs/open-questions.md Q6).
                double fixedLoad = n * idleKw + B.AuxBaseKw;
                double variable = demand1 - fixedLoad;
                double room = supplyMax - fixedLoad;
                thetaPower = variable > 1e-9 ? Math.Max(0.0, Math.Min(1.0, room / variable)) : 0.0;
                uFinal = u1 * thetaPower;
            }
            r.ThetaPower = thetaPower;
            r.Utilisation = uFinal;

            // ---- Final physical quantities --------------------------------
            double pIt = n * (idleKw + (peak - idleKw) * uFinal);
            double qIt = pIt * B.HeatFraction;
            double qFree, qEvap, qChill, qUnremoved;
            double pCool = CoolingPower(qIt, freecoolCap, evapCap, evapCop, chillerCop,
                                        out qFree, out qEvap, out qChill, out qUnremoved);
            pAux = B.AuxBaseKw + B.AuxFracOfIt * pIt;
            double demand = pIt + pCool + pAux;

            r.PItKw = pIt; r.PCoolKw = pCool; r.PAuxKw = pAux;
            r.QItKwTh = qIt; r.QFreecoolKwTh = qFree; r.QEvapKwTh = qEvap;
            r.QChillerKwTh = qChill; r.QUnremovedKwTh = qUnremoved;

            // Water drawn by the evaporative share (L/h). WUE is per kWh of
            // IT-equivalent heat; HEAT_FRACTION converts kW_th back to kWh_IT.
            double waterLh = B.HeatFraction > 0 ? qEvap / B.HeatFraction * wue : 0.0;
            r.WaterLPerH = waterLh;

            // ---- 6. Dispatch ----------------------------------------------
            Dispatch(in c, demand, solarPotential, windPotential, gridCap, dieselMax, ref r);

            // ---- Deliver to contracts by priority --------------------------
            double deliverable = fibreCut ? 0.0 : capacityKw * uFinal;
            double dTraining = Math.Min(clippedTraining, deliverable);
            double dInference = Math.Min(clippedInference, deliverable - dTraining);
            double dSpot = Math.Min(spotReq, deliverable - dTraining - dInference);
            r.DeliveredBillableKw = dTraining + dInference + dSpot;

            DeliverContracts(trainingReq, inferenceReq, dTraining, dInference, dSpot, ref r);

            // Water cost (punitive tariff while restricted).
            double waterM3 = waterLh / 1000.0;
            double waterTariff = B.WaterPriceEurM3 * (c.DroughtActive ? B.DroughtWaterTariffMult : 1.0);
            double waterCost = waterM3 * waterTariff;
            s.CashEur -= waterCost;
            r.WaterCostEur = waterCost;
        }

        private double InferenceDemandKw(Contract ct)
        {
            // Flat base ±amp diurnal ripple peaking mid-afternoon
            // (docs/systems/compute-contracts.md §2.1).
            int hour = SimClock.HourOfDay(State.Tick);
            double diurnal = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * (hour - 4) / 24.0));
            return ct.BaseKw * (1.0 - B.InferenceDiurnalAmp + 2.0 * B.InferenceDiurnalAmp * diurnal);
        }

        /// <summary>
        /// Assigns heat to plants cheapest-first (free → evaporative → chiller)
        /// and returns total cooling electrical power. Heat beyond all three
        /// plant ratings is unremoved: with θ applied to utilisation, idle-draw
        /// heat does not scale away, so deep throttle can still leave residual
        /// heat — which is correct, and is what hardware damage (M5) will read.
        /// </summary>
        private double CoolingPower(double qIt, double freecoolCap, double evapCap,
            double evapCop, double chillerCop,
            out double qFree, out double qEvap, out double qChill, out double qUnremoved)
        {
            double remaining = qIt;
            qFree = Math.Min(remaining, freecoolCap); remaining -= qFree;
            qEvap = Math.Min(remaining, evapCap); remaining -= qEvap;
            qChill = Math.Min(remaining, State.ChillerKwTh); remaining -= qChill;
            qUnremoved = remaining < 1e-9 ? 0.0 : remaining;

            return qFree / Math.Max(B.FreecoolCop, 1e-6)
                 + qEvap / Math.Max(evapCop, 1e-6)
                 + qChill / Math.Max(chillerCop, 1e-6);
        }

        private void Dispatch(in ClimateSample c, double demandKw,
            double solarPotential, double windPotential, double gridCap, double dieselMax,
            ref TickReport r)
        {
            var s = State;
            r.SolarPotentialKwh = solarPotential;
            r.WindPotentialKwh = windPotential;

            // Price before congestion — battery decisions must not depend on a
            // number that depends on the battery's own decision.
            int doy = SimClock.DayOfYear(s.Tick);
            int hour = SimClock.HourOfDay(s.Tick);
            double pSeason = B.GridPriceSeason.Evaluate(doy);
            double pDiurnalMult = B.GridPriceDiurnal.Evaluate(hour);
            double pPre = pSeason * pDiurnalMult * c.ScarcityMult;
            r.PBaseEurKwh = pSeason;

            double remaining = demandKw;

            // 1. Renewables, solar first.
            double solarUsed = Math.Min(remaining, solarPotential); remaining -= solarUsed;
            double windUsed = Math.Min(remaining, windPotential); remaining -= windUsed;
            double surplus = (solarPotential - solarUsed) + (windPotential - windUsed);

            // 2. Battery. Surplus renewables charge it; otherwise arbitrage:
            //    discharge into expensive hours, charge in cheap ones.
            double eta = Math.Sqrt(B.BatteryRte);
            double charge = 0.0, discharge = 0.0;
            double headroom = s.BatteryKwhCap - s.BatterySocKwh;
            if (surplus > 0 && headroom > 0)
            {
                charge = Math.Min(Math.Min(surplus, s.BatteryKw), headroom / eta);
                surplus -= charge;
            }
            double avgPrice = pSeason; // seasonal price ≈ the day's mean, cheap proxy
            bool priceyHour = pPre >= avgPrice * B.BatteryArbitrageSpread;
            bool cheapHour = pPre <= avgPrice / B.BatteryArbitrageSpread;
            if (remaining > 0)
            {
                double maxDischarge = Math.Min(s.BatteryKw - discharge, s.BatterySocKwh * eta);
                double gridShort = Math.Max(0.0, remaining - gridCap);
                // Always discharge to avoid exceeding the grid cap; discharge for
                // price only in expensive hours.
                double want = priceyHour ? remaining : gridShort;
                discharge = Math.Max(0.0, Math.Min(want, maxDischarge));
                remaining -= discharge;
            }

            // 3. Grid.
            double grid = Math.Min(remaining, gridCap); remaining -= grid;
            // Cheap-hour battery charging from the grid, within the cap.
            if (cheapHour && charge < s.BatteryKw && headroom - charge * eta > 0)
            {
                double extra = Math.Min(Math.Min(s.BatteryKw - charge, gridCap - grid),
                                        (headroom - charge * eta) / eta);
                if (extra > 0) { charge += extra; grid += extra; }
            }

            // 4. Diesel — covers what the grid cannot, if policy allows.
            double diesel = 0.0;
            if (remaining > 0 && dieselMax > 0)
            {
                diesel = Math.Min(remaining, dieselMax);
                remaining -= diesel;
            }

            double shed = remaining > 1e-9 ? remaining : 0.0;

            // Battery bookkeeping: equivalent full cycles wear capacity.
            s.BatterySocKwh += charge * eta - discharge / eta;
            if (s.BatterySocKwh < 0) s.BatterySocKwh = 0;
            double effCap = EffectiveBatteryCap();
            if (s.BatterySocKwh > effCap) s.BatterySocKwh = effCap;
            if (s.BatteryKwhCap > 0)
                s.BatteryCycles += (charge + discharge) / (2.0 * s.BatteryKwhCap);

            // Congestion is computed on the actual grid draw.
            double loadRatio = B.TierRegionCapKw[s.GridTier] > 0
                ? grid / B.TierRegionCapKw[s.GridTier] : 0.0;
            double congestion = 1.0 + B.CongestionK * Math.Pow(loadRatio, B.CongestionGamma);
            double pGrid = pPre * congestion;
            double pResident = pGrid * B.RetailMarkup;

            double energyCost = grid * pGrid + diesel * (B.DieselCostPerKwh + B.DieselOmPerKwh);
            s.CashEur -= energyCost;

            r.SolarUsedKwh = solarUsed; r.WindUsedKwh = windUsed;
            r.CurtailedKwh = surplus;
            r.GridImportKwh = grid; r.DieselKwh = diesel;
            r.BatteryChargeKwh = charge; r.BatteryDischargeKwh = discharge;
            r.BatterySocKwh = s.BatterySocKwh;
            r.LoadShedKwh = shed;
            r.LoadRatio = loadRatio; r.CongestionMult = congestion;
            r.PGridEurKwh = pGrid; r.PResidentEurKwh = pResident;
            r.EnergyCostEur = energyCost;
        }

        private double EffectiveBatteryCap()
        {
            // Linear fade to 80 % at BATTERY_CYCLE_LIFE cycles, continuing beyond.
            double fade = 0.2 * (State.BatteryCycles / B.BatteryCycleLife);
            double frac = 1.0 - fade;
            if (frac < 0.5) frac = 0.5;
            return State.BatteryKwhCap * frac;
        }

        private void DeliverContracts(double trainingReq, double inferenceReq,
            double dTraining, double dInference, double dSpot, ref TickReport r)
        {
            var s = State;
            var ci = CultureInfo.InvariantCulture;
            double trainingShare = trainingReq > 1e-9 ? dTraining / trainingReq : 0.0;
            double inferenceShare = inferenceReq > 1e-9 ? dInference / inferenceReq : 0.0;

            double mostUrgentFrac = -1.0;
            long mostUrgentDeadline = long.MaxValue;

            for (int i = 0; i < s.Contracts.Count; i++)
            {
                Contract ct = s.Contracts[i];
                if (ct.Status != ContractStatus.Active) continue;

                if (ct.Type == ContractType.Inference && s.Tick < ct.EndTick)
                {
                    double req = InferenceDemandKw(ct);
                    double got = req * inferenceShare;
                    ct.MonthRequestedKwh += req;
                    ct.MonthDeliveredKwh += got;
                    double pay = got * ct.RateEurPerKwhIt;
                    ct.MonthRevenueEur += pay;
                    ct.TotalRevenueEur += pay;
                    s.CashEur += pay;
                    r.RevenueEur += pay;
                }
                else if (ct.Type == ContractType.Training)
                {
                    if (s.Tick >= ct.EndTick)
                    {
                        // Deadline passed unfinished.
                        double basePayout = ct.RequiredKwh * ct.RateEurPerKwhIt;
                        double pay = basePayout * B.TrainingFailPayoutFrac;
                        s.CashEur += pay;
                        ct.TotalRevenueEur += pay;
                        r.RevenueEur += pay;
                        ct.Status = ContractStatus.FailedDeadline;
                        s.Reputation = Math.Max(0.0, s.Reputation - B.ReputationLossBreach * 2.0);
                        s.Log("contract", "Training #" + ct.Id + " MISSED its deadline at " +
                            (100.0 * ct.ProgressKwh / ct.RequiredKwh).ToString("0.0", ci) +
                            "% — paid the " + (B.TrainingFailPayoutFrac * 100).ToString("0", ci) + "% kill fee");
                        continue;
                    }

                    double got = ct.BaseKw * trainingShare;
                    // Interruption: a hard drop (< 50 % of block) loses progress
                    // back to the last checkpoint. Partial throttle just slows.
                    if (got < ct.BaseKw * 0.5 && ct.ProgressKwh > ct.CheckpointKwh)
                    {
                        double lost = ct.ProgressKwh - ct.CheckpointKwh;
                        ct.ProgressKwh = ct.CheckpointKwh;
                        s.Log("contract", "Training #" + ct.Id + " interrupted — " +
                            lost.ToString("0", ci) + " kWh of progress lost since last checkpoint");
                    }
                    else
                    {
                        ct.ProgressKwh += got * (1.0 - B.CheckpointOverhead);
                    }
                    // Checkpoint on the interval boundary.
                    if (s.Tick - ct.LastCheckpointTick >= (long)B.CheckpointIntervalH)
                    {
                        ct.CheckpointKwh = ct.ProgressKwh;
                        ct.LastCheckpointTick = s.Tick;
                    }
                    if (ct.ProgressKwh >= ct.RequiredKwh)
                    {
                        double pay = ct.RequiredKwh * ct.RateEurPerKwhIt;
                        s.CashEur += pay;
                        ct.TotalRevenueEur += pay;
                        r.RevenueEur += pay;
                        ct.Status = ContractStatus.Completed;
                        s.Reputation = Math.Min(100.0, s.Reputation + B.ReputationGainMonth * 2.0);
                        s.Log("contract", "Training #" + ct.Id + " COMPLETED — EUR " +
                            pay.ToString("0", ci) + " lump sum");
                        continue;
                    }
                    if (ct.EndTick > 0 && ct.EndTick < mostUrgentDeadline)
                    {
                        mostUrgentDeadline = ct.EndTick;
                        mostUrgentFrac = ct.RequiredKwh > 0 ? ct.ProgressKwh / ct.RequiredKwh : 0.0;
                    }
                }
            }

            // Spot revenue: whatever idle capacity the market absorbed.
            if (dSpot > 0)
            {
                double pay = dSpot * B.RateSpot;
                s.CashEur += pay;
                r.RevenueEur += pay;
            }
            r.TrainingProgressFrac = mostUrgentFrac;
        }

        private void UpdateSpotDepth()
        {
            var s = State;
            // Daily random walk around the mean; the ONLY RNG consumer in the
            // physical loop, drawn once per day in a fixed place in the order.
            double step = (s.Rng.NextDouble() * 2.0 - 1.0) * B.SpotDepthVolatility * B.SpotDepthMeanKw;
            s.SpotDepthKw += step + 0.05 * (B.SpotDepthMeanKw - s.SpotDepthKw);
            double lo = 0.2 * B.SpotDepthMeanKw, hi = 2.0 * B.SpotDepthMeanKw;
            if (s.SpotDepthKw < lo) s.SpotDepthKw = lo;
            if (s.SpotDepthKw > hi) s.SpotDepthKw = hi;
        }
    }
}
