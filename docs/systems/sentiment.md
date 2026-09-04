# System: Good Neighbor Index

One aggregate meter, `GNI ∈ [0,100]`, fed by the five Program indicators through
a memory layer, modified by Program measures, driving an escalation ladder that
attacks growth before it attacks operations.

In the fiction the GNI is the operator's own community-relations KPI. It is
reported monthly. It is on a slide.

**Inputs:** five `N_i`, Program measures, local headcount, bulletin credibility,
per-player Standing.
**Outputs:** `GNI` (gates every permit), escalation stage (gates deliveries,
hiring, expansion, and the run).

---

## 1. Memory

```
λ_i    = 1 - 0.5 ^ (1 / (HALFLIFE_i * 24))        [per tick]
M_i(t) = M_i(t-1) * (1 - λ_i) + N_i(t) * λ_i
```

| Indicator | Half-life | Why |
|---|---|---|
| Visual | 30 d | Fast — but the source never stops, so it converges high and stays |
| Noise | 45 d | You forget a noisy week; not a noisy summer |
| Price | 60 d | Bills arrive monthly; grievance builds on that rhythm |
| Air | 90 d | Smoke is remembered |
| Water | 120 d | A drought you were blamed for is a generational grudge |

**Memory is why a bad summer is felt in November**, which is why the grid-permit
re-check has teeth ([power.md](power.md) §2). Mistakes and consequences are
separated by months, and that separation is the difficulty.

---

## 2. Aggregation

```
GNI_target = 100 - Σ (W_i * M_i) + G
GNI(t)     = GNI(t-1) + GNI_ADJUST_RATE * (GNI_target - GNI(t-1))
```

`GNI_ADJUST_RATE` = 0.004/tick — a ~250 h ≈ **10-day** time constant.

**Two lags stacked**: indicator memory (30–120 d) and the meter chasing its
target (10 d). A measure installed today shows nothing this week, a little next
month, full effect in a season.

Players will conclude that measures do not work and will reach for suppression,
which is instant. **That misjudgement is the intended trap** and the entire reason
the two classes exist — and, critically, the reason they are not labelled
differently on the Program board (§4).

The UI must show `GNI_target` as a ghost marker beside `GNI` — not to remove the
lag, but so players can see they are *winning* before they are winning. Without
it the system is unfair rather than slow.

---

## 3. Escalation

Hysteresis of 5 points on each boundary, so states do not flicker.

| Stage | In-fiction | GNI | Mechanical effect |
|---|---|---|---|
| Content | — | ≥ 70 | Permits granted. Full hiring pool. |
| Complaints | "inbound community correspondence" | 55–70 | Letters, local paper. **No penalty** — this stage teaches indicator-reading before anything is at stake. |
| Petition | "community signature initiative" | 40–55 | Signatures accumulate at a rate ∝ `(55 − GNI)`, are visible, and **persist when GNI recovers.** Feeds the referendum trigger. |
| Protest | "unscheduled gate activity" | 25–40 | **Deliveries blocked. Hiring frozen.** Operations continue. |
| Injunction | "administrative pause" | 15–25 | **All expansion permits frozen**, including in-flight — clock paused, capex still spent. |
| Sabotage | "unauthorised third-party interference" | 5–15 | Fibre cut (4–18 h total delivery loss). A resident may also breach the fence for one rack or one solar row in person (incidents.md §3.9) — the only incident in the catalogue the town itself carries out. |
| Referendum | "community consultation event" | via petition | Binding vote. §6. |

**The ladder attacks growth before operations.** A group can sit at 30
indefinitely, profitable, permanently unable to expand. That is a legitimate
ending and the game should present it as one.

**Sabotage is the only stage suppression addresses, and now there are two ways
to fall into the same trap.** You buy fences to stop fibre cuts, fences raise
`N_visual`, `N_visual` lowers GNI, and lower GNI is what produced the sabotage
in the first place. A camera, floodlight or alarm against an incursion is the
identical shape on a shorter lever: each is sited, visible hardware and each
costs a sliver of `N_visual` the moment it is bought (incidents.md §3.9).
Suppression is locally rational and globally a spiral either way. The one
purchase that is not a lever on this spiral is the taser — it is carried, not
sited, and ending an incursion in person costs nothing but the walk there.

---

## 4. The Program board

A physical board in the office. Every measure below is on it, **in one list,
sorted by cost, with no category headings.** The game does not tell you which of
these is a repair and which is a defence.

### 4.1 Program safety measures (suppression)

| Measure | Capex | Opex | Effect |
|---|---|---|---|
| Perimeter fence | €40k | — | +4 `N_visual`; small sabotage reduction |
| Electric fence | €120k | €2k/mo | +12 `N_visual`; large sabotage reduction |
| Illumination mast (each) | €25k | €1.5k/mo | +9 `N_visual`; night sabotage reduction |
| Camera network | €60k | €1k/mo | +3 `N_visual`; resolves sabotage events |
| Guard patrols | — | €18k/mo | +6 `N_visual`; largest sabotage reduction |
| Legal counsel retainer | — | €25k/mo | Delays injunctions 30–60 d; **no `N_visual`** |

All instant, all effective — against **sabotage and injunctions only.** They do
nothing about the underlying GNI, and four of six actively lower it, permanently,
through a channel whose source never decays.

Legal counsel is the interesting one: no visual cost, genuinely buys time, and
therefore the *correct* purchase when a permit is in flight. It is also the
purest satire on the board, converting money directly into delay with no physical
change whatsoever.

### 4.2 Program investments (genuine fixes)

| Measure | Cost | Effect | Lag |
|---|---|---|---|
| Acoustic barrier | €300k | Source reduction on noise | 60 d |
| Closed-loop cooling | `CHILLER_CAPEX` | Water → 0; price worsens | 90–180 d |
| Hybrid dry/adiabatic | ~1.6× chiller capex | Water only above a temperature threshold | 180 d |
| On-site solar | `SOLAR_CAPEX` | Price; +2 `N_visual`/MW | 120 d |
| On-site wind | `WIND_CAPEX` | Price; +7 `N_visual` and noise each | 300 d |
| Heat reuse — district | €4M+ | **Direct `G` bonus**, small revenue | 240 d |
| Heat reuse — pool | €180k | **Large direct `G` bonus** | 90 d |
| Landscape screening | €80k | −4 → −14 `N_visual` over 3 y | 3 y |
| Local employment commitment | +18% wages | §4.3 | Immediate |
| Program benefit | €/month | `G += PROGRAM_BENEFIT_EFFICIENCY * EUR` | 30 d |
| Community Engagement Session | €15k | One-off `G` pulse × `(1 - N_visual/100)` | Immediate, decays |

### 4.3 Local employment — the strongest lever

```
G_hiring = LOCAL_HIRE_GNI_PER_FTE * local_FTE                (0.9 per FTE)
cost     = local_FTE * base_wage * (1 + LOCAL_HIRE_WAGE_PREMIUM)  (+18%)
dismissal: GNI -= DISMISSAL_GNI_PER_FTE per head             (2.2 — asymmetric)
```

Twenty local staff is **+18 GNI** — the gap between the T2 and T3 permit gates —
for ~€200k/year of extra payroll. Nothing else buys 18 points that cheaply.

**Dismissal costs 2.4× what hiring gained.** And **hiring is frozen at the Protest
stage**, so a group that lets GNI reach 30 has lost access to the cheapest route
back up. That one-way door is deliberate and is the ladder's most important
consequence — and its most destructive co-op vector
([coop-griefing.md](coop-griefing.md) §6.4).

The satire lands by itself here. A 20 MW datacenter genuinely creates very few
jobs, and the cheapest possible GNI purchase is to employ **more people than the
site needs** and describe this in a bulletin as a commitment to the region.

---

## 5. The Program bulletin

```
effect = BULLETIN_BASE_EFFECT * C                      (3.0)
C     -= CREDIBILITY_LOSS_PER_USE                      (0.15/use)
C     += CREDIBILITY_RECOVERY                          (0.02/week)
if C < CREDIBILITY_MOCKERY_THRESHOLD (0.20):  effect = -1.5
```

Cost `BULLETIN_COST` = €2,000, issued at a terminal by anyone, attachable to any
measure or standalone.

Seven uses take `C` from 1.0 to near zero; recovery takes about a year. Below
0.2 the local paper quotes bulletins sarcastically and each new one *costs* GNI.
**The spam is the mechanic:** cheap, effective, then ineffective, then harmful —
and burned on trivia long before the crisis where it was needed.

Copy is generated against the site's actual current state, so the absurdity is
always specifically earned:

> "Following extensive community consultation, the facility has adopted a
> **night-time acoustic profile** consistent with regional expectations."
> *(issued the week `N_noise` crossed 70 at 03:00)*

> "Our **thermal enrichment programme** continues to deliver measurable benefit
> to the municipal aquatic facility."
> *(issued while the pool supply is disconnected)*

> "The site has been designed to **recede visually into the agricultural
> context**."
> *(issued at `N_visual` 78, four illumination masts)*

### The responsible employee field

```
GNI            += NAMING_GNI_EFFECT * NAMING_DECAY^n           (4.0, ×0.55)
Standing_named -= STANDING_LOSS_NAMED                          (14)
```

Full mechanics in [coop-griefing.md](coop-griefing.md) §4, including false naming
and the fact-check. Accountability theatre has diminishing returns, exactly as it
does.

### The fact-check

A bulletin may cite a local-jobs figure. If actual headcount later falls below a
published figure, or resident footage contradicts a named employee, credibility
drops by `FALSE_NAMING_PENALTY` × normal and the discrepancy is quoted back.
**The lie is a mechanic**, and it is one told casually, months earlier, for four
points.

---

## 6. GNI vs Reputation vs Standing

Three meters, no shared term.

| | GNI | Reputation | Standing |
|---|---|---|---|
| Held by | The town | Customers | Your colleagues, and the town |
| Raised by | Measures, hiring, heat reuse, restraint | Delivering within SLA | Time (`STANDING_RECOVERY`) |
| Lowered by | Nuisance, suppression, growth, dismissals | Breaches, missed deadlines | Being named in a bulletin |
| Gates | Permits, deliveries, hiring, survival | Which contracts are offered | Who is blamed by default; dismissal availability |
| Timescale | Months | Fast to lose, ~32 contract-months to rebuild | Weeks |

The opposition between the first two is the design:

- **Run diesel overnight to hold an SLA.** Reputation preserved, air indicator
  damaged for 90 days.
- **Throttle during a heatwave to avoid water restrictions.** GNI preserved, SLA
  breached, Reputation −16, training contracts locked for a year.
- **Take a large training run.** The revenue funds the investments that would
  raise GNI. Running it requires the overbuild that lowers GNI.
- **Build chillers to stop drinking the town's water.** Water to zero, price up
  for the rest of the campaign.

**Only heat reuse and efficiency raise both**, and both are slow and
capital-intensive. The skill is choosing which meter to spend, and when — and in
co-op, agreeing on it, which is harder.

---

## 7. Referendum

Triggered when petition signatures exceed threshold. Held 90 days later — long
enough for a desperate campaign, short enough that suppression cannot manufacture
a result.

```
turnout   = f(signatures, months since last referendum)
yes_share = g(GNI at vote, local_FTE / TOWN_POPULATION, credibility C,
              ΔGNI over the preceding 90 days)
```

**The `ΔGNI` term matters: a town that can see things improving votes differently
from a town at the same level and falling.** A group that spends the 90 days
genuinely fixing things can survive a vote they would have lost on the day it was
called. A group that spends it on bulletins (low `C`) and fences (rising
`N_visual`) loses by more than they started.

Lose it and the run ends. This should be presented in the register the whole game
uses — not as tragedy, but as a short item in the local paper, next to something
about a school fête.

---

## 8. Coupling

| To | Via |
|---|---|
| **Power** | GNI gates every grid-tier permit, at application *and* completion. The main progression gate. |
| **Compute** | Indirect: protest and injunction block expansion; sabotage causes direct SLA breaches |
| **Cooling** | Heat reuse is the only physical system feeding `G` positively |
| **Nuisance** | The five indicators are GNI's only physical inputs |
| **Co-op** | Bulletins, naming, the benefit dial, the hiring roster, Engagement Sessions, Standing |
| **Economy** | Hiring premium, benefit, suppression opex, sessions, bulletins — GNI is a spending category, not just a meter |
| **Nuisance (back-edge)** | Safety measures raise `N_visual`. The design's only feedback from GNI *into* nuisance, and what makes suppression a spiral rather than a cost. |
