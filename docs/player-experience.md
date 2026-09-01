# Player experience

What it feels like with three or four people in the room, where they fail, and
what each failure teaches.

**Time compression:** the shared clock runs at 4 ticks/second when every player
is idle — one in-game day every 6 seconds, a month in ~3 minutes. With the
holding that co-op naturally produces, **the first in-game year is 90–150 minutes
of session time**, considerably longer than a single player would take, because
someone is always touching something.

---

## The first 15 minutes — roughly five in-game days

**Opening state.** A converted agricultural building on the edge of town.
`CAP` = 250 kW (the farm's existing connection). €800,000 and a small credit
line. `GNI_START` = 62 — the town is *mildly positive*, because you have not done
anything yet and someone has finally bought the old barn. `REPUTATION_START` = 50.
Every player at `STANDING_START` = 50. Zero racks.

**Minutes 0–3 — everyone walks around and touches things.**
There is no tutorial and no role assignment. Players find the office, the plant
room, the empty hall, the generator in the yard, the gate. **Every control they
find works**, including the diesel lever, which someone will pull within the
first four minutes because it is a lever and they are people. It runs. It makes a
noise. Somebody says "turn it off".

That is the tutorial. It cost them 40 minutes of `N_air` memory and taught the
entire design.

**Minutes 3–8 — the first useful work.**
Two racks and six GPU nodes (60 kW IT). Someone signs a small inference contract
at the terminal, probably without telling anyone. Money starts arriving hourly.

Heat appears. The site has no cooling plant. Somebody buys evaporative — it is
the cheapest thing on the procurement desk by a wide margin and there is no
reason yet to prefer anything else.

**This is the game's first real decision and nobody knows it is one.** The water
readout appears: 3 m³/day next to a town number of 500. It is 0.6%. It is fine.
It will not always be fine.

**Minutes 8–13 — the cap bites.**
Nodes are added until 250 kW is reached, and it arrives sooner than expected
because `P_cool` and `P_aux` count against it. **First lesson: the grid cap is on
the site, not on the computers.**

Someone finds the grid upgrade at the procurement desk. T1 is €450,000, 90 days,
and needs `SENTIMENT_GATE` = 45. GNI is 61. It is above `TWO_KEY_THRESHOLD`, so
two players sprint to opposite ends of the site and press two buttons within
eight seconds. This is the first time the group has had to cooperate at all, and
it is a physical act rather than a conversation.

**Minutes 13–15 — the shape is visible.**
A letter arrives about the fans at night. No mechanical effect — `N_noise` is 18
and this is the Complaints stage doing its job as a tutorial. There is a wind
arrow on the HUD that has not done anything yet. Someone notices a figure at the
fence with a phone, at distance, and cannot get any closer to it.

**What the first fifteen minutes must accomplish:** the group is comfortable,
mildly successful, and holding four unexamined assumptions — evaporative cooling
is obviously correct, the grid cap is a money problem, the letters are flavour,
and the diesel lever is a toy. All four are wrong and none will be corrected for
another hour.

---

## The first hour — through to the first summer

**Minutes 15–30 — competence, and the first divergence.**
T1 completes; 85 nodes, 850 kW IT. The group learns the contract mix and starts
taking spot aggressively once someone works out that idle nodes cost €6.54/h
whether they run or not. This is correct and it feels clever.

It is also where roles form without anyone assigning them: one player lives in
the office, one lives in the plant room, one wanders. **The game never
acknowledges this and never enforces it**, which is why it works.

GNI drifts to 58. Water is 36 m³/day, 7% of the town. Still fine.

**Minutes 30–42 — the first training contract, signed by one person.**
Reputation crosses `REP_GATE_TRAINING_MED` = 60. A 400 kW block for 18 days,
€760,000 on completion, nothing if late.

Somebody signs it. **Nobody can un-sign it.** The group now has a deadline it did
not collectively agree to and headroom it does not have, and the argument about
that is the first genuinely social moment in the session. They buy nodes on debt
and apply for T2, because the alternative is eating the penalty.

**The overbuild begins here, and it is the correct play.**

**Minutes 42–58 — July.**

Everything was sized in spring.

- `T_db` crosses 15 °C and free cooling — which they had been getting free
  without knowing it — stops.
- `EVAP_COP` slides 40 → 8. Cooling power quintuples.
- The water number goes 7% → 19% and turns amber.
- Fan noise peaks. Someone finds the "listen from the fence line" toggle and the
  room goes quiet for a moment.
- A heatwave arrives. θ drops below 1 for the first time and the compute readout
  and the contract readout disagree. **That is how the group learns what
  throttling is.**
- The training deadline is in four days.

Someone runs the diesel. It is right there, it is instant, and it works. The wind
arrow — ignored for an hour — is pointing at the town. Someone else is at the
fence-line audio toggle listening to what that sounds like from the houses.

**Minutes 58–65 — the bill, and the first bulletin.**
The training contract completes: +€760,000. A petition starts. GNI is 47.

Somebody opens the bulletin terminal, and finds the *responsible employee* field,
and the session changes character permanently. Whoever ran the generator takes
−14 Standing. GNI goes up 4. It works. Everyone now knows it works.

**What the first hour teaches:** the wind arrow was a mechanic. Evaporative
cooling was a decision. The grid permit was never a money problem. The diesel
lever that saved the contract is going to cost the upgrade. And there is a
button in the office that converts your colleague's reputation into community
goodwill at a fixed exchange rate.

None of this is said. All of it is on screen.

---

## The first in-game year

### Autumn — the reprieve, and the fork

Free cooling returns, water goes green, noise complaints stop. GNI recovers to 54
— **but not to 62**, because `HALFLIFE_AIR` is 90 days and July's diesel is still
in the memory.

The group now faces the design's defining choice, at the Program board, where
every option is in one list sorted by cost with no headings:

- **€40,000: Perimeter fence.** Instant. Stops the sabotage they are worried
  about. +4 `N_visual`, permanent.
- **€180,000: Heat reuse — public pool.** 90 days. Large GNI gain.
- **€300,000: Acoustic barrier.** 60 days.
- **€400,000/year: six more local staff.** +5.4 GNI, immediate.
- **€1,800,000: closed-loop cooling.** 180 days.

Most groups buy the fence, because it is cheap and instant and the others are
expensive and slow and it is not obvious they work. **This is the intended
mistake, and the board is designed not to warn them.** It is not punished
immediately. It is punished in about eight months, when a Community Engagement
Session returns two thirds of its value and nobody remembers why.

### Winter — the cost crisis

The group expected winter to be easy: free cooling everywhere, PUE 1.05. It is
easy *thermally* and expensive *financially*. `GRID_PRICE_WINTER` is €0.24, a
Dunkelflaute pushes `scarcity` to 2.5 for a week, and the solar installed in June
produces almost nothing for four months.

They discover wind turbines are the winter answer and that `WIND_CAPEX` has a
300-day lead time — so the answer to this winter is available next winter.
Meanwhile heat-reuse revenue would be at its annual peak, if they had built it.

**Winter's lesson: the plant that solves one season is idle in another, and lead
times mean every solution must be bought a year before it is needed.**

### Spring — building for a season you are not in

The group now knows the year has a shape. Chillers ordered in March for July.
Battery for the outage they had last August. Local hires while hiring is still
unfrozen. If the T2 permit survived its re-check, it completes here and the site
triples.

This is also when someone works out the **Program benefit dial**, cranks it to
€120k/month, and the GNI goes up, and everyone agrees this is good, and nobody
looks at the cash reserve for six weeks.

### Summer again — the exam

Same weather, different site — and different people, because by now the group has
a history. A group that learned gets throttling instead of damage, a water number
under 25%, no diesel, and a GNI floor around 55. A group that bought fences gets
`N_visual` 34, a poisoned Engagement Session, and a drought that finds them still
evaporative-only.

**By the end of year one a player should be able to look at a GNI drop and name
the indicator that caused it without opening a panel** — and, in co-op, name the
person.

---

## Where players fail, and what each failure teaches

| Failure | When | Lesson |
|---|---|---|
| Pulls the diesel lever because it is a lever | minute 4 | Everything here works, and nothing asks if you are sure |
| Fills the grid cap with IT and cannot cool it | 15 min | The cap is on the site, not the computers |
| Signs a contract without telling anyone | 30–45 min | Signed is signed. Talk first. |
| Sizes cooling in spring | first July | Seasons bind on different resources |
| Runs diesel into an easterly | first July | Wind direction is a mechanic |
| Names a colleague and it works | first July | The exchange rate exists and everyone knows it now |
| Buys the fence instead of the pool | autumn | The board does not tell you which is which |
| Loses a grid permit at re-check | month 10–14 | GNI is a commitment held over time, not a meter managed today |
| Spams bulletins | any time | Credibility is finite and the crisis comes later |
| Cranks the Program benefit dial | year 1–2 | Generosity and sabotage are the same action |
| Discharges the fire suppression as a joke | any time | `SUPPRESSION_DISCHARGE_DRIVE_LOSS` is 55% of a room |
| Dismisses local staff in a bad month | year 1–2 | Dismissal costs 2.4× what hiring gained, and hiring freezes at Protest |
| Names someone falsely and gets fact-checked | year 2 | The camera was there. The ledger was always there. |
| Does everything right and still gets a referendum | year 3+ | At 50 MW next to 4,000 people, being *good at it* is not the same as being welcome |

The last row is what the whole game is for. A group that does everything right —
closed loop, on-site renewables, heat reuse, forty local staff, never a drop of
diesel — still ends up operating a 50 MW industrial facility next to a town of
four thousand, drawing 63% of the regional feeder and putting nearly 50% on
everyone's bill. **The referendum should be winnable and it should never feel
unfair that it was held.**

---

## What co-op changes about all of this

Three things, and they are the reason this is a co-op game rather than a
single-player one with a lobby.

1. **The plateau disappears.** The single largest risk in a solo version of this
   design is a boring mid-game where the correct action every hour is nothing.
   Four people in a shared space generate incident continuously, and the sim does
   not have to. See [risks.md](risks.md) §2.
2. **Every decision acquires an author.** "We deferred the chillers" is a
   different sentence from "Player B deferred the chillers", and the ledger makes
   the second one available for the rest of the session.
3. **The satire stops being about a fictional operator.** The bulletin's
   *responsible employee* field means the corporate language is being aimed at
   someone in the room, by someone in the room, for a measurable four points. The
   game does not have to argue that this is what organisations do. It just puts
   the button on the wall and waits.

---

## UI commitments

Four things the UI must do, because the design does not survive without them.

1. **`GNI_target` shown as a ghost marker beside `GNI`.** Two stacked lags make
   measures feel inert for weeks. Without a visible target the system is unfair;
   with it, players can see they are winning before they are winning.
2. **Five indicators always visible, never collapsed.** The whole justification
   for five is diagnosis.
3. **The wind arrow and a 48-hour forecast on the main HUD from hour one** —
   present and useless for an hour, so the moment it becomes useful is a
   discovery rather than a tutorial.
4. **"Time held by NAME — location", always visible when the clock is held.**
   This is the only mitigation for time-holding abuse and it is entirely social.

And two things it must **not** do:

- **Never recommend an action or rank Program measures.** Choosing whom to harm
  is the content of the game; an advisor panel would answer the only question
  worth asking, and labelling the board would destroy §4's autumn fork.
- **Never rank players.** The ledger is a chronological list, not a scoreboard.
  The game records; the players adjudicate. That is the entertainment.
