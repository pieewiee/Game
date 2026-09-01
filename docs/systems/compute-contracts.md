# System: Compute & Contracts

The revenue side. Turns rack space, power and cooling into EUR, and turns failure
to supply into Reputation loss.

**Inputs:** available HE, cooling throttle factor θ, available power, Reputation.
**Outputs:** `P_IT` (kW), `Q_IT` (kW_th), delivered kWh_IT per contract, revenue,
SLA state, Reputation delta.

---

## 1. Physical layer

### 1.1 Nodes

Two node types only. Deliberately — the four competitors
([GDD.md](../GDD.md) §2) own component shopping, and we are not competing there.

| | GPU node | CPU node |
|---|---|---|
| Space | `GPU_NODE_HE` 4 HE | `CPU_NODE_HE` 1 HE |
| Peak | `GPU_NODE_P_PEAK` 10.0 kW | `CPU_NODE_P_PEAK` 0.6 kW |
| Idle | `GPU_NODE_P_IDLE` 1.6 kW | `CPU_NODE_P_IDLE` 0.12 kW |
| Capex | `GPU_NODE_CAPEX` €240,000 | `CPU_NODE_CAPEX` €9,000 |

```
P_node(u) = P_idle + (P_peak - P_idle) * u        [kW],  u ∈ [0,1]
Q_node(u) = P_node(u) * HEAT_FRACTION             [kW_th],  HEAT_FRACTION = 1.0
```

Linear; real servers are mildly convex. See
[open-questions.md](../open-questions.md) Q7.

**An idle GPU node still draws 16% of peak.** A group that builds capacity for a
training run that has not started is paying 1.6 kW per node for nothing. This is
what makes spot contracts worth taking despite `RATE_SPOT` being a third of
inference.

### 1.2 Racks and rooms

A rack holds `RACK_HE` = 42 HE. Racks live in **rooms**; a room has a cooling
plant assignment and a per-rack power ceiling set by its cooling class:

| Room class | `RACK_P_CAP` | GPU nodes per rack |
|---|---|---|
| Air | 30 kW | 3 (wasting 30 HE) |
| Rear-door heat exchanger | 60 kW | 6 |
| Direct liquid | 130 kW | 10 (40 of 42 HE) |

**Space is rarely binding; rack power density is.** Upgrading a room's cooling
class *creates space out of nothing* by letting existing racks fill up. That is
the first real "aha" in the game and it is why HE is in the resource model
without being the interesting resource.

**Co-op hook:** every rack has an individual power switch and every node has its
own ([coop-griefing.md](coop-griefing.md) §3). There is no "power off all racks".
Forty switches take forty seconds and are visible for all of them.

---

## 2. Contracts

| Field | Unit | Meaning |
|---|---|---|
| `archetype` | — | inference / training / spot / grey |
| `demand(t)` | kW_IT | Requested IT load at tick `t` |
| `duration` | h | Length, or deadline for training |
| `rate` | EUR/kWh_IT | Per kWh **delivered** |
| `sla_target` | fraction | Required delivered/requested over the term |
| `penalty` | EUR | §2.5 |
| `rep_gate` | points | Minimum Reputation to be offered |

Rates are per kWh of **IT load delivered**, not per kWh drawn. Cooling overhead
is entirely the operator's problem. This is the most important economic decision
in the design: every point of PUE comes straight out of margin, and it is how
real colocation and cloud contracts work.

**Anyone can sign any offered contract at the contract terminal, and nobody can
un-sign it.** See [coop-griefing.md](coop-griefing.md) §6.1.

### 2.1 Inference hosting — the floor

```
demand(t)  = base_kW * (0.85 + 0.30 * diurnal(t))
diurnal(t) = 0.5 * (1 - cos(2π * (hour_of_day - 4) / 24))
```

Flat with a ±15% daily ripple. Duration 3–12 months. `RATE_INFERENCE` = €1.35,
`SLA_INFERENCE` = 99.5%.

**Risk shape: slow bleed.** Missing the SLA never ends a run; it costs a
percentage of the month and a few Reputation points. Inference is the load you
take so the lights stay on, and it is boring on purpose — it is the baseline that
makes the other three feel like decisions.

Because it is always on, inference sets the site's minimum cooling and power
requirement. A group that fills the building with inference has no headroom for a
training run and will never be offered the interesting contracts.

### 2.2 Training runs — the reason you overbuild

```
demand(t)       = block_kW                     constant, whole term
required_energy = block_kW * term_hours        [kWh_IT]
```

Deliver `required_energy` before a hard deadline. `RATE_TRAINING` = €2.20, paid
as a **lump sum on completion**. `SLA_TRAINING` = 98% — loose, because the
deadline does the work.

**Checkpointing.**

```
progress   accumulates kWh_IT delivered
checkpoint written every CHECKPOINT_INTERVAL_DEFAULT hours,
           costing CHECKPOINT_OVERHEAD = 3% of throughput
on interruption:  progress = checkpoint
```

`CHECKPOINT_INTERVAL_DEFAULT` is **1 hour**, shortened from the 6 hours a real
run would use. This is a co-op concession, not a simulation choice: with hourly
checkpoints a malicious rack switch during a training run costs an hour, not a
week. See [coop-griefing.md](coop-griefing.md) §6.1 and
[risks.md](../risks.md) §4. The group can lengthen the interval per contract to
recover the 3% overhead, and doing so is a bet on their colleagues.

**Payment:**

```
if progress >= required_energy by deadline:  required_energy * RATE_TRAINING
else:                                        required_energy * RATE_TRAINING * 0.10
```

A 90% loss on a missed deadline. This cliff is the reason a rational group builds
power and cooling headroom they will not use in an average month — and that
overbuild is exactly what the town objects to. **The overbuild is not a mistake;
it is the correct play, and it is what makes you the antagonist.**

For feel: a medium run is 2,000 kW for 21 days = 1,008,000 kWh_IT ≈ **€2.2M**,
more than a year of the starting site's total revenue.

### 2.3 Spot / idle compute — the sponge

```
demand(t) = min(idle_capacity_kW, spot_market_depth(t))
```

`RATE_SPOT` = €0.45, no SLA, cancellable by either side any tick with no penalty.

Spot answers the idle-power problem: `GPU_NODE_P_IDLE` is burning anyway, so any
rate above marginal cooling+power cost is profit. At PUE 1.3 and €0.18/kWh,
marginal cost is ~€0.23/kWh_IT against €0.45. Thin, positive, and it teaches
marginal thinking — see [economy.md](../economy.md) §2.

`spot_market_depth(t)` is a slow random walk, so spot is not always available.
Otherwise idle capacity would not exist and the planning layer would trivialise.

### 2.4 Grey-market clients — the shortcut

Flat load, `RATE_GREY` = €2.70 (2× inference), no SLA, no questions. Never
explained in the fiction beyond routing metadata nobody is asked to log.

Accumulating regulatory attention, thresholds, audits, fines and licence review
are specified in [open-questions.md](../open-questions.md) Q3 — where I recommend
**cutting the attention meter and keeping grey contracts as a straight Reputation
trade.** Grey clients are the only revenue lever in the game that does not route
through heat, water, noise or price, and in a co-op design that already carries
GNI, Reputation and per-player Standing, a fourth meter is one too many.

### 2.5 SLA and penalties

```
uptime    = delivered_kWh_IT / requested_kWh_IT
shortfall = max(0, sla_target - uptime)
penalty   = min(SLA_PENALTY_K * shortfall * monthly_payout,
                SLA_PENALTY_CAP * monthly_payout)          [EUR]
severity  = clamp(shortfall / (1 - sla_target), 0, 3)
R        -= REPUTATION_LOSS_BREACH * severity
```

At `SLA_INFERENCE` = 99.5%, the entire monthly downtime allowance is **3.65
hours.** One bad afternoon in July spends the year's budget.

Reputation recovers at `REPUTATION_GAIN_MONTH` = 0.5 per contract-month —
**32 contract-months to undo one severity-2 breach.** Easy to lose, slow to
rebuild.

`SLA_PENALTY_CAP` exists because the uncapped formula produced penalties larger
than the contract's value at a 10 pp shortfall
([economy.md](../economy.md) §5). Flagged in
[open-questions.md](../open-questions.md) Q10.

### 2.6 Reputation gating

| Reputation | Unlocks |
|---|---|
| < 40 | Spot and grey only |
| ≥ 40 | Small inference, 100–400 kW blocks |
| ≥ `REP_GATE_TRAINING_MED` 60 | Training up to 2 MW |
| ≥ `REP_GATE_TRAINING_LARGE` 75 | Training up to 8 MW, multi-year anchor inference |

`REPUTATION_START` = 50, so the group begins able to take small inference but not
training. **The first training contract must be earned by being boring for a
while**, which is the correct emotional setup for then gambling the site on it.

---

## 3. Allocation

```
1. requested   = Σ contract.demand(t)                    [kW_IT]
2. P_available = min(power_cap, cooling_cap_as_kW_IT)    [kW_IT]
3. θ           = min(1, P_available / requested)
4. allocate in priority order, applying θ
5. record delivered_i per contract for SLA accounting
```

Default priority: training (deadline risk) → inference (SLA risk) → grey → spot.

**Spot last means spot absorbs every shortfall first**, which is why it exists. A
group that has filled the site with inference has no shock absorber and every
shortfall lands on an SLA.

Priority is set on a physical board in the office, so **anyone can reorder it**,
and reordering it during a heatwave to protect a training deadline at the expense
of an inference SLA is both expert play and a griefing vector depending on who
does it and whether they said so.

---

## 4. Coupling

| To | Via |
|---|---|
| **Cooling** | `Q_IT` is the entire input; cooling returns θ, capping delivery |
| **Power** | `P_IT` + `P_cool` is the site draw; the grid cap hard-bounds allocation |
| **Nuisance** | Indirect only — contracts make heat, heat makes every nuisance |
| **GNI** | **None directly** |
| **Co-op** | Rack switches, node switches, the contract terminal, the priority board, Run Hot |
| **Economy** | Revenue, penalties, node capex, maintenance |

**The absence of a direct Contracts → GNI edge is a design commitment.** The town
does not care what you compute. It cares about air, water, noise, money and the
view. You cannot fix the Good Neighbor Index by choosing nicer customers — only
by changing what the site does to the people outside it.
