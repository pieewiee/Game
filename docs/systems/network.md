# System: Network connectivity

Bandwidth, redundancy, peering and latency as a fourth physical resource
alongside power, heat and water.

**Inputs:** contract bandwidth and latency requirements, installed capacity, path
count, patch panel state, incident state.
**Outputs:** delivered latency (ms), utilisation, availability, contract class
eligibility, SLA breaches.

---

## 0. Scope discipline

Three of the four shipped datacenter sims ([GDD.md](../GDD.md) §2) simulate
networking well. **We do not compete there.** This system models four scalars and
one topology decision:

- **bandwidth** (Gbps installed vs committed)
- **latency** (ms delivered)
- **path count** (1 or 2, and whether they are genuinely diverse)
- **peering type** (transit vs internet exchange)

No VLANs, no routing protocols, no switch configuration, no port-level
management, no packet inspection. The only physical interaction is the **patch
panel**, and it exists because patch cables can be swapped
([construction-routing.md](construction-routing.md) §7).

New system, no contradictions with existing docs — but it does **add a field to
every contract** ([compute-contracts.md](compute-contracts.md) §2), which is a
change to that system's data model.

---

## 1. Bandwidth and utilisation

```
committed_bw  = Σ contract.bw_requirement                          [Gbps]
utilisation   = committed_bw / installed_bw
latency_ms    = base_path_latency + QUEUE_K / max(0.05, 1 - utilisation)
```

Queueing latency blows up hyperbolically as utilisation approaches 1. Concretely,
with `base_path_latency` = 3.1 ms and `QUEUE_K` = 0.4:

| Utilisation | Latency |
|---|---|
| 0.50 | 3.9 ms |
| 0.75 | 4.7 ms |
| 0.90 | 7.1 ms |
| 0.95 | 11.1 ms |
| 0.98 | 23.1 ms |

**Nothing happens, nothing happens, nothing happens, everything happens.** A
group that sizes bandwidth to 90% of committed has a working site until the day
someone signs one more contract.

Installed bandwidth comes in steps with lead times:

| Step | Capex | Monthly | Lead time |
|---|---|---|---|
| 1 Gbps | included | €400 | — |
| 10 Gbps | €18,000 | €1,900 | 30 d |
| 100 Gbps | €140,000 | €9,500 | 90 d |
| 400 Gbps | €520,000 | €31,000 | 150 d |

---

## 2. Paths and redundancy

Two routes out of the site.

### Path A — the incumbent
Cheap, fast to provision, follows the access road. **It shares a duct with the
town's own fibre.**

That last clause is a design decision doing real work: a cut on Path A takes the
town's internet with it. When the cut is caused by the group's own forklift or
their own contractor, it is a GNI event with a specific, undeniable, entirely
correct grievance attached — and it is on the local news that evening.

### Path B — the diverse route
`PATH_B_CAPEX` = €680,000, `PATH_B_MONTHLY` = €14,000, 210-day lead time. Runs
the other way, out of the valley, sharing nothing.

```
availability_single = 0.995
availability_dual   = 1 - (1 - 0.995)²  =  0.999975
```

**Path B pays for nothing at all until the day Path A is cut**, and then it pays
for everything. That is the entire pitch and the design should not soften it: it
is a €680k insurance policy against an event that may not happen in a five-year
campaign, and the group has to decide whether to buy it while the sun is out.

**Diversity is verifiable and can be faked.** A provider will sell "diverse" that
shares a duct for the last 400 m. Checking costs €12,000 and 5 days and reveals
`true_diversity ∈ {full, partial, none}`. Nobody checks, and it is a documentary
audit finding for Availability Tier IV
([certifications-audits.md](certifications-audits.md) §1).

---

## 3. Peering

| Type | Cost | Latency | Note |
|---|---|---|---|
| Transit only | Cheapest | +6.5 ms | Everything via one upstream |
| IX + transit | +€4,200/mo | +1.8 ms | Direct peering with major networks |

The IX connection is the only way to reach the latency thresholds in §4, so it is
effectively mandatory for latency-tier work and pure cost otherwise.

---

## 4. Contract coupling

**Every contract gains two fields:** `bw_requirement` (Gbps) and
`latency_max` (ms). This is a change to
[compute-contracts.md](compute-contracts.md) §2 and should be reflected there.

| Archetype | Bandwidth | Latency sensitivity | Behaviour |
|---|---|---|---|
| **Inference** | Moderate, steady | **High** | Pays a premium below a threshold |
| **Training** | High, **bursty at checkpoint boundaries** | None | Ignores latency entirely |
| **Spot** | Low | None | Fills whatever is left |
| **Grey** | Moderate | None | Wants a path that is not logged |

### The latency premium

```
rate_effective = RATE_INFERENCE * (1 + LATENCY_PREMIUM)  if latency_ms <= contract.latency_max
               = RATE_INFERENCE                          otherwise
               and if latency_ms > latency_max * 1.5: SLA breach
```

`LATENCY_PREMIUM` = 0.20. Latency-tier inference pays 20% more, requires an IX
connection, requires Availability Tier IV, and **is the first thing to break when
someone signs one contract too many** — because the premium contracts are the
ones with a latency ceiling, and utilisation is shared.

**The trap:** filling spare bandwidth with cheap spot work raises utilisation,
which raises latency, which drops the premium on the contracts that were paying
for the site. The correct action — soak up idle capacity
([economy.md](../economy.md) §2) — is wrong here, and the two systems disagree on
purpose.

### Training bursts

```
bw_demand_training(t) = base + BURST_MULT * (t mod CHECKPOINT_INTERVAL == 0)
```

`CHECKPOINT_INTERVAL_DEFAULT` = 1 h ([compute-contracts.md](compute-contracts.md)
§2.2), so a large training run produces a bandwidth spike **every hour, on the
hour**. Each spike briefly pushes utilisation up and latency with it. A site
carrying both latency-tier inference and a big training run is breaching its
inference SLA once an hour and the cause is a checkpoint.

Lengthening the checkpoint interval fixes the latency spikes and increases the
griefing blast radius. Two systems, opposite directions, one dial.

---

## 5. Incidents

| Incident | Trigger | Counter |
|---|---|---|
| **Fibre cut** ([incidents.md](incidents.md) §3.5) | Sabotage stage, forklift near the duct, road works | Path B, if it exists and is genuinely diverse |
| **DDoS** ([incidents.md](incidents.md) §3.4) | Grey-client share, no scrubbing subscription, single peering path | Scrubbing (€3,800/mo, **must be pre-bought**), shift to path B, null-route |
| **Congestion collapse** | Utilisation past 0.95 | Cancel spot work (instant, free) |
| **Patch panel error** | A swapped or mislabelled patch cable | Trace it, at `label_factor` speed |

**Scrubbing cannot be bought during an attack.** It has a 5-day provisioning time,
so it is a subscription paid for months of nothing — the same shape as Path B and
the same argument every time renewal comes round.

---

## 6. Failure modes

| Failure | Why |
|---|---|
| Redundancy never pays and feels like a tax | If fibre cuts are too rare across a campaign, Path B is strictly wrong to buy. The sabotage escalation stage must be reachable often enough to justify it. |
| Latency premium is invisible | 20% of inference revenue is real money, but only if the group ever sees the non-premium rate for comparison. The contract browser must show both. |
| Networking becomes a management layer | Four scalars and a patch panel. If it grows past that, §0 has failed. |
| Bandwidth steps are too coarse | Four steps across 1→400 Gbps means the correct purchase is usually obvious. Deliberate — this system is not meant to carry a planning puzzle. |

---

## 7. How to abuse this against teammates

| Abuse | Legitimate cover |
|---|---|
| Sign spot contracts up to 0.95 utilisation | Idle capacity is money, and the marginal-cost argument is correct |
| Swap two patch cables at the panel | Patching is the job |
| Relabel a patch port | Labelling is mandated and audited |
| Cancel the scrubbing subscription | €3,800/mo for a service that has never done anything |
| Decline the €12,000 diversity check | Nobody checks |
| Move a latency-tier contract to the transit path | Load balancing |
| Drive the forklift along the duct route | It is also the delivery route |

The utilisation one is the best of these because **the marginal-cost reasoning in
[economy.md](../economy.md) §2 actively endorses it.** Filling spare capacity with
spot work is the correct answer to one system and the wrong answer to this one,
and a player doing it can cite the other document.

---

## 8. Coupling

| To | Passes | Unit |
|---|---|---|
| [compute-contracts.md](compute-contracts.md) | Latency premium, SLA breach on latency, new contract fields | EUR/kWh_IT, boolean |
| [incidents.md](incidents.md) | Fibre cut, DDoS, congestion | availability, Gbps |
| [construction-routing.md](construction-routing.md) | Path length, hop count, patch panel state, labels | ms |
| [certifications-audits.md](certifications-audits.md) | Tier IV requires dual verified paths | findings |
| [nuisance.md](nuisance.md) | A cut on the shared duct takes the town's internet | GNI event |
| [workplace-accidents.md](workplace-accidents.md) | Forklift into the duct route | — |
| [economy.md](../economy.md) | Capex, monthly, scrubbing, diversity checks | EUR |
