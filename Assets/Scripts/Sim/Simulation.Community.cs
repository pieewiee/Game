using System;
using System.Globalization;

namespace Game.Sim
{
    public sealed partial class Simulation
    {
        /// <summary>
        /// Steps 7–8: the five Program indicators, their memories, the Good
        /// Neighbor Index, the escalation ladder, sabotage exposure and the
        /// referendum. docs/systems/nuisance.md and docs/systems/sentiment.md.
        /// </summary>
        private void StepCommunity(in ClimateSample c, ref TickReport r)
        {
            var s = State;
            var ci = CultureInfo.InvariantCulture;

            // ---- 7. Instantaneous channels, 0..100 -------------------------
            // Noise: fans (free-cooling + evaporative air movers), chiller
            // compressors, diesel. Log-domain so sources combine like sound.
            double fanKw = r.QFreecoolKwTh / Math.Max(B.FreecoolCop, 1e-6)
                         + r.QEvapKwTh / Math.Max(B.EvapCop.Evaluate(c.TwbC), 1e-6);
            double chillerKw = r.QChillerKwTh / Math.Max(B.ChillerCop.Evaluate(c.TdbC), 1e-6);
            double turbineCount = B.TurbineUnitKw > 0 ? s.WindKw / B.TurbineUnitKw : 0.0;
            double loudness = fanKw / B.NoiseFanRefKw
                            + B.NoiseChillerWeight * chillerKw / B.NoiseFanRefKw
                            + (r.DieselKwh > 1e-6 ? B.NoiseDieselWeight : 0.0)
                            + B.NoiseTurbineWeight * turbineCount;
            // nuisance.md §1: L = 10·log10(Σ sources), negative dB floors at 0 —
            // a site quieter than the fan reference is inaudible, not negative.
            double lSite = loudness > 1e-9 ? 10.0 * Math.Log10(loudness) : -100.0;
            double nNoise = B.NoiseScale * Math.Max(0.0, lSite);
            if (SimClock.IsNight(s.Tick)) nNoise *= B.NightNoiseMult;
            nNoise = Clamp01x100(nNoise);

            // Air: diesel exhaust, gated by the simulated wind direction.
            double windMult = c.WindTowardTown ? B.WindTowardMult : B.WindAwayMult;
            double nAir = Clamp01x100(B.AirEmissionFactor * (r.DieselKwh / 1000.0) * windMult);

            // Water: the site's draw against the town's, sharpened by drought.
            double siteM3Day = r.WaterLPerH * 24.0 / 1000.0;
            double droughtMult = c.DroughtActive ? B.DroughtNuisanceMultMax : 1.0;
            double nWater = Clamp01x100(100.0 * siteM3Day / B.TownWaterDemandM3Day * droughtMult);

            // Price: what the resident bill does versus what the town remembers.
            double rel = s.PriceBaselineEurKwh > 1e-9
                ? (r.PResidentEurKwh - s.PriceBaselineEurKwh) / s.PriceBaselineEurKwh : 0.0;
            double nPrice = Clamp01x100(B.PriceSensitivity * 100.0 * Math.Max(0.0, rel));
            // The remembered price drifts with the NATIONAL trend only
            // (nuisance.md §4: the operator is not blamed for inflation they did
            // not cause — and, critically, their congestion must never be
            // absorbed into the baseline and thereby forgiven).
            double nationalResident = B.GridPriceSeason.Evaluate(SimClock.DayOfYear(s.Tick)) * B.RetailMarkup;
            s.PriceBaselineEurKwh += (B.PriceBaselineAdaptPerDay / 24.0)
                                   * (nationalResident - s.PriceBaselineEurKwh);
            r.PBaselineEurKwh = s.PriceBaselineEurKwh;

            // Visual: scenario-set fortification in M1 (structures arrive in M3+).
            double nVisual = Clamp01x100(s.VisualPoints);

            r.NNoise = nNoise; r.NAir = nAir; r.NWater = nWater; r.NPrice = nPrice; r.NVisual = nVisual;

            // ---- Memories: EMA per channel, half-lives in days -------------
            UpdateMemory(0, nNoise, B.HalflifeNoiseDays);
            UpdateMemory(1, nAir, B.HalflifeAirDays);
            UpdateMemory(2, nWater, B.HalflifeWaterDays);
            UpdateMemory(3, nPrice, B.HalflifePriceDays);
            UpdateMemory(4, nVisual, B.HalflifeVisualDays);
            r.MNoise = s.ChannelMemory[0]; r.MAir = s.ChannelMemory[1];
            r.MWater = s.ChannelMemory[2]; r.MPrice = s.ChannelMemory[3];
            r.MVisual = s.ChannelMemory[4];

            // Credibility recovers slowly (sentiment.md §5); accidents decay
            // out of the public memory with their own half-life.
            s.Credibility = Math.Min(1.0, s.Credibility + B.CredibilityRecoveryPerWeek / (7.0 * SimClock.TicksPerDay));
            double accLambda = 1.0 - Math.Pow(0.5, 1.0 / (B.AccidentHalflifeDays * SimClock.TicksPerDay));
            s.AccidentScore *= 1.0 - accLambda;

            // ---- 8. GNI ----------------------------------------------------
            double goodwill = B.LocalHireGniPerFte * s.LocalFte - s.AccidentScore;
            double target = 100.0
                - (B.WNoise * s.ChannelMemory[0] + B.WAir * s.ChannelMemory[1]
                 + B.WWater * s.ChannelMemory[2] + B.WPrice * s.ChannelMemory[3]
                 + B.WVisual * s.ChannelMemory[4])
                + goodwill;
            if (target > 100.0) target = 100.0;
            if (target < 0.0) target = 0.0;
            s.Gni += B.GniAdjustRate * (target - s.Gni);
            r.GniTarget = target; r.Gni = s.Gni;
            r.Credibility = s.Credibility;
            r.AccidentScore = s.AccidentScore;
            r.RouteLossKw = s.RouteLossKw;
            r.OutageFrac = s.OutageUntilTick > s.Tick ? s.OutageFrac : 0.0;
            r.CoolingDerateMult = s.CoolingDerateMult;

            // ---- Escalation ladder with hysteresis -------------------------
            EscalationStage before = s.Stage;
            s.Stage = NextStage(s.Stage, s.Gni);
            if (s.Stage != before)
                s.Log("escalation", "Community stage: " + before + " -> " + s.Stage +
                    " (GNI " + s.Gni.ToString("0.0", ci) + ")");
            r.Stage = s.Stage;

            // Petition signatures accumulate below the petition line and persist.
            if (s.Gni < B.BandPetition + 15.0 && s.Stage >= EscalationStage.Petition)
            {
                if (s.PetitionSignatures <= 0.0) s.GniAtPetitionTrigger = s.Gni;
                s.PetitionSignatures += B.PetitionRatePerTick * Math.Max(0.0, (B.BandPetition + 15.0) - s.Gni);
                if (!s.ReferendumHeld && s.ReferendumVoteTick < 0
                    && s.PetitionSignatures >= B.PetitionThreshold)
                {
                    s.ReferendumVoteTick = s.Tick + (long)(B.ReferendumDelayDays * SimClock.TicksPerDay);
                    s.GniAtPetitionTrigger = s.Gni;
                    s.Log("referendum", "Petition reached " +
                        s.PetitionSignatures.ToString("0", ci) +
                        " signatures — community consultation event scheduled for day " +
                        SimClock.DayIndex(s.ReferendumVoteTick));
                }
            }
            r.PetitionSignatures = s.PetitionSignatures;

            // Sabotage: not random. 72 cumulative hours in the Sabotage band
            // earns a fibre cut; the neglected variable is the GNI itself.
            if (s.Stage == EscalationStage.Sabotage)
            {
                s.SabotageExposureTicks += 1.0;
                if (s.SabotageExposureTicks >= B.SabotageTriggerHours && s.FibreCutUntilTick <= s.Tick)
                {
                    s.FibreCutUntilTick = s.Tick + (long)B.FibreCutDurationH;
                    s.SabotageExposureTicks = 0.0;
                    s.Log("escalation", "Unauthorised third-party interference: fibre cut, no delivery for " +
                        B.FibreCutDurationH.ToString("0", ci) + " hours");
                }
            }

            // Referendum resolution.
            if (s.ReferendumVoteTick >= 0 && s.Tick >= s.ReferendumVoteTick)
            {
                s.ReferendumVoteTick = -1;
                s.ReferendumHeld = true;
                double keepScore = s.Gni + B.ReferendumDeltaWeight * (s.Gni - s.GniAtPetitionTrigger);
                if (keepScore >= B.ReferendumKeepThreshold)
                {
                    s.PetitionSignatures = 0.0;
                    s.ReferendumsWon++;      // power.md §2: T4 requires a WON referendum
                    s.ReferendumHeld = false; // a future petition can trigger another
                    s.Log("referendum", "Community consultation event: the site SURVIVES (keep score " +
                        keepScore.ToString("0.0", ci) + ")");
                }
                else
                {
                    s.RunOver = true;
                    s.RunOverReason = "Referendum lost (keep score " + keepScore.ToString("0.0", ci) + ")";
                    for (int i = 0; i < s.Contracts.Count; i++)
                        if (s.Contracts[i].Status == ContractStatus.Active)
                            s.Contracts[i].Status = ContractStatus.Terminated;
                    s.Log("referendum", "Community consultation event: operation REFUSED. The run is over.");
                }
            }
        }

        private void UpdateMemory(int channel, double instant, double halflifeDays)
        {
            double lambda = 1.0 - Math.Pow(0.5, 1.0 / (halflifeDays * SimClock.TicksPerDay));
            State.ChannelMemory[channel] += lambda * (instant - State.ChannelMemory[channel]);
        }

        internal EscalationStage NextStage(EscalationStage current, double gni)
        {
            double h = B.EscalationHysteresis;
            // Entering a worse stage happens at the band edge; leaving it
            // requires clearing the edge by the hysteresis margin.
            double up = current >= EscalationStage.Complaints ? h : 0.0;
            if (gni < B.BandInjunction - EnterBias(current, EscalationStage.Sabotage, h)) return EscalationStage.Sabotage;
            if (gni < B.BandProtest - EnterBias(current, EscalationStage.Injunction, h)) return EscalationStage.Injunction;
            if (gni < B.BandPetition - EnterBias(current, EscalationStage.Protest, h)) return EscalationStage.Protest;
            if (gni < B.BandComplaints - EnterBias(current, EscalationStage.Petition, h)) return EscalationStage.Petition;
            if (gni < B.BandContent - EnterBias(current, EscalationStage.Complaints, h)) return EscalationStage.Complaints;
            // Leaving Complaints for Content requires the extra margin.
            if (current >= EscalationStage.Complaints && gni < B.BandContent + up) return EscalationStage.Complaints;
            return EscalationStage.Content;
        }

        private static double EnterBias(EscalationStage current, EscalationStage candidate, double h)
        {
            // Already at (or beyond) the candidate stage: keep it until the
            // meter clears the band edge plus hysteresis — i.e. the *exit*
            // threshold is band + h, the *entry* threshold is the band itself.
            return current >= candidate ? -h : 0.0;
        }

        private static double Clamp01x100(double v)
        {
            if (v < 0.0) return 0.0;
            if (v > 100.0) return 100.0;
            return v;
        }

        /// <summary>Day-boundary drought state machine, from the deterministic precip series.</summary>
        private void UpdateDrought(long day)
        {
            var s = State;
            bool wet = Climate.IsWetDay(day);
            if (wet) { s.WetStreakDays++; s.DryStreakDays = 0; }
            else { s.DryStreakDays++; s.WetStreakDays = 0; }

            int month = SimClock.Month(s.Tick);
            bool summerHalf = month >= 5 && month <= 9;
            if (!s.DroughtActive && summerHalf && s.DryStreakDays >= (int)B.DroughtDryDays)
            {
                s.DroughtActive = true;
                s.Log("water", "Drought declared: water restricted to " +
                    (B.DroughtAllowanceFrac * 100).ToString("0", CultureInfo.InvariantCulture) +
                    "% of town demand");
            }
            else if (s.DroughtActive && (s.WetStreakDays >= (int)B.DroughtRecoverDays || !summerHalf))
            {
                s.DroughtActive = false;
                s.Log("water", "Drought lifted");
            }
        }
    }
}
