# System: Staff and shifts

NPC employees as **coverage**, not as simulated individuals.

**Inputs:** roster, wages, shift assignment, delegated job cards, TRIR.
**Outputs:** job card completion, incident detection probability, repair speed,
label error rate, payroll (EUR/month), local headcount → GNI, attrition.

---

## 0. This contradicts two committed decisions

**`GDD.md` §11** states: *"No staff simulation beyond headcount and locality."*
**`risks.md` §2** identifies the game's biggest pacing risk as idle players with
nothing legitimate to do, and prescribes **continuous small physical
maintenance** as the fix.

A system where NPCs do the small maintenance removes the fix and reinstates the
risk. That is a direct conflict, not a tension.

**Resolution, and it is the only one that keeps both:**

> Staff are an **aggregate roster with per-shift coverage**, never pathfinding
> agents you watch. And they only do work that has been **explicitly delegated**
> — delegation is a physical act, a job card moved to the DELEGATED column on the
> board in the plant room.
>
> **The default is undelegated.** Every job card starts as work the players do
> themselves. Handing it to staff costs payroll *and* costs the group something
> to do, and that second cost is real and is meant to be felt.

So staff are a lever the group chooses to pull, not a replacement for the
players. A five-player group with everything delegated has bought its own
boredom, at 25% above market wages. Logged as
[open-questions.md](../open-questions.md) Q18.

**Merge note:** local headcount and its GNI effect already live in
[sentiment.md](sentiment.md) §4.3 (`LOCAL_HIRE_GNI_PER_FTE` = 0.9,
`LOCAL_HIRE_WAGE_PREMIUM` = 0.18, `DISMISSAL_GNI_PER_FTE` = 2.2). **Those numbers
are unchanged and are not restated here.** This system adds shifts, skills,
fatigue and delegation on top of the existing headcount model; it does not
replace it.

---

## 1. The roster

Each employee is five numbers and a locality flag. No names, no faces, no
schedules to micromanage — consistent with
[art-bible.md](../art-bible.md) §3, where staff are capsules.

| Field | Range | Meaning |
|---|---|---|
| `skill_electrical` | 0–1 | Power runs, breakers, transformer service |
| `skill_mechanical` | 0–1 | Cooling plant, valves, filters, pipework |
| `skill_it` | 0–1 | Node swaps, RMA handling, patching, labelling |
| `wage` | EUR/month | Base, scaled by skill |
| `fatigue` | 0–1 | §4 |
| `is_local` | bool | Feeds the existing GNI lever |

```
wage = WAGE_BASE * (1 + WAGE_SKILL_K * mean_skill)
     * (1 + LOCAL_HIRE_WAGE_PREMIUM if is_local)
     * (1 + NIGHT_SHIFT_PREMIUM if on nights)
```

`NIGHT_SHIFT_PREMIUM` = 0.25 — anchored to German night-work supplements, which
run around 25%.

---

## 2. Shifts and coverage

Three shifts: 06–14, 14–22, 22–06.

```
coverage(shift, discipline) = Σ_staff_on_shift skill_discipline * (1 - fatigue)

required(shift) = COVERAGE_BASE
                + COVERAGE_PER_MW * site_MW
                + COVERAGE_PER_CARD * open_job_cards
                + COVERAGE_PER_INCIDENT * active_incidents

understaffing = max(0, required - coverage)
```

### What understaffing does

It never produces a popup. It produces four slow degradations:

```
p_staff (detection)  = DETECT_BASE * coverage_norm
repair_speed         = SPEED_BASE * coverage_norm
job_cards_completed  ∝ coverage_norm
label_error_rate     = ERROR_K * mean_fatigue
```

An understaffed site does not fail. It **detects incidents one stage later and
repairs them 60% slower**, which converts every ordinary fault into an SLA event.

### The night shift

The night shift is expensive and does one specific thing: it lets the site
**respond to a noise event before it accumulates.**

`N_noise` carries `NIGHT_NOISE_MULT` = 2.5 and `HALFLIFE_NOISE` = 45 days. A fan
spike at 02:00 on an uncovered site runs until 06:00 — four hours at 2.5× into a
45-day memory. On a covered site it is caught in twenty minutes.

```
noise_response_lag = uncovered ? hours_until_next_shift : NIGHT_RESPONSE_MIN
```

**Night cover is a Sentiment purchase disguised as an operations expense**, and
that is exactly the kind of thing this game should make the player work out for
themselves. It costs `NIGHT_SHIFT_PREMIUM` on top of an already-premium local
wage, and it competes directly with the payroll budget the local-hiring lever
already competes for.

---

## 3. Job cards

The board in the plant room. Three columns: **OPEN · DELEGATED · DONE**. Physical
cards, movable by anyone.

| Card | Interval | Discipline | Skipping it feeds |
|---|---|---|---|
| Filter change | 6 weeks | Mechanical | `dust_load` → wear, noise |
| Transformer tarp replace | after each inspection | Electrical | `moisture_ingress` |
| Load-bank test | Quarterly | Electrical | `battery_wear` undetected; audit finding |
| Eyewash flush | Weekly | — | Audit finding |
| Fire-stop penetrations | on each new run | Electrical | `compartment_integrity` |
| Blanking panel audit | Monthly | Mechanical | Recirculation |
| Packaging removal | on each delivery | — | `hazard_fire`, `N_visual`, airflow |
| Label verification | Quarterly | IT | `label_factor` on repairs; audit finding |
| Coolant top-up | Seasonal | Mechanical | Loop pressure, leak risk |
| Node RMA despatch | on failure | IT | Dead capacity |
| Tile reseat | on each sub-floor job | Mechanical | Under-floor pressure |

**Every card in this list is a hazard accumulator's maintenance term** in
[incidents.md](incidents.md). There is no busywork here; each one is the only
thing standing between a variable and a threshold.

This is also the answer to [risks.md](../risks.md) §2 — eleven recurring physical
tasks is enough legitimate work to occupy the third, fourth and fifth players
indefinitely, **as long as the group does not delegate all of it.**

---

## 4. Fatigue

```
fatigue += FATIGUE_RATE * hours_worked * (night ? NIGHT_FATIGUE_MULT : 1.0)
fatigue -= REST_RATE * hours_off
fatigue capped [0,1]

error_probability = ERROR_K * fatigue
```

A fatigued employee completing a job card has `error_probability` of doing it
**wrong rather than not at all**:

- Fire-stop installed but not sealed
- Filter fitted backwards (efficiency 0.4 instead of 0.9)
- **A port labelled with a plausible wrong string**

That last one matters most. It means
[construction-routing.md](construction-routing.md) §7's wrong-label penalty
(`label_factor` 2.5) has a *legitimate* source that is not a player. So when a
critical repair takes three times as long because the label was wrong, the ledger
shows a name — and the honest answer may genuinely be "the night tech was on
hour eleven".

**Fatigue is what makes mislabelling deniable**, and deniability is what makes
the blame system a comedy rather than a courtroom.

---

## 5. Staff react to the accident statistics

```
morale -= MORALE_TRIR_K * max(0, TRIR - TRIR_BASELINE)
morale -= MORALE_UNDERSTAFF_K * understaffing
morale += MORALE_WAGE_K * (wage / market_wage - 1)

attrition_rate = ATTRITION_BASE * (1 + ATTRITION_K * (1 - morale))
hiring_pool_local *= (1 - POOL_SHRINK_K * max(0, TRIR - TRIR_BASELINE))
```

Two consequences, one of which is the point of this whole section:

1. **An unsafe site cannot hire locally.** In a town of `TOWN_POPULATION` = 4,000,
   the pool is small and everyone knows everyone. Two fatalities and the local
   pool closes.
2. **Local staff who quit take GNI with them.** They leave at
   `DISMISSAL_GNI_PER_FTE` = 2.2 per head — the same asymmetric rate as being
   dismissed, because from the town's side there is no difference between being
   sacked and leaving because the place is dangerous.

So: **accidents → morale → local attrition → GNI → permit gates.**

The cheapest Sentiment lever in the game
([sentiment.md](sentiment.md) §4.3) is destroyed by the comedy layer
([workplace-accidents.md](workplace-accidents.md)), and the group will not
connect the two for about a year.

---

## 6. What staff do that players cannot be bothered to do

Honestly: all eleven job cards in §3, plus standing detection coverage. That is
the entire value proposition and it is deliberately unglamorous.

**And that is the trap.** Delegating everything is correct on a spreadsheet and
wrong for the session, because the group has then paid 18–43% wage premiums to
remove its own gameplay. The design does not warn anyone about this.

---

## 7. Failure modes

| Failure | Why |
|---|---|
| Group delegates everything, gets bored | Working as designed and not signposted. The prices are meant to make full delegation expensive enough to hesitate over. |
| Understaffing is invisible until an incident | Also as designed. The coverage readout exists on the job board and nowhere else. |
| Night shift feels like a tax with no payoff | It has exactly one payoff — noise response — and if `NIGHT_RESPONSE_MIN` is tuned wrong nobody ever buys it. |
| Staff become an NPC-management minigame | The hard boundary against this is §0: aggregate coverage, no individual agents, no pathing. If anyone proposes per-employee scheduling, this system has failed. |

---

## 8. How to abuse this against teammates

| Abuse | Legitimate cover |
|---|---|
| Move every job card to DELEGATED | "We're all busy, that's what they're for" |
| Move cards *back* to OPEN and never do them | Tidying the board |
| Drop the night shift to save payroll | It is a genuine 25% premium for one benefit |
| Dismiss local staff during a cash squeeze | Payroll is the only flexible cost line |
| Roster the same staff onto consecutive nights | Coverage looks full on the board |
| Hire non-local at 18% less | Straightforwardly cheaper |

The consecutive-nights one is the interesting one: **the coverage readout shows
headcount, and fatigue is a separate number on the roster page.** A roster that
looks fully covered can be running at 0.7 fatigue and producing mislabelled
ports all week.

---

## 9. Coupling

| To | Passes | Unit |
|---|---|---|
| [incidents.md](incidents.md) | `p_staff` detection, repair speed, job card completion | probability, h |
| [construction-routing.md](construction-routing.md) | Label errors from fatigue | `label_factor` |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Filter changes, RMA despatch, node swaps | dust, days |
| [sentiment.md](sentiment.md) | Local headcount (existing lever), attrition losses | GNI |
| [nuisance.md](nuisance.md) | Night noise response lag | points |
| [workplace-accidents.md](workplace-accidents.md) | TRIR → morale → attrition | fraction |
| [certifications-audits.md](certifications-audits.md) | Preparation hours; documented tests | h, findings |
| [economy.md](../economy.md) | Payroll incl. night and local premiums | EUR/month |
