# Balance constants

Every number in one place. Other documents cite these by name and never restate a
literal value.

**Confidence key**

| Mark | Meaning |
|---|---|
| **A** | Anchored to a public real-world figure. The reference is named. |
| **B** | Derived arithmetically from an **A** constant. |
| **U — UNVERIFIED** | Invented for game feel. No real-world referent exists. First guess only. |

About 40% of this table is **U**, and the co-op section (§8) is **entirely** **U**.
Quantities like "how much does being publicly named cost a colleague" have no true
value. They are tuning knobs and will be wrong until playtested.

---

## 1. Time and world

| Name | Value | Unit | Anchor | Conf |
|---|---|---|---|---|
| `TICK` | 1 | h | Design given. | — |
| `MONTH_TICKS` | 730 | ticks | 8760/12. | B |
| `YEAR_TICKS` | 8760 | ticks | — | B |
| `TOWN_POPULATION` | 4000 | people | Small enough that the site dwarfs it; large enough to hold a referendum. | **U** |
| `TOWN_WATER_DEMAND` | 500 | m³/day | 125 L/person/day × 4000. German household average 127 L/person/day (UBA 2023). | A |
| `TOWN_PEAK_LOAD` | 6000 | kW | 1.5 kW/person incl. commercial. Order of magnitude only. | **U** |
| `REGION_FEEDER_CAPACITY_T0` | 12000 | kW | Typical rural 20 kV feeder segment. Not checked against a real network plan. | **U** |
| `WATER_PRICE` | 2.50 | EUR/m³ | German industrial water + wastewater 2024: €1.80–3.50. | A |

## 2. Hardware and space

| Name | Value | Unit | Anchor | Conf |
|---|---|---|---|---|
| `RACK_HE` | 42 | HE | Standard 19" rack. | A |
| `RACK_CAPEX` | 4500 | EUR | Rack + PDU + cabling, trade pricing. | A |
| `GPU_NODE_HE` | 4 | HE | 8-GPU HGX chassis is 4U–6U. | A |
| `GPU_NODE_P_PEAK` | 10.0 | kW | NVIDIA HGX H100 8-GPU system rated 10.2 kW. | A |
| `GPU_NODE_P_IDLE` | 1.6 | kW | ~16% of peak; large GPU servers idle at 10–20%. | A |
| `GPU_NODE_CAPEX` | 240000 | EUR | 8×H100 server street price 2024: €230k–300k. | A |
| `GPU_NODE_LIFE` | 4 | years | Standard AI-hardware depreciation. | A |
| `CPU_NODE_HE` | 1 | HE | Dual-socket 1U. | A |
| `CPU_NODE_P_PEAK` | 0.6 | kW | Dual-socket EPYC/Xeon under load. | A |
| `CPU_NODE_P_IDLE` | 0.12 | kW | — | A |
| `CPU_NODE_CAPEX` | 9000 | EUR | — | A |
| `HEAT_FRACTION` | 1.0 | kW_th/kW_e | Essentially all electrical input leaves as heat. Physically sound. | A |
| `RACK_P_CAP_AIR` | 30 | kW/rack | Air-cooled racks practically limited to 15–30 kW. | A |
| `RACK_P_CAP_RDHX` | 60 | kW/rack | Rear-door heat exchanger. | A |
| `RACK_P_CAP_LIQUID` | 130 | kW/rack | Direct-to-chip; GB200 NVL72 is ~120 kW. | A |
| `NODE_MAINT_RATE` | 0.05 | capex fraction/yr | Spares and support. Industry rule of thumb. | **U** |
| `DRIVES_PER_NODE` | 8 | — | Used by fire-suppression damage (§8). | **U** |
| `DRIVE_REPLACEMENT_COST` | 900 | EUR | Enterprise NVMe. | A |

## 3. Cooling

`T_wb` = wet-bulb °C, `T_db` = dry-bulb °C.

| Name | Value | Unit | Anchor | Conf |
|---|---|---|---|---|
| `EVAP_COP` | 40 / 15 / 8 / 6 at `T_wb` 12/20/25/30 | kW_th per kW_e | Adiabatic cooling is very efficient at low wet-bulb and collapses as wet-bulb nears return-air temperature. Shape real, points fitted. | **U** (shape A) |
| `EVAP_WUE` | 1.4 / 1.8 / 2.2 / 2.6 at same `T_wb` | L/kWh_IT | US data-centre average WUE ≈ 1.8 L/kWh (LBNL 2021); Google reports 1.1. | A (nominal) |
| `CHILLER_COP` | 5.0 / 3.5 / 2.5 at `T_db` 5/20/35 | kW_th per kW_e | Water-cooled chiller COP 3–6 by condenser temperature. | A |
| `FREECOOL_T_THRESHOLD` | 15 | °C | Free cooling viable below ~15–18 °C dry-bulb. | A |
| `FREECOOL_COP` | 30 | kW_th per kW_e | Dry-cooler fan power only. | A |
| `HEATPUMP_COP` | 3.0 | kW_th per kW_e | Lifting 35 °C return to 70 °C flow; real projects 2.5–3.5. | A |
| `EVAP_CAPEX` | 350 | EUR/kW_th | | **U** |
| `CHILLER_CAPEX` | 600 | EUR/kW_th | | **U** |
| `FREECOOL_CAPEX` | 250 | EUR/kW_th | | **U** |
| `POOL_CONNECTION_CAPEX` | 180000 | EUR | Exmouth, Devon (2023) heated a public pool from a small data centre — the real precedent. Cost not public. | **U** |
| `HEATREUSE_CAPEX` | 1200 | EUR/kW_th | District heating retrofit. Guess. | **U** |
| `HEATREUSE_CONNECTION_CAPEX` | 400000 | EUR | Pipe run to town; flat here, distance-dependent in reality. | **U** |
| `HEAT_PRICE` | 0.08 | EUR/kWh_th | German district-heating wholesale. | A |
| `T_INLET_SAFE` | 27 | °C | ASHRAE A1 recommended upper limit. | A |
| `THROTTLE_DAMAGE_K` | 0.0004 | damage/h per (°C over)² | Pure invention. Governs how fast "run hot" destroys hardware. | **U** |

## 4. Power

| Name | Value | Unit | Anchor | Conf |
|---|---|---|---|---|
| `GRID_PRICE_WINTER` | 0.24 | EUR/kWh | German industrial incl. grid fees, 2024. | A |
| `GRID_PRICE_SUMMER` | 0.14 | EUR/kWh | Same source, seasonal low. | A |
| `RETAIL_MARKUP` | 2.0 | × | German household ≈ 2× industrial (€0.35 vs €0.18). | A |
| `CONGESTION_K` | 1.2 | — | **Invented.** Strength of "your draw raises their bill". | **U** |
| `CONGESTION_GAMMA` | 2.0 | — | **Invented.** Convexity of the same. | **U** |
| `SCARCITY_EVENT_MULT` | 2.5 | × | Dunkelflaute/heatwave spike; German spot has exceeded €0.70/kWh. | A |
| `SOLAR_CAPEX` | 750 | EUR/kWp | Ground-mount commercial PV Germany 2024: €600–900. | A |
| `SOLAR_YIELD_ANNUAL` | 1020 | kWh/kWp/yr | German average specific yield. | A |
| `SOLAR_SUMMER_WINTER_RATIO` | 5.5 | — | June vs December output. | A |
| `WIND_CAPEX` | 1600 | EUR/kW | Onshore Germany €1300–1800. | A |
| `WIND_CF_ANNUAL` | 0.24 | — | German onshore capacity factor. | A |
| `WIND_CF_JAN` / `WIND_CF_JUL` | 0.35 / 0.16 | — | Winter high, summer low. | A |
| `BATTERY_CAPEX` | 280 | EUR/kWh | Grid-scale Li-ion installed 2024. | A |
| `BATTERY_RTE` | 0.88 | — | Round-trip efficiency. | A |
| `BATTERY_CYCLE_LIFE` | 6000 | cycles to 80% | LFP datasheets. | A |
| `DIESEL_CAPEX` | 180 | EUR/kW | Containerised genset. | A |
| `DIESEL_FUEL_RATE` | 0.27 | L/kWh | Genset specific consumption. | A |
| `DIESEL_PRICE` | 1.55 | EUR/L | German commercial diesel 2024. | A |
| `DIESEL_COST_PER_KWH` | 0.42 | EUR/kWh | `DIESEL_FUEL_RATE × DIESEL_PRICE`. | B |
| `DIESEL_START_HOLD` | 3.0 | s | Physical lever hold time. See [coop-griefing](systems/coop-griefing.md). | **U** |

### Grid connection tiers

| Tier | `CAP` (kW) | `CAPEX` (EUR) | `LEAD_TIME` (d) | `SENTIMENT_GATE` | `REGION_CAPACITY` (kW) |
|---|---|---|---|---|---|
| T0 | 250 | — | — | — | 12000 |
| T1 | 1000 | 450000 | 90 | 45 | 12000 |
| T2 | 5000 | 2400000 | 210 | 55 | 20000 |
| T3 | 20000 | 11000000 | 420 | 65 | 45000 |
| T4 | 50000 | 38000000 | 730 | 75 + referendum | 80000 |

Capex and lead times are **U**. Real German grid connections run €200k–2M per MW
with 12–36 month lead times; shape right, values picked so each tier is roughly a
year of saving. `REGION_CAPACITY` grows deliberately slower than the player's
draw — see [systems/power.md](systems/power.md).

## 5. Contracts

Rates are per kWh of **IT load delivered** (`kWh_IT`), not per kWh drawn. Cooling
overhead is the operator's problem, which is the point.

| Name | Value | Unit | Anchor | Conf |
|---|---|---|---|---|
| `RATE_INFERENCE` | 1.35 | EUR/kWh_IT | €13.50/h for a 10 kW 8-GPU node = €1.69/GPU-h. Public H100 rental €1.80–2.80/GPU-h. | A |
| `RATE_TRAINING` | 2.20 | EUR/kWh_IT | Upper end of the same range, lump sum on completion. | A |
| `RATE_SPOT` | 0.45 | EUR/kWh_IT | ~33% of inference; real preemptible discounts are 60–80%. | A |
| `RATE_GREY` | 2.70 | EUR/kWh_IT | 2 × inference, per the brief. | B |
| `SLA_INFERENCE` | 0.995 | fraction | Normal colo/inference SLA. | A |
| `SLA_TRAINING` | 0.98 | fraction | Training tolerates interruption; the deadline is the constraint. | A |
| `SLA_PENALTY_K` | 12 | × monthly payout per unit shortfall | **Invented.** 0.5 pp shortfall on a 99.5% SLA costs 6% of the month. | **U** |
| `SLA_PENALTY_CAP` | 0.5 | fraction of monthly payout | Real SLAs cap remedies at 10–100% of period fees. Without it, penalties exceed contract value. | A (cap exists) / **U** (value) |
| `CHECKPOINT_INTERVAL_DEFAULT` | 1 | h | **Shortened from 6 h for co-op** — see [coop-griefing](systems/coop-griefing.md) §6. Real runs checkpoint every few hours. | **U** |
| `CHECKPOINT_OVERHEAD` | 0.03 | throughput fraction | Checkpointing costs a few percent. | A |
| `REPUTATION_START` | 50 | 0–100 | | **U** |
| `REPUTATION_GAIN_MONTH` | 0.5 | per contract-month met | | **U** |
| `REPUTATION_LOSS_BREACH` | 8 | per breach × severity | | **U** |
| `REP_GATE_TRAINING_MED` / `_LARGE` | 60 / 75 | 0–100 | | **U** |

## 6. Nuisance channels

All **U**. No external anchor exists for any of it.

| Name | Value | Unit |
|---|---|---|
| `W_NOISE` / `W_AIR` / `W_WATER` / `W_PRICE` / `W_VISUAL` | 0.20 / 0.20 / 0.25 / 0.20 / 0.15 | weight (Σ = 1.0) |
| `HALFLIFE_NOISE` | 45 | days |
| `HALFLIFE_AIR` | 90 | days |
| `HALFLIFE_WATER` | 120 | days |
| `HALFLIFE_PRICE` | 60 | days |
| `HALFLIFE_VISUAL` | 30 | days |
| `NIGHT_NOISE_MULT` | 2.5 | × between 22:00–06:00 (that a night penalty exists is standard in noise regulation) |
| `WIND_TOWARD_MULT` / `WIND_AWAY_MULT` | 3.0 / 0.3 | × on the air channel |
| `FILMED_MULT` | 2.5 | × on any nuisance sampled while residents are filming |

## 7. Good Neighbor Index (Sentiment)

All **U**.

| Name | Value | Unit |
|---|---|---|
| `GNI_START` | 62 | 0–100 |
| `GNI_ADJUST_RATE` | 0.004 | per tick (≈10-day time constant) |
| `BULLETIN_COST` | 2000 | EUR |
| `BULLETIN_BASE_EFFECT` | 3.0 | GNI points |
| `CREDIBILITY_LOSS_PER_USE` | 0.15 | fraction |
| `CREDIBILITY_RECOVERY` | 0.02 | per week |
| `CREDIBILITY_MOCKERY_THRESHOLD` | 0.20 | fraction |
| `LOCAL_HIRE_GNI_PER_FTE` | 0.9 | GNI points per local FTE |
| `LOCAL_HIRE_WAGE_PREMIUM` | 0.18 | fraction over non-local |
| `PROGRAM_BENEFIT_EFFICIENCY` | 0.00008 | GNI points per EUR/month |
| `DISMISSAL_GNI_PER_FTE` | 2.2 | GNI points lost per local FTE dismissed (asymmetric: dismissal hurts ~2.4× what hiring helps) |

### Escalation thresholds (all **U**)

| Stage | In-fiction name | GNI band |
|---|---|---|
| Content | — | ≥ 70 |
| Complaints | "inbound community correspondence" | 55–70 |
| Petition | "community signature initiative" | 40–55 |
| Protest at gate | "unscheduled gate activity" | 25–40 |
| Injunction | "administrative pause" | 15–25 |
| Sabotage | "unauthorised third-party interference" | 5–15 |
| Referendum | "community consultation event" | via petition |

## 8. Co-op, blame and griefing

**Entirely invented. Zero anchors. These decide whether griefing is funny or
session-ending, and they are the numbers I have least confidence in anywhere in
this design.**

| Name | Value | Unit | Note |
|---|---|---|---|
| `PLAYERS_MIN` / `PLAYERS_MAX` | 2 / 5 | — | Brief. |
| `STANDING_START` | 50 | 0–100 | Per player. |
| `STANDING_LOSS_NAMED` | 14 | points | Cost of being named in a Program bulletin. |
| `STANDING_RECOVERY` | 0.6 | points/day | |
| `NAMING_GNI_EFFECT` | 4.0 | GNI points | Gain from naming a responsible employee. |
| `NAMING_DECAY` | 0.55 | × per subsequent use | Fourth naming is worth 0.66 points. |
| `FALSE_NAMING_PENALTY` | 3.0 | × `CREDIBILITY_LOSS_PER_USE` | If footage contradicts the ledger. |
| `STANDING_CONSULTANT_THRESHOLD` | 20 | 0–100 | Below this, dismissal becomes available. |
| `CONSULTANT_COST_MULT` | 1.9 | × normal payroll | Rehiring a dismissed player as a contractor. |
| `CONSULTANT_GNI_PENALTY` | 0.35 | GNI points/day | Ongoing, while a dismissed-and-rehired player is on site. |
| `SUPPRESSION_DISCHARGE_DRIVE_LOSS` | 0.55 | fraction of drives in the room | Inert-gas discharge acoustic shock. Anchored to ING Bank Bucharest 2016, where a discharge destroyed dozens of drives. **The fraction is invented.** |
| `SUPPRESSION_ROOM_SCOPE` | 1 | rooms | Blast radius capped at one room. See [risks.md](risks.md) §5. |
| `DELIVERY_LEAD_MIN` / `_MAX` | 3 / 90 | days | By item class. |
| `TWO_KEY_THRESHOLD` | 500000 | EUR | Orders above this need two players at two interlocks. **My invention** — see [open-questions.md](open-questions.md) Q4. |
| `TWO_KEY_WINDOW` | 8 | s | Both interlocks within this window. |
| `TIME_HOLD_OVERRIDE` | 180 | s | After this, a majority can override a held clock. |
| `IDLE_THRESHOLD` | 6 | s | No interaction for this long = idle, for fast-forward gating. |
| `CAMERA_PRESENCE_DAY` / `_NIGHT` | 0.35 / 0.08 | probability residents are filming | |
| `CAMERA_PRESENCE_INCIDENT` | 0.85 | probability | Rises sharply once something loud or visible is already happening. |

---

## The three most likely to be wrong

1. **`RATE_INFERENCE` (1.35 EUR/kWh_IT).** Anchored to *retail* GPU rental, which
   bundles hardware amortisation and margin. As a wholesale compute rate it
   probably overstates revenue by 2–3×. Every other economic figure is calibrated
   against it, so the whole economy is wrong by a constant factor if it is.
2. **The co-op blast-radius cluster** — `SUPPRESSION_DISCHARGE_DRIVE_LOSS`,
   `STANDING_LOSS_NAMED`, `NAMING_GNI_EFFECT`, `CHECKPOINT_INTERVAL_DEFAULT`.
   Entirely invented, and they decide whether one player's five-second action
   costs the session five minutes or five in-game months. There is no anchor and
   no way to reason about it on paper.
3. **`GNI_ADJUST_RATE` and the five `HALFLIFE_*` values.** These decide whether
   the escalation ladder is ever seen. Too short and the town is toothless; too
   long and the first bad summer ends the session before anyone understands why.

Close fourth: `CONGESTION_K` / `CONGESTION_GAMMA`, which carry the entire "your
draw raises their bill" satire and are pure invention.
