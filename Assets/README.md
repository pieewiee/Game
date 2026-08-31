# Assets/ folder conventions

One place for everything, and everything in its place. If you are unsure where
something goes, ask in the team chat rather than inventing a new top-level
folder — new top-level folders are a team decision, not an individual one.

Inside each folder, group by **feature** (`Art/Characters/Goblin/`), not by
sub-type (`Art/Textures/AllOfThem/`). Features get deleted as a unit; file
types never do.

| Folder | What goes here | What does **not** |
|---|---|---|
| `Art/` | Models (`.fbx`, `.blend`), textures, sprites, materials, shaders, animation clips and controllers, fonts. | Prefabs that combine art with behaviour — those go in `Prefabs/`. Anything you downloaded — that goes in `ThirdParty/`. |
| `Audio/` | Music, SFX, voice, audio mixers (`.mixer`). | Code that plays audio (`Scripts/Runtime/`). Licensed sound packs (`ThirdParty/`). |
| `Prefabs/` | Prefabs and prefab variants — anything you drag into a scene more than once. | One-off scene-only objects; keep them in the scene. Prefabs shipped by a third-party package. |
| `Scenes/` | `.unity` scene files only. | Assets that a scene happens to reference. A scene referencing files in a sibling folder is normal and correct. |
| `Scripts/` | All of our C# code, split into four assemblies. See [`Scripts/README.md`](Scripts/README.md) for the assembly layout and dependency rules. | Third-party code and DLLs (`ThirdParty/`). |
| `Settings/` | Project-level configuration *assets*: render pipeline assets, quality/volume profiles, Input System `.inputactions`, `ScriptableObject` config assets. | Per-developer preferences — those live in `UserSettings/`, which is git-ignored. Anything with a secret in it. |
| `ThirdParty/` | Everything we did not write: Asset Store purchases, vendored libraries, downloaded art packs. One subfolder per vendor/asset, keeping the vendor's own structure intact. | Our own code or art, ever. Do not "improve" a vendor's folder structure — it makes updating impossible. |

## Rules that apply everywhere

- **Every asset has a `.meta` file, and it is committed with the asset.** The
  `.meta` holds the GUID every reference in the project points at. Commit an
  asset without its `.meta`, or a `.meta` without its asset, and you hand
  someone else a broken scene. See `CONTRIBUTING.md`.
- **Never move or rename assets outside the Unity Editor.** Use the Project
  window; Unity moves the `.meta` with it. Moving files in Explorer/Finder or
  with `git mv` orphans the `.meta` and breaks references.
- **Binary assets are stored in Git LFS.** The tracked extensions are listed in
  `/.gitattributes`. Adding a new binary type? Add the pattern there and commit
  that change *before* committing the first file of that type.
- **No secrets, no licence files, no keystores.** Not in `Settings/`, not
  anywhere.
- **`.gitkeep` files** exist only to keep otherwise-empty folders in git. Unity
  ignores dot-files, so they generate no `.meta`. Delete one once its folder has
  real content.
