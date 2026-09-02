# Open questions

Everything I am unsure about, plus every decision I made that the brief did not
specify. Options, then my recommendation. Ordered by what a wrong answer costs.

---

## Q1 — How shallow must hand-routing be to stay off the competitors' ground?

The brief requires hand-routed power, cooling and network runs *and* requires
that we not duplicate what the four shipped datacenter sims already do well —
and cabling is exactly what they do well.

My resolution ([coop-griefing.md](systems/coop-griefing.md) §5): routing models
**only** run length (a small power loss) and crossings (longer fault repair). No
patch panels, no port assignment, no topology, no tracing minigame.

| Option | Consequence |
|---|---|
| **A. Two-variable model** (current) | Serves the griefing wedge — sloppy work is inherited — without competing on their ground. Risks feeling thin to players who came for cable management. |
| B. Full routing depth | Directly competes with three games that already do it, on a part-time budget. |
| C. Cut routing entirely | Cleanest, and it is #2 on the [scope.md](scope.md) cut list anyway. |

**Recommendation: A, and be ready to drop to C.** But this is a positioning
question I cannot settle from the design side — it depends on how much of our
audience is coming *from* those four games expecting cabling.

---

## Q2 — Is power a *cost* problem or a *capacity* problem?

I decided **capacity**, and it shapes every number in
[economy.md](economy.md).

With hardware debt-financed, debt service is ~45% of revenue and grid power is
~13%. Power is not the dominant cost, but the grid `CAP` is an absolute wall.

| Option | Consequence |
|---|---|
| **A. Capacity-dominant** (current) | Realistic, and the brief's own loop is gated on the *permit*, not the bill. But a group can be sloppy about efficiency and still profit. |
| B. Cost-dominant | Cut `RATE_INFERENCE` ~60% so power is 35–40% of revenue. Every kW matters — but the business becomes marginal and the training cliff becomes instantly fatal rather than instructive. |
| C. Hybrid with a power-intensity levy | Invented mechanism, no anchor, smells like a designer patching a model. |

**Recommendation: A.** If playtests say efficiency feels weightless, pull
`RATE_INFERENCE`, not a new mechanism.

---

## Q3 — Should the grey-market attention meter exist?

The brief requires grey-market clients. I designed them and **recommend cutting
the attention meter.**

Grey clients are the only revenue lever that does not route through heat, water,
noise or price. They are a parallel scalar with their own event chain that
duplicates the "cheap now, expensive later" shape suppression already carries —
and this design already tracks GNI, Reputation *and* per-player Standing.

| Option | Consequence |
|---|---|
| **A. Cut the meter, keep grey contracts** as a straight Reputation trade | Same fiction, no fourth meter, and they touch a system that matters |
| B. Keep as designed | ~2–3 evening-weeks: meter, four event types, audit state, fine calculation, UI |
| C. Cut grey entirely | Loses a good joke and a genuine desperate-money option |

**Recommendation: A.** This is my answer to "the one system you would cut".

---

## Q4 — The two-key interlock is my invention

Not in the brief. Orders above `TWO_KEY_THRESHOLD` = €500,000 need two players at
two interlocks within 8 seconds
([coop-griefing.md](systems/coop-griefing.md) §2).

**Why I added it:** without it, one player can commit €11,000,000 in a single
interaction and the economy in [economy.md](economy.md) stops being a design.
It is not a permission system — it is a physical control, any two players can do
it including two who intend harm, and in the fiction it is an ordinary corporate
spending authorisation.

**But it is friction on shared money, and the brief's principle is that anyone
can order anything.** I may have softened something you wanted hard.

| Option | Consequence |
|---|---|
| **A. Keep it** | Protects the economy; slightly dilutes "no permissions" |
| B. Remove it | Purest expression of the brief. One player can end the session's finances in one click. |
| C. Keep it but raise the threshold to €2M | Only the grid tiers are gated; everything else is free |

**Recommendation: A or C.** Flagging it prominently because it is the one place I
traded a stated principle for economic coherence.

**Note it does not apply to the contract terminal.** Signing a ruinous training
contract is free, unilateral and irreversible, which is deliberate — the grief
there creates work rather than destroying money.

---

## Q5 — Time control: idle-gating, not majority vote

The brief asked me to decide and justify. I chose **fast-forward only when every
player is idle**, holder named on screen, majority override after 180 s
([coop-griefing.md](systems/coop-griefing.md) §8).

Justification: a vote turns time into a political negotiation every few minutes;
a majority can vote *past* a minority's crisis; idle-gating needs no UI and no
decision; and holding time becomes a physical veto consistent with everything
else in the game.

**The known weakness:** a player jiggling a valve holds the clock indefinitely.
I handle it socially (the holder is named and located) plus a 180 s override.
Whether that is enough is a prototyping question — see Q15.

---

## Q6 — The cooling/throttle loop is resolved in one pass

Cooling capacity depends on heat, heat depends on throttling, throttling depends
on cooling capacity. Single pass; surplus cooling in a throttled hour is unused.

| Option | Consequence |
|---|---|
| **A. Single pass** (current) | Deterministic, fast, slightly pessimistic — the site throttles a little harder than physics requires |
| B. Iterate to convergence | Physically right; risks oscillation and costs determinism unless the iteration count is fixed |
| C. Two fixed passes | Most of the accuracy, keeps determinism, doubles cooling cost per tick |

**Recommendation: A** for the prototype. Revisit only if players notice the site
throttling when the readouts say it should not.

---

## Q7 — Node power is linear in utilisation

`P_node(u) = P_idle + (P_peak − P_idle) × u`. Real servers are mildly convex.

**Recommendation:** keep linear. The error is a few percent and it is invisible
next to the `EVAP_COP` curve, which is invented anyway.

---

## Q8 — The host has a latency advantage

Host-authoritative with no rollback means the host wins races on physical
controls. **In a game about griefing your friends, that is a fairness issue**, and
I am choosing to accept it rather than build rollback netcode.

| Option | Consequence |
|---|---|
| **A. Accept it** | Free. The host is slightly better at grabbing the diesel lever. |
| B. Rollback / lag compensation on interactions | Weeks of work, and it fights the tick-boundary intent model |
| C. Artificial host input delay | Cheap, crude, makes the host's own game feel worse |

**Recommendation: A**, and say so in the fiction — nothing, or a line about
seniority. Revisit only if playtesters actually complain.

---

## Q9 — No host migration

If the host drops, the session ends and the save is written. Migration is weeks
of work protecting against an event a friends-group session tolerates badly
anyway.

**Recommendation: no migration.** The mitigation is that the host should be
whoever has the stable connection.

---

## Q10 — `SLA_PENALTY_CAP` is my addition

I added it while working through [economy.md](economy.md) §5, because the
uncapped formula produced a penalty larger than the contract's value at a 10 pp
shortfall. Real contracts cap remedies at 10–100% of period fees.

**Recommendation:** keep the cap, tune the value. Without it a group that
delivered 89% of a contract can end the month with negative revenue on it, which
reads as a bug even when it is not.

---

## Q11 — No grid export

Surplus renewables charge the battery, then curtail. Selling power back would be
realistic and would make renewables more attractive — and it weakens the core
tension, because a site that can sell power back is less obviously in competition
with the town for it.

**Recommendation: no export** for now. Flagged because it is a real
simplification, not an oversight.

---

## Q12 — Two node types only, no hardware refresh

`GPU_NODE_LIFE` = 4 years means a 5-year campaign should involve replacing the
fleet, which is both a huge cash event and plausible drama. I left it out.

**Recommendation:** two types for the prototype; **one** refresh event at year 4
if campaigns run that long, rather than a hardware-generation system.

---

## Q13 — This design needs a fifth and sixth assembly

[architecture.md](architecture.md) §2 proposes `Game.Sim`
(`noEngineReferences: true`) and `Game.Sim.Tests`, beyond the four already
committed in `Assets/Scripts/README.md`.

That is a change to a structure already approved, so I have not made it. **It is
the most important technical recommendation in these documents** — the balance
harness is impossible without it, and 40% of this design's constants cannot be
tuned without that harness.

**Recommendation:** approve before any code is written. Retrofitting
engine-independence later is a rewrite.

---

## Q14 — Team size was never specified

`<n>` was a placeholder in the previous brief and is absent from this one. I
assumed **2–4 people, part-time, evenings**, and
[scope.md](scope.md) and [risks.md](risks.md) are written against that.

At 1–2 people the MVP cut list roughly doubles and networking alone (8–12
evening-weeks) becomes the whole first year. At 6+ the full design is reachable.

**Recommendation:** tell me the number and I will re-scope §3 of
[scope.md](scope.md).

---

## Q15 — Smaller decisions I made that you did not specify

| Decision | Where | Overrule if |
|---|---|---|
| Hardware debt-financed at 9%/4y rather than bought outright | [economy.md](economy.md) §1 | You want a slower, smaller-scale game |
| `CHECKPOINT_INTERVAL_DEFAULT` shortened 6 h → 1 h **for co-op reasons** | [compute-contracts.md](systems/compute-contracts.md) §2.2 | You want griefing to be able to cost a week |
| `SUPPRESSION_ROOM_SCOPE` capped at one room, not the site | [coop-griefing.md](systems/coop-griefing.md) §9 | You want it session-ending |
| Uninstalled stock in the yard adds `N_visual` | [nuisance.md](systems/nuisance.md) §5 | — |
| Dismissal costs 2.4× what hiring gained | [sentiment.md](systems/sentiment.md) §4.3 | Too punitive |
| Hiring frozen at Protest, removing the best recovery lever when most needed | [sentiment.md](systems/sentiment.md) §3 | Too punitive |
| The dismissed player returns as a consultant rather than leaving | [coop-griefing.md](systems/coop-griefing.md) §4 | — |
| `p_baseline` drifts with national trend, so the operator is not blamed for inflation | [nuisance.md](systems/nuisance.md) §4 | — |
| The water indicator saturates before maximum site scale | [nuisance.md](systems/nuisance.md) §3 | — |
| Contracts have **no** direct GNI coupling | [compute-contracts.md](systems/compute-contracts.md) §4 | You want the town to object to *who* you serve |
| Residents are never seen close up | [art-bible.md](art-bible.md) §3 | — |
| No kick vote and no join approval | [multiplayer.md](multiplayer.md) §6 | — |
| Fixed site plan, not free placement | [GDD.md](GDD.md) §11 | You want a builder |
| Nuisance + GNI merged; Seasons is a data source; co-op is an interaction layer, not a system | [GDD.md](GDD.md) §12 | — |
| Program Blue as a readable index of fortification | [art-bible.md](art-bible.md) §1 | — |

---

## Q16 — What cannot be resolved on paper

Prototype questions, logged rather than guessed.

1. **Is one griefer session-ending?** The blast-radius mitigations in
   [coop-griefing.md](systems/coop-griefing.md) §9 are guesses. The
   `grief_rate` axis in the balance harness
   ([architecture.md](architecture.md) §7) answers it quantitatively; a room of
   three people answers it truthfully.
2. **Does the blame system produce comedy or resentment?** The bulletin's
   responsible-employee field is the design's biggest bet. It could be the best
   thing in the game or it could end friendships in a way that is not fun. There
   is no way to know without three real people and a bad July.
3. **Is idle-gated time-holding abused into unplayability?** (Q5.)
4. **Is the two-lag GNI response readable, or just frustrating?** The
   `GNI_target` ghost marker is the mitigation; whether it suffices is unknowable
   on paper.
5. **Is throttling legible as a state?** Players must distinguish "cooling is
   short", "power is capped", "hardware is damaged" and "the SLA is already
   breached" at a glance. On paper that is four numbers; in practice it may be
   one confusing red.
6. **Is the wind/diesel optimisation discovered, or does it need signposting?**
   The gag only lands if players work it out. If nobody does, it has to be
   taught, and teaching it destroys it.
7. **Does the unlabelled Program board read as depth or as a missing feature?**
   The single best idea in this design is also the one most likely to be
   mistaken for an oversight and "fixed" by a well-meaning UI pass.

---

# Added by the second-round systems

Q17–Q26 arise from the nine systems added in
[construction-routing.md](systems/construction-routing.md),
[hardware-lifecycle.md](systems/hardware-lifecycle.md),
[incidents.md](systems/incidents.md),
[workplace-accidents.md](systems/workplace-accidents.md),
[staff-shifts.md](systems/staff-shifts.md),
[certifications-audits.md](systems/certifications-audits.md),
[network.md](systems/network.md),
[endgame.md](systems/endgame.md) and
[meta-progression.md](systems/meta-progression.md).

---

## Q17 — Routing depth reverses a committed positioning decision

**This is now the most consequential open question in the project**, replacing Q1.

[coop-griefing.md](systems/coop-griefing.md) §5 caps routing at two variables
*specifically* so we do not compete with the four shipped datacenter sims on
cabling, which is what they do well. [scope.md](scope.md) lists hand-routed runs
as cut **#2**. The second-round brief makes routing system **#1**, with capacity,
derating, validation, patch panels and fire compartments.

That is a reversal, not a refinement.

| Option | Consequence |
|---|---|
| **A. Depth only where it feeds another system** (what I wrote) | Ampacity, derating, penetrations, water paths, airflow and label truth all exist because a leak needs somewhere to run and a fire needs a way through a wall. No topology, no port config, no tracing minigame. The wedge survives; the build cost roughly triples versus the two-variable model. |
| B. Full routing depth | Competes head-on with three games that already do it, on a part-time budget. |
| C. Keep the two-variable model | Loses the water, fire and airflow chains, which are now load-bearing for [incidents.md](systems/incidents.md). |

**Recommendation: A**, with a standing test for every future addition — *does it
change what an incident does?* If not, it does not go in.

This is a positioning call I cannot make from the design side. It depends on how
much of the audience arrives *from* those four games expecting cable management,
and whether `scope.md`'s cut list should be re-ordered.

---

## Q18 — Staff NPCs conflict with two committed decisions

[GDD.md](GDD.md) §11 states: *"No staff simulation beyond headcount and
locality."* [risks.md](risks.md) §2 identifies the biggest pacing risk as idle
players with nothing legitimate to do, and prescribes continuous small physical
maintenance as the fix.

NPCs doing that maintenance remove the fix.

**My resolution** ([staff-shifts.md](systems/staff-shifts.md) §0): staff are
aggregate per-shift *coverage*, never pathfinding agents, and they only perform
work explicitly moved to the DELEGATED column of a physical job board. **The
default is undelegated.** Delegating costs payroll *and* costs the group
something to do, and that second cost is meant to be felt.

**Recommendation:** accept, and treat any proposal for per-employee scheduling or
visible NPC pathing as evidence this system has failed.

---

## Q19 — `node_efficiency` changes what `kWh_IT` means

Mixed-age fleets require a per-node efficiency term
([hardware-lifecycle.md](systems/hardware-lifecycle.md) §0). Contracts become
payable per **reference** kWh_IT — the output of a current-generation node.

```
delivered_reference_kWh = P_node(u) * node_efficiency * dt
```

All existing formulas hold with `node_efficiency = 1.0` and no constant in
[balance-constants.md](balance-constants.md) changes. But it is a semantic change
to [compute-contracts.md](systems/compute-contracts.md) §2, and that document
should be amended rather than left to disagree with this one.

**Recommendation:** accept. Without it, a four-year-old node is economically
identical to a new one, there is no reason to ever replace anything, and the
entire hardware lifecycle system has no purpose.

---

## Q20 — Accident statistics feed `G`, not a sixth indicator

`TRIR` enters GNI through the goodwill term:

```
G_safety = -SAFETY_K * max(0, TRIR - TRIR_BASELINE)
```

**Not** as a sixth Program indicator. The five in
[nuisance.md](systems/nuisance.md) are physical harms to the town and their
weights sum to 1.0; a sixth would rebalance all five and break the design's claim
that every grievance is physical.

**Recommendation:** accept. Flagged because "the town is upset about your safety
record" is arguably a legitimate sixth grievance and a reasonable person could
disagree.

---

## Q21 — Certifications duplicate Reputation as a contract gate

Both gate contracts. My defence
([certifications-audits.md](systems/certifications-audits.md) §0): Reputation is
**behavioural** and gates contract *size*; certification is **structural** and
gates contract *class*. They are orthogonal — a site can have a spotless delivery
record and no security certification, or three certifications and a ruined
record.

| Option | Consequence |
|---|---|
| **A. Keep both as orthogonal axes** | Defensible, but players may never experience them as two different things |
| B. Fold class-gating into Reputation, keep the audit as a scheduled cleanup event | Loses little. The audit is the good part; the certification is the wrapper around it. |

**Recommendation: A**, watched in playtest. If players cannot articulate the
difference, take B — the 14-day audit crunch survives either way.

---

## Q22 — Whose meta-progression, in a co-op game?

Not specified, and it is a real question at 2–5 players.

**Recommendation** ([meta-progression.md](systems/meta-progression.md) §4): the
**host's** carry applies to the session — blueprints, client reputation, hardware
availability and town memory all come from the host's save, because it is the
host's town. Blueprints are shareable files, so a non-host can contribute one
before the run starts. **Standing does not carry for anyone**, which sidesteps
whether a player who was scapegoated last session should start this one at 36.

---

## Q23 — Is the mandatory accident bulletin collective punishment?

One player dies through their own carelessness and the *group's* bulletin
credibility takes `CREDIBILITY_LOSS_PER_USE`
([workplace-accidents.md](systems/workplace-accidents.md) §2).

That is satirically exact — the company publishes, not the person — and it is the
same individual-action/collective-cost asymmetry already flagged in
[risks.md](risks.md) §4.

| Option | Consequence |
|---|---|
| **A. Collective, as written** | Accurate, and it makes safety a group concern |
| B. Also cost the deceased Standing | Turns dying into a performance failure, which is the joke inverted |
| C. Free bulletin with no credibility cost | Removes the punishment entirely, and "the punishment is the press release" is the brief's own line |

**Recommendation: A.**

---

## Q24 — Is the hydrogen vent fan too cruel?

Turning off a code-mandated safety system **genuinely and immediately improves
the Good Neighbor Index**, because the fan is noisy and noise is an indicator
([hazard-inventory.md](systems/hazard-inventory.md) §7). Then the battery room
deflagrates.

It is my favourite thing in this round and it may be a step too far: it
weaponises the game's own core metric against the player, and it requires no lie
at any point — the ledger entry "turned off vent fan, 23:14" is true, complete
and useless.

**Recommendation:** keep it, and put it in the first playtest specifically to find
out. It is the clearest candidate for "that felt like a gotcha".

---

## Q25 — The acquisition letter is unguarded and ends everyone's session

Accepting Exit B is a single physical signature at the office desk that
terminates the run for up to four other people
([endgame.md](systems/endgame.md) §6).

The two-key interlock (Q4) does **not** apply, because the interlock guards
*spending* and this is receiving. That is internally consistent and possibly
wrong.

| Option | Consequence |
|---|---|
| A. Unguarded, as written | Consistent with "no consequential action behind a confirmation". The largest single-interaction consequence in the design. |
| B. Extend the two-key interlock to acceptance | Guards the one action that ends the session, at the cost of a special case |
| **C. A 24 in-game-hour delay between signature and completion** | Keeps the no-dialog rule, makes the act visible on the manifest board, and gives the group one in-game day to have the argument |

**Recommendation: C.** I did not write it that way because it was not specified.

---

## Q26 — `balance-constants.md` now needs a consolidation pass

This round introduces roughly **70 new constants** — derating, thermal, wear,
dust, failure distribution, market, TRIR, coverage, fatigue, audit weights,
network, endgame and carry constants — and they currently live inside their
system documents.

That breaks [balance-constants.md](balance-constants.md)'s own premise that every
number lives in one place, and it breaks the single `balance.json` plan in
[architecture.md](architecture.md) §5.

I did not do the merge because the brief scoped this round to `docs/systems/`
plus the two named files plus these two updates.

**Recommendation:** consolidate before any code, marking confidence honestly. My
estimate is that **fewer than fifteen of the seventy have real-world anchors** —
the bundling derate curve, the 10 °C Arrhenius rule, TRIR's 200,000-hour basis,
the 25% night-shift premium, the availability-tier definitions and the fibre
availability figures. The rest are invented, and the co-op and hazard constants
are invented with no referent at all.

---

## Q27 — Placards are blank

[art-bible.md](art-bible.md) §3 asks for placards that are "readable but held
by shapes". The presence layer keeps the figures at the distance §3 itself
demands — 14 m and more for walkers, 9 m for anyone standing at the gate —
and at that range legible text on a 0.6 m board is either a smear of vertex
colour or an invitation to walk up and read it, which §3 forbids ("never
approachable"). The figures also step away when the player closes in, so a
readable slogan would only ever be read on the run.

The implementation therefore ships **blank boards** (Render on an Earth stick,
two in three protesters carry one). The joke stays on the operator: whatever
the placards say, the gate is not going to tell you.

Options if you want the text back:

| Option | Trade-off |
|---|---|
| **A. Blank boards** (current) | Reads as protest at any distance; says nothing |
| **B. Three or four stock slogans as vertex-coloured block glyphs** | Legible only at ~5 m, which the retreat rule never allows; costs a glyph atlas |
| **C. Slogans surface through the news ticker instead** | Keeps the distance rule; the text arrives through the Program's own channel, which is the tone the bible asks for |

**Decision pending: blank placards for now.** C is the cheapest honest upgrade.
