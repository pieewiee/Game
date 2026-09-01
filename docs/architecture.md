# Architecture — how we *would* build this

**Description only. Nothing here is implemented**, and nothing should be until
the questions in [open-questions.md](open-questions.md) are settled.

This proposes changes to the assembly layout already committed in
`Assets/Scripts/README.md`; those are flagged as
[open-questions.md](open-questions.md) Q13.

---

## 1. The one rule

**The simulation must not reference UnityEngine.**

Not "should not". The entire simulation — nodes, contracts, cooling, power,
climate, indicators, GNI, economy, the blame ledger — lives in a plain C#
assembly with `"noEngineReferences": true`, targeting netstandard2.1, that knows
nothing about GameObjects, MonoBehaviours, `Time.deltaTime`, `Random.Range` or
coroutines.

Four reasons, ordered by how much they matter to a part-time team:

1. **Balance work is otherwise impossible.** ~70 constants, 40% invented, with
   feedback loops on 30-to-120-day timescales. Tuning means running hundreds of
   simulated years across many weather seeds. That is a console program that
   finishes in seconds. Inside Unity it is a thing you leave running overnight
   and cannot easily script.
2. **Tests need no scene.** Every formula in the systems documents is a pure
   function.
3. **Determinism is achievable.** No engine floats, no frame coupling, no engine
   RNG. Same seed plus same intent list produces the same state. This is what
   makes saves, replays and bug reports work.
4. **The renderer is the disposable part.** A management game's presentation gets
   rewritten. The simulation should survive that.

The cost is real: no `Vector3`, no `Mathf`, no `ScriptableObject` for sim data,
and a hand-written mapping layer between sim state and view. Pay it anyway.

---

## 2. Assembly split

The scaffold currently has four assemblies. This design needs six.

```
  Game.Sim                    netstandard2.1, noEngineReferences: true
     ▲                        the entire simulation. no Unity types.
     │
     ├───────────────┐
     │               │
  Game.Runtime    Game.Sim.Tests        EditMode, references Game.Sim only
  (Unity)         ▲                     the bulk of the test suite
     ▲            │
  Game.Editor  ──┘
     ▲
  Game.Tests.EditMode / Game.Tests.PlayMode
```

| Assembly | References | Contains |
|---|---|---|
| `Game.Sim` | **nothing** | State, systems, formulas, RNG, ledger, save serialisation |
| `Game.Sim.Tests` | `Game.Sim`, NUnit | Most tests. No Unity, no scene, milliseconds. |
| `Game.Runtime` | `Game.Sim`, Unity, netcode | Views, interaction, presentation, audio, the tick pump, replication |
| `Game.Editor` | `Game.Runtime`, `Game.Sim` | Inspectors, balance-sweep menu, data authoring |
| `Game.Tests.EditMode` | `Game.Runtime`, `Game.Editor` | The thin layer that needs Unity |
| `Game.Tests.PlayMode` | `Game.Runtime` | Presentation and interaction smoke tests |

`Game.Sim` referencing nothing is the load-bearing part. Everything else follows.

**Note the networking boundary:** `Game.Sim` knows nothing about multiplayer
either. It exposes `Tick(state, intents, rng) -> state`, and `Game.Runtime` is
responsible for deciding which machine calls it and how the result is broadcast.
See [multiplayer.md](multiplayer.md).

---

## 3. Tick model

```
Simulation.Tick(SimState state, IntentBuffer intents, ISimRandom rng) -> SimState
```

One in-game hour per call, systems in the fixed order in
[GDD.md](GDD.md) §4.

**Presentation never writes to sim state.** Every player action — pulling the
diesel lever, turning a valve, signing a contract, cranking the benefit dial —
becomes an **intent** appended to a buffer and applied at the start of the next
tick. This is what gives the blame ledger its integrity and what makes the
host-authoritative model in [multiplayer.md](multiplayer.md) work at all.

**Time compression is tick count, not tick size.** At 20× the pump calls `Tick`
twenty times per second; it never passes a larger `dt`. The moment tick size
varies, throttling, memory decay and battery cycling stop being reproducible. The
tick is one hour, forever.

**Performance target: 10,000 ticks under 100 ms** on a laptop — one simulated
year in a tenth of a second. That is what makes the balance harness viable, and
it is easily achievable if `Tick` does not allocate. A few hundred contracts, a
few thousand nodes, a dozen plants: arrays and arithmetic, not an ECS problem.

**Determinism requires** a seeded PRNG owned by `SimState` (never
`System.Random` statics, never `UnityEngine.Random`), no reliance on iteration
order over hash containers, and no wall-clock reads.

### The one feedback loop

Cooling capacity depends on heat load, heat load depends on throttling,
throttling depends on cooling capacity. Resolved in **a single pass, not to
convergence** — compute cooling against requested heat, derive θ, apply it, done.
Surplus cooling goes unused in a throttled hour. Deliberate simplification,
logged as [open-questions.md](open-questions.md) Q6.

---

## 4. Save format

**Versioned JSON, hand-rolled DTOs. Not Unity serialisation, not `PlayerPrefs`,
not `BinaryFormatter`.**

```
{
  "version": 4,
  "seed": 8172634,
  "tick": 51840,
  "players": [ { "stableId": "...", "standing": 43 } ],
  "ledger":  [ { "tick": 4102, "actorId": "...", "action": "...", ... } ],
  "state":   { ... full SimState ... }
}
```

- **Full state snapshot, not an intent log.** A log is smaller and prettier and
  breaks the first time a formula changes — which, during balancing, is weekly.
  Snapshots survive rebalancing; logs do not. Keep the intent buffer for replay
  and debugging; save the snapshot.
- **The ledger and per-player Standing are part of the save**, keyed by stable
  player id, so a player who drops and rejoins next session resumes their
  history. This is a requirement, not a nicety: a blame system that forgets is
  not a blame system.
- **`version` with explicit migrations.** Either migrate or refuse to load —
  never load silently and wrong.
- **JSON because it is diffable.** Bug reports arrive as save files.
- Unity's `JsonUtility` is unusable here: no dictionaries, no polymorphism, no
  nullable handling. Use `System.Text.Json` or Newtonsoft, inside `Game.Sim`.

---

## 5. Data authoring

Balance constants live in **JSON in `StreamingAssets`, not in ScriptableObjects.**

ScriptableObjects are the Unity-native choice and wrong here: they are `.asset`
YAML that merges badly, they cannot be read by a headless harness without booting
Unity, and they put the numbers on the wrong side of the assembly boundary.

A single `balance.json` mirroring
[balance-constants.md](balance-constants.md) is loadable by the game, the tests
and the sweep harness, and it diffs cleanly in a pull request — which matters,
because balance changes are the changes this project will review most often.

The Editor assembly can still provide an inspector window that edits that JSON.

---

## 6. Presentation and interaction

The view subscribes to state; it does not own it.

- **The site** is a small 3D scene: a fixed plot, building shells, instanced
  racks, plant, and the diesel plume. Asset inventory in
  [art-bible.md](art-bible.md).
- **Every physical control is an interactable** with a sim-state binding, a
  sound, and an interaction prompt. There are ~18 control types
  ([coop-griefing.md](systems/coop-griefing.md) §3) and each needs exactly one
  thing: to convert a player interaction into an intent.
- **No control ever shows a confirmation.** This is an architectural rule as much
  as a design one, because the first time somebody adds one "just for the fire
  suppression" the design is gone.
- **Presentation reads a snapshot, never live state.** At 20× the sim runs twenty
  ticks per frame; the view renders end-of-frame state and never tears across a
  tick.
- **Most of the build effort is UI**, not 3D: charts, the five indicator meters,
  the contract browser, the Program board, the ledger terminal, the news ticker.

---

## 7. The balance harness

A console entry point in `Game.Sim` — **not a Unity build** — that runs:

```
for seed in 0..99:
  for policy in {evaporative_only, chiller_only, hybrid, hybrid+renewables}:
    for grief_rate in {none, occasional, constant}:
      run 5 simulated years with a scripted player policy
      record: survived?, final GNI, peak debt, referendum triggered?,
              months spent below each escalation threshold
```

Output is a CSV. This is how `CONGESTION_K`, the five `W_*` weights, the five
`HALFLIFE_*` values and the entire co-op blast-radius cluster get tuned, and
**there is no other credible way** — they are slow, coupled, and invisible over a
single playthrough.

The `grief_rate` axis is the co-op-specific addition and it matters: it answers
"does one bad actor end the session" quantitatively rather than by argument.

**Building this harness early is the highest-leverage engineering decision in the
project**, and it is only possible because of the rule in §1.

---

## 8. What this does not need

Called out so nobody adds them:

- **No ECS/DOTS.** A few thousand entities updated hourly. Plain arrays are
  faster to write, faster to debug, and fast enough by three orders of magnitude.
- **No DI container.** `Tick` takes its dependencies as parameters.
- **No event bus.** Systems run in a fixed order and read each other's outputs
  from `SimState`. An event bus would make tick order implicit, which is the one
  thing that must stay explicit.
- **No rollback netcode.** Host authority, no client prediction of sim state.
- **No lockstep determinism across machines.** See
  [multiplayer.md](multiplayer.md) §1.
- **No addressables.** The asset budget is a plume, some boxes and a lot of UI.
- **No scene loader abstraction.** There are two scenes.

Per the brief's standing rule, none of these would be added without asking.
