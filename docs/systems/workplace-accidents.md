# System: Workplace accidents

The comedy layer, designed as real mechanics. Players do not die — they become
**incidents**, and an incident is a line in a statistic, and the statistic is a
number the operator has to publish.

**Inputs:** hazard interactions, player position, equipment state.
**Outputs:** respawn events, lost carried items, `TRIR`, mandatory Program
bulletins, credibility burn, staff morale, GNI via `G_safety`.

---

## 1. Death, respawn, and why the death is not the punishment

A player who dies respawns at the **visitor bays on the street outside the
pedestrian gate** as a new temp worker.

```
on death:
    carried items       -> dropped at the point of death
    key card            -> dropped (physical; anyone can pick it up)
    respawn location    -> the visitor bays on the street outside the pedestrian gate
    respawn delay       -> ~7 s walk back (24 m: pavement, gate, walkway, door)
    Standing            -> unchanged
    recordable_incidents += 1
```

Standing is untouched, because being killed is not a performance issue. What
happens instead is §2.

**Losing the key card matters more than dying.** It is a physical object, it is
now on the floor of a compartment, and somebody else can pick it up
(§4.9). A player who dies in a locked room has left their access in a
locked room.

The 20-second walk is deliberate: it is long enough to be a real cost during an
incident and short enough that slapstick stays cheap.

---

## 2. The accident statistic is the actual mechanic

Using the real industry denominator:

```
TRIR = (recordable_incidents * 200000) / total_staff_hours
```

200,000 hours is the standard OSHA-style basis — 100 full-time workers for a
year. A 22-person site accumulates roughly 45,000 staff-hours a year, so **one
recordable incident produces a TRIR of about 4.4**, which is bad, and three
produces 13, which is the kind of number that gets read out at a council meeting.

### It feeds GNI through `G`, not through a sixth indicator

```
G_safety = -SAFETY_K * max(0, TRIR - TRIR_BASELINE)
```

`TRIR_BASELINE` = 2.4 (roughly the real all-industry average).

**Decision, flagged:** accident statistics do *not* become a sixth Program
indicator. The five in [nuisance.md](nuisance.md) are physical harms to the town
and their weights sum to 1.0; adding a sixth would rebalance every one of them
and break the design's central claim that every grievance is physical. Safety
enters through the goodwill term instead, alongside local hiring and heat reuse.
Logged as [open-questions.md](../open-questions.md) Q20.

### The press release is the punishment

Every recordable incident triggers a **mandatory Program bulletin**. It is free,
it is automatic, and it burns `CREDIBILITY_LOSS_PER_USE` = 0.15 like any other
bulletin ([sentiment.md](sentiment.md) §5).

Seven accidents and the group's bulletin credibility is at zero — so the bulletin
they wanted to issue about the new heat-reuse connection is worth nothing, and
below `CREDIBILITY_MOCKERY_THRESHOLD` it is worth less than nothing.

The copy writes itself from the site's actual state:

> "A colleague was involved in an **unplanned interaction with cooled airflow**
> in Hall 2 on Tuesday. The Good Neighbor Program's safety culture remains a
> cornerstone of our commitment to the region."

> "We can confirm a **temporary personnel displacement event** involving
> materials handling equipment. All Program safety measures were operating as
> designed."

The bulletin names a **responsible employee** by default: the deceased. Anyone
can edit that field before it goes out ([coop-griefing.md](coop-griefing.md) §4),
which means the first thing a group learns about workplace fatalities is that
they can be attributed to whoever is least present.

---

## 3. Lethality classes

| Class | Effect | Recordable | Example |
|---|---|---|---|
| **Fatal** | Respawn, items dropped | Yes | Suppression flood, forklift, freezing |
| **Injurious** | 90 s incapacitation, items kept | Yes | Taser, dropped cylinder, tile fall |
| **Property** | No player effect | No | Forklift into a CRAC |
| **Latent** | No immediate effect at all | Only if it later hurts someone | Bypass switch, vent fan off |

**Latent hazards are the dangerous ones**, because they generate no statistic
until the day they do.

---

## 4. The hazards

Every one has a legitimate primary function, and the function is the reason it
cannot be removed, patched or restricted. Full table in
[hazard-inventory.md](hazard-inventory.md).

### 4.1 Manual fire suppression pull stations
**Function:** manual activation when a human sees fire before the detector does.
Required by code, and genuinely faster than detection in a fast-developing fire.

Wire-and-seal secured. The seal is cuttable with the **wire cutters from the tool
cabinet**, which are there for cable work.

```
pull -> PRE_ALARM = 30 s countdown + siren
     -> compartment floods
     -> everyone inside: FATAL
     -> SUPPRESSION_DISCHARGE_DRIVE_LOSS = 0.55 of drives in the compartment
     -> effectiveness scaled by compartment gas_retention
```

**The door opens from both sides during the countdown — and can be held shut.**
This is a deliberate PvP murder mechanic and it is the most contentious thing in
the design. It is also exactly how the doors work.

The countdown, the siren and the seal are three separate warnings. The abuse case
requires cutting a seal, pulling a station, and physically holding a door for 30
seconds while a siren runs — the loudest, slowest, most attributable murder
available.

### 4.2 Cooling setpoint dial
**Function:** you must set supply air temperature, and dropping it before a
forecast heatwave buys genuine thermal headroom
([cooling-water.md](cooling-water.md)).

No safety limits, because a real setpoint has none.

```
T_supply < 0 °C:
    frost accumulates on coils (cooling capacity ↓ — it is self-defeating)
    icicles form on CRAC discharge
    a player in the cold aisle:  exposure += EXPOSURE_K * (0 - T_supply) * Δt
    exposure >= 1.0  ->  FATAL, the player becomes an ice block
```

The block is a **physics obstruction**: it blocks the aisle, so recirculation
rises and `T_inlet` rises. It melts over ~6 hours into a puddle, which finds a
cable tray ([construction-routing.md](construction-routing.md) §4). See the
February Chain, [incidents.md](incidents.md) §2.1.

Within [art-bible.md](../art-bible.md) constraints a frozen player is a capsule
in Pale Blue with a slight scale-up. It costs one material colour.

### 4.3 Taser
**Function:** issued at the Sabotage escalation stage (GNI 5–15) against
unauthorised third-party interference. It is a legitimate Program safety measure
and it genuinely reduces fibre-cut incidents.

Works on residents **through the fence**, and on colleagues.

```
on use:                    target incapacitated 90 s, INJURIOUS
on use against a resident: GNI -= TASER_RESIDENT_GNI (large)
                           local news item that evening, guaranteed
                           camera presence forced to 1.0
on use against a colleague: recordable incident, TRIR +1
```

**Every use appears on local news that evening**, regardless of target. There is
no version of this that is not public.

### 4.4 Forklift
**Function:** hardware arrives on pallets. You cannot install a rack without it.

A pure physics object with mass and momentum. Hits:

| Target | Result |
|---|---|
| Player | FATAL |
| CRAC unit | Property; cooling capacity loss until repaired |
| Transformer | Property; `moisture_ingress` and physical damage → §3.8 of incidents |
| Fence | Property; `N_visual` and a security-certification finding |
| Fibre duct route | **Fibre cut** ([incidents.md](incidents.md) §3.5) |
| Rack (lifted) | Topples. Nodes destroyed at full `GPU_NODE_CAPEX`. |

Lifting a rack to reposition it is a legitimate and necessary operation. Lifting
it too high, or turning while raised, topples it.

### 4.5 Emergency power-off buttons
**Function:** legally mandated, and genuinely necessary — an EPO is what you press
when a colleague is being electrocuted. **Unprotected by law**, because a cover
costs seconds.

```
press -> entire compartment de-energised instantly
      -> every running training job loses progress since its last checkpoint
      -> every inference contract in that compartment breaches
      -> restart sequence: 8-14 minutes
```

One press, no countdown, no confirmation, no undo. This is the purest expression
of the design's no-dialog principle and it will produce complaints
([risks.md](../risks.md) §16).

### 4.6 UPS maintenance bypass switch
**Function:** required to service the UPS without dropping the load. Without it,
UPS maintenance means a full outage.

```
bypass engaged:  racks run straight off the grid
                 pq_factor = PQ_BYPASS (1.8)  -> hardware ages 1.8x
                 next flicker: total drop, no ride-through
```

**Latent.** It works perfectly until the flicker, which may be weeks later, on
someone else's shift. The Bypass Chain, [incidents.md](incidents.md) §2.3.

### 4.7 Blanking panels
**Function:** correct airflow management. Fitted panels are one of the cheapest
real PUE improvements there is.

```
panel removed -> recirculation ↑ in that column
              -> T_inlet +4 to +8 °C locally
              -> fans compensate: P_cool ↑, N_noise ↑
              -> NIGHT_NOISE_MULT = 2.5 applies at night
```

Pulling a panel is necessary to reach a cable behind it. Not replacing it is not
an event.

**Invisible cause, audible effect**, and the effect lands on a channel with a
45-day memory. Finding a missing rectangle of sheet metal across forty racks is
the diagnosis problem this game will be criticised for.

### 4.8 Water inlet valve
**Function:** isolation for maintenance, and throttling to manage the water
indicator during a drought — a genuine and frequently correct action.

```
wide open: V_water unbounded -> N_water saturates -> town supply pressure drops
shut:      evaporative cooling capacity -> 0 -> Q_cap collapses -> throttle
```

A single quarter-turn, either direction, either disaster.

### 4.9 Key cards
**Function:** access control. **Required for the security certification**
([certifications-audits.md](certifications-audits.md) §2), which gates a contract
class and the 4-hour on-site RMA tier.

Physical objects. Carried, dropped on death, swappable, confiscatable.

- Lock a colleague **out** of the compartment they need.
- Lock a colleague **in** — combine with §4.1.
- Take a dead player's card off the floor before they walk back from the visitor bays on the street outside the pedestrian gate.

The certification requires that access be controlled. It does not require that it
be controlled by the right people.

### 4.10 The staff barbecue
**Function:** catering for the Community Engagement Session, which is one of the
cheapest GNI measures on the Program board.

Its stack shares the air channel with the diesel exhaust.

```
during an Engagement Session:  N_air += BBQ_AIR
                               with CAMERA_PRESENCE_INCIDENT = 0.85 active
                               and FILMED_MULT = 2.5
```

The reconciliation event wrecks the air readings. The Open Day Chain,
[incidents.md](incidents.md) §2.6.

### 4.11 Open-day visitors
**Function:** the Engagement Session itself. Visitors walk a route through the
building.

```
N_visual += Σ VISUAL_WEIGHT(thing_seen) for everything on the route
```

Cable spaghetti, lifted floor tiles, pallets in the aisle, icicles, a forklift
embedded in a transformer, an ice block that used to be a colleague. **The
session's effect is already scaled by `(1 - N_visual/100)`**
([sentiment.md](sentiment.md) §4.2); this makes the route itself a live
inspection.

The route is set by the players. Routing it past the tidy hall is legitimate and
transparent and everybody does it.

### 4.12 Untarped transformer
**Function:** the tarp must come off for inspection and oil service.

Sabotage by omission. The Tarp Chain, [incidents.md](incidents.md) §2.4.

---

## 4b. Five more, same spirit

### 4.13 Raised floor tiles and the tile lifter
**Function:** sub-floor power and cooling routing requires lifting tiles. The
lifter is the only tool that does it.

```
open tile:      a hole. Player walks in -> INJURIOUS. Forklift in -> FATAL + damage.
tiles_lifted:   under-floor static pressure ↓ across the WHOLE room
                airflow_delivered *= (1 - TILE_PRESSURE_K * tiles_lifted)
```

**A tile lifted in one corner starves a rack in the other.** Real, and it is the
one invisible-cause hazard that is actually visible if anyone looks at the floor.

### 4.14 The load bank
**Function:** generator and UPS testing. **The uptime certification requires
documented load-bank tests**, and it is the only way to discover a degraded UPS
battery string before it fails ([incidents.md](incidents.md) §3.7).

```
running: draws full rated kW and dumps it as heat
         outdoor unit -> N_noise, and N_air if generator-fed
         portable indoor unit -> Q_IT += load_bank_kW  (full heat, no revenue)
```

Scheduling a mandatory test during a heatwave is a legitimate scheduling
decision. Leaving the portable unit running indoors adds pure heat load with no
billable output.

### 4.15 Gas cylinders
**Function:** refilling the closed coolant loop and recharging the suppression
system. Stored on site because they must be.

```
cap off + knocked over  ->  projectile: FATAL on hit, property damage on miss
cross-connected fill port -> coolant loop contaminated
                          -> COP degraded ~15% until flushed (14-day job)
```

Which port is which is determined by **a player-typed label**
([construction-routing.md](construction-routing.md) §7). Cross-connecting is what
happens when the label is wrong, and the label is wrong because somebody was
tired ([staff-shifts.md](staff-shifts.md) §4).

Leaving caps off in the forklift route is not an event.

### 4.16 The battery room hydrogen vent fan
**Function:** battery strings off-gas hydrogen; code requires ventilation. The fan
is on a switch **because it is noisy**, and its noise is on the `N_noise` channel.

```
fan off:  N_noise -= VENT_FAN_NOISE          (a real, immediate GNI improvement)
          h2_concentration += H2_RATE * Δt
          battery_room_temp ↑ -> battery_wear ↑
h2 above threshold + ignition source (contactor arc, dropped tool):
          deflagration -> FATAL in compartment, UPS destroyed, fire
```

**This is the best hazard in the design.** Turning off a safety system is a
legitimate, defensible, *immediately effective* optimisation of the game's own
core metric. "I turned the fan off to help with the night noise complaints" is
true, helpful, and the reason the battery room went up. Nobody has to lie.

### 4.17 Combustible loading — packaging and pallets
**Function:** hardware arrives in cardboard on pallets, and somebody has to break
it down and walk it to the skip. Unpacking is a real job card.

```
packaging_in_hall:  hazard_fire += PACKAGING_FIRE_K * volume * Δt
                    aisle blocked -> recirculation ↑
                    N_visual += (already: uninstalled stock, nuisance.md §5)
                    automatic audit finding: combustible loading
```

This ties the existing procurement griefing vector
([coop-griefing.md](coop-griefing.md) §2) — order a lot and never unpack it —
into fire, airflow, visual nuisance and the audit at once. Ordering hardware is
never suspicious.

*Also worth building, one line:* the **eyewash station**, mandated beside the
battery room, must be flushed weekly for the audit, and flushing it puts water on
the floor next to the compartment with the most cable trays in it.

---

## 5. Failure modes

| Failure | Why |
|---|---|
| Death becomes free slapstick and stops mattering | The ~7 s walk and item drop are cheap. The TRIR and the mandatory bulletin are the real costs — if those are tuned too low, this whole system is a toy. |
| One player is murdered repeatedly | `NAMING_DECAY` does not apply to deaths. See [risks.md](../risks.md) §14. |
| Suppression-door-holding ends friendships | It is designed to be the loudest, slowest, most attributable act available. Whether that is enough is a prototype question. |
| The comedy overwhelms the simulation | Eleven of seventeen hazards are latent or property-only, on purpose. If sessions become pure slapstick, the ratio is wrong, not the hazards. |

---

## 6. How to abuse this against teammates

Covered per-hazard above and tabulated in
[hazard-inventory.md](hazard-inventory.md). The governing property:

**Every hazard here has a legitimate function that a reasonable person performs
on an ordinary day.** Cutting a seal, turning a dial, pressing an EPO, driving a
forklift, pulling a panel, flipping a bypass, closing a valve, switching off a
noisy fan — all of these are the job. The ledger records the name and the time.
It records nothing about why, and there is no field for it.

---

## 7. Coupling

| To | Passes | Unit |
|---|---|---|
| [incidents.md](incidents.md) | Ice, forklift damage, discharge, EPO, hydrogen | hazard accumulators |
| [sentiment.md](sentiment.md) | `G_safety`, mandatory bulletins, credibility burn | GNI, fraction |
| [nuisance.md](nuisance.md) | Barbecue air, vent fan noise, visitor route visual | points |
| [staff-shifts.md](staff-shifts.md) | TRIR → morale → local attrition | fraction |
| [certifications-audits.md](certifications-audits.md) | Cut seals, EPO covers, eyewash, combustible loading | findings |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Bypass → `pq_factor`; discharge → drive loss | fraction |
| [construction-routing.md](construction-routing.md) | Melt water paths, tile pressure, label truth | — |
| [coop-griefing.md](coop-griefing.md) | Every hazard is a physical unguarded object | intents |
