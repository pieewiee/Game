# System: Program indicators (nuisance channels)

Five separate meters, each measuring one way the site harms the town. In the
fiction they are the *Program indicators* — the operator's own reporting
framework, which is why they exist at all and why they are phrased as metrics
rather than as complaints.

They exist so players can **diagnose what they broke.** One number would say you
are disliked; five say why, and that is the difference between a puzzle and a
punishment.

Physically one module with [sentiment.md](sentiment.md) — see
[GDD.md](../GDD.md) §12.

**Inputs:** fan and compressor power, diesel runtime, wind vector, water draw,
`p_resident`, installed structures, time of day, drought state, camera presence.
**Outputs:** five values `N_i ∈ [0,100]`, sampled every tick, consumed only by
GNI.

Each channel is scaled so **100 means "an ordinary person would move house over
this"**, and 100 must be reachable in normal play. A channel that saturates at 40
is one the player never learns to read.

**The filming multiplier applies to all five:**

```
N_i_effective = N_i * (residents_filming ? FILMED_MULT : 1.0)      (2.5)
```

See [coop-griefing.md](coop-griefing.md) §7. Timing is a first-class decision.

---

## 1. Noise

```
L_site  = 10 * log10( P_fan/P_fan_ref + 2.5 * P_chiller/P_chiller_ref
                      + 8.0 * diesel_running + 1.2 * turbine_count )
N_noise = clamp(noise_scale * max(0, L_site - L_wall_reduction), 0, 100)
          * (is_night(hour) ? NIGHT_NOISE_MULT : 1.0)
```

`NIGHT_NOISE_MULT` = 2.5 between 22:00 and 06:00.

- **Fan power scales with cooling demand**, which peaks in summer, when people
  sleep with the windows open. Summer nights are the worst case by a wide margin
  and nothing about the site's *load* has changed.
- **Diesel dominates.** The 8.0 weight means one generator outweighs the entire
  cooling plant. Running it at 03:00 is ~20× worse than at 14:00.
- **Noise walls are a fixed dB reduction, not a multiplier** — excellent against a
  moderate source, nearly useless against a generator. Correct real-world
  behaviour, and it stops you buying your way out of the diesel decision.

**Program measures:** acoustic barrier (genuine), variable-speed fans (genuine),
generator enclosure (genuine, expensive), rescheduling diesel to daytime (free,
and everyone does it).

---

## 2. Air

```
N_air = diesel_kWh_this_tick * AIR_EMISSION_FACTOR * wind_mult
wind_mult = in_west_sector(wind_direction)  ? WIND_TOWARD_MULT (3.0)   # houses, 270° ± 45°
          : in_north_sector(wind_direction) ? TOWN2_WIND_MULT  (2.0)   # apartments, 0° ± 30°
                                            : WIND_AWAY_MULT   (0.3)
```

**Wind direction is a real simulated variable**, sampled from a seasonal rose
([seasons.md](seasons.md)). There are two residential sectors: the town houses
west of the site (270° ± 45°, ×3.0) and the apartment blocks to the north
(0° ± 30°, ×2.0 — fewer, farther residents); the west sector wins where they
would overlap. **A factor of ten** between the same generator run on two
different afternoons — and up to 25× once `FILMED_MULT` is applied.

This is the design's sharpest mechanic. The wind forecast is on the HUD. Players
will learn to read it, will start timing generator runs to the wind, and will at
some point notice what they have become. The optimisation is *correct* — it
genuinely reduces harm — and it is also obviously monstrous.

`HALFLIFE_AIR` = 90 days, the longest source memory. A bad August is still
costing GNI when the November permit is re-checked.

**Program measures:** don't run diesel (genuine, expensive — batteries or
accepted SLA breaches), exhaust scrubbing (partial), HVO/renewable diesel (large
reduction at ~2× fuel cost — a rare "pay money, harm nobody" option that should
exist precisely so players can choose not to buy it), or timing to the wind
(free, effective, damning).

---

## 3. Water

```
N_water = clamp(100 * (V_player_m3day / TOWN_WATER_DEMAND) * drought_mult, 0, 100)
```

`TOWN_WATER_DEMAND` = 500 m³/day; `drought_mult` up to 3.0 under restrictions.

The ratio reaches 1.73 at T3, so **this channel saturates before the site reaches
full scale** — deliberately. Beyond a certain size the town's objection cannot get
worse because it is already total; what changes is that it can no longer be
argued with. A saturated water channel is a permanent −25 GNI (`W_WATER` = 0.25)
that must be compensated elsewhere.

`HALFLIFE_WATER` = 120 days, the longest memory of all. Water grievances outlast
the drought that caused them by a season.

**Program measures:** chillers (genuine, moves harm to price), hybrid dry/adiabatic
plant that runs dry below a threshold (genuine, expensive, the correct answer),
rainwater harvesting (partial — and the single best bulletin subject in the
game), heat reuse (reduces the heat needing removal at all).

---

## 4. Price

```
N_price = clamp(PRICE_SENSITIVITY * (p_resident - p_baseline) / p_baseline, 0, 100)
```

`p_baseline` is the town's remembered pre-site price, drifting slowly with the
national trend **so the operator is not blamed for inflation they did not
cause.** If they were, the channel would be unmanageable and the satire would tip
into the "residents are irrational" failure the tone rules forbid.

**Diffuse, delayed and hard to attribute** — which is what makes it interesting.
Nobody sees a price rise the way they see a plume. It accumulates quietly for
months and arrives as a fully-formed political position, too late to fix quickly
because `HALFLIFE_PRICE` = 60 days and the underlying `load_ratio` is structural.

It is also the only channel you **share**: every euro on the town's bill is a euro
on yours, at half the rate. Reducing your draw to help the town also cuts your
largest opex line. This should never be pointed out.

**Program measures:** on-site solar and wind (genuine, and the only measure that
is also straightforwardly profitable), batteries for peak shaving, efficiency
anywhere, grid tier upgrades (which raise `REGION_CAPACITY` by less than they
raise your draw — the relief is temporary by construction).

---

## 5. Visual

```
N_visual = clamp(Σ fortification_points - Σ screening_points, 0, 100)
```

| Structure | Points | Filed on the Program board as |
|---|---|---|
| Perimeter fence | +4 | Program safety measure |
| Electric fence | +12 | Program safety measure |
| Searchlight mast (each) | +9 | Program illumination measure |
| Camera mast (each) | +3 | Program safety measure |
| Guard post | +6 | Program safety measure |
| Cooling plant, exposed | +5 | — |
| Wind turbine (each) | +7 | Program investment |
| Solar array (per MW) | +2 | Program investment |
| Diesel stack | +4 | — |
| **Uninstalled stock in the yard** | +1 per €100k | — |
| Earth berm | −6 | Program landscape measure |
| Tree screening (matures over 3 y) | −4 → −14 | Program landscape measure |
| Architectural cladding | −8 | Program landscape measure |

**Suppression is permanent.** Unlike the other four channels there is no physical
process that stops — a fence is up until taken down. So although
`HALFLIFE_VISUAL` = 30 days is the *shortest* memory, the source never decays and
the memory converges to the full value and stays. **A safety measure is a
permanent GNI tax.**

Two consequences:

- **Local news references the compound.** Above 45, generated headlines describe
  the site's appearance rather than its activity. Above 70 that is all they
  describe.
- **Community Engagement Sessions scale with `(1 - N_visual/100)`.** At
  `N_visual` 80 a session returns 20% of its effect — and inviting the town to
  look at a prison is arguably worse than not inviting them.

**Note the uninstalled-stock row.** It is what makes a griefing procurement order
([coop-griefing.md](coop-griefing.md) §2) create *work* rather than pure damage:
€2M of chillers sitting in the yard is +20 `N_visual` until somebody installs
them.

Tree screening maturing over three in-game years is the design's one piece of
unambiguous optimism: cheap, effective, and it requires you to have planted it
long before you needed it.

---

## 6. Coupling summary

| Indicator | Sourced from | Weight | Half-life | Shape |
|---|---|---|---|---|
| Noise | Cooling, Power, Co-op | `W_NOISE` 0.20 | 45 d | Spiky, nocturnal, seasonal |
| Air | Power (diesel only) | `W_AIR` 0.20 | 90 d | Spiky, wind-gated, long memory |
| Water | Cooling (evaporative only) | `W_WATER` 0.25 | 120 d | Structural, saturating, longest |
| Price | Power (grid draw) | `W_PRICE` 0.20 | 60 d | Diffuse, delayed, campaign-long |
| Visual | Power, Cooling, GNI (suppression), procurement | `W_VISUAL` 0.15 | 30 d | Permanent once built |

**No indicator is sourced from Compute.** The town does not care what you
compute. Every grievance is physical. This is the design's central tone
commitment expressed as a data-flow constraint, and it is why grey-market
clients — whose only cost is regulatory — sit outside the loop
([open-questions.md](../open-questions.md) Q3).

All five weights sum to 1.0. All five are **UNVERIFIED** and none has a
real-world anchor, because "how many times worse is a diesel plume than a water
shortage" has no answer outside a specific community's politics. They will be
wrong. Keeping the channels *separate* is what makes them independently tunable
once playtesting says so.
