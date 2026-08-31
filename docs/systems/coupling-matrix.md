# Coupling matrix

Which system feeds which, and in what units. Every edge in the design, in one
place.

**Codes**

| Code | System | Code | System |
|---|---|---|---|
| `CLI` | [Climate / seasons](seasons.md) | `INC` | [Incidents](incidents.md) |
| `CON` | [Compute & contracts](compute-contracts.md) | `ACC` | [Workplace accidents](workplace-accidents.md) |
| `PWR` | [Power](power.md) | `STF` | [Staff & shifts](staff-shifts.md) |
| `COOL` | [Cooling & water](cooling-water.md) | `CRT` | [Certifications & audits](certifications-audits.md) |
| `RTE` | [Construction & routing](construction-routing.md) | `NUI` | [Program indicators](nuisance.md) |
| `HW` | [Hardware lifecycle](hardware-lifecycle.md) | `GNI` | [Good Neighbor Index](sentiment.md) |
| `NET` | [Network](network.md) | `ECO` | [Economy](../economy.md) |
| `COOP` | [Co-op interaction layer](coop-griefing.md) | `END` | [Endgame](endgame.md) |
| `META` | [Meta-progression](meta-progression.md) | | |

---

## 1. Edges by producer

### CLI — Climate
Pure source. Reads nothing, writes to everything downstream.

| → | Quantity | Unit |
|---|---|---|
| COOL | `T_db`, `T_wb` → every COP; drought → water allowance | °C, m³/day |
| PWR | Irradiance → solar; wind speed → turbines; temp → `p_base`, `scarcity` | kW, EUR/kWh |
| NUI | **Wind direction** → `WIND_TOWARD_MULT`/`AWAY` on air | ×3.0 / ×0.3 |
| INC | Storm → transformer, grid; freeze–thaw → leak; rain → moisture ingress | accumulator |
| HW | Ambient particulate (harvest, construction) → dust | dimensionless |
| ACC | Below-freezing ambient interacts with the setpoint dial | °C |

### CON — Compute & contracts

| → | Quantity | Unit |
|---|---|---|
| COOL | `Q_IT` | kW_th |
| PWR | `P_IT`, `P_aux` | kW |
| NET | `bw_requirement`, `latency_max` per contract | Gbps, ms |
| ECO | Revenue, SLA penalties | EUR |
| GNI | **Nothing.** Deliberate — the town does not care what you compute. | — |

### PWR — Power

| → | Quantity | Unit |
|---|---|---|
| CON | Grid `CAP` bounds allocatable load | kW |
| NUI | `p_resident` → price; diesel → air; diesel/chillers/turbines → noise; arrays and stacks → visual | points |
| HW | Flicker events → `pq_factor` | dimensionless |
| INC | `hazard_grid`, transformer condition, UPS state | accumulator |
| ECO | Energy cost, fuel | EUR/h |
| END | Grid tier gates Exit C | tier |

### COOL — Cooling & water

| → | Quantity | Unit |
|---|---|---|
| CON | θ (throttle factor) | 0–1 |
| PWR | `P_cool` | kW |
| HW | `T_inlet` → `temp_factor` → wear rate | °C |
| NUI | `V_water` → water; fan/compressor power → noise | m³/day, points |
| GNI | Heat reuse → `G` (the only positive physical coupling) | GNI points |
| INC | `Q_cap` shortfall → overheating cascade; setpoint → freeze–thaw | accumulator |
| ACC | Setpoint dial below 0 °C → ice | °C |

### RTE — Construction & routing

| → | Quantity | Unit |
|---|---|---|
| PWR | Conductor loss | kW |
| COOL | Pump power; recirculation → `T_inlet` | kW, °C |
| HW | `T_inlet` via recirculation → wear | °C |
| NET | Path length, hop count, patch panel state | ms |
| INC | `hazard_fire` (tray derate), water paths, `compartment_integrity`, `label_factor` → repair time | accumulator, × |
| CRT | Penetrations, ampacity, labels, housekeeping | findings |
| NUI | Fan speed → noise; visible mess and uninstalled stock → visual | points |
| META | Blueprints | — |

### HW — Hardware lifecycle

| → | Quantity | Unit |
|---|---|---|
| CON | `node_efficiency`, node availability | fraction, count |
| PWR | Full `P_IT` regardless of efficiency | kW |
| COOL | Full `Q_IT` regardless of efficiency; dust → `T_inlet` | kW_th, °C |
| INC | Dust as fire load; failure clusters | accumulator |
| STF | Filter changes, RMA, node swaps → job cards | h |
| NUI | Dust → fan power → noise | points |
| ECO | Replacement capex, RMA fees, market timing, salvage | EUR |
| END | Fleet value at wind-down / due diligence | EUR |

### NET — Network

| → | Quantity | Unit |
|---|---|---|
| CON | Latency premium; SLA breach above `latency_max × 1.5` | EUR/kWh_IT |
| INC | Fibre cut, DDoS, congestion collapse | availability |
| CRT | Tier IV requires dual verified paths | findings |
| NUI | Cut on the shared duct → **the town loses internet** | GNI event |
| ECO | Capex, monthly, scrubbing, diversity check | EUR |

### INC — Incidents
The connective tissue. Consumes accumulators from everywhere, writes back to
everywhere.

| → | Quantity | Unit |
|---|---|---|
| CON | Delivery loss, checkpoint loss, SLA breach | kWh_IT |
| COOL | Capacity loss from plant damage | kW_th |
| PWR | Outage, transformer loss, UPS failure | kW |
| NET | Fibre cut, saturation | Gbps |
| HW | Node destruction | count |
| NUI | Fan noise, diesel air, fire-brigade visual, town outage | points |
| GNI | Mandatory bulletins, credibility burn | GNI, fraction |
| STF | Repair job cards | h |
| ECO | Damage, emergency hire, penalties | EUR |

### ACC — Workplace accidents

| → | Quantity | Unit |
|---|---|---|
| INC | Ice blocks, forklift damage, discharge, EPO, hydrogen, tile pressure | accumulator |
| HW | Bypass → `pq_factor`; discharge → drive loss | ×, fraction |
| COOL | Setpoint, valves, blanking panels → `T_inlet` and `Q_cap` | °C, kW_th |
| RTE | Melt water → tray paths; tiles → under-floor pressure | — |
| GNI | `G_safety` from TRIR; **mandatory bulletins** | GNI, fraction |
| NUI | Barbecue → air; vent fan → noise; visitor route → visual | points |
| STF | TRIR → morale → attrition → local headcount | fraction |
| CRT | Cut seals, EPO obstruction, vent fan, eyewash, TRIR | findings |

### STF — Staff & shifts

| → | Quantity | Unit |
|---|---|---|
| INC | `p_staff` detection, repair speed, job card completion | probability, h |
| RTE | **Fatigue → label errors** | `label_factor` |
| HW | Filter changes, RMA despatch | dust, days |
| NUI | Night noise response lag | points |
| GNI | Local headcount (existing lever), attrition losses | GNI |
| CRT | Preparation hours, documented tests | h |
| ECO | Payroll incl. night and local premiums | EUR/month |

### CRT — Certifications & audits

| → | Quantity | Unit |
|---|---|---|
| CON | Contract **class** access | boolean per class |
| NET | Tier IV gates latency-tier work | boolean |
| PWR | Environmental cert is a T4 precondition | gate |
| HW | Security cert gates 4-hour on-site RMA | tier |
| NUI | Security cert **requires cameras** → visual | points |
| STF | Remediation job cards, 14-day crunch | h |
| ECO | Audit fees, preparation, consultancy | EUR |
| END | Due-diligence discount at Exit B | fraction |

### NUI — Program indicators

| → | Quantity | Unit |
|---|---|---|
| GNI | Five `N_i` through memory `M_i` | 0–100 |

Sole consumer. This is why NUI and GNI are one code module
([GDD.md](../GDD.md) §12).

### GNI — Good Neighbor Index

| → | Quantity | Unit |
|---|---|---|
| PWR | **Gates every grid-tier permit**, at application *and* completion | gate |
| CON | Protest/injunction block expansion; sabotage → SLA breaches | — |
| STF | Hiring frozen at Protest | gate |
| NUI | **Back-edge:** safety measures raise `N_visual` | points |
| ECO | Program benefit, hiring premium, suppression opex, bulletins | EUR |
| END | Referendum result; GNI ≥ 40 gates Exit B; GNI ≥ 75 gates T4 → Exit C | gate |
| META | Town memory carry | 0–100 |

### ECO — Economy
Accumulator. Reads from everything, writes cash constraints back.

| → | Quantity | Unit |
|---|---|---|
| all | Cash available for capex, payroll, remediation | EUR |
| END | Bankruptcy; acquisition offer size | EUR |

### COOP — Interaction layer
Writes intents into every system. Owns only the ledger and per-player Standing.

| → | Quantity | Unit |
|---|---|---|
| all | Player intents at tick boundaries | — |
| GNI | Bulletins, naming, benefit dial, hiring roster | GNI, Standing |

### END / META
END reads final state from everything and terminates. META writes starting
conditions into CON (`REPUTATION_START`), GNI (`GNI_START`, credibility), HW
(catalogue availability), RTE (blueprints).

---

## 2. Most-connected systems

| System | In | Out | Note |
|---|---|---|---|
| **INC** | 9 | 9 | The hub. Every neglected variable arrives here and leaves as damage. |
| **PWR** | 6 | 6 | Touches four of five indicators. Last system that can be simplified. |
| **ACC** | 4 | 8 | Writes far more than it reads — it is a *source* of chaos, not a consumer. |
| **RTE** | 3 | 8 | Same shape. Both are input layers dressed as systems. |
| **NUI** | 8 | **1** | Eight producers, one consumer. Confirms the NUI+GNI merge. |
| **CLI** | 0 | 6 | Pure source, confirms it is a data service, not a system. |

**The three systems with in-degree ≈ 0 or out-degree ≈ 1 are not systems.**
Climate is a data source, Nuisance is GNI's input stage, and Co-op is an
interaction layer. Recorded in [GDD.md](../GDD.md) §12 and unchanged by this
round of design.

---

## 3. Cycles

Only four closed loops exist in the whole design. Every one is intentional and
every one is *negative* (self-limiting) except the last.

1. **Cooling ⇄ Compute.** `Q_IT` → `Q_cap` → θ → `P_IT` → `Q_IT`. Resolved in a
   single pass, not iterated ([open-questions.md](../open-questions.md) Q6).
2. **Power price ⇄ own draw.** `P_grid_draw` → `load_ratio` → `p_grid` → the
   operator's own bill. Self-limiting; the player pays for their own congestion.
3. **Heat ⇄ wear.** `T_inlet` → wear → failures → capacity loss → less heat.
   Self-limiting the ugly way.
4. **Suppression → Visual → GNI → Sabotage → Suppression.** The only
   **positive** feedback loop in the design, and the one the whole game is about.
   It is why Program safety measures are a spiral rather than a cost
   ([sentiment.md](sentiment.md) §8).

Any new system that adds a fifth cycle should be treated as a design error until
proven otherwise.

---

## 4. The longest causal chain

Traced end to end, this is the deepest path the simulation supports. Every hop
is an edge above.

```
STF fatigue
  → RTE wrong label on a coolant fill port
  → ACC gas cylinder cross-connected
  → COOL loop contaminated, COP −15%
  → CON θ < 1 during a heatwave
  → CON training deadline missed (−€1.98M)
  → PWR diesel started to recover capacity
  → CLI easterly wind, WIND_TOWARD_MULT ×3.0
  → NUI residents filming, FILMED_MULT ×2.5
  → NUI air indicator, HALFLIFE_AIR 90 days
  → GNI falls below SENTIMENT_GATE
  → PWR T3 permit refused at re-check (−€11M)
  → END referendum petition threshold reached
```

**Twelve hops, eleven months, one tired technician.** The ledger records the
label edit and nothing else about any of it.
