# Risks

Where this gets boring, where it breaks, and where it exceeds a part-time team of
2–4 people working evenings. Team size is an assumption — see
[open-questions.md](open-questions.md) Q14.

---

## 1. The largest risk: networking is 8–12 evening-weeks before anything is fun

Co-op is the wedge. It is also the single biggest cost in
[scope.md](scope.md) §3 and it produces **no playable value until it is nearly
done**. A team that starts with networking spends two months with nothing to show
each other; a team that starts with the simulation builds something fun and then
discovers that making it multiplayer touches every interaction they wrote.

**Mitigation, and it is the whole project plan:**

1. Build `Game.Sim` engine-independent
   ([architecture.md](architecture.md) §1) — it is unaffected by networking
   either way.
2. Build the **text-only harness** (§2) and answer the pacing question.
3. Build the physical control layer **single-player first**, with intents already
   going through a buffer at tick boundaries. The intent model is the networking
   model; single-player is the one-client case.
4. Add host authority last. If the intent boundary is respected from day one,
   this is replication work rather than rework.

If the intent boundary is *not* respected from day one — if one interaction
writes directly to sim state because it was quicker — this risk becomes a rewrite
and the project dies of it.

---

## 2. Where it gets boring

In a single-player version of this design the answer would be the mid-game
plateau: once the cooling mix works and GNI is above 70, the correct action every
hour is nothing, for possibly two in-game years.

**Co-op largely fixes that**, and that is the strongest argument for this brief
over the single-player one. Four people in a shared space generate incident
continuously; the simulation does not have to.

**What is boring instead is the third-to-fifth player when the site is healthy.**
The failure mode has moved from pacing to staffing. With two players there is
always something to do. With five and a stable site, two of them have no
legitimate work, and a design whose entire premise is that idle hands reach for
the diesel lever will get exactly that — which is funny once and corrosive by the
fourth time.

Three responses, in order of preference:

1. **Continuous small physical maintenance.** Filter changes, coolant top-ups,
   drive replacements, calibration, delivery unpacking. Genuine low-stakes work
   that gives idle players something legitimate to do *and* something to do
   badly. This is cheap and it is the right answer.
2. **Auto-pause on state changes that matter** — an indicator crosses a
   threshold, θ drops below 1, a contract offer arrives, a permit reaches
   re-check. Turns the game into a sequence of decision points.
3. Cap at 4 players (already #3 on the [scope.md](scope.md) cut list).

**This needs prototyping before the UI is built**, and it is cheap to test.

---

## 3. Where it breaks: the double-lag legibility problem

GNI has **two lags stacked** — indicator memories of 30–120 days, and a meter
chasing its target with a ~10-day time constant. A measure bought today shows
nothing for a fortnight and reaches full effect in a season.

The design's central lesson is "genuine fixes beat suppression". The players'
lived experience is "the expensive slow thing did nothing and the cheap instant
thing worked". **The mechanic and the feedback point in opposite directions**, and
the only thing between them is a UI affordance — the `GNI_target` ghost marker.

If that is insufficient, players rationally learn the wrong lesson and the
game's thesis inverts.

**This risk is worse in co-op**, because a group converges on a shared wrong
belief faster than an individual does, and then defends it.

Fallbacks if it fails: asymmetric response (goodwill lands faster than
grievance — false to life but readable), per-indicator projected trajectories, or
a shorter memory that costs the "bad summer remembered in November" effect that
makes the permit re-check meaningful.

---

## 4. Where the co-op design conflicts with the economy design

**They operate on different timescales and that mismatch is real.** The economy
moves in months and millions; griefing resolves in seconds. Four specific
collisions:

| Conflict | Detail |
|---|---|
| **Instant actions vs. capital costs** | One fire-suppression discharge destroys €396,000 of drives in a second, against a monthly margin of ~€1M. One grief = a third of a good month. |
| **Shared pot, no permissions, big numbers** | The economy assumes deliberate capital allocation. The co-op design assumes anyone can spend anything. The cash reserve is managed by the least careful player. |
| **Collective penalty, individual action** | SLA penalties hit the shared balance; the ledger only records who. Griefing has no personal cost except Standing, which is soft. Satirically correct, mechanically asymmetric. |
| **The training cliff** | 90% loss on a missed deadline was designed to force overbuilding. It is also the juiciest griefing target in the game: one rack switch at the wrong hour. |

**Three mitigations are already built into the constants**, and they are the
reason those numbers differ from a single-player version:

1. `CHECKPOINT_INTERVAL_DEFAULT` shortened **6 h → 1 h**.
2. `SUPPRESSION_ROOM_SCOPE` capped at **one room**, not the site.
3. The **two-key interlock** above €500,000.

All three reduce the *blast radius* without reducing the action. The lever still
works, the scream still happens, the session survives.

**The one I am least sure about is the two-key interlock**, because it is
friction on shared money and the brief's principle is that anyone can order
anything. Flagged as [open-questions.md](open-questions.md) Q4.

**And one conflict I have not resolved:** the contract terminal is deliberately
*not* gated, so signing a ruinous training contract remains unilateral and
irreversible. That is correct — the grief creates work rather than destroying
money — but it means a determined player can commit the group to a deadline every
few in-game weeks, indefinitely, and nothing in the design stops them. I think
that is fine and funny. I am not certain.

---

## 5. Where it breaks: the blame system could produce resentment instead of comedy

The responsible-employee field is the design's biggest bet. It is also the one
mechanic that reaches outside the fiction and operates on the actual people in
the room.

The intended experience is *recognition* — the sick laugh of watching a corporate
reflex work exactly as it does in life. The failure mode is a group where one
person is named every time, their Standing floors, they get "realigned", and they
stop enjoying the session an hour before anyone notices.

Design features that already push against this:

- `NAMING_DECAY` = 0.55 means repeated naming stops paying, so scapegoating one
  person has diminishing returns *mechanically*.
- The fact-check punishes false naming hard.
- The ledger is a chronological list, never a scoreboard or a ranking.
- The consultant mechanic makes dismissal a cost rather than an exit.

**None of that guarantees anything**, because the problem is social. This is
prototype question #2 in [open-questions.md](open-questions.md) Q16 and it should
be tested early with people who will tell you the truth.

---

## 6. Where the tone breaks

The tone rule — the joke is always on the operator, residents are always
factually correct — is one careless string away from being violated, and the
violation will not be caught in code review unless somebody is looking for it.

The dangerous surfaces are the generated news headlines, the petition copy and
the protest flavour text. It is very easy to write a line where a resident sounds
shrill, and one such line reframes the game as being about unreasonable NIMBYs.

**Mitigations, all cheap:**

- The writing rule stated in `CONTRIBUTING.md` and applied to every string.
- Residents seen only at distance ([art-bible.md](art-bible.md) §3), which
  removes the surface where caricature usually creeps in.
- Protest chants deliberately muffled and never intelligible.

Cheap to mitigate, expensive to discover late — by then there are several hundred
strings.

---

## 7. Where the balance surface is too large to tune by hand

~70 constants, 40% invented, with feedback loops on 30-to-120-day timescales,
**plus a new axis: how much griefing the session contains.** Nobody can tune this
by playing. A session is 90+ minutes for one in-game year and produces one sample
from a stochastic weather process crossed with a stochastic social process.

**Mitigation: build the balance harness early**
([architecture.md](architecture.md) §7), including the `grief_rate` axis. This is
only possible if `Game.Sim` is engine-independent
([open-questions.md](open-questions.md) Q13). Without that architectural
decision this risk has no mitigation and the game ships badly balanced.

---

## 8. Where the art constraints hurt

Detailed in [art-bible.md](art-bible.md); the two that are project risks:

- **The diesel plume carries the game's atmosphere.** With flat shading, no
  normal maps and one material, there is nothing else doing it. It is also the
  image of the best mechanic in the design. If the plume is not good, the game
  looks like a diagram. **Prototype it first**, before the mesh kit.
- **No faces means the funniest moment cannot be shown.** In a co-op griefing
  game the payoff is someone's expression when they realise, and we have
  capsules. That reaction must land entirely in the news ticker, the bulletin
  copy and the audio — which is why audio gets 25% of the art budget and why
  "listen from the fence line" is worth building properly.

---

## 9. Scope, honestly

MVP **73–110 evening-weeks**; full design **104–158**. At 3 people × 8 h/week
that is **6–9 months to MVP** and 18–24 months to something shippable.

The three line items that will actually kill the schedule:

1. **Networking** (§1) — unavoidable, front-loaded, invisible until done.
2. **UI, 15–25 evening-weeks** — five indicator meters, per-contract SLA
   tracking, wind forecast, financial history, permit pipeline, the Program
   board, the ledger terminal, the news ticker. It is 20–30% of the project and
   it looks like 5%.
3. **Playtesting, 10–20 weeks** — open-ended by nature, and this design has two
   questions (§2, §5) that *only* playtesting can answer.

**Every "small addition" to this design is two to four evening-weeks.** The scope
in [GDD.md](GDD.md) §11 has to hold absolutely.

---

## 10. Things that are fine

Stated so they do not attract effort they do not need.

- **Simulation performance.** A few thousand nodes updated hourly is arithmetic.
  The 10,000-ticks-in-100 ms target has enormous headroom.
- **Bandwidth.** `SimState` deltas are hundreds of bytes at 10 Hz.
- **The 3D.** A fixed plot, 15 meshes, one material.
- **Determinism.** Easy if `Game.Sim` never touches UnityEngine, and nearly
  impossible if it does.
- **The physics.** Every thermal and electrical relationship here is first-order
  and well-anchored. Heat equals power. COP is a division. Honest without being
  complicated, which is the right trade for this project.

---

## 11. The single largest compound risk

**That §2 (idle players with nothing legitimate to do) and §5 (the blame system
producing resentment) turn out to be the same problem, and it is discovered after
the networking in §1 is built.**

An idle player reaches for the diesel lever. Someone names them in a bulletin.
Their Standing floors. They are realigned. They are now a consultant with nothing
to do and a grudge. Every one of those steps is working exactly as designed, and
the session is no longer fun.

**Mitigation: the text-only harness, played by three real people, before any
networking or UI exists.** It is a console program plus a shared screen. It costs
perhaps four evenings. It answers §2 and §5 directly, and it produces the balance
harness from §7 almost for free.

If the loop is not interesting in a console window with three people arguing over
one keyboard, no amount of networking, UI or VFX will fix it — and if it is,
everything after that is presentation.

---

# Added by the second-round systems

---

## 12. This round roughly doubles the project

Nine new systems, seventeen hazards, twelve incident accumulators, six escalation
chains, three endings.

| Component | Evening-weeks |
|---|---|
| Construction, rooms, compartments, grid placement | 8–12 |
| Routing: runs, ampacity, derating, water paths, airflow, labels | **10–16** |
| Hardware lifecycle: wear, dust, failure distribution, RMA, market | 6–9 |
| Incident engine: accumulators, stages, detection, six chains | 8–12 |
| Workplace accidents: 17 hazards, physics, death and respawn | **10–15** |
| Staff: roster, coverage, fatigue, job cards | 4–6 |
| Certifications and audits | 4–6 |
| Network | 3–4 |
| Endgame: three exits, wind-down, three epilogues | 5–8 |
| Meta-progression | 2–3 |
| Additional UI for all of the above | **10–16** |
| Additional balance surface (~70 constants) | 4–8 |
| **Subtotal** | **74–115** |

Against [scope.md](scope.md)'s original 73–110 for the MVP and 104–158 for the
full game, **the full design is now 180–270 evening-weeks** — at 3 people × 8
h/week, three to five years.

**That is not buildable part-time as specified.** The MVP definition in
[scope.md](scope.md) §2 becomes the *only* realistic target, and it needs
updating to say which of these nine are in it. My recommendation: incidents
(three of eight), six hazards, routing at reduced depth, no staff, no
certifications, no meta-progression, one ending.

---

## 13. Observability — the real cost of "no random events"

The rule that every incident traces to a neglected variable is the best decision
in the design and it has a price nobody has paid yet.

There are now **twelve hazard accumulators** (fire, leak, grid, transformer
moisture, battery wear, dust, DDoS exposure, compartment integrity, hydrogen,
recirculation, filter load, tray thermal) plus five Program indicator memories,
plus `effective_age` per node, plus per-shift coverage and fatigue.

A player asking "why did that happen?" must be able to get an answer. If they
cannot, the design's promise inverts and **the game feels *more* arbitrary than
one with honest random events**, because it claims causality it cannot show.

Mitigations, in order of cost:

1. **The as-built board and sensor readouts must display accumulators, not
   states.** A number climbing toward a line is the entire mechanic; a red light
   is not.
2. **The ledger should support "what happened to this object?"** — a physical
   query at a terminal, not a search UI.
3. **Post-incident, name the top three contributing variables.** Not a tutorial —
   an incident report, in Program voice, which is also a joke.

This is the risk most likely to be discovered late and it is largely a UI
problem, which [risks.md](risks.md) §9 already identifies as the most
underestimated line item.

---

## 14. The blame system meets player death

[risks.md](risks.md) §5 already flags that the responsible-employee field could
produce resentment rather than comedy. Player death makes that worse in a
specific way: `NAMING_DECAY` = 0.55 stops repeated *naming* from paying, but
nothing stops repeated *killing*.

One player can be tased, forklifted, frozen and flooded indefinitely. Each event
is a recordable incident that costs the group, so the group has a reason to
object — but the design has no mechanical brake, and the social brake is the
same one that has to stop scapegoating.

**Deliberately not adding one.** A cooldown or an immunity would be a rule about
what players may do to each other, and this design has no such rules anywhere
else. It stays a prototype question
([open-questions.md](open-questions.md) Q16.2).

---

## 15. Latent hazards will be reported as bugs

Thirteen of seventeen hazards are latent
([hazard-inventory.md](systems/hazard-inventory.md) §6): the UPS bypass, the
missing blanking panel, the unsealed penetration, the absent tarp, the wrong
label, the skipped filter.

Every one of them produces its effect **weeks to months** after its cause, with
no visible link. That is the design working. It is also indistinguishable, from
the player's chair, from a simulation that is broken.

**Mitigation:** §13's incident report, and accepting that early builds will get
bug reports that are actually correct behaviour. Worth telling playtesters
explicitly, and worth *not* telling players.

---

## 16. The hazard players will call unfair

**Blanking panels.**

The brief itself specifies "invisible cause, audible effect", and the
consequences compound exactly where the game is least forgiving: `N_noise` at
night carries `NIGHT_NOISE_MULT` = 2.5 into a 45-day memory, and the cause is a
missing rectangle of sheet metal on one of forty racks.

The complaint will be: *the game punished me for something I could not see, could
not find, and did not do.* All three clauses can be true simultaneously — a
fatigued NPC or a teammate reaching for a cable removed it, the audit shows only
a count, and the noise it caused had already been absorbed into a memory that
takes six weeks to decay.

**Runner-up: the EPO mushroom button.** One press, no countdown, no cover, two
hours of training progress gone. But that complaint is about *griefing*, not
unfairness — the cause is instantly legible and has a name attached. It will
generate louder complaints and fewer refunds.

**Mitigation for blanking panels, and it should be built:** a per-rack airflow
readout, and a "missing panels: 9" count on the as-built board that can be walked
to. Cheap, and it converts an unfair mechanic into a chore, which is the correct
trade.

---

## 17. The audit could turn the game into chores

The 14-day audit crunch is the only scheduled cooperative deadline in the design
and I think it is the best thing
[certifications-audits.md](systems/certifications-audits.md) contributes. It is
also eleven job-card types of housekeeping with a timer.

The difference between a heist-movie scramble and a cleaning rota is entirely in
the density: **fewer, heavier findings scramble; many light findings grind.** The
weights in §3 of that document should be tuned toward few-and-heavy, and if
playtest says the fortnight is homework, the fix is to remove finding *types*,
never to add more time.

---

## 18. What this round did to the earlier risk assessment

| Earlier risk | Status now |
|---|---|
| §1 Networking front-loaded | Unchanged, still the largest single item |
| §2 Idle players when the site is healthy | **Substantially fixed.** Eleven recurring job cards ([staff-shifts.md](systems/staff-shifts.md) §3) are enough legitimate work — *provided the group does not delegate all of it*, which it can. |
| §3 Double-lag legibility | Unchanged, and §13 above compounds it |
| §4 Co-op vs economy timescales | Unchanged. The new hazards mostly obey the blast-radius mitigations already agreed. |
| §5 Blame producing resentment | **Worse.** See §14. |
| §6 Tone | Unchanged. The comedy layer is aimed entirely at the operator, and residents remain unseen and unmocked. |
| §7 Balance surface too large | **Much worse.** ~70 new constants, fewer than fifteen anchored. The harness is now mandatory rather than advisable. |
| §9 Scope | **Superseded by §12.** |
