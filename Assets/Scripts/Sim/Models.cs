using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public enum ContractType { Inference, Training, Spot }

    public enum ContractStatus { Active, Completed, FailedDeadline, Terminated }

    public enum DieselPolicy
    {
        Never,        // the generator never auto-starts
        ProtectSla,   // covers shortfall that would throttle contracted load
        Always        // M1: identical to ProtectSla; reserved for later policy nuance
    }

    /// <summary>Escalation ladder stages. docs/systems/sentiment.md §3.</summary>
    public enum EscalationStage { Content, Complaints, Petition, Protest, Injunction, Sabotage }

    /// <summary>
    /// One customer contract. Rates are EUR per kWh_IT of *billable* load:
    /// a node at utilisation u bills u × P_peak while drawing
    /// P_idle + (P_peak − P_idle) × u. Idle overhead is the operator's problem.
    /// </summary>
    public sealed class Contract
    {
        public int Id;
        public ContractType Type;
        public ContractStatus Status = ContractStatus.Active;
        public long StartTick;
        public long EndTick;                 // inference: term end. training: the deadline.
        public double BaseKw;                // inference base / training block size
        public double RateEurPerKwhIt;
        public double SlaTarget;             // inference only; 0 = no SLA

        // Inference: current settlement-month accumulators.
        public double MonthRequestedKwh;
        public double MonthDeliveredKwh;
        public double MonthRevenueEur;

        // Training: progress and checkpoints. docs/systems/compute-contracts.md §2.2.
        public double RequiredKwh;
        public double ProgressKwh;
        public double CheckpointKwh;
        public long LastCheckpointTick;

        public double TotalRevenueEur;
        public double TotalPenaltyEur;
    }

    /// <summary>A grid-tier application in flight. docs/systems/power.md §2.</summary>
    public sealed class PendingPermit
    {
        public int TargetTier;
        public double RemainingLeadTicks;   // paused while an injunction is active
        public double CapexPaidEur;
    }

    /// <summary>One line of the public record: what happened, when, and category.</summary>
    public struct SimEvent
    {
        public long Tick;
        public string Category;   // "permit", "contract", "escalation", "power", "water", "referendum"
        public string Message;

        public SimEvent(long tick, string category, string message)
        {
            Tick = tick; Category = category; Message = message;
        }
    }
}
