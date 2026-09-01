# Multiplayer

2–5 players, drop-in co-op, one shared site. Authority, replication, ledger
integrity and disconnects.

**Described, not built.** See [architecture.md](architecture.md) for the
assembly and tick structure this assumes.

---

## 1. Authority model

**Host-authoritative. The simulation runs on exactly one machine.**

One player hosts (listen server). Clients send *intents*; the host applies them
at tick boundaries and broadcasts state. Clients never simulate.

Why not lockstep determinism, which would be the other obvious choice for a
tick-based sim:

- Lockstep requires every client to agree bit-for-bit on floating-point results.
  Achievable, but it makes every future change to a formula a desync risk, and
  this design will have its formulas rebalanced weekly for months
  ([risks.md](risks.md) §3).
- Lockstep stalls the whole session on the slowest client. With a shared,
  contested clock (§5) that is a bad interaction.
- The state is small. A full `SimState` is a few thousand values; deltas are
  hundreds of bytes. There is no bandwidth argument for lockstep here.

Determinism is still required — for saves, replays and the balance harness — but
only *within one machine*, which is a far weaker and cheaper guarantee.

**Trade-off accepted:** the host has a latency advantage on physical controls.
In a co-op game about griefing your friends, this is a fairness issue and I am
choosing to accept it rather than build rollback. Logged as
[open-questions.md](open-questions.md) Q8.

---

## 2. What must be replicated

| Data | Rate | Notes |
|---|---|---|
| `SimState` deltas | 10 Hz | Contracts, loads, GNI, indicators, cash, weather. Small. |
| Physical control states | on change | Valve angles, switch positions, mode selectors, dial values. **These are sim state**, not cosmetic. |
| Ledger appends | on change | Append-only, host-assigned actor and tick. |
| Delivery manifest | on change | Item, cost, ETA. Everyone must see what was ordered. |
| Player transforms | 20 Hz | Position, look, current interaction target. |
| Interaction intents | immediate | Client → host only. |
| Bulletin and news text | on generation | Generated host-side so everyone reads identical copy. |
| Time state | on change | Rate, holder identity, override vote status. |

**Physical control states are the subtle one.** A half-turned valve is not an
animation, it is a simulated position that feeds cooling flow. It must be
authoritative or two players fighting over a valve produces different cooling on
different screens.

## 3. What can be client-side

- Camera, UI layout, chart rendering, panel state.
- Interaction highlighting and hover prompts.
- VFX and audio: plume particles, steam, warning lights, the noise mix. These are
  driven by replicated *parameters* (wind vector, burn rate, fan power), so they
  look consistent without being replicated themselves.
- The "listen from the fence line" audio mode ([art-bible.md](art-bible.md) §5).
- Player locomotion, **client-predicted**. Player movement affects nothing in the
  sim, so misprediction is cosmetic and can be corrected by simple snapping.
- Derived readouts: PUE, margin, the ghost `GNI_target` marker.

The dividing line is exactly: **if it can change a number in `SimState`, the host
owns it.**

---

## 4. Keeping blame consistent

The blame ledger is the one piece of state where an inconsistency would be
noticed immediately and would poison the social game.

**Rules:**

1. **The actor is assigned by the host on receipt of the intent.** A client never
   sends "player X did this"; it sends "I am interacting with object Y", and the
   host stamps the connection's player id. A modified client cannot attribute an
   action to someone else.
2. **The tick is assigned by the host**, not taken from the client clock.
3. **Intents arriving in the same tick are ordered by arrival**, and all of them
   are applied and recorded. If two players flip the same switch in one tick,
   both appear in the ledger and the last one wins the state. This is the correct
   outcome socially: the ledger shows the argument.
4. **The ledger is append-only and is part of the save.** No editing, no
   deletion, no compaction that loses actor attribution.
5. **Naming in a bulletin references a ledger entry id.** The fact-check
   ([coop-griefing.md](systems/coop-griefing.md) §4) resolves the named actor
   against that entry and against camera presence at that tick. This is why
   camera presence must be recorded in the ledger entry, not recomputed later —
   the weather that decided whether anyone was filming is long gone by the time
   the bulletin goes out.
6. **Standing is host-authoritative and keyed by a stable player id**, not by
   connection or by slot, so it survives a disconnect and rejoin.

**What the ledger deliberately does not do:** rank players, score griefing, or
summarise. It is a chronological list at a terminal. The game never adjudicates;
it only records. Adjudication is the players' job and is the entertainment.

---

## 5. Time control across the network

Fast-forward is available only when every player is idle
([coop-griefing.md](systems/coop-griefing.md) §8).

Networking consequences:

- **Idle state is host-evaluated** from the last intent received per player, with
  a grace allowance for latency. A player with 200 ms ping must not be
  permanently "active".
- **The holder's identity and location are replicated**, because naming the
  holder on screen is the entire mitigation for time-holding abuse.
- **Rate changes are host-decided and broadcast.** Clients never locally change
  their own rate; a client running at a different speed would see a different
  world.
- At 20× the host runs 20 ticks/second and still broadcasts state at 10 Hz.
  Clients interpolate readouts. Nobody needs to see every simulated hour.
- **The override vote** (after `TIME_HOLD_OVERRIDE` = 180 s) is host-tallied and
  its status is replicated so everyone can see it pending.

---

## 6. Disconnects

### A client drops

1. Their avatar remains as an idle capsule for 60 s, then despawns. Long enough
   that a brief drop is not disruptive; short enough that they stop holding time.
2. **Anything they were holding is released** — a part-turned valve stays where
   it is, a held lever returns to rest, a time hold clears, a pending two-key
   interlock expires.
3. Their Standing and full ledger history persist in the save, keyed by stable
   player id.
4. **Nothing is rolled back.** Actions taken before the drop stand. This must be
   explicit, because the alternative — undoing a leaver's damage — would make
   disconnecting a griefing strategy.
5. On rejoin they resume with the same Standing and the same ledger. If they were
   dismissed and rehired as a consultant, they rejoin as a consultant.

### The host drops

The session ends and the save is written from the last completed tick.

**Host migration is out of scope.** It requires transferring authoritative state
mid-session, re-establishing every client's connection and reconciling in-flight
intents, and for a part-time team it is weeks of work protecting against an event
that a friends-group co-op session tolerates badly anyway. Logged as
[open-questions.md](open-questions.md) Q9, and the mitigation is simply that the
host should be whoever has the stable connection.

### Drop-in

A player joining mid-session receives a full state snapshot, spawns at the gate,
and starts at `STANDING_START` = 50 with an empty ledger — unless their stable id
is already in the save, in which case they resume everything.

There is no join approval and no kick vote. **A group that invited someone has
already made that decision**, and a kick vote in a game whose subject is
scapegoating a colleague would be in poor taste and mechanically redundant:
Program personnel realignment already exists, costs something, and is funnier.

---

## 7. Save and session

- The save is written **host-side** on a fixed interval and on clean shutdown.
- Format is the versioned JSON snapshot in [architecture.md](architecture.md) §4,
  with the ledger and the per-player Standing table included.
- A session is resumable by the same host with any subset of the original
  players. Absent players' Standing and history remain in the file.

---

## 8. What this does not do

Called out so nobody builds them:

- **No dedicated server.** Listen server only.
- **No matchmaking, no lobbies, no public sessions.** Friends-list and direct
  join. This is a game about ruining the work of people whose names you know;
  matched strangers would produce the actions without the comedy.
- **No voice chat.** Use the platform's.
- **No anti-cheat.** Host authority makes the obvious attacks pointless, and
  everything else in this game is *supposed* to be sabotage.
- **No spectator mode, no replay viewer.** The ledger terminal is the replay.
- **No cross-play** at MVP ([scope.md](scope.md)).
