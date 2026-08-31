# System: Cooling & Water

The central trade-off. Removes `Q_IT` kW_th by spending either water or power —
and both anger the town, in different ways, on different timescales.

Water is documented here because it has one producer (evaporative plant) and one
consumer (the water indicator). See [GDD.md](../GDD.md) §12.

**Inputs:** `Q_IT`, climate (`T_db`, `T_wb`), drought state, external heat demand.
**Outputs:** `P_cool`, water draw, throttle factor θ, hardware damage, heat-reuse
revenue, noise contribution.

---

## 1. The four plants

```
P_cool = Q_removed / COP(climate)          [kW]
```

A plant at COP 30 costs almost nothing. A plant at COP 2.5 costs 40% of the IT
load on top of the IT load. **That factor-of-twelve swing across the year, driven
entirely by outdoor conditions, is why seasons matter.**

### 1.1 Evaporative

```
COP   = EVAP_COP(T_wb)         40 @12°C · 15 @20°C · 8 @25°C · 6 @30°C
water = EVAP_WUE(T_wb) * E_IT  1.4 · 1.8 · 2.2 · 2.6  [L/kWh_IT]
capex = EVAP_CAPEX             350 EUR/kW_th
```

Cheap to buy, cheap to run, and it drinks. **Both curves move the wrong way
together**: as wet-bulb rises, efficiency falls *and* water per kWh rises. In a
July heatwave the plant uses more power and much more water to remove the same
heat.

It also has a hard physical ceiling: it cannot cool below the wet-bulb
temperature. Above roughly `T_wb` 24 °C it cannot hold a safe inlet temperature
at full load no matter how much water it is given, and throttling begins.
**Money cannot fix this**, which makes it a genuine constraint rather than an
expense.

### 1.2 Chillers / closed loop

```
COP   = CHILLER_COP(T_db)      5.0 @5°C · 3.5 @20°C · 2.5 @35°C
water = 0
capex = CHILLER_CAPEX          600 EUR/kW_th
```

At 35 °C, COP 2.5 means `P_cool = 0.4 × Q_IT` — the ~40% overhead in the brief.
On a 5 MW site that is **2 MW of extra draw**, moving `load_ratio` and therefore
the town's electricity bill.

Chillers are the "responsible" choice and **the game must never say so.** They
trade an acute, visible, local grievance (water) for a diffuse, delayed, regional
one (price). Both are real. You are choosing who to harm, not whether to.

Chiller compressors also run continuously, contributing to `N_noise` in a way
evaporative fans do not.

### 1.3 Free cooling

```
available when T_db <= FREECOOL_T_THRESHOLD = 15 °C
COP   = FREECOOL_COP = 30
water = 0
capex = FREECOOL_CAPEX = 250 EUR/kW_th
```

Nearly free, no water, minimal noise — and unavailable for about four months.
In central European conditions `T_db ≤ 15 °C` holds for roughly 60% of annual
hours, almost none of them in July.

**Free cooling is a capacity trap.** It is so cheap that a group commissioning
the site in October concludes cooling is solved and sizes everything else
accordingly. The bill arrives in June. This is the most important seasonal lesson
in the game and it should be allowed to happen without warning.

Free cooling belongs *alongside* another plant, not instead of one.

### 1.4 Heat reuse

```
Q_export   <= min(Q_available, external_demand(month))       [kW_th]
P_heatpump  = Q_export / HEATPUMP_COP                        [kW]
revenue     = Q_export * HEAT_PRICE                          [EUR/h]
```

Heat reuse removes heat *and* earns money *and* is **the only Program measure
that actively raises GNI rather than merely reducing a nuisance.** It is also,
honestly modelled, barely profitable:

At 3 MW_th exported and `HEAT_PRICE` €0.08/kWh_th, revenue is €240/h against
heat-pump power of ~1 MW × €0.20 = €200/h. **Net €40/h against €4M of capex.
Payback is measured in centuries.** Heat reuse is not an investment; it is a GNI
purchase with a small rebate, and the design should be honest about that rather
than fudging the numbers to make it "work".

| | District heating | Public pool |
|---|---|---|
| Winter demand | Full `Q_design` | ~300 kW_th |
| Summer demand | ~0 | ~300 kW_th |
| GNI weight | Moderate | **High** — visible, beloved |
| Capex | `HEATREUSE_CAPEX` + `HEATREUSE_CONNECTION_CAPEX` | `POOL_CONNECTION_CAPEX` €180,000 |

**The pool is a hostage.** Once connected, withdrawing the heat costs several
times the GNI that connecting it ever earned. In co-op this is devastating: one
player connects it in spring, another disconnects it in February to free
heat-pump power for a training deadline, and the town's memory does not care
which of them did what. Real precedent: Exmouth, Devon, 2023.

---

## 2. Capacity, throttling and damage

```
Q_cap = Σ (COP_i * P_cool_available_i), bounded by plant kW_th rating
θ     = clamp(Q_cap / Q_IT, 0, 1)
```

Three regimes:

**θ = 1 — normal.**

**θ < 1 — throttling.** Delivered compute falls below contracted; the SLA clock
burns. This is the *safe* failure: expensive, reversible, no lasting damage. The
group should end up here often.

**Run Hot.** A physical switch in the plant room, operable by anyone. Throttling
is suppressed, load is delivered in full, and excess heat raises inlet
temperature:

```
T_inlet  = T_supply + ΔT * (Q_IT - Q_cap) / Q_IT
damage/h = THROTTLE_DAMAGE_K * max(0, T_inlet - T_INLET_SAFE)²   per node
```

`T_INLET_SAFE` = 27 °C (ASHRAE A1). At damage 1.0 a node fails permanently and
costs full `GPU_NODE_CAPEX` = €240,000.

**The quadratic term matters**: 3 °C over is survivable for days, 10 °C over
destroys hardware in hours. Run Hot is a real option for the last six hours of a
training deadline and a catastrophe as standing policy. The switch must be easy
to find and its consequences delayed enough to be a genuine decision — and in
co-op, easy for someone else to find too.

**Ordering: throttle → damage → SLA breach.** All three visible and
distinguishable, because diagnosing which one you are in is the core skill.

---

## 3. Water

```
V_water(t) = EVAP_WUE(T_wb) * E_IT_evap(t)      [L/h]
```

Shown as **m³/day against `TOWN_WATER_DEMAND` = 500 m³/day**, because the
comparison is the point:

| Site IT load, all evaporative | Water | vs town |
|---|---|---|
| 250 kW (T0) | 11 m³/day | 2% |
| 1 MW (T1) | 43 m³/day | 9% |
| 5 MW (T2) | 216 m³/day | **43%** |
| 20 MW (T3) | 864 m³/day | **173%** |

At T3 the site drinks nearly twice what four thousand people do. **These numbers
are real** — they fall out of a published WUE of 1.8 L/kWh and a published German
household consumption of 127 L/person/day. No exaggeration is applied. The satire
writes itself and the design should get out of its way.

**Drought** restricts supply to a fraction of normal. The group then chooses:
punitive tariff (money), switch to chillers (price indicator — and only if
chillers exist, since capex has a lead time you do not have during a drought), or
throttle (SLA). A group that built evaporative-only to save capex has no third
option.

`WATER_PRICE` = €2.50/m³ means the T3 site spends €2,160/day on water against
~€100,000/day of revenue. **Water is free in EUR and ruinous in GNI.** That
asymmetry is the design.

**Co-op:** the water valves and the per-room cooling mode selectors are physical
and unguarded. Switching a room to evaporative during a drought spends the
allowance in hours ([coop-griefing.md](coop-griefing.md) §6.2).

---

## 4. Seasonal shape

5 MW IT, all evaporative:

| | Jan | Apr | Jul | Oct |
|---|---|---|---|---|
| `T_db` / `T_wb` | 2 / 1 °C | 12 / 9 °C | 26 / 20 °C | 11 / 9 °C |
| Free cooling | yes | mostly | **no** | mostly |
| Evaporative COP | 40 | 40 | 15 | 40 |
| `P_cool` | 125 kW | 125 kW | **333 kW** | 125 kW |
| Water | 190 m³/d | 190 m³/d | **216 m³/d** | 190 m³/d |
| PUE | 1.05 | 1.05 | 1.10 | 1.05 |

With chillers instead:

| | Jan | Jul |
|---|---|---|
| Chiller COP | 5.0 | 2.5 |
| `P_cool` | 1,000 kW | **2,000 kW** |
| PUE | 1.22 | **1.42** |
| Extra draw vs evaporative | +875 kW | +1,667 kW |

The chiller site pays ~1.7 MW of extra draw in July, pushing `load_ratio` from
0.25 to 0.33 at T2 — about **3 percentage points on every household bill in
town, every day, all summer.** That is the trade, stated numerically.

---

## 5. Coupling

| To | Via |
|---|---|
| **Compute** | Consumes `Q_IT`; returns θ, capping delivery, revenue and SLA |
| **Power** | `P_cool` is often the second-largest demand term; in a chiller summer it approaches half of `P_IT` |
| **Nuisance — water** | `V_water` vs `TOWN_WATER_DEMAND`. Sharp, local, immediately legible |
| **Nuisance — price** | Indirectly and powerfully: chiller draw → `load_ratio` → `p_resident` |
| **Nuisance — noise** | Compressors continuous; fan speed scales with cooling demand, so worst on summer nights people sleep through with windows open |
| **GNI** | Heat reuse is the only positive-direction coupling from a physical system |
| **Climate** | `T_db` and `T_wb` set every COP; drought sets water availability |
| **Co-op** | Valves, mode selectors, Run Hot, fire suppression, the pool connection |

**The defining property: there is no option with no victim.** Water, price,
noise — every route out of the heat lands on someone. The only exception is heat
reuse, which is why it costs €4M and pays €40/h.
