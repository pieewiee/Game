# System: Construction and routing

Rooms, walls, doors, fire compartments, and the hand-routed physical runs that
carry power, cooling and network between them.

**Inputs:** player construction intents, room geometry, equipment placement,
run topology, player-typed labels.
**Outputs:** power loss (kW), pump power (kW), airflow quality, latency (ms),
fire-compartment integrity, water flow paths, repair-time multipliers,
`N_visual` contributions.

---

## 0. This contradicts two committed decisions

Stated plainly, per the brief's instruction.

**`coop-griefing.md` §5** says routing is *deliberately shallow* — two variables
only, length and crossings — specifically so we do not compete with the four
shipped datacenter sims on cabling, which is what they do well.
**`scope.md`** lists hand-routed runs as cut **#2**.

This brief makes routing system #1, with capacity, validation, patch panels and
fire compartments. That is a real reversal and it needs a decision, not a fudge.

**How I have resolved it here, and it is the only resolution I think survives
the market position:**

> Routing gets real depth, but **only where it feeds another system.** Length,
> ampacity, derating, pressure drop, compartment penetrations, water paths,
> airflow obstruction, label truth. All of it exists because a leak needs
> somewhere to run to and a fire needs a way through a wall.
>
> Routing gets **no depth as a puzzle in itself.** No network topology, no VLANs,
> no port configuration, no switch management, no signal-integrity minigame, no
> "trace the cable" challenge. Those are the competitors' ground and we stay off
> it entirely.

The test for any future addition to this system: *does it change what an incident
does?* If not, it does not go in. Logged as
[open-questions.md](../open-questions.md) Q17, which is the most consequential
open question in the project.

---

## 1. The grid

A 2 m × 2 m floor grid. Everything occupies whole cells.

| Element | Footprint | Blocks |
|---|---|---|
| Wall | cell edge | People, airflow, fire, suppression gas, water. Requires a door to pass. |
| Door | cell edge | Nothing when open. Fire/gas/water when closed. **Opens from both sides, always.** |
| Rack | 1 cell | People, airflow (it is the airflow). `RACK_HE` = 42 vertically. |
| CRAC / cooling unit | 2 cells | People. Generates directed airflow. |
| Cable tray | overhead, cell edge | Nothing. Carries runs. **Catches water.** |
| Pipe run | overhead or sub-floor | Nothing. Carries coolant or water. |
| Conduit | in wall or sub-floor | Nothing. |
| Patch panel | wall-mounted | Nothing. Terminates network runs. |
| Raised floor tile | 1 cell | Nothing when down. **A hole when lifted.** |

**Nothing is ever refused.** The game does not block an illegal build; it builds
it and lets the consequences accumulate. Validation is advisory
(§5) and always after the fact.

---

## 2. Fire compartments

A **fire compartment** is a set of cells fully enclosed by rated walls and doors.
It is the unit of:

- Fire spread (fire crosses a compartment boundary only through a breach)
- Suppression discharge (`SUPPRESSION_ROOM_SCOPE` = 1 compartment, consistent
  with [coop-griefing.md](coop-griefing.md) §3)
- Smoke and gas containment
- Access control zones ([certifications-audits.md](certifications-audits.md))

### Penetrations — the thing players will get wrong

Every run that crosses a compartment wall makes a **penetration**. A penetration
must be **fire-stopped** (a €40 sealing kit, 6 minutes of work) or the compartment
is not a compartment.

```
compartment_integrity = 1 - (unsealed_penetrations / total_penetrations)

fire_spread_probability_per_tick = SPREAD_K * (1 - compartment_integrity)
gas_retention                    = compartment_integrity
suppression_effectiveness        = gas_retention
```

Two consequences that make this worth simulating:

1. **A discharge into a leaky compartment does not put the fire out** — the gas
   escapes — but it still destroys `SUPPRESSION_DISCHARGE_DRIVE_LOSS` = 55% of
   the drives, because the acoustic shock does not care about gas retention. You
   get the damage without the benefit.
2. **Sealing is invisible once done and invisible when skipped.** It is a
   documented audit finding ([certifications-audits.md](certifications-audits.md)
   §3) and nothing else surfaces it until a fire.

Fire-stopping is the single most boring task in the game and it is the difference
between one dead rack and one dead building.

---

## 3. Power runs

```
I_run       = P_served / (V_LL * √3 * pf)                        [A]
loss_kW     = 3 * I_run² * R_per_m * length_m / 1000             [kW]
```

`V_LL` = 400 V, `pf` = 0.98. `R_per_m` depends on the conductor size the player
chose when drawing the run.

### Bundling derate

Real, and the most useful thing this system contributes:

```
derate       = max(DERATE_FLOOR, 1 - DERATE_K * (n_circuits_in_tray - 1))
capacity_eff = rated_ampacity * derate
overload     = max(0, I_run / capacity_eff - 1)
```

`DERATE_K` ≈ 0.05 per additional circuit, floor 0.5. Six circuits in one tray run
at 75% of nameplate. **The tray does not tell you this.**

```
tray_temperature = T_ambient + THERMAL_K * Σ loss_kW_in_tray
hazard_fire     += FIRE_ACCUM_K * max(0, tray_temperature - TRAY_T_SAFE)
```

`TRAY_T_SAFE` = 70 °C. That `hazard_fire` accumulator is the trigger variable for
the tray fire in [incidents.md](incidents.md) §3.3 — **not a random roll.** A player
who bundles nine circuits into one tray to save a walk has scheduled a fire and
the game will let them.

### What sloppy power routing costs

A deliberately badly routed 4 MW site versus a tidy one:

| | Tidy | Sloppy |
|---|---|---|
| Average run length | 34 m | 71 m |
| Conductor loss | 18 kW (0.45%) | 47 kW (1.2%) |
| Worst tray derate | 0.90 | 0.65 |
| Trays above `TRAY_T_SAFE` | 0 | 3 |
| Added `load_ratio` | — | +0.007 |

29 kW of pure loss is €50/day and a rounding error in
[economy.md](../economy.md). **The money is not the point — the fire is.** The loss
exists so the player has a legible number long before the hazard fires.

---

## 4. Cooling and water runs

```
Δp          = K_PIPE * length_m * (flow_lpm / d_mm²)²            [kPa]
pump_kW     = flow_lpm * Δp / (60000 * PUMP_EFF)                 [kW]
```

Long, thin, elbow-heavy runs cost pump power, which goes into `P_cool` and
therefore straight into PUE and the price indicator
([power.md](power.md) §3).

### Water finds trays

This is the mechanic that makes routing matter more than anything else in this
document.

Every overhead run has a position and the floor has a fall. Water released
anywhere — a leak, a melting ice block, a flushed eyewash station, a
suppression-system false fill — **follows the geometry**:

```
water_path = downhill cells from source
for each cell in water_path:
    if cell has a cable tray below the water source elevation:
        tray_wetted += volume_share
        if tray carries live power:
            short_probability += SHORT_K * tray_wetted
```

A player who runs power trays *underneath* pipe runs has built the escalation
chain in [incidents.md](incidents.md) §2.1 with their own hands, months earlier,
to save six metres of cable. Running power above and water below is free, takes
one extra thought, and nothing in the game ever mentions it.

---

## 5. Validation — advisory, never blocking

There is an **as-built board** in the plant room: a physical object showing every
run, its load, its derate, its penetration status and its label. It is the only
place violations are surfaced, and reading it is a deliberate act.

| Class | Example | Surfaced by |
|---|---|---|
| **Capacity** | Tray above derated ampacity | As-built board, amber |
| **Thermal** | Tray above `TRAY_T_SAFE` | As-built board, red + a real hot smell VFX |
| **Integrity** | Unsealed penetration | As-built board only |
| **Documentary** | Missing or wrong label | As-built board only |
| **Airflow** | Tray obstructing a CRAC discharge | Nothing. Only the room temperature. |

**Nothing here is a popup and nothing blocks construction.** A group that never
looks at the board never learns any of it, which is the correct level of
punishment for not looking at the board.

---

## 6. Airflow

Racks have a front (cold aisle) and a back (hot aisle). Cooling units discharge
into cold aisles.

```
recirculation = f(blanking_panel_gaps, aisle_containment, tray_obstruction,
                  lifted_floor_tiles)
T_inlet(rack) = T_supply + RECIRC_K * recirculation * ΔT_rack
```

Four independent ways to break it, three of them invisible:

1. **Missing blanking panels** — hot exhaust recirculates through the empty HE.
   See [workplace-accidents.md](workplace-accidents.md) §4.7.
2. **Cable trays across a CRAC discharge** — a routing decision that looked
   tidy.
3. **Lifted raised-floor tiles** — under-floor static pressure drops across the
   whole room, so a tile lifted in one corner cools a rack in another.
   [workplace-accidents.md](workplace-accidents.md) §4.13.
4. **Packaging and pallets in the aisle** — blocks flow and is a fire load.

Because `T_inlet` drives both throttling ([cooling-water.md](cooling-water.md) §2)
and hardware wear ([hardware-lifecycle.md](hardware-lifecycle.md) §1), a
recirculation problem shows up first as a fan speed increase — which shows up as
`N_noise` at night — which shows up as a GNI complaint eleven days later with no
visible cause at all.

---

## 7. Labels — the best griefing vector in the game

**Every cable, port and patch panel position has a label that is a string a
player typed.** The simulation knows the real connection. The label is just text
on the world.

Labels can be written, edited, swapped and removed by anyone, at any time, with
no record beyond the ledger line saying a label was changed.

```
repair_time = base_repair
            * (1 + SPAGHETTI_K * crossings_in_room)
            * label_factor

label_factor:  correct label    1.0
               no label         1.6
               WRONG label      2.5
```

**A wrong label is worse than no label**, because people act on it. That is true
in every building ever wired and it is the entire joke: the most destructive
thing you can do to a colleague is type four correct-looking characters on a
piece of tape.

It is also completely deniable. Labels are typed by tired people
([staff-shifts.md](staff-shifts.md) §4 — fatigued staff mislabel ports on their
own), so a wrong label has no reliable author even with the ledger open.

**Legitimate function:** labels are how anyone finds anything, they cut repair
time by 60%, and **the audit checks them**
([certifications-audits.md](certifications-audits.md) §3). A site with no labels
fails its uptime certification.

---

## 8. Failure modes

| Failure | Cause | First visible as |
|---|---|---|
| Tray fire | Bundling derate ignored, `hazard_fire` accumulates | Smell VFX, then smoke detection |
| Short from water | Power tray below a pipe run | Breaker trip, a whole rack row dark |
| Compartment breach | Unsealed penetration | Nothing, until a fire crosses it |
| Suppression ineffective | Low `gas_retention` | Drives dead, fire still burning |
| Pump power creep | Long, thin, elbow-heavy runs | PUE, then the price indicator |
| Recirculation | Panels, trays, tiles, packaging | Fan noise at night |
| Repair takes three times as long | Crossings and wrong labels | An SLA breach during an ordinary fault |

---

## 9. How to abuse this against teammates

Every one of these is also something a tired person does by accident, which is
the point.

| Abuse | Legitimate cover |
|---|---|
| Bundle nine circuits into one tray | "It was the shortest route" |
| Route the power tray under the chilled-water main | "There was no room above" |
| Skip fire-stopping a penetration | "I was going to come back to it" |
| Swap two patch cables at the panel | Patching is the normal job |
| Relabel a port with a plausible wrong string | Labelling is mandatory and audited |
| Lift six floor tiles across a room | Sub-floor routing requires lifting tiles |
| Lay a tray across the CRAC discharge | It is the straightest line |
| Leave pallets in the cold aisle | Deliveries arrive on pallets |

**None of these is a griefing tool.** All of them are Tuesday. The blame ledger
records who drew the run and who typed the label; it records nothing at all about
why.

---

## 10. Coupling

| To | Passes | Unit |
|---|---|---|
| [power.md](power.md) | Conductor loss, pump power | kW |
| [cooling-water.md](cooling-water.md) | Pump power, `T_inlet` via recirculation | kW, °C |
| [compute-contracts.md](compute-contracts.md) | Throttling via `T_inlet`; outage via breaker trips | θ |
| [network.md](network.md) | Path length, hop count, patch panel routing | ms, Gbps |
| [incidents.md](incidents.md) | `hazard_fire`, water paths, compartment integrity, repair time | — |
| [hardware-lifecycle.md](hardware-lifecycle.md) | `T_inlet` → wear rate | °C |
| [nuisance.md](nuisance.md) | Fan speed → `N_noise`; visible mess → `N_visual` | points |
| [certifications-audits.md](certifications-audits.md) | Penetrations, ampacity, labels, housekeeping | findings |
| [coop-griefing.md](coop-griefing.md) | Every run and label is a physical, unguarded object | intents |

### Merge note

This system **absorbs** `coop-griefing.md` §5 entirely. That section should be
replaced by a pointer here rather than maintained in parallel.
