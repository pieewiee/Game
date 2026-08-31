# Game

Unity project scaffold for a small hobby team. There is no game code yet — this
repo currently contains folder conventions, assembly definitions, git hygiene,
editor config and a CI pipeline, plus one smoke test that proves the pipeline
works.

## Project decisions

| | |
|---|---|
| **Unity version** | `6000.0.58f1` (Unity 6 LTS) — pinned in `ProjectSettings/ProjectVersion.txt` and in `.github/workflows/tests.yml` |
| **Target platforms** | Windows standalone (`StandaloneWindows64`) and macOS standalone (`StandaloneOSX`) |
| **Multiplayer** | Netcode for GameObjects + Unity Transport |
| **Binary assets** | Git LFS |
| **Default branch** | `main`, protected |

Everyone must use **exactly** `6000.0.58f1`. Unity rewrites serialized assets
when a project is opened in a newer editor, and mixed versions turn every merge
into a fight. Install it from the Unity Hub.

---

## First-time setup

Do all five steps. Steps 2 and 3 are per-machine and git does **not** do them
for you when you clone.

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

**Every one of us has to do this locally.** It cannot be committed: git refuses
to take merge-driver commands from a repository, because that would let a repo
run arbitrary commands on your machine. `.gitattributes` already marks Unity
YAML files with `merge=unityyamlmerge`; without the driver registered, git
silently falls back to a plain text merge and scenes/prefabs come out mangled.

Run the command for your platform from inside the repo. Adjust the version
folder if yours differs.

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
git config --get merge.unityyamlmerge.driver     # should print the path above
ls "C:/Program Files/Unity/Hub/Editor/6000.0.58f1/Editor/Data/Tools/"   # or the macOS path
```

If the `UnityYAMLMerge` binary is not at that path, find it under your editor
install and use the real path. Adding `--global` to those three commands sets it
for every Unity repo on your machine, which is usually what you want — but the
path then has to be valid for every project you work on.

`--fallback none` makes a merge that SmartMerge cannot resolve *fail loudly*
rather than produce a plausible-looking broken scene. Resolve those by hand in
the Unity Editor, or pick one side wholesale.

### 4. Open the project

Open the repo folder from the Unity Hub with `6000.0.58f1`.

The first open will generate a lot of files that are **not** in this repo yet,
because they cannot be created without the Editor: every `.meta` file, the rest
of `ProjectSettings/`, and `Packages/packages-lock.json`. **Commit them.** See
[`MANUAL_SETUP.md`](MANUAL_SETUP.md) — it is a checklist, work through it once.

### 5. Read the conventions

- [`Assets/README.md`](Assets/README.md) — what goes in which folder
- [`Assets/Scripts/README.md`](Assets/Scripts/README.md) — the four assemblies and their dependency direction
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branches, commits, and the `.meta` rule

---

## Repository layout

```
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

Dependencies flow one way only: tests → editor → runtime. `Game.Runtime` never
references anything of ours. The full rules are in
[`Assets/Scripts/README.md`](Assets/Scripts/README.md).

---

## Running tests

**In the Editor:** `Window > General > Test Runner`, pick the *EditMode* or
*PlayMode* tab, `Run All`.

**In CI:** `.github/workflows/tests.yml` runs both modes on every PR into `main`
and on pushes to `main`, using [GameCI](https://game.ci). Results are uploaded
as build artifacts (`test-results-editmode`, `test-results-playmode`).

### CI secrets — these must be added before CI can pass

The workflow references three repository secrets that **do not exist yet**. Until
they are added, every CI run fails at the Unity activation step. This is
expected, not a broken workflow.

Add them at
`https://github.com/pieewiee/Game/settings/secrets/actions`:

| Secret | Value | How to get it |
|---|---|---|
| `UNITY_LICENSE` | The entire contents of the `.ulf` activation file, pasted verbatim (including the XML header) | Follow GameCI's activation guide: <https://game.ci/docs/github/activation>. It has you run a one-off workflow that produces an `.alf`, upload that at <https://license.unity3d.com/manual>, and download the resulting `.ulf`. |
| `UNITY_EMAIL` | The email address of the Unity account the licence belongs to | — |
| `UNITY_PASSWORD` | That account's password | Use a dedicated Unity account for CI if you would rather not put a personal password in a repo secret. |

**On Unity Pro/Plus instead:** delete `UNITY_LICENSE` from the `env:` block in
`.github/workflows/tests.yml` and add a `UNITY_SERIAL` secret holding the serial
key; keep `UNITY_EMAIL` and `UNITY_PASSWORD`.

**Never commit a `.ulf`, `.alf` or serial key.** `.gitignore` blocks the obvious
filenames, but that is a safety net, not a policy.

Note that this repository is **public**: workflow logs are public, and secrets
are not exposed to workflows triggered by pull requests from forks. PRs from
forks will therefore fail activation — review those by pulling the branch
locally, or by pushing it as a branch on this repo.

---

## Branch protection

`main` is protected: PRs require both CI checks (`test (editmode)` and
`test (playmode)`) to pass and one approving review. If you rename the CI job or
its matrix values, update the protection rule to match, or merges will block on
a check that never reports.
