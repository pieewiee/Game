# Contributing

Small team, evenings only. These rules exist to keep merges cheap, not to be
ceremony. Three things actually matter: branch off `main`, write a readable
commit subject, and never separate an asset from its `.meta`.

## Branches

Never commit to `main` directly — it is protected, so the push will be rejected
anyway. Branch off an up-to-date `main`:

```bash
git switch main && git pull
git switch -c feat/enemy-patrol
```

Naming: `<type>/<short-kebab-case-description>`

| Prefix | For |
|---|---|
| `feat/` | new gameplay, systems or content |
| `fix/` | bug fixes |
| `chore/` | tooling, CI, dependencies, project settings |
| `docs/` | documentation only |
| `refactor/` | restructuring with no behaviour change |
| `test/` | tests only |

Keep it short and specific: `feat/enemy-patrol`, `fix/jump-buffer-timing`,
`chore/bump-input-system`. No personal names, no ticket numbers unless we
actually start using an issue tracker.

**One branch, one concern.** A branch that renames 200 files *and* changes
gameplay is unreviewable — and with Unity YAML, an unreviewable diff is one
where nobody catches a broken GUID.

## Commits

[Conventional Commits](https://www.conventionalcommits.org):

```
<type>(<optional scope>): <subject in the imperative, lowercase, no full stop>

<optional body: why, not what — the diff already says what>
```

Same type list as branches, plus `ci:` and `build:`.

```
feat(enemy): add patrol route following
fix(input): stop jump buffering across scene loads
chore(ci): cache the Library folder between runs
docs(assets): explain what belongs in ThirdParty
```

- Subject under ~72 characters, imperative mood ("add", not "added"/"adds").
- Body wrapped at 72, explaining *why* — that is the part the diff cannot tell
  a reader six months from now.
- Commit working states. `wip` commits on your own branch are fine; squash them
  before asking for review.

## Pull requests

- PRs into `main` need both CI checks green and **one approving review**.
- Describe what changed and how you tested it. If it touches a scene or prefab,
  say which — that is the reviewer's cue to check for GUID damage.
- Merging is the author's job once approved.

## The `.meta` rule

**Every `.meta` file is committed alongside the asset it describes. Always. No
exceptions.**

Unity stores each asset's GUID in its `.meta` file, and every reference in every
scene, prefab and script points at that GUID rather than at a path. So:

- **Asset committed without its `.meta`** → the next person to open the project
  gets a *new* GUID generated locally. Their project now disagrees with
  everyone else's about what that asset is, and any reference to it breaks.
- **`.meta` committed without its asset** → Unity deletes the orphan `.meta` on
  import, producing a spurious deletion in someone else's working tree.
- **Deleting an asset** → delete its `.meta` in the same commit.
- **Moving or renaming an asset** → do it *inside the Unity Editor's Project
  window*, never in Explorer/Finder and never with `git mv`. The Editor moves
  the `.meta` with the file; the OS does not.
- **Folders have `.meta` files too.** Same rules apply.

Before pushing, check nothing is stranded:

```bash
git status --short           # nothing unexpectedly untracked
git diff --cached --name-only | sort
```

If you see a `.cs` staged without its `.cs.meta`, stop and fix it before
committing.

## Binary assets

New binary file types go in `.gitattributes` under the LFS section, and that
change is committed **before** the first file of that type. Files committed
before their pattern existed stay in ordinary git history and would need a
history rewrite to migrate — which we are not going to do.

Check that a new binary actually went to LFS:

```bash
git lfs status
git check-attr filter -- Assets/Art/Whatever/thing.png   # want: filter: lfs
```

## Never commit

- Unity licence files (`.ulf`, `.alf`) or serial keys
- Passwords, API keys, `.env` files, keystores
- `Library/`, `Temp/`, `Logs/`, `Build/`, `UserSettings/`, `*.csproj`, `*.sln`

If a secret does get committed, say so immediately — rotating the secret is the
fix, and it needs doing before anything else.
