# System: Incident catalogue

The connective tissue. Every other system accumulates a hazard variable; this
system is what happens when one crosses its threshold, and how one incident
becomes the next.

**Inputs:** hazard accumulators from every system, detection coverage, staff
coverage, player proximity.
**Outputs:** capacity loss, damage (EUR), repair job cards, SLA breaches,
Sentiment effects, and *other incidents*.

---

## 1. The rule: nothing is random

Every incident has an accumulator fed by neglected variables and a threshold.

```
hazard_i(t) = hazard_i(t-1)
            + Σ accumulation(neglected_variables)
            - maintenance_effect

P(trigger this tick) = 0                                   if hazard_i < THRESH_i
                     = TRIGGER_K * (hazard_i - THRESH_i)   otherwise
```

**Randomness only decides *when* after the group has earned it, never *whether*.**
Below threshold the probability is exactly zero. This is a hard rule and it is
what makes the blame ledger meaningful — every incident has a cause with a name
and a date attached.

The one near-exception is hardware failure
([hardware-lifecycle.md](hardware-lifecycle.md) §3), where the *rate* is fully
determined but the dice pick which node. Stated openly there.

### Stages

Every incident runs the same five-stage machine:

| Stage | Meaning | Detection |
|---|---|---|
| **Latent** | Accumulator above threshold, nothing visible | Instrumentation only |
| **Detectable** | A sensor could see it; a person could smell it | Sensor, staff round, or a player standing there |
| **Active** | Doing damage now | Obvious |
| **Cascading** | Has triggered another accumulator past threshold | Obvious and expensive |
| **Consumed** | Resolved, or it has finished doing what it does | — |

```
stage_progression_rate = BASE_RATE * (1 + UNDETECTED_K * ticks_undetected)
```

**An undetected incident progresses faster**, because nobody is fighting it. That
single term is what makes instrumentation and night-shift coverage
([staff-shifts.md](staff-shifts.md)) worth paying for.

### Detection

```
P(detect per tick) = 1 - (1 - p_sensor)(1 - p_staff)(1 - p_player)

p_sensor  from installed instrumentation in that compartment
p_staff   from shift coverage × skill
p_player  1.0 if a player is in the compartment and it is at Detectable or worse
```

Instrumentation is purchasable per compartment: humidity/leak tape (€4k), smoke
(mandatory), thermal (€6k), power quality (€9k), vibration (€5k). All of them are
cheap and none of them is exciting, so groups buy them after the first time.

---

## 2. Escalation chains

The point of this system. **One incident is a nuisance; a chain ends a quarter.**
Six named chains that the design guarantees are reachable.

### 2.1 The February Chain
```
cooling setpoint set below freezing (a dial, no limits)
  → frost on coils, icicles on the CRAC discharge
  → a player lingers in the cold aisle and becomes an ice block
  → the block blocks the aisle (recirculation ↑, T_inlet ↑)
  → it melts over ~6 h into a puddle
  → the puddle follows the floor fall to a cable tray
  → power tray routed BELOW the pipe run gets wetted
  → short → breaker trips → one rack row dark
  → a training run loses an hour of progress (CHECKPOINT_INTERVAL_DEFAULT = 1 h)
  → inference SLA burns 40 min of its 3.65 h monthly allowance
```
Elapsed: about eight hours. Authors: whoever turned the dial, whoever routed the
tray months earlier, and whoever stood still.

### 2.2 The Dust Chain
```
filter job cards skipped for a quarter
  → filter_efficiency ↓, dust_load ↑
  → ΔT_dust +4 °C  →  temp_factor 1.32  →  wear_rate ×1.32
  → fans compensate → P_cool ↑ and N_noise ↑ at night
  → noise complaints, GNI drift
  → 14 months later: a failure cluster
  → capacity shortfall during a training deadline
```
Elapsed: over a year. This is the slowest chain in the game and the one nobody
attributes correctly.

### 2.3 The Bypass Chain
```
UPS put on maintenance bypass to service it, and left there
  → pq_factor 1.8, hardware ages faster (invisible)
  → grid flicker (storm, or load_ratio spike)
  → no conditioning: every rack hard-drops simultaneously
  → all training progress since the last checkpoint lost
  → all inference SLAs breached in the same minute
  → and the UPS batteries had degraded anyway, so the transfer
    would not have held either
```
Elapsed: cause to effect, weeks. **The person who flipped the switch is usually
not on shift when it bites.**

### 2.4 The Tarp Chain
```
transformer tarp removed for inspection and not replaced
  → moisture_ingress accumulates through autumn
  → storm event
  → transformer failure, grid connection lost
  → diesel auto-starts (policy set months ago by someone else)
  → wind is easterly: WIND_TOWARD_MULT ×3.0
  → residents filming: FILMED_MULT ×2.5
  → N_air spikes; HALFLIFE_AIR = 90 days
  → the T3 permit re-check three months later: REFUSED
  → €11,000,000 of committed capex lost
```
**Sabotage by omission.** The griefing action is doing nothing at all, and the
ledger has no entry for it, because not-replacing-a-tarp is not an event.

### 2.5 The Panel Chain
```
blanking panels pulled (to reach a cable, and never replaced)
  → recirculation ↑ in that rack column
  → T_inlet +6 °C locally
  → fans spin to compensate
  → N_noise spike, at night, NIGHT_NOISE_MULT ×2.5
  → complaints, GNI drift, 45-day memory
  → nobody can find the cause because the cause is a missing rectangle
    of sheet metal on one of forty racks
```
**Invisible cause, audible effect.** See [risks.md](../risks.md) §16 — this is
the hazard I expect players to call unfair.

### 2.6 The Open Day Chain
```
Community Engagement Session scheduled (a genuine GNI measure)
  → staff barbecue for catering, sited near the plant intake
  → its stack shares the air channel with the diesel
  → N_air spikes during the event
  → visitors walking the building see cable spaghetti, a lifted floor tile,
    and pallets in the cold aisle → N_visual
  → residents filming at CAMERA_PRESENCE_INCIDENT = 0.85
  → the reconciliation event lowers GNI
```
The measure the group bought to fix things is the thing that breaks them.

---

## 3. The catalogue

### 3.1 Water leak above a rack

| | |
|---|---|
| **Accumulator** | `hazard_leak += (joint_count × vibration + pipe_effective_age + freeze_thaw_cycles) × Δt` |
| **Neglected variables** | Pipe run age, elbow/joint count from sloppy routing, vibration from unbalanced fans, freeze–thaw from the setpoint dial |
| **Stages** | Seep (humidity sensor) → drip (visible, audible) → stream (obvious) |
| **Detection** | Leak tape (€4k/compartment) at Seep; a player standing under it at Drip |
| **Response** | Close the local valve (physical), catch it, replace the joint (job card, 45 min) |
| **Cost of ignoring** | Follows the floor fall to whatever tray is downhill. See §2.1. |
| **Sentiment** | Only via the water indicator if the loop must be refilled — and during a drought that is a visible draw |

### 3.2 Grid outage

| | |
|---|---|
| **Accumulator** | `hazard_grid` from regional `load_ratio`, storm severity, and **transformer condition** |
| **Neglected variables** | Transformer moisture ingress (§3.8), own `load_ratio` |
| **Stages** | Flicker (power quality event) → brownout → full loss |
| **Detection** | Immediate |
| **Response** | UPS holds for its remaining runtime; diesel per policy; battery discharge |
| **Cost of ignoring** | Total capacity loss for the duration |
| **Sentiment** | **The town loses power too.** A player-caused outage — a forklift into the transformer — is an entirely different GNI event from a storm. |

### 3.3 Fire

| | |
|---|---|
| **Accumulator** | `hazard_fire` from tray over-ampacity ([construction-routing.md](construction-routing.md) §3), dust load, combustible packaging, and shorts from §3.1 |
| **Stages** | Hot spot (thermal sensor, smell VFX) → smoke (detector, mandatory) → flame → compartment involvement |
| **Detection** | Smoke detection is mandatory; thermal is optional and catches it a stage earlier |
| **Response** | Extinguisher (physical, in a bracket), pull station, or automatic discharge. **All three have the same drive cost.** |
| **Cost of ignoring** | Compartment loss. With `compartment_integrity` < 1, spread. |
| **Sentiment** | Fire brigade attendance is a public event. `N_visual` and a **mandatory Program bulletin** ([workplace-accidents.md](workplace-accidents.md) §2). |

Note the trap: **suppression into a leaky compartment destroys 55% of the drives
and does not put the fire out**, because the gas escapes through unsealed
penetrations. You pay the full price for nothing.

### 3.4 DDoS

| | |
|---|---|
| **Accumulator** | `hazard_ddos` from grey-client share, absent scrubbing subscription, single peering path, and public exposure of the site's address range |
| **Neglected variables** | Contract mix, network redundancy ([network.md](network.md)) |
| **Stages** | Elevated traffic → link at 90% (latency blows up) → saturation → all contracts on that path breach |
| **Detection** | Traffic graphs, immediate if anyone is looking |
| **Response** | Enable scrubbing (monthly subscription, must be pre-bought), shift contracts to the second path, null-route the target |
| **Cost of ignoring** | Every contract on the saturated path, simultaneously |
| **Sentiment** | None directly. **This is the only major incident with no GNI component** and it is a useful counterexample. |

### 3.5 Fibre cut

| | |
|---|---|
| **Accumulator** | Sabotage stage (GNI 5–15), construction activity on the access road, **forklift proximity to the duct route** |
| **Stages** | Instant |
| **Detection** | Immediate |
| **Response** | Fail over to path B if it exists; otherwise wait for the splice crew (8–18 h) |
| **Cost of ignoring** | Total delivery loss |
| **Sentiment** | **If path A shares the duct with the town's own fibre — which the cheap path does — the town loses internet too.** A cut caused by your own contractor is a GNI event and it is on the local news. |

### 3.6 Overheating cascade

| | |
|---|---|
| **Accumulator** | `Q_cap` shortfall + recirculation (panels, tiles, trays, packaging) + dust |
| **Stages** | One rack above `T_INLET_SAFE` → its exhaust raises its neighbour's intake → spatial spread along the row |
| **Detection** | Per-rack thermal, or the throttle readout |
| **Response** | Throttle (safe), lower the setpoint, replace blanking panels, open doors, portable cooling |
| **Cost of ignoring** | `THROTTLE_DAMAGE_K` accumulates quadratically; nodes fail permanently |
| **Sentiment** | Fans at maximum → `N_noise`, and if diesel is started for portable cooling, `N_air` |

**Spatial contagion is what makes this a cascade** rather than a state. It spreads
rack to rack along the row at a rate set by aisle containment, so it can be
stopped by physically opening a door — which is free, obvious in hindsight, and
almost nobody's first instinct.

### 3.7 UPS battery failure

| | |
|---|---|
| **Accumulator** | `battery_wear` from cycle count, ambient temperature in the battery room, age, **and hydrogen vent fan off** (heat builds) |
| **Neglected variables** | Never load-testing ([certifications-audits.md](certifications-audits.md) requires documented tests) |
| **Stages** | Reduced runtime (**invisible without a test**) → fails to hold at the next flicker |
| **Detection** | **Only a load-bank test.** This is the point. |
| **Response** | Replace strings (capex, 14-day lead) |
| **Cost of ignoring** | The UPS does nothing at the moment it exists to do something |
| **Sentiment** | Via the outage it fails to prevent |

### 3.8 Transformer failure in a storm

| | |
|---|---|
| **Accumulator** | `moisture_ingress += rain × (tarp_off ? 1.0 : 0.05) × Δt`, plus thermal cycling from `load_ratio` swings, plus **physical damage from forklift impact** |
| **Stages** | Elevated dissolved-gas reading (needs the €9k power-quality sensor) → partial discharge (audible buzz) → failure |
| **Detection** | Instrumentation, or the buzz if a player walks past |
| **Response** | Replace the tarp (free, 2 minutes), oil service (job card), replacement (€340k, 60-day lead) |
| **Cost of ignoring** | Grid connection lost until replaced. **60 days on diesel.** |
| **Sentiment** | See §2.4. This is the worst chain in the game. |

### 3.9 Incursion (fence breach)

The one entry in the catalogue driven by the *community* accumulator (GNI)
rather than a physical one, and the one the town causes rather than suffers —
docs/open-questions.md Q28's overrule made residents solid and approachable,
Q29 extends that to this. Same rule as everywhere else in this file: nothing
about **whether** is random. What **is** rolled, once the threshold is
crossed, is which asset — the same stated exception hardware-lifecycle.md §3
takes for which node fails.

| | |
|---|---|
| **Accumulator** | Cumulative hours in the Sabotage band (`INCURSION_TRIGGER_HOURS`, own counter, independent of the fibre cut's) |
| **Neglected variable** | GNI, same as every Sabotage-stage effect — this is what staying that unpopular for that long earns |
| **Stages** | Scheduled (telegraph, `INCURSION_TELEGRAPH_HOURS` — the cast resident's routine visibly bends toward the target fence segment) → breached (`INCURSION_BREACH_HOURS` — the resident is at the real rack or solar row, in person, taser-interruptible) → resolved |
| **Detection** | A camera reveals the specific asset and resident from the moment it is scheduled; without one, only the vague ticker line runs until the breach itself |
| **Response** | A floodlight is the only *passive* defense — it deterministically aborts every other attempt, not a percentage roll, so it stays inevitable-in-hindsight rather than a visible dice roll (§1's rule again); an alarm adds a HUD/audio cue with no auto-resolve; a taser lets a player in person end it with zero loss. Camera/floodlight/alarm are sited hardware and each adds a small, fixed `VisualPoints` cost on purchase — the same fence trap §3 of sentiment.md already runs, on a shorter lever; the carried taser is exempt |
| **Cost of ignoring** | One rack (`INCURSION_RACK_NODES` nodes) or one solar row (`INCURSION_SOLAR_KWP` kWp), whichever the die picked — the same asset Facility.cs was already about to render as the newest one, so it goes dark exactly where it stood |
| **Sentiment** | None extra — GNI caused this, GNI does not also charge for it. The Program's own aftermath line ("the Program regrets nothing it is required to disclose") is the whole cost on that side of the ledger |

---

## 4. Failure modes of this system itself

| Failure | Why it happens |
|---|---|
| Players cannot tell which accumulator fired | 12 accumulators, one UI. See [risks.md](../risks.md) §15. |
| Incidents feel random anyway | The threshold is invisible; only the effect is visible. Mitigation: the as-built board and sensor readouts must show accumulators, not just states. |
| Cascades produce unrecoverable spirals | Every chain in §2 must have at least one cheap physical interrupt — a door, a valve, a breaker. All six do. Check any new chain against this. |
| Nothing ever fires | A tidy group sees no incidents for two in-game years and the system is invisible. **This is correct** and should not be "fixed" by adding randomness. |

---

## 5. How to abuse this against teammates

Every entry is an act of *omission* or a legitimate action at a chosen moment.

| Abuse | Legitimate cover |
|---|---|
| Don't replace the transformer tarp | Inspection genuinely requires removing it |
| Skip filter job cards | Nobody enjoys filters |
| Never buy leak tape | €4k per compartment for a sensor that has never fired |
| Cancel the DDoS scrubbing subscription | Real monthly saving, no visible effect |
| Route power trays under water pipes | Shortest path |
| Leave the UPS on bypass | Bypass is required for service |
| Never run a load-bank test | It makes noise and draws full power |
| Turn the battery room vent fan off | **It genuinely reduces `N_noise` at night** |
| Drive the forklift near the duct route | The duct route is also the delivery route |

**None of these is detectable as intent.** The ledger records that a tarp was
removed, not that it was never put back — because "never put back" is not an
event, and that is exactly why it is the best griefing vector in the game.

---

## 6. Coupling

| To | Passes | Unit |
|---|---|---|
| [construction-routing.md](construction-routing.md) | `hazard_fire`, water paths, compartment integrity, repair time | — |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Failure clusters, dust, power quality | count, °C |
| [cooling-water.md](cooling-water.md) | `Q_cap` loss, setpoint, freeze–thaw | kW_th, °C |
| [power.md](power.md) | Outage, transformer, UPS, diesel start | kW |
| [network.md](network.md) | Fibre cut, DDoS saturation | Gbps, ms |
| [compute-contracts.md](compute-contracts.md) | Delivery loss, checkpoint loss, SLA breach | kWh_IT |
| [nuisance.md](nuisance.md) | Fan noise, diesel air, fire brigade visual, town outage | points |
| [sentiment.md](sentiment.md) | Mandatory bulletins, credibility burn, incursions (§3.9) | GNI |
| [staff-shifts.md](staff-shifts.md) | Detection coverage, repair job cards | hours |
| [workplace-accidents.md](workplace-accidents.md) | Ice blocks, forklift damage, suppression discharge | — |
