# System: Power

Supplies kW to everything. Owns the main progression gate (the grid permit) and
one of the five Program indicators (price).

**Inputs:** `P_IT`, `P_cool`, climate, GNI (for permits), cash.
**Outputs:** delivered kW, hourly energy cost, `p_resident`, diesel runtime,
noise contribution.

---

## 1. Demand

```
P_demand = P_IT + P_cool + P_aux          [kW]
P_aux    = 0.02 * P_IT + 15               [kW]
```

If demand exceeds supply, Compute is throttled. **Power never browns out
silently** — it always resolves into a visible delivery shortfall.

---

## 2. Grid connection — the progression gate

A **hard cap** in kW. It cannot be exceeded for even one tick.

| Tier | `CAP` | `CAPEX` | `LEAD_TIME` | `SENTIMENT_GATE` | `REGION_CAPACITY` |
|---|---|---|---|---|---|
| T0 | 250 kW | — | — | — | 12,000 kW |
| T1 | 1,000 kW | €450,000 | 90 d | 45 | 12,000 kW |
| T2 | 5,000 kW | €2,400,000 | 210 d | 55 | 20,000 kW |
| T3 | 20,000 kW | €11,000,000 | 420 d | 65 | 45,000 kW |
| T4 | 50,000 kW | €38,000,000 | 730 d | 75 + referendum | 80,000 kW |

Upgrading needs **all three** of capital, lead time, and a permit.

```
apply:      cash >= CAPEX  AND  GNI >= SENTIMENT_GATE
             -> capex committed immediately, non-refundable
in flight:  LEAD_TIME days
complete:   re-check GNI >= SENTIMENT_GATE
             if GNI has fallen below the gate: REFUSED, capex lost,
             application may be resubmitted
```

**The re-check at completion is the cruellest and most important rule in the
game.** You pay up front, wait three months to two years, and can lose the whole
amount because of a bad summer that happened while you waited. It turns GNI from
a meter you manage into a commitment you must *hold*.

It also creates the central tension: the fastest way to earn the money for the
upgrade is to run hard, and running hard is what costs you the permit.

**Co-op consequence:** applying is a physical action at the procurement desk and
sits above `TWO_KEY_THRESHOLD`, so it needs two players
([coop-griefing.md](coop-griefing.md) §2). The re-check, however, is nobody's
action — it simply happens, and the group finds out together.

`REGION_CAPACITY` grows with each tier but **deliberately slower than the
player's draw**:

| Tier | Draw at full cap | `REGION_CAPACITY` | `load_ratio` |
|---|---|---|---|
| T1 | 1,000 kW | 12,000 | 0.08 |
| T2 | 5,000 kW | 20,000 | 0.25 |
| T3 | 20,000 kW | 45,000 | 0.44 |
| T4 | 50,000 kW | 80,000 | 0.63 |

This is what makes the price indicator a **campaign-long worsening pressure**
rather than a problem solved once. Every upgrade relieves the capacity constraint
and tightens the price constraint.

---

## 3. Grid price

```
p_base(month, hour) = seasonal(month) * diurnal(hour)              [EUR/kWh]
load_ratio          = P_grid_draw / REGION_CAPACITY(tier)
congestion          = 1 + CONGESTION_K * load_ratio ^ CONGESTION_GAMMA
p_grid              = p_base * congestion * scarcity(t)
p_resident          = p_grid * RETAIL_MARKUP
```

`seasonal` runs `GRID_PRICE_SUMMER` €0.14 (July) to `GRID_PRICE_WINTER` €0.24
(January). `diurnal` is ×0.75 night trough to ×1.25 evening peak. `scarcity` is
1.0 normally, up to `SCARCITY_EVENT_MULT` = 2.5 in heatwaves and Dunkelflaute.

Congestion at full draw per tier:

| Tier | `load_ratio` | `congestion` | Effect on the town's bill |
|---|---|---|---|
| T1 | 0.08 | ×1.008 | invisible |
| T2 | 0.25 | ×1.075 | +7.5% — noticed, not blamed |
| T3 | 0.44 | ×1.23 | +23% — a local political issue |
| T4 | 0.63 | ×1.48 | **+48%** — the referendum's actual argument |

**You pay `p_grid` and the town pays `p_resident`, and they are the same number.**
There is no "we'll absorb the cost" option anywhere in the design.

**On-site generation reduces `P_grid_draw`, reduces `load_ratio`, reduces the
town's bill.** Solar and wind are therefore not merely cheaper power; they are the
only Program measure that pays for itself. That should be discoverable and never
stated.

---

## 4. Supply sources

### 4.1 Solar

```
P_solar(t) = kWp * irradiance_factor(month, hour, cloud)     [kW]
```

`SOLAR_YIELD_ANNUAL` 1,020 kWh/kWp/yr, `SOLAR_SUMMER_WINTER_RATIO` 5.5, zero at
night, `SOLAR_CAPEX` €750/kWp.

Solar peaks in July, at the exact hours evaporative efficiency has collapsed and
cooling demand is maximum. **It looks like the answer to summer and is about a
third of it**, because the cooling load it must offset grew faster than it did.
Also occupies land: +2 `N_visual` per MW.

### 4.2 Wind

```
P_wind(t) = kW * cf(month, wind_speed)                       [kW]
```

`WIND_CF_ANNUAL` 0.24, `WIND_CF_JAN` 0.35, `WIND_CF_JUL` 0.16, `WIND_CAPEX`
€1,600/kW.

**Anti-correlated with solar across the year** — together they fit the seasonal
load far better than either alone, and discovering that is one of the game's
better lessons.

Wind carries two nuisance costs solar does not: permanent `N_visual` (+7 each,
visible from everywhere) and `N_noise` scaling with wind speed. **There is no free
mitigation in this design.**

### 4.3 Battery

```
charge:    E += P_charge * BATTERY_RTE^0.5 * Δt
discharge: E -= P_discharge / BATTERY_RTE^0.5 * Δt
cycles    += |ΔE| / (2 * capacity_kWh)
capacity   = nominal * (1 - 0.2 * cycles / BATTERY_CYCLE_LIFE)
```

`BATTERY_CAPEX` €280/kWh, `BATTERY_RTE` 0.88, `BATTERY_CYCLE_LIFE` 6,000 cycles.

Three uses, learned in this order:

1. **Arbitrage** — night trough to evening peak. Gross spread ~40%, net ~23%.
   Slow money.
2. **Peak shaving** — stay under the hard grid cap during bursts without
   upgrading. Dramatically cheaper than a tier upgrade for 10–20% headroom.
3. **Diesel avoidance** — ride out an outage or price spike without starting a
   generator. **The only way to hold an SLA during an outage without visibly
   poisoning the town.**

Degradation makes aggressive cycling a real cost: twice-daily cycling reaches 80%
capacity in ~8 in-game years.

**Gag hook:** night charging can push `load_ratio` high enough to trigger
congestion pricing *at night*, when the town has never seen a spike. Unusual
enough to be noticed and attributed correctly. Your most sensible efficiency
measure produces your most legible grievance.

### 4.4 Diesel

```
fuel_L = P_diesel * DIESEL_FUEL_RATE * Δt                    [L]
cost   = P_diesel * (DIESEL_COST_PER_KWH + DIESEL_OM) * Δt   [EUR]
```

`DIESEL_CAPEX` €180/kW, ~€0.44/kWh all-in — **2.3–3× grid**. Instant, uncapped,
and the only source that can exceed the grid cap, which makes it the only way to
save a training deadline during an outage or an administrative pause.

```
N_air   += diesel_kWh * emission_factor * wind_mult
N_noise += diesel_noise_points (before NIGHT_NOISE_MULT)
```

`wind_mult` is `WIND_TOWARD_MULT` 3.0 or `WIND_AWAY_MULT` 0.3. `HALFLIFE_AIR` is
90 days — a week of diesel in August still depresses GNI when the T3 permit is
re-checked in November.

**Co-op:** the generator is started by holding a physical lever for
`DIESEL_START_HOLD` = 3 seconds. Anyone can. Nobody is asked to confirm. Stopping
it does not stop the plume, and nothing stops the ninety days.

**Diesel is the short-term/long-term trap in its purest form.** It always solves
this hour's problem and always costs more than the problem was worth.

---

## 5. Dispatch

```
1. Solar and wind      marginal cost 0, always first
2. Battery discharge   if p_grid high, or grid-capped
3. Grid                up to CAP(tier)
4. Diesel              only if 3 is exhausted or unavailable
5. Throttle Compute    if still short
```

Surplus renewables charge the battery, then curtail. **Export to the grid is not
modelled** — [open-questions.md](../open-questions.md) Q11.

Players configure *policy*, not individual dispatch: battery reserve floor,
diesel auto-start rule (never / protect SLA / protect training deadline /
always), arbitrage price threshold. **The diesel auto-start rule is the most
consequential setting in the game** and it sits on a panel in the switchgear room
that looks like plumbing, because that is where that decision gets made in real
life — and because anyone can change it without telling anyone.

---

## 6. Coupling

| To | Via |
|---|---|
| **Compute** | Grid cap bounds allocatable IT load |
| **Cooling** | `P_cool` is a demand term; chillers roughly double site draw vs evaporative, moving `load_ratio` and everyone's price |
| **Nuisance — price** | `p_resident` |
| **Nuisance — air** | Diesel runtime × wind direction |
| **Nuisance — noise** | Diesel, chiller compressors, turbines |
| **Nuisance — visual** | Solar area, turbine count, diesel stack |
| **GNI** | Via those four indicators, and the permit gate in the other direction |
| **Climate** | Irradiance, wind speed, temperature, scarcity events |
| **Co-op** | Diesel lever, main breaker, dispatch policy panel, two-key procurement |

Power touches four of the five indicators. **If the project must be descoped,
power is the last system that can be simplified.**
