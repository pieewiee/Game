# Economy

Where the money comes from, where it goes, and two worked months — one that works
and one that does not. The ruinous one is a co-op month, because in this game
that is what a ruinous month looks like.

Constants cited by name from [balance-constants.md](balance-constants.md).

---

## 1. Financing

400 GPU nodes is €96M of hardware and no group starts with that. **Hardware is
debt-financed**, as real operators do it.

```
downpayment  = 20% of capex
monthly_debt = financed * r / (1 - (1+r)^-n),  r = 9%/12,  n = 48 months
```

Per GPU node: €240,000 capex → €48,000 down, €192,000 financed, **€4,777/month =
€6.54/h.**

Two consequences that shape everything:

1. **Debt service is the largest single cost line, about 3× power.** This is not
   primarily a power-intensive business; it is a capital-intensive business that
   happens to need power. Power matters as a *constraint* (the grid cap), not as
   a cost. Confirmed as a design choice in
   [open-questions.md](open-questions.md) Q2.
2. **Debt service does not stop when nodes are idle.** A node earning nothing
   still costs €6.54/h. That is why spot contracts exist and why a missed
   training deadline is catastrophic rather than disappointing.

**Co-op:** there is one balance and no per-player wallets. Orders above
`TWO_KEY_THRESHOLD` = €500,000 need two players at two interlocks
([coop-griefing.md](systems/coop-griefing.md) §2) — the only friction on shared
money, and it is physical rather than a permission.

---

## 2. What a rack costs

One liquid-cooled rack: ten GPU nodes, 40 of 42 HE, 100 kW IT, within
`RACK_P_CAP_LIQUID`.

### To build

| Item | Cost | Share |
|---|---|---|
| 10 × GPU node | €2,400,000 | 85.6% |
| Cooling plant share (100 kW_th, free + evaporative) | €60,000 | 2.1% |
| Power distribution, switchgear, UPS share | €40,000 | 1.4% |
| Building fit-out share | €300,000 | 10.7% |
| Rack, PDU, cabling | €4,500 | 0.2% |
| **Total** | **€2,804,500** | |
| **Cash required** | **€884,500** | |

The nodes are almost the entire number. Everything the group argues over —
cooling type, fences, screening, the benefit dial — is rounding error against the
silicon. That is true in reality and worth noticing.

### To run, per hour, at 100% utilisation

Autumn, free cooling, `p_grid` ≈ €0.199/kWh:

| Line | €/h |
|---|---|
| Debt service (10 nodes) | 65.40 |
| Grid power (100 kW IT + 3.3 cooling + 2.2 aux) | 21.03 |
| Maintenance (`NODE_MAINT_RATE` 5%/yr) | 13.70 |
| Payroll share (22 FTE / 40 racks) | 3.86 |
| Building + plant amortisation share | 2.28 |
| Water (free cooling) | 0.00 |
| **Full cost** | **€106.27/h** |
| **Marginal cost** (power + water only) | **€21.03/h** |

### The most useful table in this document

| Contract | Revenue/h | vs full cost | vs marginal cost |
|---|---|---|---|
| Inference | €135.00 | **+28.73** | +113.97 |
| Training | €220.00 | **+113.73** | +198.97 |
| Spot | €45.00 | **−61.27** | **+23.97** |
| Grey | €270.00 | +163.73 | +248.97 |

**Spot loses money against full cost and makes money against marginal cost.** The
fixed lines are sunk the moment the rack exists, so any rate above €21.03/h is
worth taking on capacity that would otherwise idle. A group evaluating spot
against full cost will never take it and will bleed €65/h per rack on idle debt
service instead.

This is the most transferable idea in the game and it should be taught by the
numbers, never by a tooltip.

In a July heatwave with chillers and `scarcity` at 2.5, the same rack's marginal
cost rises to ~€52/h — spot becomes marginal, and somebody has to notice.

---

## 3. Capex vs opex

| Category | Capex | Recurring | Lead time |
|---|---|---|---|
| GPU node | €240,000 (20% cash) | €4,777/mo debt + 5%/yr maintenance | 30–90 d |
| Rack | `RACK_CAPEX` | — | 14 d |
| Evaporative plant | `EVAP_CAPEX`/kW_th | Water + power | 90 d |
| Chiller plant | `CHILLER_CAPEX`/kW_th | Power | 90–180 d |
| Free cooling | `FREECOOL_CAPEX`/kW_th | Fan power | 60 d |
| Heat reuse — district | €4M+ | Heat-pump power; **earns** revenue | 240 d |
| Heat reuse — pool | €180k | Heat-pump power | 90 d |
| Solar | `SOLAR_CAPEX`/kWp | ~€12/kWp/yr | 120 d |
| Wind | `WIND_CAPEX`/kW | ~€45/kW/yr | 300 d |
| Battery | `BATTERY_CAPEX`/kWh | Degradation (capex paid in arrears) | 90 d |
| Diesel | `DIESEL_CAPEX`/kW | `DIESEL_COST_PER_KWH` + O&M | 30 d |
| Grid tier | Tier table | — | 90–730 d |
| Program safety measures | €25k–120k | €1k–18k/mo | 7–30 d |
| Program investments | €80k–4M | Varies | 60 d–3 y |
| Staff | — | €4,600/FTE/mo loaded, +18% if local | Instant; **frozen at Protest** |
| Program benefit | — | Dial setting, €/mo | — |
| Drive replacement | `DRIVE_REPLACEMENT_COST` €900 each | — | 7 d |

**Lead times are the real currency.** Money can be borrowed; 300 days cannot. A
group realising in June that they need chillers will have them in December.

---

## 4. Worked example: a good month

**Site:** T2, `CAP` 5,000 kW. 400 GPU nodes, 40 liquid racks, 4,000 kW IT peak.
Free cooling + evaporative. 22 staff, 14 local. 2.4 MWp solar. Four players.
GNI 68, Reputation 71. Nobody has touched the diesel lever since May.

**Month:** October, `T_db` ≈ 11 °C — free cooling nearly every hour.

### Load and power

| | |
|---|---|
| Average IT load | 3,800 kW (95% utilisation) |
| Energy delivered | **2,774,000 kWh_IT** |
| Cooling (`FREECOOL_COP` 30) | 127 kW |
| Aux | 91 kW |
| Total draw | 4,018 kW → **PUE 1.057** |
| Grid energy (net of 210 MWh solar) | 2,723,140 kWh |
| `load_ratio` 4,018 / 20,000 | 0.201 → congestion ×1.048 |
| `p_grid` | €0.199/kWh |
| **Grid cost** | **€541,905** |

### Revenue

| Mix | kWh_IT | Rate | € |
|---|---|---|---|
| Inference 60% | 1,664,400 | `RATE_INFERENCE` | 2,246,940 |
| Training 30%, on schedule | 832,200 | `RATE_TRAINING` | 1,830,840 |
| Spot 10% | 277,400 | `RATE_SPOT` | 124,830 |
| **Total** | 2,774,000 | | **4,202,610** |

### Costs

| Line | € |
|---|---|
| Debt service | 1,910,800 |
| Grid power | 541,905 |
| Maintenance | 400,000 |
| Payroll (8 non-local, 14 local at +18%) | 112,792 |
| Building + fit-out amortisation | 50,000 |
| Cooling and solar amortisation | 26,000 |
| Insurance, connectivity, licences | 45,000 |
| Program benefit (dial at €20k) | 20,000 |
| Program safety measures opex (fence, cameras) | 3,000 |
| **Total** | **3,109,497** |

### Result

**+€1,093,113. Margin 26.0%.** GNI drifts up ~1.5 points.

**Read the shape, not the total.** Debt service is 45% of revenue, power 13%. The
month is profitable because the hardware was busy, not because energy was cheap.
October is easy for reasons that have nothing to do with skill — or with anyone
behaving well.

---

## 5. Worked example: a ruinous month

**Same site, nine months later. July.** Four players. This is a co-op month, so
it has authors.

**What was decided in March, by whom:**

- **Player A** signed a 1,000,000 kWh_IT training contract at the contract
  terminal, deadline 20 July, worth €2.2M. Nobody else was in the office.
  Nobody can un-sign it.
- **Player B**, to afford the node down payment for it, deferred the chiller
  expansion from 3,000 to 1,500 kW_th, and set the diesel dispatch policy to
  *auto-start to protect SLA* in the switchgear room.

Everything below follows from those two actions.

### What the weather did

Heatwave, 8 days, `T_db` 34 °C, `T_wb` 22 °C. `EVAP_COP` → ~10, `EVAP_WUE` → 2.4.
Drought declared week 2, water restricted to 100 m³/day. **Easterly wind for most
of the heatwave.** Storm on day 26 takes the grid down for 14 hours.

### The cooling arithmetic

```
Water allowance        100 m³/day = 4,167 L/h
Evaporative capacity   4,167 / 2.4 = 1,736 kW_IT of heat
Chiller capacity       1,500 kW_th   (the deferred expansion)
Q_cap                  3,236 kW_th   against Q_IT of 4,000
θ                      0.79
```

**The site can only run 79% of its hardware, all month**, and no amount of money
fixes it inside July. Emergency chiller hire (€180,000) added 400 kW_th after a
two-week wait.

### What the players did

- **Player C** ran the diesel manually on days 3–9 to hold the inference SLA,
  into an easterly. `WIND_TOWARD_MULT` 3.0 applied for 118 of 120 hours.
  `CAMERA_PRESENCE_DAY` meant residents filmed it on four of those days —
  `FILMED_MULT` 2.5 on top.
- **Player D** discharged the Room 2 fire suppression during a false heat alarm.
  `SUPPRESSION_DISCHARGE_DRIVE_LOSS` 0.55 × 100 nodes × `DRIVES_PER_NODE` 8 =
  **440 drives destroyed**, Room 2 down for three days.
- Someone enabled **Run Hot** for the last six days before the deadline.
  `T_inlet` reached 33 °C, six over `T_INLET_SAFE`. Three nodes failed.
- The group issued a bulletin naming **Player C** as responsible employee for the
  air quality complaints. Resident footage showed the generator starting itself
  under Player B's dispatch policy. **Fact-check fired.**

### Power

| | |
|---|---|
| IT delivered (θ ≈ 0.79, less Room 2 downtime) | 3,180 kW average |
| Cooling (evap 174 + chiller 600) | 774 kW |
| Aux | 79 kW |
| Total draw | 4,033 kW → **PUE 1.268** |
| `p_grid` normal / heatwave (`scarcity` 2.5) | €0.147 / €0.368 |
| **Grid cost** | **€604,000** |
| Diesel (48,000 kWh) | €21,120 |
| Water at punitive drought tariff | €30,410 |

### Revenue

| Contract | Outcome | € |
|---|---|---|
| Training, due 20 July | **870,000 of 1,000,000 delivered. Missed.** 10% payout | 220,000 |
| Inference | Delivered; uptime 88.4% vs `SLA_INFERENCE` 99.5% | 1,694,250 |
| Spot | | 90,000 |
| SLA penalty | capped by `SLA_PENALTY_CAP` | **−847,000** |
| **Net revenue** | | **€1,157,250** |

The missed deadline alone forgoes **€1,980,000**. The work was 87% done.

### Costs

| Line | € |
|---|---|
| Debt service | 1,910,800 |
| Grid power | 604,000 |
| Maintenance | 400,000 |
| **Node replacements (3 × `GPU_NODE_CAPEX`, Run Hot)** | **720,000** |
| **Drive replacements (440 × `DRIVE_REPLACEMENT_COST`)** | **396,000** |
| Emergency chiller hire | 180,000 |
| Payroll | 112,792 |
| Water (punitive) | 30,410 |
| Diesel | 21,120 |
| Building, plant, insurance, benefit, safety measures | 144,000 |
| **Total** | **€4,519,122** |

### Result

**−€3,361,872.**

### And then the part that actually ends the run

| Indicator | What happened | Half-life |
|---|---|---|
| Water | Saturated at 100 during a declared drought | 120 d |
| Air | 120 h of diesel, easterly, ×3.0, filmed on four days at ×2.5 | 90 d |
| Noise | Chillers and fans at maximum through summer nights | 45 d |
| Price | `load_ratio` 0.20, but heatwave scarcity doubled the town's bill too | 60 d |
| Credibility | Fact-check on the false naming: `FALSE_NAMING_PENALTY` 3× | — |

GNI fell 66 → 41 over six weeks and was still 49 in November — when the **T3
permit applied for in March completed its 420-day lead time and was re-checked
against `SENTIMENT_GATE` = 65.**

**Refused. €11,000,000 of committed capex, lost.**

Player C's Standing is 36 and they did not start the generator. Player B set the
policy that did and was never named. The ledger says so, and anyone can read it,
and by November nobody has.

### What they should have done

Any *one* of these turns July from ruinous into merely bad:

- **Build the full 3,000 kW_th of chillers in March** — €900k, holds θ at 1.0
  through the drought, delivers the contract, costs ~3 GNI on price.
- **Talk to Player A before signing.** The two-key interlock does not apply to
  contracts, only to spending. See [open-questions.md](open-questions.md) Q4.
- **Build 4 MWh of battery in spring** (€1.1M) — rides the 14-hour outage with no
  diesel at all, saving 90 days of air memory.
- **Deliberately breach the inference SLA** to protect the training deadline.
  Reputation −16, but €2.2M collected.
- **Never enable Run Hot.** Saves €720,000, delivers 5% less compute.
- **Not discharge the fire suppression.** Saves €396,000 and three days.

Four of those six cost money the group did not have in March, because it had just
gone on nodes for the contract. **The trap is a cash-flow trap wearing a weather
costume**, with four people's hands on it — and that is the correct shape for this
game.

---

## 6. Cash flow over a campaign

| Phase | Months | Position |
|---|---|---|
| T0, 250 kW, 25 nodes | 1–6 | Break-even. Learning. |
| T1, 1 MW, 85 nodes | 6–18 | ~20% margin, saving for T2 |
| T2, 5 MW, 400 nodes | 18–40 | Large absolute profit, heavy debt, first real crisis |
| T3, 20 MW | 40–70 | Political rather than financial constraint |
| T4, 50 MW | 70+ | Referendum territory |

The financial danger is never slow decline; it is **a single quarter where a
missed deadline, a penalty and a hardware loss coincide** against debt service
that does not care. The design should guarantee enough runway to survive one such
quarter and never two.

Bankruptcy ends the run but should be the *rare* ending. **The intended ending is
the referendum** — losing the business by losing the argument, not the arithmetic.
