# Assembly layout

Four assemblies, one job each. Assembly definition files are JSON, and JSON
has no comment syntax, so the dependency rules live here instead of inside the
`.asmdef` files. Read this before adding a new assembly.

```
                  ┌─────────────────────────┐
                  │   Game.Tests.EditMode   │   Editor-only, UNITY_INCLUDE_TESTS
                  │  (Scripts/Tests/EditMode)│
                  └───────────┬─────────────┘
                     │        │
          references │        │ references
                     ▼        ▼
        ┌────────────────┐   ┌──────────────────────────┐
        │  Game.Editor   │──▶│      Game.Runtime        │◀──┐
        │(Scripts/Editor)│   │    (Scripts/Runtime)     │   │
        │  Editor-only   │   │ ships in the built game  │   │
        └────────────────┘   └──────────────────────────┘   │
                                          ▲                 │
                                          │ references      │
                             ┌────────────┴─────────────┐   │
                             │   Game.Tests.PlayMode    │───┘
                             │ (Scripts/Tests/PlayMode) │
                             │ all platforms, tests only│
                             └──────────────────────────┘

  Arrows point from "depends on" to "depended upon".
  Dependencies flow DOWNWARD ONLY. There is no arrow back up, ever.
```

## The rules

1. **`Game.Runtime` depends on nothing of ours.** It is the only assembly that
   ships inside a player build. It references `Unity.InputSystem`,
   `Unity.Netcode.Runtime` and `Unity.Networking.Transport` and nothing else of
   ours. It must never reference `Game.Editor` — that would break the build,
   because editor assemblies do not exist in a player.

2. **`Game.Editor` depends on `Game.Runtime`, never the reverse.** Inspectors,
   custom editors, build scripts and editor tooling go here.
   `"includePlatforms": ["Editor"]` keeps it out of player builds.

3. **`Game.Tests.EditMode` depends on both.** It is Editor-only and gated behind
   `"defineConstraints": ["UNITY_INCLUDE_TESTS"]`, so it is stripped from
   builds entirely. It references `UnityEngine.TestRunner` +
   `UnityEditor.TestRunner`, with `nunit.framework.dll` as a precompiled
   reference (which is why `overrideReferences` is `true`).

4. **`Game.Tests.PlayMode` depends on `Game.Runtime` only.** It runs inside a
   real player loop on every platform, so it must not touch `Game.Editor`.
   Same `UNITY_INCLUDE_TESTS` gate.

5. **Nothing production depends on a test assembly.** `autoReferenced` is
   `false` on both test assemblies so they are never picked up implicitly.

## Adding a new assembly

Split by *layer or feature*, not by file type, and only when the split buys you
something concrete (faster compiles, an enforced boundary, a platform gate).
A new feature assembly may reference `Game.Runtime`; `Game.Runtime` may not
reference it back. If you find yourself wanting a cycle, the two assemblies want
to be one assembly.
