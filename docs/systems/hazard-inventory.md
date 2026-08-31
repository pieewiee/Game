# Hazard inventory

Every physical hazard in the game: what it is legitimately for, how it is abused,
what it does to a person, and what it does to the Good Neighbor Index.

**The governing rule:** every hazard has a real operational purpose that a
reasonable person performs on an ordinary day. Nothing exists purely to grief
with. That is what makes them unbannable, what makes every accident ambiguous,
and what means the blame ledger can record the name and never the intent.

**Lethality classes**

| Class | Meaning |
|---|---|
| **FATAL** | Player respawns at the visitor car park, drops carried items, TRIR +1 |
| **INJ** | 90 s incapacitation, items kept, TRIR +1 |
| **PROP** | Property damage only, no recordable incident |
| **LATENT** | No immediate effect. Recordable only if it later hurts someone. |
| **ECON** | Money and Sentiment only |

---

## 1. Fatal

| Hazard | Legitimate function | Abuse | Sentiment consequence |
|---|---|---|---|
| **Fire suppression pull station** | Manual activation when a human sees fire before the detector. Code-required and genuinely faster. Seal cuttable with the tool-cabinet wire cutters, which are there for cable work. | Cut seal, pull, hold the door shut for the 30 s countdown. Also: discharge into a leaky compartment destroys 55% of drives *without* extinguishing anything. | Mandatory bulletin. Fire-brigade attendance → `N_visual`. Cut seal is a −15 audit finding. |
| **Cooling setpoint dial** | Setting supply air temperature. Dropping it before a forecast heatwave buys real thermal headroom. No safety limits, because real setpoints have none. | Below 0 °C: icicles, frost, and a lingering player becomes an ice block that obstructs the aisle and melts into a puddle that finds a cable tray. | Via the February Chain: fan noise, then an SLA breach, then a bulletin. |
| **Forklift** | Moves pallets from the loading dock. You cannot install a rack without it. | Into people, CRACs, transformers, the fence, the fibre duct route. Lifted racks topple at full `GPU_NODE_CAPEX` each. | Fence damage → `N_visual` + security finding. Transformer → the Tarp Chain. Duct → the town loses internet. |
| **Gas cylinders** | Refilling the closed coolant loop and recharging suppression. Stored on site because they must be. | Caps off in the forklift route → projectile. Cross-connected fill ports → loop contaminated, COP −15% for 14 days. | Indirect: COP loss → throttling → diesel → air. |
| **Hydrogen vent fan (off)** | Battery strings off-gas hydrogen; ventilation is code-required. The fan is on a switch **because it is noisy**. | Turn it off to reduce `N_noise` at night — a real, immediate GNI improvement. Hydrogen accumulates; a contactor arc or dropped tool does the rest. | **Turning it off raises GNI.** Then a deflagration destroys the UPS and starts a fire. −20 audit finding. |
| **Raised floor tile (open)** | Sub-floor power and cooling routing requires lifting tiles. The lifter is the only tool that does it. | Leave a hole. A player falls (INJ); a forklift into it is FATAL plus damage. | Open tiles on the open-day visitor route → `N_visual`. −5 each at audit. |

---

## 2. Injurious

| Hazard | Legitimate function | Abuse | Sentiment consequence |
|---|---|---|---|
| **Taser** | Issued at the Sabotage stage (GNI 5–15) against unauthorised third-party interference. Genuinely reduces fibre cuts. | Works on colleagues. Works on residents **through the fence**. | **Every use is on local news that evening**, regardless of target. Against a resident: large direct GNI loss, camera presence forced to 1.0. |
| **Raised floor tile (fall)** | As above. | As above. | As above. |
| **Gas cylinder (near miss)** | As above. | As above. | As above. |

---

## 3. Property

| Hazard | Legitimate function | Abuse | Sentiment consequence |
|---|---|---|---|
| **Emergency power-off** | Legally mandated. It is what you press when a colleague is being electrocuted. **Unprotected by law**, because a cover costs seconds. | One press de-energises the compartment. Every training job loses progress since its last checkpoint; every inference contract in the room breaches. 8–14 min restart. | Via the SLA breach and the Reputation loss. −9 audit finding if obstructed. |
| **Rack and node switches** | Individual maintenance isolation. | Cut one rack during a training run. Forty switches take forty seconds and are visible for all of them. | Indirect, via SLA. |
| **Main breaker** | Site-wide isolation for electrical work. | Site dark. | Diesel start, if policy says so. |
| **Forklift (into plant)** | As §1. | CRAC, transformer, fence. | `N_visual`, audit findings. |

---

## 4. Latent — the dangerous ones

These produce no statistic until the day they do, which is why they are the best
griefing vectors in the design.

| Hazard | Legitimate function | Abuse | Sentiment consequence |
|---|---|---|---|
| **UPS maintenance bypass** | Required to service the UPS without dropping load. Without it, maintenance means an outage. | Leave it engaged. `pq_factor` 1.8 ages the whole fleet; the next flicker drops every rack at once. Cause and effect are weeks apart and usually different shifts. | Via the mass SLA breach. Nothing visible until then. |
| **Blanking panels (pulled)** | Correct airflow management; one of the cheapest real PUE improvements. Must be pulled to reach a cable behind them. | Not replacing one is not an event. Recirculation, `T_inlet` +4–8 °C locally, fans spin up. | **Invisible cause, audible effect.** `N_noise` at night × `NIGHT_NOISE_MULT` 2.5, into a 45-day memory. −3 each at audit. |
| **Transformer tarp (absent)** | The tarp must come off for inspection and oil service. | Do nothing. `moisture_ingress` accumulates until a storm. **Sabotage by omission — the ledger has no entry for "never put back".** | The Tarp Chain: transformer loss → 60 days on diesel → air indicator → permit refused. |
| **Fire-stop penetrations (unsealed)** | Every run crossing a compartment wall makes one. Sealing is €40 and 6 minutes. | Skip it. Invisible when done and invisible when skipped. | Nothing, until a fire crosses a compartment or a discharge fails to hold gas. −12 each at audit. |
| **Cable tray bundling** | Trays exist to hold cables and the shortest route is a legitimate preference. | Nine circuits in one tray: derate to 0.65, tray above `TRAY_T_SAFE`, `hazard_fire` accumulates. | Via fire. −10 each at audit. |
| **Power tray under a water pipe** | There is often genuinely no room above. | Every leak, melt and flush now finds live copper. | Via short → breaker → SLA. |
| **Wrong cable or port label** | Labels are how anyone finds anything, cut repair time 60%, and are audited. Player-typed text. | A plausible wrong string. `label_factor` 2.5 vs 1.6 for no label — **wrong is worse than missing, because people act on it.** Fatigued staff do it on their own, so it has no reliable author. | Via repair time during an incident. −5 per zone at audit. |
| **Skipped filter job cards** | Filters are consumables on a 6-week interval. | Skip them. Dust → `T_inlet` → wear ×1.32 → a failure cluster fourteen months later. | Fan noise; nobody attributes it. |
| **Combustible loading (packaging)** | Hardware arrives in cardboard on pallets. Breaking it down is a real job card. | Order a lot and never unpack. Fire load, blocked aisles, `N_visual`. | Uninstalled stock already adds `N_visual`. −8 at audit. Visible on the open-day route. |
| **Load bank (indoors / badly timed)** | Generator and UPS testing. **The uptime certification requires documented tests**, and it is the only way to find a degraded battery string before it fails. | Schedule the mandatory test during a heatwave. Leave the portable unit running indoors: full kW of heat, zero billable output. | `N_noise`, and `N_air` if generator-fed. |
| **Eyewash station** | Mandated beside the battery room; weekly flushing is an audit requirement. | Flush it, repeatedly, next to the compartment with the most cable trays. | Via water on the floor. −4 at audit if *not* flushed. |
| **DDoS scrubbing (cancelled)** | A €3,800/month subscription that has never done anything. **Cannot be bought during an attack** — 5-day provisioning. | Cancel it at renewal. | Via total link saturation later. |
| **Diversity check (declined)** | €12,000 and 5 days to verify Path B is genuinely diverse. Nobody checks. | Decline it. The "diverse" path shares a duct for the last 400 m. | Via a single cut taking both paths. Tier IV documentary finding. |

---

## 5. Economic and Sentiment only

| Hazard | Legitimate function | Abuse | Sentiment consequence |
|---|---|---|---|
| **Program benefit dial** | The community fund. It genuinely raises GNI. | Crank it to €200k/month. **A griefing action indistinguishable from virtue**, defensible out loud, visible only as a cash-flow problem a quarter later. | **Raises** GNI. That is the problem. |
| **Hiring roster** | Hiring and dismissal. | Dismiss twelve local staff: −26 GNI in an afternoon, at 2.4× the rate hiring gained. And **hiring freezes at Protest**, so the route back up closes behind you. | Direct, large, and asymmetric. |
| **Contract terminal** | Signing offered work. | Sign a 4 MW training run the site cannot cool. **Nobody can un-sign it.** Not covered by the two-key interlock. | Indirect: the overbuild that follows is what the town objects to. |
| **Bulletin terminal** | Program communications. Attaches to any measure. | Spam it to zero credibility; name a colleague who did not do it. | Below `CREDIBILITY_MOCKERY_THRESHOLD` bulletins *cost* GNI. False naming, if footage contradicts it, costs 3× credibility and 2× the naming gain. |
| **Water inlet valve** | Isolation for maintenance; throttling to manage the water indicator during drought — frequently the correct action. | Wide open drains the town. Shut kills cooling. One quarter-turn either way. | Wide open: `N_water` saturates and town supply pressure drops. |
| **Key cards** | Access control. **Required for the security certification**, which gates a contract class and the 4-hour RMA tier. | Lock colleagues out. Lock them **in** — combine with the pull station. Take a dead player's card off the floor before they walk back. | −7 audit finding for unreconciled access rights. |
| **Gate control** | Perimeter security and delivery access. | Open during unscheduled gate activity: residents inside the fence, camera presence 0.85, everything × `FILMED_MULT` 2.5. Closed: blocks your own deliveries. | Large, immediate, on every indicator at once. |
| **Staff barbecue** | Catering for the Community Engagement Session, one of the cheapest GNI measures. | Its stack shares the air channel with the diesel. | **The reconciliation event wrecks the air readings during the event.** |
| **Open-day visitor route** | The Engagement Session itself. The route is set by the players. | Route it past the tidy hall — legitimate, transparent, universal. Or don't, and let them see the ice block. | Everything on the route feeds `N_visual`, and the session's effect is already scaled by `(1 − N_visual/100)`. |
| **Diesel start lever** | Instant uncapped power. The only way to hold a deadline during an outage or an administrative pause. | Three-second hold. Time it to an easterly for ×3.0, or to daytime filming for ×2.5. | `N_air`, 90-day memory. The worst single indicator action available. |
| **Run Hot switch** | Delivering full load through a cooling shortfall — genuinely correct for the last six hours of a training deadline. | Leave it on. Quadratic damage above `T_INLET_SAFE`; nodes fail at €240,000 each. | Via fan noise and, eventually, capacity loss. |
| **Procurement desk** | Ordering everything. | Order €2M of chillers that sit in the yard as uninstalled stock. Above `TWO_KEY_THRESHOLD` needs two players; below it does not. | `N_visual` at +1 per €100k of unpacked stock. |
| **Acquisition letter** | The Exit B offer. A physical letter and a signature. | **Sign it alone.** Ends the session for four other people. Not covered by the two-key interlock, because the interlock guards spending. | The epilogue. |

---

## 6. Summary counts

| Class | Count | Note |
|---|---|---|
| FATAL | 6 | Enough for slapstick, few enough that death stays notable |
| INJ | 3 | Mostly overlaps with FATAL hazards at lower severity |
| PROP | 4 | |
| LATENT | 13 | **The largest class, deliberately.** |
| ECON | 13 | |

**Thirteen latent hazards against six fatal ones is the intended ratio.** If
sessions become pure slapstick, the ratio is wrong rather than the hazards — the
comedy is supposed to be mostly *delayed*, and the funniest thing in the game
should be a consequence arriving eleven months after a defensible decision, not a
forklift.

---

## 7. The four that are legitimate GNI *improvements*

Worth isolating, because they are the sharpest expression of the design:

1. **Hydrogen vent fan off** — reduces night noise. Real, immediate, correct.
2. **Program benefit dial up** — raises GNI directly. Generous.
3. **Blanking panels pulled** to reach a cable during a fix — the fix is real.
4. **Water valve shut** during a drought — reduces the water indicator. Correct.

Each of these makes the number the game asks players to care about go **the right
way, today**, and each is the first link in a chain that ends somewhere much
worse. No lie is required at any point, and the ledger's entry — "turned off vent
fan, 23:14" — is true, complete, and useless.
