# GOOD NEIGHBOR PROGRAM — Game Design Document

A co-op simulation for 2–5 players who jointly run an AI datacenter next to a
small town. You build compute capacity to sell. Every route to more revenue harms
the neighbours in a different way. The neighbours can shut you down.

> **Status: paper design.** Nothing prototyped. Every formula is a hypothesis.
> Numbers live in [balance-constants.md](balance-constants.md), cited by name.

---

## 1. The title is the design

"Good Neighbor Program" is what the operator calls its community-relations
function. The game is the gap between that phrase and what the site actually
does, so the fiction uses the vocabulary everywhere and never winks:

| Thing | In-game name |
|---|---|
| Sentiment meter | **Good Neighbor Index (GNI)** |
| The five nuisance channels | **Program indicators** |
| The mitigation board | **the Good Neighbor Program** |
| Community fund | **Program benefit** |
| Perimeter fence, cameras, guards | **Program safety measures** |
| Noise wall, closed-loop cooling, solar | **Program investments** |
| Press release | **Program bulletin** |
| Open day | **Community Engagement Session** |
| Local hiring | **Program local employment commitment** |
| Dismissing a player | **Program personnel realignment** |
| Protest at the gate | **unscheduled gate activity** |
| Injunction | **administrative pause** |
| Sabotage | **unauthorised third-party interference** |
| Referendum | **community consultation event** |

### The single most important consequence

**The Program board does not distinguish suppression from genuine fixes.**

An electric fence and a noise wall are both "Program measures". They are filed at
the same desk, on the same form, under the same heading, sorted by cost. Nothing
in the UI marks one as a repair and the other as a defence. The brief requires
that players learn the difference *the hard way* — the corporate vocabulary is
the mechanism that hides it. Labelling them would be a designer telling the
player the answer, and would also break the satire, because no real programme of
this kind labels them either.

**Tone rule, absolute:** the joke is always on the operator. Residents are quoted
accurately and are always factually correct. The absurdity is in the operator's
language, the operator's certainty, and the widening gap between the two. If a
playtester comes away thinking the residents were unreasonable, the writing has
failed and should be rewritten, not rebalanced.

---

## 2. Market position

Four datacenter sims shipped on Steam in 2026 — Data Center (Waseku, co-op),
Datacenter Simulator (Jelle), Data Center Simulator Game (DC Uplink), A Game
About AI (TaleHammer). **All of them simulate the inside: racks, cables,
networking. None simulates the town outside.**

Three wedges, and every decision below serves them:

1. **The community conflict is the core system**, not flavour. The town has five
   independently tracked grievances, a memory measured in months, and the power
   to end the run. It is the progression gate.
2. **Chaotic co-op.** Players can ruin each other's work, and the design is built
   so that they can — no permissions, no confirmations, no undo.
3. **Real depth in power, heat and water**, which reviewers say competitors lack.
   PUE is an output, not a stat. Cooling COP is a function of wet-bulb
   temperature. Water is measured against the town's supply.

**What we deliberately do not build**, because those four already do it well:
rack cabling as a puzzle, network topology, switch configuration, server
component shopping, first-person cable-tracing minigames. Our hand-routing
(§7) exists to create shared mess and blame, not as a cable sim, and it must
stay shallower than theirs on purpose. See
[open-questions.md](open-questions.md) Q1.

---

## 3. Resource model

| Resource | Unit | Note |
|---|---|---|
| Power | kW | Energy over a tick = kW × 1 h. |
| Heat | kW_th | `HEAT_FRACTION` = 1.0 kW_th per kW_e — all electricity becomes heat. |
| Water | m³/day | Evaporative cooling only. Shown against the town's supply. |
| Space | HE | `RACK_HE` = 42 per rack. |
| Money | EUR | One shared balance. No per-player wallets. |
| GNI (Sentiment) | 0–100 | Community approval. Five indicator channels. |
| Reputation | 0–100 | Commercial standing. **Separate from GNI, often opposed.** |
| Standing | 0–100 | **Per player.** Personal standing, spent by the blame system. |
| Time | 1 tick = 1 h | 730 h/month, 8760 h/year. Shared, contested. |

---

## 4. Tick order

One tick = one simulated hour. Systems evaluate in this order; each sees the
current tick's output of everything above it.

```
0.  Intents      player interactions received this tick are applied (host-ordered)
1.  Climate      T_db, T_wb, wind speed/direction, irradiance, precipitation
2.  Demand       contracts declare required IT load
3.  Compute      allocate load -> P_IT (kW), Q_IT (kW_th)
4.  Cooling      given Q_IT and climate -> P_cool, water draw, throttle factor θ
5.  Compute'     apply θ -> delivered load        (single pass, not iterated)
6.  Power        draw = P_IT + P_cool + P_aux; dispatch; price
7.  Nuisance     five indicators sample this tick's physical state
8.  GNI          indicator memories decay/accumulate; aggregate; check escalation
9.  Economy      revenue, opex, penalties, cash
10. Ledger       append (actor, tick, event) for everything a player caused
11. Events       seasonal, weather, community, media
```

Step 5 is the only feedback loop and is resolved in **one pass, not iterated** —
see [open-questions.md](open-questions.md) Q6.

Step 0 is what makes co-op work: intents are applied at tick boundaries in
host-assigned order, so two players flipping the same switch in the same tick
resolve deterministically and both appear in the ledger.

---

## 5. Core formulas

Derivations in the system documents. This is the spine.

### 5.1 Compute

```
P_node(u) = P_idle + (P_peak - P_idle) * u              [kW]
Q_node(u) = P_node(u) * HEAT_FRACTION                   [kW_th]
P_IT      = Σ P_node(u_i)                               [kW]
P_aux     = 0.02 * P_IT + 15                            [kW]
```

### 5.2 Cooling

```
P_cool = Q_removed / COP(climate)                       [kW]
PUE    = (P_IT + P_cool + P_aux) / P_IT                 [dimensionless, an OUTPUT]
θ      = clamp(Q_cap / Q_IT, 0, 1)
```

| Plant | COP driver | Water |
|---|---|---|
| Evaporative | `EVAP_COP(T_wb)` — 40 at 12 °C, 6 at 30 °C | `EVAP_WUE(T_wb)` L/kWh_IT |
| Chiller | `CHILLER_COP(T_db)` — 5.0 at 5 °C, 2.5 at 35 °C | none |
| Free cooling | `FREECOOL_COP` = 30, only below `FREECOOL_T_THRESHOLD` | none |
| Heat reuse | `HEATPUMP_COP` = 3.0, capped by external demand | none |

With throttling suppressed (**Run Hot**, a physical switch anyone can flip):

```
T_inlet  = T_supply + ΔT * (Q_IT - Q_cap) / Q_IT        [°C]
damage/h = THROTTLE_DAMAGE_K * max(0, T_inlet - T_INLET_SAFE)²   per node
```

### 5.3 Water

```
V_water = EVAP_WUE(T_wb) * E_IT_evap                    [L/h]
```

Shown as m³/day against `TOWN_WATER_DEMAND` = 500. At 20 MW IT on evaporative
cooling the site drinks **1.7× the entire town's water.** That number falls
straight out of published WUE and published household consumption. No
exaggeration is applied or required.

### 5.4 Power price

```
load_ratio = P_grid_draw / REGION_CAPACITY(tier)
p_grid     = p_base(month, hour)
             * (1 + CONGESTION_K * load_ratio ^ CONGESTION_GAMMA)
             * scarcity(t)                              [EUR/kWh]
p_resident = p_grid * RETAIL_MARKUP                     [EUR/kWh]
```

The player pays `p_grid`, the town pays `p_resident`, **and they are the same
number.** There is no "we'll absorb it" available.

### 5.5 Program indicators

```
N_noise  = f(fan power, chillers, diesel, turbines) * night_mult
N_air    = diesel_kWh * emission_factor * wind_mult
N_water  = 100 * V_player_daily / TOWN_WATER_DEMAND * drought_mult
N_price  = 100 * (p_resident - p_baseline) / p_baseline
N_visual = Σ fortification_points - Σ screening_points

any indicator sampled while residents are filming is × FILMED_MULT (2.5)
```

`wind_mult` is `WIND_TOWARD_MULT` = 3.0 when the wind blows into the west town
(270° ± 45°), `TOWN2_WIND_MULT` = 2.0 into the north apartment blocks (0° ± 30°),
else `WIND_AWAY_MULT` = 0.3. **Wind direction is a real simulated variable.** So
is whether anyone is at the fence with a phone.

### 5.6 Good Neighbor Index

```
λ_i    = 1 - 0.5 ^ (1 / (HALFLIFE_i * 24))              [per tick]
M_i(t) = M_i(t-1) * (1 - λ_i) + N_i(t) * λ_i
GNI_target = 100 - Σ (W_i * M_i) + G
GNI(t)     = GNI(t-1) + GNI_ADJUST_RATE * (GNI_target - GNI(t-1))
```

Two lags stacked — 30–120 day indicator memory, ~10 day meter response. A
mitigation shows nothing this week and its full effect in a season. That lag is
the central difficulty and the reason players reach for suppression.

### 5.7 Reputation and Standing

```
R += REPUTATION_GAIN_MONTH   per contract-month within SLA
R -= REPUTATION_LOSS_BREACH * severity   on breach

Standing_p -= STANDING_LOSS_NAMED        when named in a Program bulletin
Standing_p += STANDING_RECOVERY / day
```

Reputation gates contracts. GNI gates permits. Standing gates nothing directly —
it decides who the town blames by default and when "Program personnel
realignment" becomes available (§8).

---

## 6. The loop

```
        more contracts
              |
              v
          more heat  -----------------------------+
              |                                    |
    +---------+---------+---------+                |
    v                   v         v                |
 water cooling    power cooling  diesel            |
    |                   |         |                |
    v                   v         v                |
 Water indicator   Price ind.   Air indicator      |
    \                   |         /                |
     +--------+---------+--------+                 |
              v                                    |
          GNI falls                                |
              |                                    |
              v                                    |
   grid upgrade permit blocked <-------------------+
              |
              v
   cannot take the large contracts
              |
        +-----+-----+
        v           v
  Program       Program
  investments   safety measures
  (expensive,   (cheap now, worsens
   slow,        Visual, compounds later)
   permanent)
              \
               \  and both are on the same board,
                \ under the same heading, at the same desk
```

The real decision is never "which cooling system". It is **which of five people
to annoy, and how long the memory of that annoyance lasts** — and, in co-op,
**which of your colleagues gets named for it.**

---

## 7. Co-op: the physical world rule

**No menu action is ever consequential.** Every dangerous thing is an object in
the world that anyone can walk up to and operate. No permissions, no
confirmation dialogs, no undo. A confirmation dialog kills the fun.

Fully specified in [systems/coop-griefing.md](systems/coop-griefing.md). The
shape:

- **Shared account.** Anyone can order anything. Deliveries have lead time; you
  watch the truck arrive and can no longer stop it. Uninstalled equipment sitting
  in the yard adds to the Visual indicator until someone deals with it.
- **Inertia, no undo.** `DIESEL_START_HOLD` = 3 seconds on the lever, minutes of
  smoke, 90 days of air-indicator memory.
- **Blame ledger.** The sim records who triggered what. Program bulletins have a
  *responsible employee* field. Naming a teammate raises GNI by
  `NAMING_GNI_EFFECT`, costs them `STANDING_LOSS_NAMED`, and decays by
  `NAMING_DECAY` each time it is used.
- **Hand-routed power, cooling and network runs.** Deliberately shallow — this is
  not a cable-management sim (§2). Sloppy routing is permanent, costs efficiency,
  and lengthens fault repair.
- **Every valve, rack switch and cooling mode selector is individually operable.**
- **Manual fire suppression on the wall.** Discharge kills
  `SUPPRESSION_DISCHARGE_DRIVE_LOSS` of the drives in that room via acoustic
  shock, as in reality (ING Bank Bucharest, 2016).
- **Time is shared.** Fast-forward only when every player is idle; the holder is
  named on screen; majority override after `TIME_HOLD_OVERRIDE` = 180 s.
- **Residents film from the fence.** Footage appears on the evening local news,
  and anything sampled while they are filming costs `FILMED_MULT` = 2.5×.

---

## 8. Escalation

| Stage | GNI | Mechanical effect |
|---|---|---|
| Content | ≥ 70 | Permits granted. Full local hiring pool. |
| Complaints | 55–70 | Letters, local paper. **No penalty** — this stage teaches indicator-reading before anything is at stake. |
| Petition | 40–55 | Signatures accumulate and **persist** when GNI recovers. Feeds the referendum trigger. |
| Protest at gate | 25–40 | **Deliveries blocked. Hiring frozen.** |
| Injunction | 15–25 | **All expansion permits frozen**, including in-flight ones — clock paused, capex already spent. |
| Sabotage | 5–15 | Fibre cut (instant SLA breach), power interference, coolant damage. |
| Referendum | via petition | Binding vote. Lose it and the run ends. |

**The ladder attacks growth before operations.** A group can sit at GNI 30
indefinitely, profitable, permanently unable to expand. That is a legitimate
ending, not a failure state.

**Program personnel realignment.** If a player's Standing falls below
`STANDING_CONSULTANT_THRESHOLD` = 20, the group may dismiss them in a bulletin
for a one-off GNI gain. The dismissed player is still in the session — they
return as a *consultant*, identical abilities, `CONSULTANT_COST_MULT` = 1.9×
payroll, and an ongoing `CONSULTANT_GNI_PENALTY` because the town notices. You
fired them publicly and rehired them at higher cost, which is both the funniest
and the most accurate thing in the design.

---

## 9. Required gags, as mechanics

### 9.1 The diesel plume
A volume advected by the *simulated* wind vector. Length scales with cumulative
runtime, opacity with instantaneous burn. In the town sector, `N_air` × 3.0;
otherwise × 0.3 — a factor of **ten** between the same run on two afternoons.
The wind forecast is on the HUD from hour one. Players will learn to schedule
pollution around it, and that optimisation is both genuinely harm-reducing and
obviously monstrous.

### 9.2 The Program bulletin
```
effect = BULLETIN_BASE_EFFECT * C
C     -= CREDIBILITY_LOSS_PER_USE   per use
C     += CREDIBILITY_RECOVERY       per week
if C < CREDIBILITY_MOCKERY_THRESHOLD:  effect = -1.5   // the sign flips
```
Seven uses take credibility to near zero; recovery takes about a year. Below the
threshold the local paper quotes bulletins sarcastically and each new one *costs*
GNI. Copy is generated against the site's actual state, so the absurdity is
always specifically earned.

### 9.3 Fortification becomes the problem
Fence, searchlights, camera masts, guard posts each add `N_visual`. Above 45 the
local paper starts describing the site's appearance rather than its activity.
And **Community Engagement Sessions scale with `(1 - N_visual/100)`** — the
cheapest genuine measure is poisoned by the cheapest safety measure, both filed
under the same heading on the same board.

### 9.4 The Program benefit dial *(emergent)*
The community fund is a physical dial in the office. Anyone can crank it to
€200k/month. GNI rises, so it *looks* like generosity — and it quietly consumes
the cash reserved for the grid upgrade, whose permit needs that GNI. **A griefing
action indistinguishable from virtue.** The purest expression of this game.

### 9.5 The pool hostage *(emergent)*
Heat reuse to the town pool: `POOL_CONNECTION_CAPEX` = €180,000, roughly
profit-neutral, large GNI gain because it is visible and loved. Once connected,
**withdrawing the heat costs several times what supplying it ever earned.** In
co-op this is devastating: one player connects it, another disconnects it to free
heat-pump power for a training deadline, and the town's memory does not care
which of them did what. Real precedent: Exmouth, Devon, 2023.

### 9.6 The fact-check *(emergent)*
A bulletin may cite a local-jobs figure, or name a responsible employee. If
actual headcount later falls below a published figure — or if resident footage
contradicts the named employee — a fact-check fires, credibility drops by
`FALSE_NAMING_PENALTY` × the normal amount, and the discrepancy is quoted back.
**Lying is a mechanic**, and it is a lie told casually, months earlier, for four
GNI points.

### 9.7 The scheduled Engagement Session *(emergent)*
Anyone can schedule a Community Engagement Session. Once announced it **cannot be
cancelled** — cancelling is worse than holding it. If it is scheduled three days
out and the site is at `N_visual` 70, the group now has three days to physically
remove searchlights they paid for. A griefing action that forces cooperative
labour.

### 9.8 The rebrand *(emergent)*
After a referendum scare the site may be renamed (€50k). This clears the *brand*
component of GNI memory but not the indicator memories. Local media thereafter
prefixes every mention with "formerly known as", and the next incident restores
the cleared component in full. Corporate identity laundering, priced.

---

## 10. Seasons

Each quarter binds on a different resource. Detail in
[systems/seasons.md](systems/seasons.md).

| Season | Cooling | Power | Dominant threat |
|---|---|---|---|
| **Spring** | Free cooling mostly available | Moderate wind, rising solar | None. Build now — and its danger is that it is pleasant. |
| **Summer** | Free cooling gone; evaporative COP collapses; chillers worst | Solar peaks; **wind at annual minimum**; heatwave scarcity spikes | **Capacity crisis.** Heat, drought and water at once. |
| **Autumn** | Free cooling returns | Wind rising, solar falling | Commercial: training deadlines cluster here. |
| **Winter** | **Free cooling nearly free** | **Wind peaks, solar ~zero**, `GRID_PRICE_WINTER` high, Dunkelflaute | **Cost crisis**, plus peak heat-reuse revenue. |

**Summer is a capacity crisis, winter is a cost crisis, and the plant that solves
one is wrong for the other.** Free cooling + chillers survives both at high
capex. Evaporative alone is cheap and dies in July. Chillers alone survive July
and bleed all winter.

---

## 11. Deliberately not in this design

- No rack cabling puzzle, network topology or component shopping — the four
  competitors own that ground (§2).
- No per-player permissions, wallets, or roles. One account, one balance.
- No staff simulation beyond headcount and locality.
- No competitor operators. Demand is a curve, not a market.
- No tech tree. Everything is available from hour one, gated by capital,
  lead time and permits.
- No named individual residents, and **no resident seen close up** — see
  [art-bible.md](art-bible.md) §6, where this is a tone requirement before it is
  an art-budget one.
- No free-form base building. A fixed site plan.
- No host migration ([multiplayer.md](multiplayer.md) §6).

---

## 12. Systems that should be merged

1. **Nuisance and GNI are one system, documented as two.** The indicators have no
   consumer but GNI, and GNI has no physical input but the indicators. One
   `Community` module.
2. **Seasons is not a system, it is a data source.** No state beyond a clock and
   a weather generator; nothing writes to it. A `Climate` service that Power,
   Cooling and Nuisance sample.
3. **Water is not separable from Cooling.** One producer (evaporative plant), one
   consumer (the water indicator).
4. **Co-op/griefing is not a system either — it is an interaction layer over all
   the others.** It owns exactly two pieces of state: the blame ledger and
   per-player Standing. Everything else it does is *removing* the menus that
   would otherwise wrap the existing systems.

That leaves **four real systems** — Compute/Contracts, Power, Cooling, Community
— plus Climate as a driver, Economy as an accumulator, and the physical
interaction layer over all of it.

---

## Document map

| Document | Contents |
|---|---|
| [systems/compute-contracts.md](systems/compute-contracts.md) | Nodes, racks, four contract archetypes, SLA, Reputation |
| [systems/power.md](systems/power.md) | Grid tiers, dynamic price, solar, wind, battery, diesel, dispatch |
| [systems/cooling-water.md](systems/cooling-water.md) | Four plants, COP curves, water, throttling, damage, heat reuse |
| [systems/nuisance.md](systems/nuisance.md) | The five Program indicators |
| [systems/sentiment.md](systems/sentiment.md) | GNI memory, aggregation, escalation, the Program board, credibility |
| [systems/seasons.md](systems/seasons.md) | The Climate driver |
| [systems/coop-griefing.md](systems/coop-griefing.md) | Physical controls, blame ledger, Standing, griefing vectors, time |
| [economy.md](economy.md) | Capex/opex, per-rack cost, a profitable month and a ruinous one |
| [balance-constants.md](balance-constants.md) | Every number, anchor, confidence |
| [multiplayer.md](multiplayer.md) | Authority, replication, ledger consistency, disconnects |
| [player-experience.md](player-experience.md) | First 15 minutes, first hour, first in-game year |
| [art-bible.md](art-bible.md) | Palette, mesh kit, VFX list, audio list |
| [architecture.md](architecture.md) | How this would be structured in Unity. Described, not built. |
| [scope.md](scope.md) | MVP definition and the ordered cut list |
| [open-questions.md](open-questions.md) | Unsettled decisions, options, recommendations |
| [risks.md](risks.md) | Boring, broken, or unbuildable |
