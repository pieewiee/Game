# Art bible

A concrete asset inventory a part-time team with **no artist** can actually
produce. Flat-shaded low-poly, vertex colours, one material, URP, baked lighting.

The governing principle: **personality lives in text and audio, not in
geometry.** The meshes are a diagram. The press releases, the news ticker and the
sound of the site at 3 a.m. are the game.

---

## 1. Palette — 12 colours

One material, vertex colours only. No textures, no UV unwrapping, no normal
maps, no PBR. Every mesh is painted from this list and nothing else.

The rule survives imported art: the CC0 kits under `Assets/ThirdParty`
(Kenney's city and car kits, see `CREDITS.md` there) arrive with UVs into a
colormap, and the importer bakes those into vertex colours quantised to this
table — minus Program Blue, Amber and Alarm Red, which carry meaning and never
appear on a house or a car — then drops the UVs. They render through the same
material as everything procedural.

| # | Name | Hex | Used for |
|---|---|---|---|
| 1 | Concrete | `#8E8F8A` | Building shells, hardstanding, plinths |
| 2 | Slate | `#3A3F44` | Racks, plant, switchgear, structural |
| 3 | Render | `#D9D5CC` | Office walls, town houses, road markings |
| 4 | Program Blue | `#2E5C8A` | **The operator's brand.** Signage, livery, hard hats, the fence, all corporate surfaces |
| 5 | Pale Blue | `#7FA6C9` | Secondary brand, glazing, water |
| 6 | Amber | `#E0A63C` | Warning beacons, hazard stripes, throttle state |
| 7 | Alarm Red | `#C4463A` | Fault state, fire suppression, breach indicators |
| 8 | Field | `#7A8C5A` | Grass, verges, the land the site sits on |
| 9 | Foliage | `#4A5B3C` | Trees, hedges, screening |
| 10 | Earth | `#6B5844` | Soil, berms, unmade ground, timber |
| 11 | Sky | `#BFD3E0` | Skybox base, distant haze |
| 12 | Ink | `#22262A` | Outlines, shadow ambient, text, night |

**Program Blue is a mechanic.** Everything the operator has installed is that
blue — fence panels, hard hats, signage, the Program board, the bulletin
terminal. As the site fortifies, the view from the road becomes *more corporate
blue and less field green*, and that shift is readable at a glance before any
number is. The Visual indicator has a colour.

### Seasons

Seasons are a **global colour grade plus a skybox tint**, not new assets.

| Season | Grade | Skybox |
|---|---|---|
| Spring | Slight green lift, high saturation | Bright `#BFD3E0` |
| Summer | Warm, bleached, low contrast in haze | Pale, hot |
| Autumn | Warm shift on Field → Earth | Amber-tinted |
| Winter | Desaturated, blue shift, low sun angle | Grey-blue |

**Where this hurts:** it does not give you snow. Snow needs a ground-shader
parameter and a bare-tree mesh, which is one extra mesh and one extra shader
branch. Budget for it or accept a winter that reads as "grey autumn". I
recommend paying for it — winter is a third of the game's lesson and it should
look different, not just dimmer.

---

## 2. Mesh kit — 15 core

Every one is a hard-surface box-modelling job. No organics, no characters beyond
a capsule, nothing that requires sculpting or rigging.

| # | Mesh | Notes | Reused as |
|---|---|---|---|
| 1 | **Rack shell** | 42 HE, open front | — |
| 2 | **Node module** | 1 HE slab; stack 4 for a GPU node | Blank panel, by colour |
| 3 | **Cable tray segment** | Straight; rotate for corners | Pipe rack |
| 4 | **Pipe segment** | Straight + one elbow | Coolant, water, heat-reuse main |
| 5 | **CRAC / cooling unit** | Box with a grille face | Chiller, free-cooling dry cooler, by colour |
| 6 | **Fence panel** | Post + mesh panel | Electric fence, by colour + VFX |
| 7 | **Transformer / switchgear** | Box with fins | Battery container, by colour |
| 8 | **House** | Simple gabled box | The whole town, by scale/rotation/colour |
| 9 | **Tree** | Trunk + 2 cone tiers | Hedge, by scale; bare variant for winter |
| 10 | **Diesel container** | Container box + exhaust stack | Generic plant container |
| 11 | **Solar panel row** | Tilted plane on legs | — |
| 12 | **Wind turbine** | Tower + nacelle + 3-blade rotor | — |
| 13 | **Evaporative tower** | Louvred box with open top | Water tank, by scale |
| 14 | **Delivery truck** | Cab + trailer box | All deliveries |
| 15 | **Character capsule** | Capsule + hard hat + hi-vis band | All staff and all players |

**If time allows (4 more):** gate (sliding panel), mast (one mesh; searchlight or
camera by tip attachment), road segment, ground tile variants.

That is 15–19 meshes for an entire game. The site is built by **instancing and
recolouring**, which is also what makes it cheap to render and easy to lay out
programmatically.

### Where this hurts

**Reading the site at a glance.** With one material and twelve colours, telling
an evaporative room from a chiller room at fifty metres is genuinely hard. The
mitigations must be deliberate:

- Colour-code plant by **function**, not by manufacturer flavour: Pale Blue for
  water-based, Slate for closed-loop, Amber trim for anything currently
  throttling.
- Let **VFX do the identification** — steam plume means evaporative, nothing
  means closed loop. See §4.
- Accept that the primary diagnostic surface is the UI, not the 3D view. This is
  a spreadsheet with a view, and the view's job is atmosphere and *interaction
  targets*, not data.

---

## 3. Characters

Capsules with hard hats. No faces, no lipsync, no facial rig, no IK beyond foot
placement if that is free.

- **Players and staff:** Program Blue hard hat, hi-vis band, capsule body. Player
  identity is hard-hat colour variation plus a floating name.
- **Animation:** idle, walk, run, one generic "interact" reach. Four clips.

### Residents are never seen close up

This is a **tone requirement before it is a budget decision**, and the two
happen to agree.

Capsules-with-hard-hats is fine for staff. Applying the same treatment to the
people the game refuses to mock would flatten them into props at exactly the
moment the design needs them to be the most real thing on screen. A faceless
capsule holding a phone at the fence is not a person; it is a joke about a
person, and the tone rule forbids that joke.

So residents are represented at **distance only**:

- Silhouettes at the fence line, backlit, never approachable.
- **Phone screen glow** at night — the tell that filming is happening, and one of
  the best small visual mechanics in the design.
- Car headlights on the lane, lit windows in the town at dusk.
- Placards at the gate during unscheduled gate activity, readable but held by
  shapes.
- Everything else is **text**: the local news ticker, letters, petition counts,
  quoted complaints.

> **Overruled by the owner, 2026-09-02.** "Distance only" is no longer the
> rule. Residents are bodies you can walk up to (Kenney's CC0 Blocky
> Characters, baked to the palette like the rest of the town), they are solid,
> they walk their own errands through the streets, and **E** gets one line out
> of them. What the tone rule still holds onto, and what the implementation
> still obeys: the line is the town's own state read back, residents are
> quoted plainly and never played for laughs, nobody comes inside the wire,
> and nothing a resident does touches the simulation. The characters carry no
> faces — the bake keeps flat colour and drops the kit's painted eyes — so the
> "faceless capsule as a joke about a person" is avoided from the other side.
> See [open-questions.md](open-questions.md) Q28.

The town has a population of `TOWN_POPULATION` = 4,000 and you never meet one of
them. That is both the cheapest possible solution and the correct one.

### Where this hurts

**The funniest moment in a co-op griefing game is someone's face when they
realise, and we cannot show a face.** The reaction has to land entirely in the
news ticker, the bulletin copy and the audio. That is a real loss and it is why
§5 and §6 get more of the budget than §2 does.

---

## 4. VFX list

**VFX carries the atmosphere.** With flat shading and no normal maps there is
nothing else doing it. This is the highest-risk art work in the project and the
one place to spend real effort.

| # | Effect | Priority | Driven by |
|---|---|---|---|
| 1 | **Diesel plume** | **Critical** | Wind vector, cumulative runtime (length), instantaneous burn (opacity). Must visibly cover the houses when the wind is in the town sector. |
| 2 | Evaporative steam | High | Water draw rate; identifies the plant type at distance |
| 3 | Fire suppression discharge | High | One-shot, white, fast, room-filling. Must feel violent — it destroys 55% of the drives in the room. |
| 4 | Warning beacon | High | Rotating amber; fault, throttle, delivery arriving |
| 5 | Heat shimmer | Medium | Over cooling plant, scales with `Q_removed` |
| 6 | Truck dust | Medium | Arrival on the unmade access road |
| 7 | Rain / snow | Medium | `TickReport.RainMmH`: streaks above 0.5 °C, flakes below; nothing under a roof. The sky behind it is palette-anchored: horizon `#BFD3E0` Sky by day, Ink at night, a dusk tint (0.93, 0.62, 0.40) toward the sun at the horizon crossing, Amber-shifted in autumn |
| 8 | Fog | Medium | Fog curve: dawn bump × clear sky × autumn (radiation fog), wet air, cloud, snow; wind thins it. Colour = the sky's horizon so the fogged ground meets the dome. Clouds sink into it; the sun disc (Amber → white) and the moon (Sky tint) fade with a milder haze term |
| 9 | Searchlight beam | Medium | Cheap cone mesh, not volumetrics. Contributes to the "compound" read. |
| 10 | **Phone glow at the fence** | High | Tiny. Tells you `CAMERA_PRESENCE` is active. |

The plume is the hero asset. If it does not look good, the game has no
atmosphere and its best mechanic has no image. Prototype it first.

---

## 5. Audio list

**Audio matters more than visuals here, because noise is a core mechanic.** The
noise indicator is the only Program indicator the player can perceive directly
rather than reading, and that is a deliberate advantage.

### Site sources (all positional, all parameter-driven)

| Sound | Parameter |
|---|---|
| Fan bank loop | Fan power → volume + pitch. **The site's main voice.** |
| Chiller compressor loop | On/off + load |
| Diesel: start / run loop / stop | The three-second lever hold has its own sound |
| Water pump, coolant flow | Flow rate |
| Transformer hum | Draw |
| Alarm klaxon | Fault state |
| **Fire suppression discharge** | One-shot. The loudest thing in the game. |
| Truck arrival + reversing beeper | Delivery events |
| Gate motor | Gate control |

### Interaction

Rack switch clunk, valve squeak, lever ratchet, breaker throw, dial detent,
footsteps on gravel / concrete / grating. **Every physical control needs a sound**
because in co-op it is how you know somebody else just did something.

### Ambience

Seasonal room tone (birds, insects, wind, rain), distant town, a church bell,
and a **distant protest chant — muffled, never intelligible.** Making the words
audible would tip into caricature; keeping them muffled keeps them real.

### The one audio feature worth building properly

**"Listen from the fence line."** A toggle that switches the mix to how the site
sounds from the town: heavy low-pass, distance attenuation, reverb, town ambience
brought forward.

It is one mixer snapshot. It costs almost nothing. And it converts an abstract
number — `N_noise` with a `NIGHT_NOISE_MULT` of 2.5 — into something a player
hears and immediately understands at two in the morning in July.

If one thing on this page gets extra polish, make it this.

---

## 6. Text — where the personality actually is

Not an art asset, but the largest content surface in the game and the one that
carries the tone.

| Surface | Volume estimate |
|---|---|
| Program bulletin euphemism bank | 200–300 fragments, combinatorial |
| Local news ticker items | 150–250 templates |
| Resident letters and complaints | 80–120 |
| Petition and referendum copy | 40–60 |
| Contract descriptions and client names | 60–100 |
| Corporate slide deck (monthly GNI report) | 20–30 layouts |
| Ledger event descriptions | 60–80 |

**The writing rule, applied to every string:** residents are quoted accurately
and are always factually correct; only the operator's language is absurd. Every
generated line must be checkable against it, because it is one careless string
away from becoming a different and much worse game. See
[risks.md](risks.md) §6.

---

## 7. Budget summary

| Area | Effort share | Risk |
|---|---|---|
| Meshes (15–19) | ~15% | Low. Box modelling. |
| Palette, materials, lighting bake | ~5% | Low |
| VFX (10 effects, one critical) | ~30% | **High.** The plume carries the game. |
| Audio (~25 sources + mixer states) | ~25% | Medium. Mostly library + parameterisation. |
| UI art | ~15% | Medium. Charts, meters, the board, the ticker. |
| Text | ~10% | Low risk, high volume |

**The two things a non-artist team can get wrong here are the plume and the
audio**, and they are also the two things that make the game feel like anything
at all. Meshes are a solved problem; atmosphere is not.
