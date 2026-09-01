# System: Seasons — the Climate driver

**Seasons is not a system. It is a data source.**

No state beyond a clock and a weather generator, nothing writes to it, it makes
no decisions. It should be a `Climate` service that Power, Cooling and Nuisance
sample. Documented here because the brief lists it as a system; implemented as a
driver. See [GDD.md](../GDD.md) §12.

**Inputs:** tick, RNG seed.
**Outputs:** `T_db`, `T_wb`, wind speed, **wind direction**, irradiance, drought
state, scarcity events. Read-only for everyone else.

---

## 1. Signals

Smooth seasonal base plus a bounded stochastic term, so a year is recognisable
but not identical between sessions.

| Signal | Unit | Jan | Apr | Jul | Oct | Consumers |
|---|---|---|---|---|---|---|
| `T_db` | °C | 2 | 12 | 26 | 11 | Cooling (chiller COP, free-cooling gate) |
| `T_wb` | °C | 1 | 9 | 20 | 9 | Cooling (evaporative COP and WUE) |
| Wind speed | m/s | 7.2 | 5.8 | 4.1 | 6.3 | Power (turbines), Nuisance (turbine noise) |
| **Wind direction** | sector | SW dominant | variable | E/NE more common | SW dominant | **Nuisance (air)** |
| Irradiance | kWh/m²/d | 0.8 | 3.9 | 5.4 | 1.7 | Power (solar) |
| Precipitation | mm/mo | 55 | 45 | 70 | 60 | Drought state |

`T_wb` is **derived** from `T_db` and a seasonal humidity curve, never generated
independently, so the two cannot contradict each other. Wet-bulb must never
exceed dry-bulb; a bug there silently makes evaporative cooling free.

### Wind direction

A seasonal rose: south-westerlies dominate in winter, **easterlies markedly more
common in summer.** The town occupies a fixed 90° sector from the site.

This is not decoration. It is the multiplier on the air indicator
(`WIND_TOWARD_MULT` 3.0 vs `WIND_AWAY_MULT` 0.3) and therefore a factor of ten on
the cost of running a generator. And because easterlies are more common in
summer, and summer is when cooling failures force diesel starts, **the season
that most requires diesel is also the season that most often points it at the
houses.** That correlation is deliberate and must not be smoothed out.

A 48-hour wind forecast is on the HUD from hour one. It is accurate. What the
group does with it is the point.

---

## 2. Four seasons, four different games

**No single build order survives all four.** Each quarter binds on a different
resource.

### Spring — the build window
Free cooling mostly available, moderate wind, rising solar, no drought, falling
grid prices. Nothing is scarce.

This is when construction happens, and its danger is that it is *pleasant*. A
group commissioning in spring sees excellent PUE, low bills and a content town,
and sizes summer's plant against spring's numbers.

### Summer — the capacity crisis
- Free cooling **unavailable** (`T_db` > 15 °C most hours).
- `EVAP_COP` collapses 40 → 6–8; `EVAP_WUE` rises 1.4 → 2.6 L/kWh_IT.
- `CHILLER_COP` falls 5.0 → 2.5; cooling power roughly doubles.
- **Wind at annual minimum** (`WIND_CF_JUL` 0.16). Turbines nearly idle.
- Solar peaks — nowhere near enough to cover the extra cooling draw.
- **Drought risk** peaks, restricting the water evaporative cooling needs.
- Heatwaves push `scarcity` toward `SCARCITY_EVENT_MULT` 2.5.
- Fan speeds peak → `N_noise` peaks → and people sleep with windows open.
- Easterlies more common → the plume more often reaches the town.
- Residents are outside more → `CAMERA_PRESENCE_DAY` is at its practical maximum.

**Everything fails at once and it all traces back to one variable.** Grid price is
*low* in summer, which is the cruel part: power is cheap exactly when you cannot
get rid of the heat it makes.

### Autumn — the deadline season
Free cooling returns, wind rises, solar falls, drought lifts. Conditions are
good, which is why training contracts cluster with year-end deadlines. Pressure
is commercial rather than physical: the site can run flat out, so it will, and
the overbuild that made summer survivable now sits idle unless it is filled.

### Winter — the cost crisis
- Free cooling nearly free; PUE at its annual best.
- **Wind peaks** (`WIND_CF_JAN` 0.35). Turbines earn their capex here or not at all.
- **Solar near zero** for four months.
- `GRID_PRICE_WINTER` €0.24, plus Dunkelflaute at up to ×2.5.
- **Heat-reuse revenue peaks** — district demand at maximum.
- The town's own bills peak, so `N_price` grievance lands hardest.

Winter does not threaten capacity; it threatens cash. A group that solved summer
with chillers now runs them cheaply but pays the year's highest unit price to do
it, while the town pays double that and notices.

---

## 3. Events

Sampled from climate state rather than scheduled, so they always have a traceable
cause.

| Event | Trigger | Duration | Effect |
|---|---|---|---|
| **Heatwave** | `T_db` > 30 °C sustained | 3–10 d | Evaporative COP to floor; chiller COP 2.5; `scarcity` up; cooling may fall below `Q_IT` |
| **Drought** | Cumulative precipitation deficit | 4–12 wk | Water restricted; `drought_mult` up to 3.0 |
| **Dunkelflaute** | Winter, low wind + low irradiance | 3–9 d | Solar and wind near zero; `scarcity` 2.5; batteries drain; diesel tempting |
| **Storm** | High wind | 1–2 d | **Turbines cut out entirely** above cut-out speed; possible grid outage |
| **Cold snap** | `T_db` < −8 °C | 3–7 d | Free cooling excellent; heat-reuse revenue peaks; grid price spikes |
| **Shoulder anomaly** | Spring/autumn | 1–5 d | Small — exists so shoulder seasons are not simply safe |

**Drought is the most important event** because it is the only one that removes an
option you already paid for. A site built entirely around evaporative cooling,
during a drought, has no legal way to remove its heat: punitive tariffs,
emergency chiller hire at several times capex, or throttle into breach. That
trilemma is worth more than any three other events combined.

Storms cutting turbines *out* rather than merely reducing them is small, accurate
and reliably surprising. Players assume more wind is more power.

---

## 4. Coupling

| To | Via |
|---|---|
| **Cooling** | `T_db` gates free cooling and sets chiller COP; `T_wb` sets evaporative COP and WUE. The dominant input to the whole system. |
| **Power** | Irradiance → solar; wind speed → turbines; temperature and events → `p_base` and `scarcity` |
| **Nuisance — air** | **Wind direction**, the ten-fold multiplier on diesel harm |
| **Nuisance — water** | Drought → `drought_mult` |
| **Nuisance — noise** | Wind speed → turbine noise; cooling demand → fan speed |
| **Co-op** | Weather decides when the physical controls become tempting. Nobody touches the diesel lever in April. |
| **Compute** | **None directly.** Contracts do not care about the weather; that asymmetry is why you get caught by it. |

Climate writes nothing and reads nothing. It is a pure function of
`(tick, seed)`, which makes it trivially testable, replayable, and — importantly
for balance work — **sweepable**: the same site can be run against a hundred
generated years to find the one that kills it
([architecture.md](../architecture.md) §7).
