# System: Hardware lifecycle

Nodes age, get dirty, fail on a distribution, and are replaced from a market
whose price moves with the same AI demand that generates the contracts.

**Inputs:** `T_inlet` (°C), power quality, runtime (h), dust load, market demand
index.
**Outputs:** node failures, replacement capex (EUR), RMA lead times (days),
`node_efficiency`, visible rack condition, maintenance job cards.

---

## 0. One change to an existing system

Mixed-age fleets require a per-node **`node_efficiency ∈ (0,1]`**, and that
changes what `kWh_IT` means in
[compute-contracts.md](compute-contracts.md).

```
delivered_reference_kWh = P_node(u) * node_efficiency * Δt
```

Contracts are paid per **reference** kWh_IT — the output of a current-generation
node. An older node still draws its full `GPU_NODE_P_PEAK` = 10 kW and still
produces its full 10 kW_th of heat, but delivers only `node_efficiency` of the
billable work.

All existing formulas stay valid with `node_efficiency = 1.0`. Every constant in
[balance-constants.md](../balance-constants.md) is unaffected.

**Why it has to exist:** without it, a four-year-old node is economically
identical to a new one and there is no reason to ever replace anything, which
removes this entire system. With it, an old node is a rack slot that costs full
power, full cooling, full debt service and pays 60% — and *that* is the capacity
planning problem the brief is asking for.

Logged as [open-questions.md](../open-questions.md) Q19.

| Generation | `node_efficiency` | Age when current |
|---|---|---|
| Current | 1.00 | 0–18 months |
| Previous | 0.72 | 18–36 months |
| Two back | 0.48 | 36–54 months |
| Three back | 0.31 | 54 months+ |

---

## 1. Wear

Wear is an **effective age** that runs faster than the calendar when conditions
are bad.

```
wear_rate = BASE_WEAR
          * temp_factor(T_inlet)
          * pq_factor(power_quality)
          * (1 + DUST_WEAR_K * dust_load)

temp_factor = 2 ^ ((T_inlet - T_WEAR_REF) / 10)

effective_age += wear_rate * Δt                                   [h]
```

`T_WEAR_REF` = 22 °C. **The 10 °C doubling is the standard Arrhenius rule of
thumb for electronics reliability** — real, and it is the most useful anchored
number in this document.

What it means in play:

| `T_inlet` | `temp_factor` | Four-year node lasts |
|---|---|---|
| 18 °C | 0.76 | 5.3 years |
| 22 °C | 1.00 | 4.0 years |
| 27 °C (`T_INLET_SAFE`) | 1.41 | 2.8 years |
| 32 °C | 2.00 | **2.0 years** |
| 37 °C (Run Hot territory) | 2.83 | 1.4 years |

Running warm to save cooling power does not break anything today. It halves the
life of €96M of hardware over two years, and the bill arrives as a cluster of
failures in a month nobody connects to a decision made eighteen months earlier.

This is deliberately **separate from** `THROTTLE_DAMAGE_K` in
[cooling-water.md](cooling-water.md) §2, which is the acute damage from Run Hot.
Wear is the chronic version. Both exist; they do not overlap because
`THROTTLE_DAMAGE_K` only applies above `T_INLET_SAFE` and wear applies always.

### Power quality

```
pq_factor = 1 + PQ_K * flicker_events_per_week
```

Flicker events come from: grid instability (weather, `load_ratio`), transformer
condition ([incidents.md](incidents.md) §3.8), and **the UPS maintenance bypass
switch being left on**, which removes the conditioning entirely
([workplace-accidents.md](workplace-accidents.md) §4.6).

A site running on bypass ages its hardware roughly 1.8× faster and shows no other
symptom until the next flicker drops the whole room.

---

## 2. Dust

```
dust_load  += DUST_RATE * airflow_m3h * (1 - filter_efficiency) * Δt
filter_efficiency = FILTER_NEW * (1 - FILTER_DEGRADE * filter_load)
filter_load       += airflow_m3h * ambient_particulate * Δt

ΔT_dust = DUST_THERMAL_K * dust_load                              [°C]
```

Dust does three things, compounding:

1. **Raises `T_inlet`** by insulating heatsinks — which raises `wear_rate` through
   `temp_factor`.
2. **Raises fan power**, because fans compensate — which raises `N_noise` and
   `P_cool`.
3. **Is a fire load** in trays and filters.

`ambient_particulate` rises with: agricultural activity next door (real, and
seasonal — harvest is dusty), construction on site, and the diesel generator.
**Running the generator dirties your own filters**, which is a small, correct,
entirely unremarked-upon detail.

Filter changes are a **job card** ([staff-shifts.md](staff-shifts.md) §3): 20
minutes, €180 of filters, and the single most skippable task in the building.

---

## 3. Failure distribution

A three-term hazard function. Bathtub curve.

```
h(a) = h_early * exp(-a / TAU_EARLY)          infant mortality
     + h_random                                constant
     + h_wear * (a / L_RATED)^BETA             wear-out

a = effective_age (hours),  BETA ≈ 3.0,  L_RATED = GPU_NODE_LIFE * 8760
P(fail this tick) = 1 - exp(-h(a) * Δt)
```

**This is the one place the design uses a probability distribution, and it is not
a violation of the "no purely random events" rule** — the *rate* is fully
determined by neglected variables (temperature, dust, power quality, age). The
dice only decide which node and which hour. Stated explicitly because it is the
closest thing to an exception in the whole design.

Three regimes players will feel:

- **Infant mortality**, first ~500 h: new hardware fails more. `TAU_EARLY` = 400 h.
  A group that installs 100 nodes at once gets a cluster of failures in the first
  three weeks and will believe they bought bad hardware. They did not.
- **Random**, small and flat: the background noise that makes RMA worth having.
- **Wear-out**, cubic: harmless until it is not. At `effective_age` = 0.8 ×
  `L_RATED` the wear term is 51% of its end-of-life value; at 1.0 it is the
  dominant term. **A hot-run fleet reaches this in year two.**

---

## 4. RMA and warranty

| Tier | Cost | Lead time | Notes |
|---|---|---|---|
| None (used market) | — | — | Dead is dead |
| Standard | included with new | 21 days | Ship it, wait |
| Next business day | 4% of capex/yr | 1–3 days | |
| On-site 4-hour | 8% of capex/yr | 4 hours | Needs the security cert for site access |

```
capacity_lost = failed_nodes * GPU_NODE_P_PEAK * rma_lead_days * 24   [kWh_IT]
```

**Warranty is a bet on your own failure rate**, which is a bet on how well you
run the building. A group with `T_inlet` at 19 °C should buy standard; a group
running hot should buy 4-hour and will still be short. Neither knows which they
are until year two.

Note the coupling: the 4-hour on-site tier **requires the security certification**
([certifications-audits.md](certifications-audits.md) §2), because the vendor
will not give unescorted site access to a facility without access control. A
certification that looked like a contract gate turns out to gate your spares
strategy too.

---

## 5. The market

Three sources, one price index.

```
p_gpu(t) = GPU_BASE_PRICE
         * (1 + demand_index(t)) ^ GPU_ELASTICITY
         * supply_factor(t)
```

`GPU_ELASTICITY` ≈ 1.4. `demand_index(t)` is a slow index (months) that **also
drives the contract offer stream** in
[compute-contracts.md](compute-contracts.md) — offer rate, block sizes and the
frequency of large training runs all scale with it.

**One variable drives both sides**, and that is the whole decision:

| `demand_index` | Contracts | GPU price | The trap |
|---|---|---|---|
| High | Abundant, large, well-paid | **Expensive** | Expansion costs most exactly when it is most obviously worth it |
| Low | Thin, small, spot-heavy | **Cheap** | You can afford to expand and have nothing to fill it with |

The correct play is to buy in the trough for capacity you will need in the peak,
which requires holding cash through a period of low revenue. Almost nobody will
do it the first time.

| Source | Price | Warranty | `effective_age` at delivery | Lead time |
|---|---|---|---|---|
| New | `p_gpu` | Standard | 0 | 30–90 d |
| Refurb | 0.55 × `p_gpu` | 12 months | ~1.2 y, **known** | 14 d |
| Used / auction | 0.30 × `p_gpu` | None | **Unknown — a distribution** | 3 d |

Used hardware's `effective_age` is drawn from a wide distribution and **is not
revealed until the node has run for 200 hours**. Buying used is a genuine gamble
with a genuine payoff: three-day lead time is the only way to replace capacity
inside a crisis.

Refurb is the boring correct answer and the game should never say so.

---

## 6. Visible ageing

Within the [art-bible.md](../art-bible.md) constraints — vertex colours, one
material, no textures — a rack shows its condition through:

| Signal | Driven by |
|---|---|
| Filter panel darkening (Render → Earth) | `filter_load` |
| Status LED colour (green → Amber → Alarm Red) | `effective_age / L_RATED` |
| Blanking panel gaps | Nodes pulled and not replaced |
| Cable tray sag and colour | `crossings`, run count |
| Dust haze VFX in the aisle | `dust_load` |
| Sticker/label density | Player-typed labels accumulating |

**A well-run hall and a neglected one should be distinguishable from the door**,
before any panel is opened. That is the whole visual job of this system and it
costs four colour ramps.

---

## 7. Repair versus replace

```
repair  if  repair_cost + downtime_cost < replace_cost - salvage
        and effective_age < REPAIR_AGE_THRESHOLD * L_RATED

REPAIR_AGE_THRESHOLD = 0.7
```

Past 70% of rated effective life, a repaired node fails again inside a year — the
cubic wear term guarantees it. The game does not say this; the player learns it
by repairing the same node three times.

`salvage` follows the market: dumping a fleet when `demand_index` is low recovers
almost nothing, which matters at the endgame
([endgame.md](endgame.md) §2.1).

---

## 8. Mixed-age fleets

The capacity-planning problem the brief asks for, stated concretely.

A rack of ten nodes bought in three waves:

| Wave | Nodes | Age | `node_efficiency` | Draws | Delivers |
|---|---|---|---|---|---|
| Y0 | 4 | 4.0 y | 0.31 | 40 kW | 12.4 ref-kW |
| Y2 | 3 | 2.0 y | 0.72 | 30 kW | 21.6 ref-kW |
| Y4 | 3 | 0.2 y | 1.00 | 30 kW | 30.0 ref-kW |
| **Total** | 10 | | | **100 kW** | **64 ref-kW** |

The rack is at `RACK_P_CAP_LIQUID` = 130 kW ceiling territory, consumes 100 kW of
the grid cap, produces 100 kW_th of heat that must be removed at full cost —
**and bills as 64 kW.** Its PUE is unchanged and its margin is destroyed.

Three consequences:

1. **Contract sizing gets hard.** "Can we take a 2 MW training block?" is no
   longer a question about kW installed; it is a question about reference-kW
   available, and those two numbers diverge over years.
2. **The grid cap binds sooner than it should.** Old hardware consumes the
   progression gate ([power.md](power.md) §2) without earning anything.
   Replacing old nodes *creates grid capacity*, which is the same "aha" as
   upgrading a room's cooling class.
3. **Failures cluster by wave.** Everything bought in one month wears out in one
   month, four years later, during whatever season that happens to be.

**The correct strategy is to buy in smaller, more frequent waves.** It costs more
in lead-time overhead and it is the opposite of what the training-contract cliff
in [compute-contracts.md](compute-contracts.md) §2.2 pushes you toward. The two
systems pull in opposite directions on purpose.

---

## 9. Failure modes

| Failure | Neglected variable | Surfaces as |
|---|---|---|
| Failure cluster at 3 weeks | Bought 100 nodes at once | "We bought bad hardware" |
| Failure cluster at year 2 | Ran hot to save `P_cool` | Capacity shortfall, SLA breaches |
| Fleet ages 1.8× fast | UPS bypass left engaged | Nothing, until it is everything |
| Rising `T_inlet` site-wide | Filters never changed | Fan noise at night, then throttling |
| Rack draws full power, bills 60% | Mixed-age fleet unmanaged | Margin erosion with no single cause |
| No spares when it matters | Standard warranty, hot-run fleet | 21 days of dead capacity |

---

## 10. How to abuse this against teammates

| Abuse | Legitimate cover |
|---|---|
| Nudge the cooling setpoint up 4 °C | Saves real `P_cool` and real money, visible on the PUE readout as an improvement |
| Leave the UPS on maintenance bypass | You genuinely have to bypass to service the UPS |
| Skip filter job cards | The most skippable task in the building |
| Buy the whole expansion in one wave | Simplest procurement, best unit price |
| Buy used at auction with the group's money | Three-day lead time is genuinely the only crisis answer |
| Downgrade the warranty tier at renewal | Saves 4% of capex per year, and the failures are months away |
| Sell hardware into a low `demand_index` | Somebody has to raise cash |

Every one of these makes a number go the right way today.

---

## 11. Coupling

| To | Passes | Unit |
|---|---|---|
| [compute-contracts.md](compute-contracts.md) | `node_efficiency`, node availability | fraction, count |
| [cooling-water.md](cooling-water.md) | Full `Q_IT` regardless of efficiency; dust → `T_inlet` | kW_th, °C |
| [power.md](power.md) | Full `P_IT` regardless of efficiency | kW |
| [construction-routing.md](construction-routing.md) | `T_inlet` from recirculation drives wear | °C |
| [incidents.md](incidents.md) | Dust as fire load; failure clusters as capacity incidents | — |
| [economy.md](../economy.md) | Replacement capex, RMA, market timing, salvage | EUR |
| [staff-shifts.md](staff-shifts.md) | Filter changes, RMA handling, swaps as job cards | hours |
| [nuisance.md](nuisance.md) | Dust → fan power → `N_noise` | points |
| [endgame.md](endgame.md) | Fleet value at wind-down or acquisition | EUR |
