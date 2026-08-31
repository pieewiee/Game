# System: Co-op and griefing

Not a system so much as an **interaction layer over every other system**. It owns
exactly two pieces of state — the blame ledger and per-player Standing — and its
main job is *removing* the menus that would otherwise wrap the simulation.

**Inputs:** player intents (host-ordered, [multiplayer.md](../multiplayer.md)),
current sim state.
**Outputs:** state mutations on every other system, ledger entries, Standing
changes, GNI effects via bulletins.

---

## 1. The rule

**No menu action is ever consequential. No confirmation dialogs. No undo.**

Every dangerous thing is an object in the world that anyone can walk up to and
operate. If an action can lose money, break an SLA, or move the Good Neighbor
Index, it has a physical location and anyone can reach it.

Menus exist only for things that are genuinely inert: reading a contract's terms,
reading the ledger, looking at a chart, panning the site map. **The moment a menu
can change the world, it becomes a lever, a dial, a switch or a desk.**

The corollary matters as much: **there is no "are you sure?".** A dialog converts
a physical mistake into an administrative one, which is the opposite of this
design. The confirmation is the three seconds you spend holding the lever while
someone runs across the yard shouting.

---

## 2. Shared account, no permissions

One balance. One account. No roles, no ownership, no locks.

**Anyone can order anything.** Ordering happens at a physical procurement desk in
the office. The order goes onto a manifest board — also physical, also readable
by everyone — showing item, cost and arrival date.

```
DELIVERY_LEAD_MIN = 3 days      (consumables, small parts)
DELIVERY_LEAD_MAX = 90 days     (chiller plant, transformers)
```

**Once dispatched, an order cannot be cancelled.** You watch the truck arrive.
This is deliberate and it is the single most important consequence of "no undo":
the group's cash is not a resource the group manages, it is a resource the least
careful player manages.

**What you can do instead of cancelling:** nothing, until it arrives. Then the
equipment sits in the yard as uninstalled stock, adding to `N_visual` until
someone installs it or sells it back at a loss. **The grief creates work, not
just damage** — which is far better co-op design, because the recovery is
something the group does together.

### The two-key interlock

My addition, flagged in [open-questions.md](../open-questions.md) Q4.

Orders above `TWO_KEY_THRESHOLD` = €500,000 require two players to press two
interlocks at opposite ends of the site within `TWO_KEY_WINDOW` = 8 seconds.

This is **not a permission system** — it is a physical control, in the fiction it
is an ordinary corporate spending authorisation, and any two players can do it
including two who intend harm. It exists because without it a single player can
commit €11,000,000 in one click and the economy in
[economy.md](../economy.md) stops being a design.

It also produces one of the better co-op moments: sprinting to opposite ends of
the site to buy a chiller before the heatwave, and the discussion about whether
to.

---

## 3. The physical control inventory

Every one of these is operable by any player at any time.

| Control | Location | Effect | Recovery |
|---|---|---|---|
| **Diesel start lever** | Generator yard | Hold `DIESEL_START_HOLD` = 3 s. Runs until stopped. | Stop it — but the plume persists for minutes and `HALFLIFE_AIR` is 90 days |
| **Fire suppression trigger** | Wall of each room | Discharges inert gas. Destroys `SUPPRESSION_DISCHARGE_DRIVE_LOSS` = 55% of drives in that room by acoustic shock | None. Replace the drives. |
| **Rack power switches** | Per rack | Cuts one rack instantly | Flip it back; lost work is lost |
| **Node switches** | Per node | Cuts one node | As above |
| **Cooling mode selector** | Per room | Evaporative / chiller / free / off | Instant, but water spent is spent |
| **Water valves** | Plant room | Throttle or isolate the evaporative loop | Instant |
| **Coolant valves** | Plant room | Same for the closed loop | Instant |
| **Run Hot switch** | Plant room | Suppresses throttling; inlet temperature rises | Flip back; accumulated damage is permanent |
| **Main breaker** | Switchgear | Site-wide grid disconnect | Reclose it |
| **Gate control** | Perimeter | Open/close. Open during a protest lets residents *inside* the fence | Close it, after |
| **Procurement desk** | Office | Orders anything, subject to §2 | No |
| **Program board** | Office | Every Program measure, suppression and investment alike, one list sorted by cost | Some are removable, at cost |
| **Bulletin terminal** | Office | Issues a Program bulletin, incl. the *responsible employee* field | No |
| **Program benefit dial** | Office | Sets the monthly community fund in EUR | Turn it back; money spent is spent |
| **Hiring roster** | Office | Hire or dismiss staff, local or not | Dismissal costs `DISMISSAL_GNI_PER_FTE` = 2.2 per head, ~2.4× what hiring gained |
| **Contract terminal** | Office | Signs any offered contract | **No.** Signed is signed. |
| **Engagement Session calendar** | Office | Schedules a Community Engagement Session | **No** — cancelling is worse than holding it |
| **Route tools** | Anywhere | Lays power, cooling and network runs | Rip it out and redo it |

Note what is *not* here: nothing sets a policy, nothing schedules automation,
nothing applies "to all rooms". Every room has its own selector and every rack
has its own switch, because a griefer flipping forty switches one at a time is
visible for forty seconds and a griefer clicking "all rooms: off" is not.

---

## 4. The blame ledger

Every state-changing intent produces an append-only entry:

```
{ tick, actorId, action, target, magnitude }
```

The actor is assigned **by the host on receipt**, never by the client — see
[multiplayer.md](../multiplayer.md) §4. The ledger is part of the save.

It is readable by everyone, at a terminal, as a chronological list. It is not
summarised, not ranked, and there is no leaderboard: the game never tells anyone
who the worst player is. It tells you what happened and at what time, and the
argument is yours to have.

### Naming a responsible employee

A Program bulletin has an optional *responsible employee* field.

```
GNI      += NAMING_GNI_EFFECT * NAMING_DECAY^(n_prior_namings)     (4.0, ×0.55 each)
Standing_named -= STANDING_LOSS_NAMED                              (14)
Standing_p     += STANDING_RECOVERY per day                        (0.6)
```

The fourth naming is worth 0.66 GNI. Accountability theatre has diminishing
returns, exactly as it does.

**You may name someone who did not do it.** The ledger knows; the town does not.
A false naming works mechanically — GNI still rises — unless resident footage
(§7) contradicts it, at which point a fact-check fires:

```
credibility -= FALSE_NAMING_PENALTY * CREDIBILITY_LOSS_PER_USE     (3 × 0.15)
GNI         -= 2 * NAMING_GNI_EFFECT
```

So lying is a gamble against the camera-presence probability at the time of the
original incident. A player who caused something at 03:00 with nobody filming can
be blamed for it safely. A player blamed for the 14:00 plume that six people
recorded cannot.

### Program personnel realignment

Below `STANDING_CONSULTANT_THRESHOLD` = 20, the group may dismiss a player in a
bulletin. One-off GNI gain.

**The dismissed player remains in the session.** They return the next morning as
a consultant: identical abilities, identical access to every control,
`CONSULTANT_COST_MULT` = 1.9× payroll, and `CONSULTANT_GNI_PENALTY` = 0.35 GNI
per day for as long as they are on site, because the town noticed.

You fired them publicly for the good of the community and rehired them at nearly
double the cost within a day. Nothing else in the design is this close to the
register of the title.

---

## 5. Hand-routed runs — deliberately shallow

Power, cooling and network runs are drawn by hand along cable trays and pipe
racks. **This is not a cable-management sim.** Three of the four competitors
([GDD.md](../GDD.md) §2) do cabling depth well and we must not compete there.

What routing models, and nothing more:

```
efficiency_loss = ROUTE_LOSS_K * total_run_length                  [kW]
repair_time     = base_repair * (1 + SPAGHETTI_K * crossings_in_room)
```

- **Length costs power.** Real, small, and it means a lazy run is measurable.
- **Crossings cost repair time.** A room with tangled runs takes longer to fix
  when something fails, and failures happen at the worst moments.
- **Runs are permanent until ripped out**, and ripping out means the thing it
  fed is down while you do it.

That is the entire model. It exists so that sloppy work is *inherited* — someone
else has to untangle it, months later, during a heatwave. Any further depth here
is scope spent on the competitors' ground.

---

## 6. Griefing vectors that emerge from the systems

Beyond the controls in §3, these arise from the economy and community systems
without being designed as griefing at all.

### 6.1 The contract signature
Anyone can sign any offered contract. Signing a 4 MW training run that the site
cannot cool commits the group to a deadline, a load curve and a breach penalty.
**Nobody can un-sign it.** The griefer creates months of work rather than damage,
which makes it the most socially interesting vector in the game — the group now
has to decide whether to build for it or eat the penalty.

Mitigated by design: `CHECKPOINT_INTERVAL_DEFAULT` is **1 hour**, shortened from
the 6 hours a real run would use, specifically so that a single malicious rack
switch during a training run costs an hour of progress and not a week. The
griefer still gets the scream; the group does not lose the session.

### 6.2 The cooling mode selector during a drought
Switching a room from chiller to evaporative while water is restricted spends the
group's entire allowance in hours. Instantly reversible, and completely
irreversible in consequence: the water is gone, the indicator saturates, and
`HALFLIFE_WATER` = 120 days.

### 6.3 The Program benefit dial
Cranking the community fund to €200k/month **raises** GNI. It looks like
generosity. It is also the fastest way to drain the reserve the grid upgrade
needs — and the upgrade's permit is gated on the GNI that the dial is raising.
A griefing action that is indistinguishable from virtue, defensible out loud, and
visible only as a slow cash-flow problem three months later.

### 6.4 The hiring roster
Dismissing local staff costs `DISMISSAL_GNI_PER_FTE` = 2.2 per head against
`LOCAL_HIRE_GNI_PER_FTE` = 0.9 gained — dismissal hurts about 2.4× what hiring
helped. A dismissal spree of twelve local staff is −26 GNI in one afternoon.

And because **hiring is frozen at the Protest stage**, a spree that pushes GNI
below 40 removes the group's cheapest route back up. This is the most destructive
single sequence available to one player and it takes about ninety seconds.

### 6.5 The gate during a protest
Opening the main gate during unscheduled gate activity lets residents *inside the
fence*. Camera presence goes to `CAMERA_PRESENCE_INCIDENT` = 0.85 and every
indicator sampled while they are inside is × `FILMED_MULT` = 2.5. Closing the
gate instead blocks your own deliveries.

### 6.6 The scheduled Engagement Session
Announced, then uncancellable. Schedule one three days out while `N_visual` is 70
and the group must physically dismantle searchlights they paid for, or hold an
open day at a site the local paper already calls a compound.

---

## 7. Residents at the fence

Residents appear at the perimeter and film. Presence is probabilistic:

```
P(filming) = CAMERA_PRESENCE_DAY   (0.35)  daytime
             CAMERA_PRESENCE_NIGHT (0.08)  night
             CAMERA_PRESENCE_INCIDENT (0.85) once something loud or visible is underway
```

Any indicator sampled while filming is multiplied by `FILMED_MULT` = 2.5, and the
footage appears on the local news that evening as a generated item.

This makes **timing** a first-class decision alongside wind direction: the same
generator run costs between 0.3× and 7.5× depending on the hour and who is
watching. It also produces the game's cruellest incentive — a player learns to do
the harmful thing at 03:00 in an easterly, and the design should let them, and
the local news should eventually run an item about how the noise always seems to
happen at night.

Residents are **never seen close up** — silhouettes, phone screens, car
headlights, lit windows. That is an art-budget decision in
[art-bible.md](../art-bible.md) §6 and a tone requirement first: capsules with hard
hats are fine for staff, and would be a real problem for the people the game
refuses to mock.

---

## 8. Time as a shared resource

**Decision: fast-forward is available only when every player is idle.**

```
idle(p)      = no interaction for IDLE_THRESHOLD = 6 s
fast_forward = all players idle
holder       = the non-idle player, named on screen
override     = majority vote, available after TIME_HOLD_OVERRIDE = 180 s
```

**Why idle-gating rather than a majority vote**, which was the alternative:

- A vote turns time into a political negotiation every few minutes, and 2–5
  players voting on the clock is a lot of voting.
- A majority can vote *past* a minority's crisis. If one player is mid-repair
  during a heatwave, three others should not be able to skip them through it.
- Idle-gating requires no UI and no decision: the clock runs when the team has
  nothing to do, which is exactly when you want it to.
- Holding time is a **physical veto** with no menu — you hold it by touching
  something, which is already how you do everything else.

**The obvious abuse** — jiggling a valve to hold the clock forever — is handled
socially rather than mechanically: the holder is named on screen with their
location ("Time held by SAM — plant room"), which makes obstruction visible and
therefore self-limiting. The 180-second majority override is the backstop, and it
is deliberately long enough to be annoying, because being annoyed at each other
is the product.

---

## 9. Coupling

| To | Via |
|---|---|
| **Compute** | Rack/node switches, the contract terminal, Run Hot |
| **Cooling** | Mode selectors, valves, Run Hot, fire suppression |
| **Power** | Diesel lever, main breaker, procurement of generation |
| **Nuisance** | Every one of the above, plus camera presence and the gate |
| **GNI** | Bulletins, naming, the benefit dial, the hiring roster, Engagement Sessions |
| **Economy** | Procurement, the benefit dial, payroll, consultant multipliers |
| **Multiplayer** | Intent ordering and ledger authority — [multiplayer.md](../multiplayer.md) |

### The conflict with the economy, stated plainly

The economy operates on months and millions; griefing operates on seconds. That
mismatch is real and is analysed in [risks.md](../risks.md) §4. Three mitigations
are already built into the numbers above and are the reason those numbers differ
from a single-player version of this design:

1. `CHECKPOINT_INTERVAL_DEFAULT` shortened 6 h → **1 h**.
2. `SUPPRESSION_ROOM_SCOPE` capped at **one room**, not the site.
3. The **two-key interlock** above `TWO_KEY_THRESHOLD`.

All three reduce the *blast radius* of an instant action without reducing the
action itself. The lever still works. The scream still happens. The session
survives.
