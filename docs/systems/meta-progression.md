# System: Meta-progression

What survives a run. Deliberately thin, entirely optional, and designed so that
run two is a fresh problem rather than an easier one.

**Inputs:** exit type, final Reputation, final GNI, blueprints saved, hardware
generations used.
**Outputs:** starting conditions for the next run.

---

## 0. Optionality is a requirement, not a feature

**The game must be complete without this system.** A group that plays one run and
stops has played the whole game. A group that never enables carry-over sees no
missing content, no locked hardware and no greyed-out anything.

That is why the persistence list below is short and why almost all of it is
either a convenience or a *complication* rather than a boost.

At new-run creation there is a toggle per category. Defaults:

| Category | Default |
|---|---|
| Blueprints | **On** — pure quality of life |
| Client reputation | Off |
| Hardware tiers | Off |
| Town memory | Off |

Everything but blueprints changes difficulty in one direction or the other, so it
is a choice the group makes out loud.

---

## 1. What persists

### 1.1 Blueprints
Saved room layouts, cable tray runs, pipe routes, patch panel plans. Importable
into a new site as a **stencil** — it shows where things went, it does not build
them. Materials and labour are paid again.

**This is the largest real benefit and it is entirely a convenience.** A group
that spent a run learning that power trays go *above* water pipes
([construction-routing.md](construction-routing.md) §4) can express that in a
blueprint instead of re-deriving it. Nothing about the second site is easier; the
group is just not made to re-type its own conclusion.

Blueprints are **shareable as files**, which is the closest this design gets to
user-generated content and costs nothing to support.

### 1.2 Client reputation — partial
```
REPUTATION_START_next = 50 + REP_CARRY_K * (R_final - 50)
REP_CARRY_K = 0.25, result clamped [40, 65]
```
A run finishing at Reputation 90 starts the next at 60 — enough to unlock
`REP_GATE_TRAINING_MED` immediately, which skips perhaps four in-game months of
being deliberately boring. That is the correct size: it removes repetition, not
challenge.

Clamped at 65 so it can never reach `REP_GATE_TRAINING_LARGE` = 75 on carry
alone.

### 1.3 Hardware tiers — availability only
A generation the group has run for 200+ hours becomes **offerable** earlier in
the next run. Not free, not discounted, not better. It appears in the
procurement catalogue at a point where it would otherwise not yet exist.

This matters because [hardware-lifecycle.md](hardware-lifecycle.md) §5 ties GPU
price to `demand_index`: knowing a generation exists lets a group buy into a
trough they would otherwise have sat out.

### 1.4 Town memory — the interesting one
```
GNI_START_next = GNI_START + TOWN_MEMORY_K * (GNI_final - GNI_START)
TOWN_MEMORY_K = 0.35, result clamped [38, 74]
```

**The new site is on the same land, next to the same town.** The company is new,
the name is new, the branding is new. The town is not fooled and the local paper
opens with "the previous operator".

By exit type ([endgame.md](endgame.md) §4):

| Exit | Effect on town memory |
|---|---|
| **A — referendum lost** | Carries **well**. A town that removed an operator and was proved right starts the next one warily but fairly. `GNI_START` ≈ 55–60. |
| **B — acquisition** | Carries **badly**. The town watched the operator sell to someone who cut everything. `GNI_START` ≈ 38–45, and the first bulletin's credibility starts at 0.7. |
| **C — growth** | Carries **neutrally**. High GNI, but the town's expectations are now calibrated to a 50 MW site that employed everyone. `GNI_START` high, and `LOCAL_HIRE_GNI_PER_FTE` **reduced 20%** because the bar has moved. |

C's carry is a penalty dressed as a bonus, which is the correct shape for this
game.

---

## 2. What deliberately does not persist

Each of these was considered and rejected, with a reason.

| Not carried | Why |
|---|---|
| **Cash and hardware** | Always start from nothing. Carrying capital removes the entire first act. |
| **The built site** | You get blueprints, not a building. Construction is content. |
| **Grid tier and permits** | **The single most important exclusion.** The permit gate ([power.md](power.md) §2) *is* the progression system; carrying T2 would delete the game's spine. Always start at T0. |
| **Certifications** | Structural, not behavioural — they describe a building that no longer exists. |
| **Staff roster** | A different town hiring pool, a different site. |
| **Player Standing and the blame ledger** | Per-run social state. Carrying grudges across sessions turns a comedy into bookkeeping. **See §4.** |
| **Weather seed** | New seed every run, always. |
| **Contract offer sequence** | Otherwise the second run is a memorised script. |
| **Incident accumulator states** | Obviously. |
| **Bulletin credibility** | Except the exit-B penalty above, which is deliberate and small. |

**The rule I applied:** carry *understanding*, never *position*. Blueprints and
hardware availability are crystallised understanding. Cash, tier and buildings are
position.

---

## 3. The biggest carry is not modelled

The player's own knowledge. That power trays go above water pipes. That free
cooling in October is a trap. That the vent fan is off for a reason and the
reason is bad. That the person who signs the training contract should say so
first.

**None of that is stored anywhere and all of it is the actual meta-progression.**
The system above exists so that knowledge does not have to be re-typed, and for
nothing else. Any proposal to add a skill tree, a currency, or unlockable
mechanics should be measured against this paragraph and will fail it.

---

## 4. Co-op: whose meta-progression?

The brief did not specify this and it is a real question in a 2–5 player game.

**Recommendation:**

- **The host's carry applies to the session.** Blueprints, client reputation,
  hardware tiers and town memory all come from the host's save. It is the host's
  town.
- **Blueprints are shareable files**, so a non-host can hand theirs over before
  the run starts.
- **Standing does not carry at all**, for anyone, which sidesteps the question of
  whether a player who was scapegoated last session starts this one at 36.

The alternative — averaging or unioning across the lobby — makes joining a
stranger's session a mechanical decision, and this game is explicitly not
matchmade ([multiplayer.md](../multiplayer.md) §8).

Logged as [open-questions.md](../open-questions.md) Q22.

---

## 5. Failure modes

| Failure | Why |
|---|---|
| Run two is easier rather than different | The whole §2 list is the defence. If a group's second run is smoother in a way that is not just competence, something in §1 is too generous. |
| Groups feel obliged to grind runs | Nothing is gated behind repetition. Hardware tiers only affect *timing* of availability, never capability. |
| Town memory makes run two unwinnable after exit B | Clamped at 38, which is inside the Petition band but above Protest. Uncomfortable, not fatal. |
| It becomes the reason to play | It must not. If playtesters describe the appeal as "unlocking things", this system has eaten the game and should be cut wholesale. |

---

## 6. How to abuse this against teammates

Thin by design, but not zero:

| Abuse | Legitimate cover |
|---|---|
| Import a blueprint with the power trays under the water pipes | "I made this last run, it works" |
| Import a blueprint with deliberately wrong labels baked in | Labels are part of a layout |
| Host with a bad town-memory carry and not mention it | Nobody asks what the host's last run ended in |

The blueprint ones are genuinely nasty: a stencil is trusted, it is copied
without reading, and the consequence — a puddle finding a power tray — arrives
eight in-game months later.

---

## 7. Coupling

| To | Passes | Unit |
|---|---|---|
| [endgame.md](endgame.md) | Exit type determines the carry profile | — |
| [sentiment.md](sentiment.md) | `GNI_START`, initial bulletin credibility | 0–100, fraction |
| [compute-contracts.md](compute-contracts.md) | `REPUTATION_START` | 0–100 |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Catalogue availability by generation | — |
| [construction-routing.md](construction-routing.md) | Blueprint stencils | — |
| [multiplayer.md](../multiplayer.md) | Host's save supplies session carry | — |
