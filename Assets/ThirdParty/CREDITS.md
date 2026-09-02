# Third-party assets

Everything under `Assets/ThirdParty/` is CC0 (public domain dedication) and
needs no attribution; we list it anyway. Vendor folder layouts are kept as
shipped, minus the duplicate model formats (only the FBX set is checked in).

Nothing here is used as delivered: `Assets/Scripts/Editor/KitImport.cs` bakes
each model's colormap UVs into vertex colours quantised to the game's palette
(`Palette.Town`), drops the UVs and imports no materials, so every imported
mesh renders through the one `GNP/VertexColor` material like the procedural
world. The catalogue the game reads is `Assets/Settings/Resources/AssetKit.asset`
(menu *GNP > Rebuild Asset Kit*). Runtime code always keeps a procedural
fallback; the game builds and runs without these folders.

| Folder | Source | Licence | Used for |
|---|---|---|---|
| `Kenney/city-kit-suburban` | https://kenney.nl/assets/city-kit-suburban (v2.0) | CC0 | Houses of the west and south residential blocks |
| `Kenney/city-kit-commercial` | https://kenney.nl/assets/city-kit-commercial (v2.1) | CC0 | Apartment and commercial blocks north and east |
| `Kenney/city-kit-roads` | https://kenney.nl/assets/city-kit-roads | CC0 | Street props (signs, traffic lights, poles); the road itself stays procedural |
| `Kenney/car-kit` | https://kenney.nl/assets/car-kit (v3.1) | CC0 | Parked cars and the residents' cars on the ring road |
| `Kenney/ui-audio`, `Kenney/interface-sounds` | https://kenney.nl/assets/ui-audio, https://kenney.nl/assets/interface-sounds | CC0 | Window open/close clicks |
| `Kenney/impact-sounds` | https://kenney.nl/assets/impact-sounds | CC0 | Switch and breaker clunks |
| `Freesound/485621-protest-crowd.mp3` | https://freesound.org/people/InspectorJ/sounds/485621/ by InspectorJ | CC0 | The muffled protest chant at the vehicle gate (low-passed; MP3 preview, see LICENSE.txt) |

Kenney: https://kenney.nl — "Created/distributed by Kenney (www.kenney.nl)", CC0.
