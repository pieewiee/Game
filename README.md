# GOOD NEIGHBOR PROGRAM

A co-op simulation for 2–5 players who jointly run an AI datacenter next to a
small town. You build compute capacity to sell. Every route to more revenue harms
the neighbours in a different way. The neighbours can shut you down.

Four datacenter sims shipped on Steam in 2026 and all of them simulate the
inside — racks, cables, networking. **None of them simulates the town outside.**
That is the wedge: the community conflict is the core system, the co-op is
chaotic enough that players can ruin each other's work, and the power, heat and
water models have real depth.

**Status: paper design.** There is no game code. This repository contains the
design documentation plus a Unity project scaffold — folder conventions, assembly
definitions, git hygiene, editor config and a CI pipeline, with one smoke test
that proves the pipeline works.

## Design documentation

Start with the GDD; everything else hangs off it.

| Document | Contents |
|---|---|
| [docs/GDD.md](docs/GDD.md) | Full design: the title as a mechanic, tick order, every core formula, the loop, the gags |
| [docs/systems/compute-contracts.md](docs/systems/compute-contracts.md) | Nodes, racks, the four contract archetypes, SLA, Reputation |
| [docs/systems/power.md](docs/systems/power.md) | Grid tiers and the permit gate, dynamic price, solar, wind, battery, diesel |
| [docs/systems/cooling-water.md](docs/systems/cooling-water.md) | Four cooling plants, COP curves, water, throttling, heat reuse |
| [docs/systems/nuisance.md](docs/systems/nuisance.md) | The five Program indicators |
| [docs/systems/sentiment.md](docs/systems/sentiment.md) | The Good Neighbor Index, memory, escalation, the Program board |
| [docs/systems/seasons.md](docs/systems/seasons.md) | The Climate driver |
| [docs/systems/coop-griefing.md](docs/systems/coop-griefing.md) | Physical controls, the blame ledger, Standing, griefing vectors, shared time |
| [docs/economy.md](docs/economy.md) | Capex vs opex, cost per rack per hour, a profitable month and a ruinous one |
| [docs/balance-constants.md](docs/balance-constants.md) | Every number, with its real-world anchor and confidence |
| [docs/multiplayer.md](docs/multiplayer.md) | Authority, replication, ledger integrity, disconnects |
| [docs/player-experience.md](docs/player-experience.md) | First 15 minutes, first hour, first in-game year |
| [docs/art-bible.md](docs/art-bible.md) | Palette, 15-mesh kit, VFX list, audio list — buildable without an artist |
| [docs/architecture.md](docs/architecture.md) | How this *would* be structured in Unity. Described, not built. |
| [docs/scope.md](docs/scope.md) | MVP definition and the ordered cut list |
| [docs/open-questions.md](docs/open-questions.md) | Unsettled decisions, options, recommendations |
| [docs/risks.md](docs/risks.md) | Where it gets boring, breaks, or exceeds the team |

## Project decisions

| | |
|---|---|
| **Unity version** | `6000.0.58f1` (Unity 6 LTS) — pinned in `ProjectSettings/ProjectVersion.txt` and in `.github/workflows/tests.yml` |
| **Target platforms** | Windows standalone (`StandaloneWindows64`) and macOS standalone (`StandaloneOSX`) |
| **Multiplayer** | Netcode for GameObjects + Unity Transport |
| **Binary assets** | Git LFS |
| **Default branch** | `main`, protected |

Everyone must use **exactly** `6000.0.58f1`. Unity rewrites serialized assets when
a project is opened in a newer editor, and mixed versions turn every merge into a
fight. Install it from the Unity Hub.

---

## First-time setup

Do all five steps. Steps 2 and 3 are per-machine and git does **not** do them for
you when you clone.

### 1. Install prerequisites

- **Unity Hub** + Unity **6000.0.58f1**, with the *Windows Build Support* and
  *Mac Build Support* modules.
- **Git LFS** — <https://git-lfs.com>. Check with `git lfs version`.

### 2. Clone, with LFS

```bash
git lfs install            # once per machine, not per repo
git clone https://github.com/pieewiee/Game.git
cd Game
git lfs pull               # fetch the actual binaries, not the pointer files
```

If you see 130-byte text files where a `.png` should be, you skipped
`git lfs install` — run it, then `git lfs pull`.

### 3. Enable Unity SmartMerge (UnityYAMLMerge)

**Every one of us has to do this locally.** It cannot be committed: git refuses to
take merge-driver commands from a repository, because that would let a repo run
arbitrary commands on your machine. `.gitattributes` already marks Unity YAML
files with `merge=unityyamlmerge`; without the driver registered, git silently
falls back to a plain text merge and scenes and prefabs come out mangled.

**Windows (Git Bash or PowerShell):**

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.0.58f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p --force --fallback none %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```

**macOS:**

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.0.58f1/Unity.app/Contents/Tools/UnityYAMLMerge" merge -p --force --fallback none %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```

Verify it landed:

```bash
git config --get merge.unityyamlmerge.driver
```

Adding `--global` sets it for every Unity repo on your machine, which is usually
what you want — but the path then has to be valid for every project you work on.

`--fallback none` makes a merge SmartMerge cannot resolve *fail loudly* rather
than produce a plausible-looking broken scene.

### 4. Open the project

Open the repo folder from the Unity Hub with `6000.0.58f1`.

The first open generates a lot of files that are **not** in this repo yet, because
they cannot be created without the Editor: every `.meta` file, the rest of
`ProjectSettings/`, and `Packages/packages-lock.json`. **Commit them.** See
[MANUAL_SETUP.md](MANUAL_SETUP.md).

### 5. Read the conventions

- [`Assets/README.md`](Assets/README.md) — what goes in which folder
- [`Assets/Scripts/README.md`](Assets/Scripts/README.md) — the assemblies and their dependency direction
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branches, commits, and the `.meta` rule

---

## Repository layout

```
docs/                   the design (start here)
Assets/
  Art/  Audio/  Prefabs/  Scenes/  Settings/  ThirdParty/
  Scripts/
    Runtime/            Game.Runtime          ships in the build
    Editor/             Game.Editor           editor-only tooling
    Tests/EditMode/     Game.Tests.EditMode   fast, no player loop
    Tests/PlayMode/     Game.Tests.PlayMode   runs in a real player loop
Packages/manifest.json  pinned package versions
ProjectSettings/        project config (Unity generates most of this)
.github/workflows/      CI
```

`docs/architecture.md` proposes two further assemblies — `Game.Sim`
(engine-independent) and `Game.Sim.Tests`. That change has **not** been made; see
[docs/open-questions.md](docs/open-questions.md) Q13.

---

## Running tests

**In the Editor:** `Window > General > Test Runner`, pick *EditMode* or
*PlayMode*, `Run All`.

**In CI:** `.github/workflows/tests.yml` runs both modes on every PR into `main`
and on pushes to `main`, using [GameCI](https://game.ci).

### CI secrets — these must be added before CI can pass

The workflow references three repository secrets that **do not exist yet**. Until
they are added, every CI run fails at the Unity activation step. This is expected,
not a broken workflow.

Add them at `https://github.com/pieewiee/Game/settings/secrets/actions`:

| Secret | Value | How to get it |
|---|---|---|
| `UNITY_LICENSE` | The entire contents of the `.ulf` activation file, verbatim | GameCI's activation guide: <https://game.ci/docs/github/activation> |
| `UNITY_EMAIL` | The Unity account email | — |
| `UNITY_PASSWORD` | That account's password | Use a dedicated CI account if you would rather not put a personal password in a repo secret |

**On Unity Pro/Plus:** delete `UNITY_LICENSE` from the `env:` block in
`.github/workflows/tests.yml` and add `UNITY_SERIAL` instead.

**Never commit a `.ulf`, `.alf` or serial key.**

This repository is **public**: workflow logs are public, and secrets are not
exposed to workflows triggered by pull requests from forks.

---

## Branch protection

`main` is protected: PRs require both CI checks (`test (editmode)` and
`test (playmode)`) to pass and one approving review. If you rename the CI job or
its matrix values, update the protection rule to match.
