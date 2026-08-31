# System: Endgame

Three exits. All reachable. None marked correct.

**Inputs:** GNI, petition count, Reputation, grid tier, contract history,
certifications, cash.
**Outputs:** run termination, epilogue, meta-progression carry
([meta-progression.md](meta-progression.md)).

---

## 0. The design constraint

**No ending may be scored.** No "best ending" marker, no achievement hierarchy, no
percentage. All three epilogues are the same artefact — a retrospective in the
local paper, eighteen months later, same layout, same length, same voice —
containing different facts.

The comparison the game wants the player to make is between what happened to the
group and what happened to the town, and it can only make that comparison if it
refuses to make it for them.

A fourth exit exists and is **not** one of the three: **bankruptcy**
([economy.md](../economy.md) §6). It is a loss state, it should be rare, and it
gets a short epilogue rather than a full one.

---

## 1. Exit A — Lost referendum

The only ending where the town is unambiguously better off.

### Trigger
Petition signatures exceed threshold → referendum scheduled 90 days out →
`yes_share` computed per [sentiment.md](sentiment.md) §7 → lost.

Recall that the `ΔGNI` term means a group visibly improving can survive a vote
they would have lost on the day it was called. **A group that loses this vote has
usually spent the 90 days on bulletins and fences.**

### Final sequence — 90 days of wind-down
Playable, not a cutscene. The group must actually do it.

```
day 0    operating licence revoked, effective in 90 days
         no new contracts, no permits, no expansion
day 0-90 contracts must be terminated (penalty) or migrated (client's choice)
         hardware sold into the market model
         staff dismissed
         site decommissioned
day 90   power down
```

Two mechanics make this more than paperwork:

- **Dumping the fleet moves the market.** Selling 400 nodes into a low
  `demand_index` depresses `p_gpu` further
  ([hardware-lifecycle.md](hardware-lifecycle.md) §5), so a fire sale recovers
  perhaps 40% of book value. Selling gradually recovers more and runs the clock.
- **Dismissing local staff costs `DISMISSAL_GNI_PER_FTE` = 2.2 each**, and GNI
  still matters for exactly one thing: the epilogue. A group that lays everyone
  off in week one and one that keeps them to day 89 get different final
  paragraphs.

### Epilogue
The site is demolished or, more often, left. What the paper reports:

- The pool goes cold if it was connected, and the article says when.
- Local employment falls to what it was, and the number is stated.
- Grid prices fall back to baseline over six months, and the article notes the
  amount and that residents had been told it was unrelated.
- The five indicator memories decay, and the article says which one people still
  mention.
- Two residents are quoted, accurately, and neither is triumphant.

---

## 2. Exit B — Acquisition by a hyperscaler

The most money. The worst outcome for the town.

### Trigger
All of:
```
Reputation      >= 85
grid tier       >= T3
a large training contract delivered within SLA in the last 12 months
GNI             >= 40          (they do not buy a site facing a referendum;
                                they do not require one that is loved)
```

The offer arrives as **a physical letter at the office**, followed by a site
visit. It expires after 60 days and does not come back for at least 18 months.

### Final sequence — due diligence
A mini-audit, using the same finding machinery as
[certifications-audits.md](certifications-audits.md) §3, and it reads the
facility's actual state.

```
offer_final = offer_base
            * (1 - DD_DISCOUNT_K * Σ finding_weights)
            * (1 + REP_PREMIUM * (Reputation - 85) / 15)
```

**The readiness consultancy does not work on due diligence.** They send their own
people and they look behind the racks. A group that has run a tidy building for
three years is paid for it here and nowhere else in the game.

### Epilogue
The acquirer's own press release, verbatim, in the acquirer's voice, followed by
the paper reporting what actually happened over the next eighteen months:

- The site triples. The T4 connection the group could never get is granted in
  four months, because the applicant is different.
- **Local staff are cut to a service contract.** The employment number the group
  spent three years buying goes to a fraction of itself.
- **The Program benefit is discontinued** at the end of the fiscal year, in a
  footnote.
- **The pool heat contract is not renewed.** The paper gets a quote about
  "portfolio-level thermal strategy".
- Everything the group did to placate the town is dropped, because it was never
  load-bearing for anyone but them.

The players are rich and the town is worse off than under the people who were at
least *there*. **The joke is that the operator was the moderate.**

### Why this is not obviously correct
- It ends the run immediately. No T4, no Exit C, no seeing what the site becomes
  under your hands.
- It is the only ending whose epilogue is actively unpleasant to read, and the
  design should let it be.
- Meta-progression ([meta-progression.md](meta-progression.md)) carries **client
  reputation** strongly from B and **town memory** badly, so the next run starts
  with better contracts and a colder town — which is a real trade, not a penalty.

---

## 3. Exit C — Unbounded growth

You become the thing the game is satirising.

### Trigger
```
grid tier == T4 (50,000 kW)
survived N_STABLE = 12 consecutive months at T4 without an injunction
a referendum has been held and WON
```

Winning a referendum is required. You cannot arrive here by suppression alone —
the town has to have voted for it, and §3's epilogue is about what that vote was
actually buying.

### Final sequence
There is no terminating event. At the trigger the game offers the epilogue
whenever the group chooses to stop, and otherwise continues indefinitely. Exit C
is a **state**, not a cutscene.

At T4 the site draws 63% of the regional feeder, puts ~48% on every household
bill ([power.md](power.md) §3), and — if it is still on evaporative cooling —
consumes more water than the town does.

### Epilogue
The town's identity has changed and the paper reports it without editorialising:

- Largest employer in the district. The number is genuinely large and genuinely
  good for the people in it.
- The local paper discloses, in six-point type, that it receives a Program
  benefit.
- Two of the residents who signed the first petition now work on site. One is
  quoted, accurately, and is not embarrassed.
- The GNI is high. It is high **because you employ everyone**, and the epilogue
  states the local-employment share and the GNI number next to each other and
  makes no comment.
- The pool is warm, and has been for four years, and the article mentions that
  the contract is renewed annually.

**This is the most uncomfortable of the three and it is the success ending.** It
should not feel like a reward and it must not feel like a punishment.

---

## 4. Balancing the three

The design risk is that players optimise for cash and always take B. Four things
push against it:

1. **B pays most and ends soonest.** It is the only exit that forecloses the
   others, and the offer arrives around year 3–4 when the group is mid-plan.
2. **C is the only one where the site keeps existing.** Groups get attached to
   buildings they routed by hand.
3. **A pays nothing and is the only exit where the town wins**, and the epilogue
   says so plainly, once, without moralising.
4. **Meta-progression carries differently from each** (§5), so no exit
   strictly dominates the next run.

| | Cash | Town outcome | Carries strongly | Carries badly |
|---|---|---|---|---|
| **A** — referendum | None | **Best** | Town memory, blueprints | Client reputation |
| **B** — acquisition | **Highest** | **Worst** | Client reputation, hardware tiers | Town memory |
| **C** — growth | High, ongoing | Ambiguous | Everything, moderately | Nothing |

Note that A carries **town memory** well — a town that removed the last operator
and was proved right starts the next one warily but fairly, whereas a town
abandoned to a hyperscaler starts the next operator cold
([meta-progression.md](meta-progression.md) §3).

---

## 5. Failure modes

| Failure | Why |
|---|---|
| B becomes the default | The 60-day expiry and the immediate run-end are the only brakes. If playtesting shows everyone takes it, lengthen the T4 path rather than nerfing the offer. |
| C never happens | T4 requires GNI ≥ 75 plus a won referendum. If nobody reaches it, the referendum model in [sentiment.md](sentiment.md) §7 is too harsh, not the endgame. |
| A feels like losing | It is a loss, commercially. The epilogue must not console the player and must not scold them. This is the hardest writing in the game. |
| The wind-down is tedious | 90 playable days of decommissioning could be dull. Mitigation: the market-timing decision on the fleet sale is a real one, and the staff question is a real one. If those two are not enough, shorten it to 45. |

---

## 6. How to abuse this against teammates

| Abuse | Legitimate cover |
|---|---|
| Accept the acquisition offer alone at the office desk | It is a physical letter and a signature, and it expires |
| Dump the whole fleet on day 1 of a wind-down | Somebody has to raise cash before the deadline |
| Dismiss all local staff in the first week of wind-down | Payroll during a shutdown is pure loss |
| Let the acquisition offer expire without mentioning it | Post arrives at the office; not everyone reads the post |
| Trigger a referendum deliberately by cranking hazards | Every individual action is defensible |

**Accepting the acquisition is a single unguarded physical action that ends the
session for four other people.** It is the largest-consequence single interaction
in the design, it sits behind no confirmation, and per
[open-questions.md](../open-questions.md) Q4 the two-key interlock does **not**
apply to it — the interlock guards *spending*, and this is receiving.

I think that is correct and I am not certain. Logged as
[open-questions.md](../open-questions.md) Q25.

---

## 7. Coupling

| To | Passes | Unit |
|---|---|---|
| [sentiment.md](sentiment.md) | Referendum result, GNI thresholds | 0–100 |
| [compute-contracts.md](compute-contracts.md) | Reputation gate, contract termination penalties | 0–100, EUR |
| [power.md](power.md) | Grid tier gate | tier |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Fleet salvage into the market | EUR |
| [certifications-audits.md](certifications-audits.md) | Due-diligence findings | discount |
| [staff-shifts.md](staff-shifts.md) | Dismissals during wind-down | GNI |
| [meta-progression.md](meta-progression.md) | Which exit, and its carry profile | — |
