# Scope

The smallest version that still proves the loop is fun, and the order in which
things get cut when — not if — the schedule slips.

Team assumption: **2–4 people, part-time, evenings.** Team size was a placeholder
in the brief; see [open-questions.md](open-questions.md) Q14.

---

## 1. What has to be true for the MVP to prove anything

The MVP is not "a small version of the game". It is **an experiment with a
hypothesis**, and the hypothesis is:

> Two or three people, given a shared datacenter with no permissions and a town
> that remembers, will produce a funnier and more interesting hour than either a
> single-player version or a datacenter sim without a town.

Everything in the MVP is there to test that sentence. Everything not needed to
test it is cut, however good it is.

Three things must be present or the experiment answers nothing:

1. **The physical control layer**, because it is the wedge. If actions are menu
   items, this is a different game and the test is void.
2. **The five indicators with memory**, because diagnosis-over-months is the
   other wedge. A single sentiment number does not test it.
3. **A season boundary**, because the loop's whole shape is "your spring
   decisions kill you in July". An MVP that never reaches July proves nothing.

---

## 2. MVP definition

**Target: 2–3 players, one in-game year, 90 minutes, one site.**

### In

| Area | MVP content |
|---|---|
| **Players** | 2–3. Host + clients, no drop-in mid-session. |
| **Site** | One building, three rooms, fixed plan. T0 → T1 → T2, two permit gates. |
| **Compute** | GPU nodes only. **No CPU nodes.** |
| **Contracts** | **Inference and training only.** Both archetypes, full SLA and checkpoint logic. |
| **Cooling** | **Evaporative, chiller, free cooling.** All three, with real COP curves. |
| **Water** | Full model, including drought. |
| **Power** | Grid cap + tiers, dynamic price with the congestion term, **diesel**. |
| **Indicators** | **All five.** They are formulas, not features — cutting them saves days and destroys the wedge. |
| **GNI** | Full memory model, escalation to **Protest**. |
| **Program board** | ~8 measures: fence, illumination mast, guards, acoustic barrier, closed-loop, pool heat reuse, local hiring, Program benefit dial. Mixed on one list, unlabelled. |
| **Bulletins** | Full, including credibility decay and the **responsible employee** field. |
| **Co-op controls** | Diesel lever, rack switches, cooling mode selectors, water valves, Run Hot, fire suppression, procurement desk, contract terminal, Program board, bulletin terminal, benefit dial, hiring roster. **12 of the 18.** |
| **Blame** | Ledger + Standing + naming + fact-check. |
| **Seasons** | Full year: all four, all climate signals, **wind direction**, heatwave and drought events. |
| **Time** | Shared clock, idle-gated fast-forward, named holder. |
| **Residents** | Fence-line silhouettes, phone glow, camera presence, local news ticker. |
| **Art** | 15-mesh kit, 12-colour palette, plume + steam + suppression discharge + beacons, core audio + fence-line mix. |

### Out

| Cut | Why it is safe to cut |
|---|---|
| Spot contracts | Third archetype; the marginal-cost lesson can wait |
| Grey market and the attention meter | Recommended cut in general — see §5 |
| 4th and 5th players | 3 proves the social dynamic; 5 is a networking and legibility problem, not a design one |
| Wind turbines, batteries | Two more power sources; the grid/solar/diesel triangle already forces the seasonal lesson |
| District heat reuse | Keep the **pool** — cheaper, better, and it carries the hostage mechanic |
| CPU nodes | One node type proves everything two do |
| Injunction, sabotage, referendum | Escalation to Protest is enough to prove the ladder. **Ends at "GNI hits 0 → run over"** |
| Rebrand, Engagement Sessions, open days | Good gags, not load-bearing |
| Hand-routed runs | See §4 — the riskiest thing in the design to build early |
| Drop-in / rejoin | Session is a session |
| Save migrations | Accept breaking saves during development, loudly |
| Snow, fog, rain VFX | Seasonal grade only |

### The MVP's success test

Not "is it finished" but: **do three people, after 90 minutes, argue about who
was responsible for July?**

If yes, build the rest. If they instead argue about the UI, or nobody touches
anything dangerous, or the town never enters the conversation, the design is
wrong and no amount of remaining content fixes it.

---

## 3. Effort estimate

Evening-weeks, one person at ~8 h/week.

| Component | MVP | Full |
|---|---|---|
| `Game.Sim` core: state, tick, four systems | 10–14 | 14–18 |
| Climate + events | 3–4 | 4–5 |
| Contracts, SLA, Reputation | 3–4 | 5–7 |
| Indicators, GNI, memory, escalation | 5–7 | 6–8 |
| Economy and financing | 3–4 | 3–4 |
| **Physical control layer (12 / 18 controls)** | **5–8** | 8–11 |
| **Blame ledger + Standing + naming** | **3–5** | 4–6 |
| **Networking: host authority, replication, intents** | **8–12** | 12–18 |
| Save/load | 2–3 | 3–4 |
| Balance harness + first tuning pass | 4–6 | 6–9 |
| UI | 10–15 | **15–25** |
| 3D site + plume + VFX | 5–8 | 6–10 |
| Audio + fence-line mix | 3–5 | 3–5 |
| Generated text | 3–5 | 5–8 |
| Playtesting and rebalancing | 6–10 | **10–20** |
| **Total** | **73–110** | **104–158** |

At 3 people × 8 h/week, the MVP is **6–9 months of calendar time** and the full
game is comfortably 18–24 months.

**Networking is the line item that separates this from the single-player
version** and it is 8–12 evening-weeks before anything is fun. It is also
unavoidable: co-op is the wedge.

---

## 4. Ordered cut list

When the schedule slips, cut in this order. Each entry says what is lost.

| # | Cut | Loses | Saves |
|---|---|---|---|
| 1 | **Grey market + attention meter** | One joke, one desperate-money option | 2–3 wk |
| 2 | **Hand-routed runs** | Inherited mess as a griefing vector | 3–5 wk |
| 3 | **5th player** (cap at 4) | Little; legibility improves | 1–2 wk |
| 4 | **Wind turbines** | The solar/wind seasonal complement | 1–2 wk |
| 5 | **Batteries** | Peak shaving and the night-arbitrage gag | 2–3 wk |
| 6 | **District heat reuse** (keep the pool) | Winter revenue; pool keeps the mechanic | 1–2 wk |
| 7 | **Referendum** → GNI 0 ends the run | The best ending in the design | 2–3 wk |
| 8 | **Spot contracts** | The marginal-cost lesson | 1 wk |
| 9 | **Drop-in / rejoin** | Convenience | 2–3 wk |
| 10 | **Sabotage stage** | The suppression spiral's payoff | 1–2 wk |
| 11 | **CPU nodes** | Almost nothing | 0.5 wk |
| 12 | **Rebrand, Engagement Sessions** | Two good gags | 1–2 wk |

### What must never be cut

Below this line the game stops being the thing that differentiates it:

- **The physical control layer.** Menu-ify one dangerous action and the wedge is
  gone. This is the first thing a tired team will propose and it must be refused.
- **The five indicators, separately tracked, with memory.** They are cheap
  (formulas) and they are the entire reason the town is a system rather than a
  meter.
- **Wind direction as a real variable.** It is one signal and it carries the best
  mechanic in the game.
- **The blame ledger and the responsible-employee field.** Without it the co-op
  is just co-op.
- **The unlabelled Program board.** Labelling the two mitigation classes would be
  a one-line change that removes the lesson the whole design exists to teach.
- **The permit re-check at completion.** One `if`. It is the progression gate.

---

## 5. What I would cut from the full design regardless of schedule

**The grey-market archetype's attention meter.** It is the only revenue lever
that does not route through heat, water, noise or price, it duplicates the
"cheap now, expensive later" shape that suppression carries better, and in a
design already tracking GNI, Reputation and per-player Standing it is a fourth
meter.

Keep grey contracts as a straight Reputation trade — pays double, costs
Reputation continuously. Same fiction, no new meter, and it finally touches a
system that matters. Detail in [open-questions.md](open-questions.md) Q3.

---

## 6. Beyond MVP: order of addition

1. Injunction and sabotage stages, then the referendum (completes the ladder)
2. Players 4–5, drop-in and rejoin
3. Spot contracts, CPU nodes
4. Batteries, then wind
5. District heat reuse
6. Remaining 6 physical controls, hand-routed runs
7. Rebrand, Engagement Sessions, remaining gags
8. Seasonal VFX (snow, fog, rain)
9. A second site plan
