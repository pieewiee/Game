# System: Certifications and audits

Certifications gate contract *classes*. Audits are scheduled inspections that
read the facility's actual state — and are the only cooperative deadline in the
game.

**Inputs:** facility state (routing, housekeeping, access control, documentation),
TRIR, PUE/WUE history, preparation spend.
**Outputs:** certification status, contract class access, audit findings,
remediation job cards, Reputation effects.

---

## 0. Duplication risk with Reputation — stated, not hidden

[compute-contracts.md](compute-contracts.md) §2.6 already gates contracts on
Reputation. This system also gates contracts. That is a duplication and it needs
a defence or a merge.

**The defence, and I think it holds:**

| | Reputation | Certification |
|---|---|---|
| Measures | **Behaviour** — did you deliver, historically | **Structure** — is the building compliant, now |
| Earned by | Contract-months within SLA | Passing an audit |
| Lost by | Breaches | Failing an audit, or letting it lapse |
| Gates | Contract **size** (2 MW / 8 MW blocks) | Contract **class** (regulated, public-sector, latency-tier) |
| Timescale | ~32 contract-months to rebuild | One audit cycle |

They are orthogonal: a site can have a spotless delivery record and no security
certification (it takes anything, at any size, but not from a regulated client),
or three certifications and a ruined record (it is eligible for everything and
offered nothing large).

**If this system gets cut** ([scope.md](../scope.md)), fold its contract gating
into Reputation and keep only the audit as a scheduled cleanup event. Logged as
[open-questions.md](../open-questions.md) Q21.

---

## 1. The three certifications

Generic names, deliberately — these are recognisable analogues, not claims about
real standards.

### Availability Tier (I–IV)
Requires demonstrated redundancy: N+1 or 2N on power and cooling paths,
documented load-bank tests, no single points of failure in the as-built board.

| Tier | Requires | Unlocks |
|---|---|---|
| I | Single path, working | Nothing; the default |
| II | N+1 cooling | `SLA_INFERENCE` contracts above 400 kW |
| III | Concurrently maintainable: N+1 both, dual power paths to each rack | Anchor inference, multi-year |
| IV | Fault tolerant: 2N throughout, dual network paths | Latency-tier contracts ([network.md](network.md) §4) |

### Security
Requires access control (**key cards** —
[workplace-accidents.md](workplace-accidents.md) §4.9), camera coverage of the
perimeter and halls, visitor logging, and a documented escort policy.

Unlocks regulated clients. **Also unlocks the 4-hour on-site RMA tier**
([hardware-lifecycle.md](hardware-lifecycle.md) §4), because no vendor gives
unescorted access to an uncontrolled site.

The trap: cameras and access control are **Program safety measures** and add to
`N_visual` ([nuisance.md](nuisance.md) §5). **The security certification requires
the group to make the site look more like a compound.** It is a certification you
buy with Sentiment.

### Environmental
Requires reported PUE and WUE, a heat-reuse installation *or* a documented plan,
waste and packaging management, and water-use disclosure.

Unlocks public-sector and EU-domiciled clients — **and it is a precondition for
the T4 grid tier**, because at 50 MW the utility will not connect a site that
cannot report its own water use.

This is the one certification that is genuinely aligned with the town's
interests, and it is therefore the one groups reach last.

---

## 2. Holding and lapsing

```
certification_valid_until = last_pass + CERT_CYCLE            (18 months)
audit scheduled at         valid_until - AUDIT_LEAD_NOTICE    (14 days notice)
lapse -> contract class access revoked immediately
      -> existing contracts in that class run to term, then cannot renew
```

Lapsing does not cancel work in flight. It quietly stops the pipeline, and the
group notices two months later when the offers thin out.

---

## 3. The audit

**Findings are read from actual simulation state.** There is no checklist roll and
no hidden die.

```
score = 100 - Σ weight(finding) * severity(finding)
pass  if score >= PASS_THRESHOLD (70)
```

### Physical findings — cannot be faked

| Finding | Source | Weight |
|---|---|---|
| Unsealed compartment penetrations | [construction-routing.md](construction-routing.md) §2 | 12 each |
| Cable tray above derated ampacity | §3 | 10 each |
| Missing blanking panels | [workplace-accidents.md](workplace-accidents.md) §4.7 | 3 each |
| Combustible loading (packaging, pallets) | §4.17 | 8 |
| Aisles blocked | routing / packaging | 6 |
| Lifted floor tiles left open | §4.13 | 5 each |
| Cut suppression seals | §4.1 | 15 each |
| EPO obstructed or covered | §4.5 | 9 |
| Battery room ventilation disabled | §4.16 | **20** |
| Eyewash station not flushed | §4b | 4 |
| Forklift damage to plant or fence | §4.4 | 7 |
| Transformer tarp absent | §4.12 | 6 |

### Documentary findings — can be faked, once

| Finding | Source | Weight |
|---|---|---|
| Missing or wrong cable and port labels | [construction-routing.md](construction-routing.md) §7 | 5 per zone |
| No documented load-bank test this cycle | [staff-shifts.md](staff-shifts.md) §3 | 11 |
| Visitor log incomplete | Open days, deliveries | 4 |
| PUE / WUE not reported | Environmental only | 9 |
| TRIR above threshold | [workplace-accidents.md](workplace-accidents.md) §2 | 8 |
| Access rights not reconciled | Key cards on the floor, on dead players, swapped | 7 |

**Note that a wrong label scores the same as a missing one here**, unlike repair
time where wrong is worse. The auditor cannot tell; the technician at 3 a.m. can.

---

## 4. Preparation

`AUDIT_LEAD_NOTICE` = 14 days. What the group does with them is the content.

```
preparation_cost = remediation_job_cards * staff_hours * wage
                 + materials
                 + opportunity cost of pulling players off operations
```

Remediation is the same job cards as
[staff-shifts.md](staff-shifts.md) §3 — fire-stops, blanking panels, packaging
removal, label verification, tile reseating, load-bank test — with a deadline
attached. **Fourteen days of everyone doing housekeeping together is the only
scheduled cooperative crunch in the design**, and it is the best thing this
system contributes. The audit is not the interesting part; the fortnight before
it is.

### The shallow expensive fake

A **readiness consultancy**: €180,000, 14 days, arrives with lanyards.

```
effect: suppresses ALL documentary findings for this audit
        suppresses NO physical findings
        works ONCE per certification
        second use in the same cycle -> detected
                                     -> automatic fail
                                     -> Reputation -15
                                     -> the finding is "misrepresentation"
```

It does exactly what such firms do: it fixes nothing and documents everything
favourably. A group with a physically sound but badly documented site passes
comfortably. A group with unsealed penetrations and a disabled battery vent fails
anyway, €180,000 lighter, having spent the fortnight not fixing the penetrations.

**It is a real option and it is sometimes correct**, which is the only way this
kind of satire works.

---

## 5. How the audit interacts with the mess

This is the payoff for every deferred job card in the game. A worked example, a
site fourteen months in that has been busy:

| Finding | Count | Score |
|---|---|---|
| Unsealed penetrations | 4 | −48 |
| Trays over derate | 2 | −20 |
| Missing blanking panels | 9 | −27 |
| Packaging in Hall 2 | — | −8 |
| Battery vent fan off (for the noise) | — | **−20** |
| Wrong labels, 3 zones | — | −15 |
| No load-bank test | — | −11 |
| TRIR 8.9 | — | −8 |
| **Score** | | **−57** |

That site scores negative against a threshold of 70. Everything on the list was
a legitimate decision at the time, several of them improved a number the group
cared about, and one of them — the vent fan — was **turned off specifically to
reduce noise complaints**, which is to say, to raise the Good Neighbor Index.

The readiness consultancy would have removed 34 points of it and the site would
still have failed by 53.

---

## 6. Failure modes

| Failure | Why |
|---|---|
| Audits become a chore, not a crunch | If remediation is grindy rather than a scramble, the 14 days are homework. The fix is fewer, heavier findings — not more. |
| Groups fail their first audit and disengage | First audit should be reachable. Recommend the Availability Tier II audit be scheduled at ~month 10, when the site is still small enough to fix. |
| The consultancy becomes the default | It costs 4% of a good month and works once per cycle. If groups buy it every time, the price is wrong. |
| Certification duplicates Reputation in play | §0. Watch for whether players ever experience them as two things. |

---

## 7. How to abuse this against teammates

| Abuse | Legitimate cover |
|---|---|
| Let a certification lapse | Audits cost money and the fortnight is expensive |
| Book the audit for a heatwave week | The auditor offers dates; someone has to pick one |
| Buy the consultancy early, wasting the once-per-cycle use | "Getting ahead of it" |
| Relabel ports the week before | Label verification is a mandated card |
| Confiscate the key card of whoever is doing access reconciliation | Access control is the certification |
| Route the auditor's walk past the tidy hall | Everybody does this |
| Turn the battery vent fan off two days before | It reduces `N_noise` and the audit is about compliance, isn't it |

The last one is the same action as
[workplace-accidents.md](workplace-accidents.md) §4.16 and it now costs 20 audit
points as well as a possible deflagration — **a hazard that punishes you three
different ways for one defensible decision** is the shape this whole design is
aiming at.

---

## 8. Coupling

| To | Passes | Unit |
|---|---|---|
| [compute-contracts.md](compute-contracts.md) | Contract class access | boolean per class |
| [network.md](network.md) | Tier IV gates latency-tier contracts | boolean |
| [power.md](power.md) | Environmental cert is a T4 precondition | gate |
| [hardware-lifecycle.md](hardware-lifecycle.md) | Security cert gates 4-hour RMA | tier |
| [construction-routing.md](construction-routing.md) | Penetrations, ampacity, labels | findings |
| [workplace-accidents.md](workplace-accidents.md) | Seals, EPO, vent fan, TRIR, eyewash | findings |
| [staff-shifts.md](staff-shifts.md) | Preparation hours, documented tests | h |
| [nuisance.md](nuisance.md) | Security cert requires cameras → `N_visual` | points |
| [sentiment.md](sentiment.md) | Environmental cert is a bulletin subject with real content | GNI |
| [economy.md](../economy.md) | Audit fees, preparation, consultancy | EUR |
