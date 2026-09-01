# Manual setup checklist

Everything in this file requires the Unity Editor GUI and therefore could not be
scripted into the scaffold. **One person** should work through Part 1 once and
commit the result; everyone else only needs Part 3.

---

## Part 1 — First open of the project (do this once, then commit)

- [ ] **1.1 Open the project** from the Unity Hub with **`6000.0.58f1`**.
      If the Hub does not offer that exact patch version, install it — or change
      the version in *all three* places and tell the team:
      `ProjectSettings/ProjectVersion.txt`,
      `.github/workflows/tests.yml` (`unityVersion:`), and the SmartMerge paths
      in `README.md`.

- [ ] **1.2 Let the first import finish.** It takes several minutes and
      generates `Library/` (git-ignored), every `.meta` file, the rest of
      `ProjectSettings/`, and `Packages/packages-lock.json`.

- [ ] **1.3 Verify the packages resolved.** *Window > Package Manager >
      In Project*. Expect: Input System, Netcode for GameObjects, Unity
      Transport, Test Framework. If a version pinned in `Packages/manifest.json`
      does not exist for this editor, Package Manager substitutes the nearest
      valid one — that is fine, but commit the resulting
      `Packages/packages-lock.json`, which is the real lockfile.

- [ ] **1.4 Accept the Input System backend prompt.** Installing the Input
      System package prompts to enable the new input backends and restart the
      Editor. Say yes. This writes `activeInputHandler` into
      `ProjectSettings/ProjectSettings.asset`.

- [ ] **1.5 Set version control and serialization mode.**
      *Edit > Project Settings > Editor*:
      - *Version Control > Mode* = **Visible Meta Files**
      - *Asset Serialization > Mode* = **Force Text**

      Both are the default in Unity 6 — confirm rather than assume. Without
      them, `.meta` files are hidden from git and assets serialize as binary,
      which makes merge conflicts unresolvable and makes SmartMerge useless.

- [ ] **1.6 Create a first scene.** *File > New Scene*, save as
      `Assets/Scenes/Main.unity`, then add it to the scene list in
      *File > Build Profiles*. (A scene file is Unity YAML full of generated
      GUIDs; hand-writing one is a good way to produce a subtly broken project,
      which is why the scaffold ships none.)

- [ ] **1.7 Set Product Name and Company Name.**
      *Edit > Project Settings > Player*. These determine the persistent data
      path, so changing them later moves everyone's save files.

- [ ] **1.8 Confirm the build targets.** *File > Build Profiles*: confirm
      **Windows** (x86_64) and add **macOS**. Each needs its Build Support
      module installed through the Unity Hub.

- [ ] **1.9 Run the smoke test in the Editor.**
      *Window > General > Test Runner > EditMode > Run All*.
      `SmokeTest.TestPipeline_Runs` must pass. If the EditMode tab is empty the
      assembly definitions did not compile — check the Console.

- [ ] **1.10 Commit the generated files.**

      ```bash
      git switch -c chore/unity-first-import
      git add -A
      git status     # expect: many .meta files, ProjectSettings/, packages-lock.json
      git commit -m "chore(unity): generate meta files and project settings on first import"
      ```

      **`Library/` must not appear in `git status`.** If it does, stop and fix
      `.gitignore` before committing. Then open a PR — `main` is protected.

---

## Part 2 — Decisions still open (nobody has made these yet)

These were deliberately left out of the scaffold rather than guessed at.

- [ ] **2.1 Render pipeline.** No render pipeline package (URP/HDRP) is
      installed, so the project is on the Built-in pipeline. Switching later
      means re-authoring every material, so decide early. URP would mean adding
      `com.unity.render-pipelines.universal` to `Packages/manifest.json`.

- [ ] **2.2 `com.unity.ide.visualstudio`.** Not installed, because it was not in
      the agreed package list. Without it Unity generates no `.csproj`/`.sln`,
      so **VS Code and Visual Studio cannot resolve `UnityEngine` types** — you
      get red squiggles on correct code. The committed `.vscode/` config assumes
      it is present. Install it via Package Manager if anyone uses VS Code or
      Visual Studio. Rider users want `com.unity.ide.rider` instead.

- [ ] **2.3 uGUI.** `com.unity.ugui` is not in the manifest. If you want classic
      Canvas-based UI, add it.

- [ ] **2.4 A PlayMode test.** `Game.Tests.PlayMode` exists as an assembly but
      contains no tests, so the `test (playmode)` CI job runs zero tests. It
      should still report success — if it instead errors with "no tests were
      executed", add one trivial PlayMode test mirroring
      `Assets/Scripts/Tests/EditMode/SmokeTest.cs`.

---

## Part 3 — Per-developer, per-machine (everyone does this)

- [ ] **3.1 `git lfs install`** — once per machine, not per repo.
- [ ] **3.2 Register Unity SmartMerge** in your local `.git/config`. The exact
      commands are in [`README.md`](README.md). This *cannot* be committed: git
      refuses to take merge-driver commands from a repository.
- [ ] **3.3 Install Unity `6000.0.58f1`** with Windows + Mac Build Support.
- [ ] **3.4 Install the recommended editor extensions** — VS Code offers them
      from `.vscode/extensions.json` on first open.

---

## Part 4 — Repository administration (owner)

- [ ] **4.1 Add the CI secrets**: `UNITY_LICENSE`, `UNITY_EMAIL`,
      `UNITY_PASSWORD`. The activation flow is described in
      [`README.md`](README.md). **CI cannot pass until these exist** — every run
      fails at the Unity activation step.
- [ ] **4.2 Invite collaborators** — *Settings > Collaborators*. Branch
      protection requires one approving review, and GitHub does not let you
      approve your own PR, so a solo owner cannot merge anything until a second
      person has repo access.

---

## Part 5 — After Milestone 1 (simulation core)

- [ ] **5.0 Sign in to the Unity Hub first — without an account token the
      editor refuses to start.** Symptom: *"No valid Unity Editor license
      found"*, while *Hub > Settings > Licenses* still lists an old Personal
      entry (which is a stale local record, not proof of a working licence).

      How to confirm it is the sign-in and not the project — two logs under
      `%LOCALAPPDATA%\Unity\`:

      - `Unity.Licensing.Client.log` →
        `[Code: 401] Token not found in cache`, followed by
        `ulf update failed. Details: No ULF license found.`
      - `Unity.Entitlements.Audit.log` → every request, e.g.
        `com.unity.editor.ui`, comes back `granted: False`.

      A stale Personal record cannot be refreshed while no account token
      exists, so the editor is granted zero entitlements.

      **Fix:** Hub → account icon (top left) → *Sign in* → then
      *Settings > Licenses* → remove the old Personal entry →
      *Add license* → *Get a free personal license*.

      This Hub is the **MSIX build**, so its config lives under
      `%LOCALAPPDATA%\Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\`
      and **not** `%APPDATA%\UnityHub\`. If a sign-in does not survive a Hub
      restart, install the classic (non-MSIX) Hub from unity.com instead.

      **Never paste `.ulf` contents into a chat, an issue or a commit.**
      The same file is the `UNITY_LICENSE` CI secret (Part 4.1) — once the
      sign-in works, generating it is the next unblocked step.

- [ ] **5.1 Open the project** with `6000.0.58f1` and let it import the new
      `Game.Sim` and `Game.Sim.Tests` assemblies. Confirm both compile
      (Console clean). Commit the newly generated `.meta` files for
      `Assets/Scripts/Sim*`, `Assets/StreamingAssets/**`.
- [ ] **5.2 Run the EditMode tests in the Editor.**
      *Window > General > Test Runner > EditMode > Run All* — the same 19 tests
      that run under `dotnet test Tools/SimTests/SimTests.csproj` must pass.
      If counts differ between Unity and dotnet, stop and report it.
- [ ] **5.3 Do not add a JSON package.** The tuning format is deliberately
      flat key=value (`balance.tuning`) so `Game.Sim` needs no dependencies —
      see the Milestone 1 PR description before changing this.

---

## Part 6 — After Milestone 2 (debug interface)

- [ ] **6.1 Open any scene and press Play.** The debug console spawns itself
      (`[GNP Debug Console]` via `RuntimeInitializeOnLoadMethod`) — no scene
      wiring exists or is needed. **F1** toggles it. Tabs: Meters, Graphs,
      Tuning, Actions, Contracts, Events, Scenario.
- [ ] **6.2 The balancing loop:** Tuning tab → drag sliders (LIVE, next tick) →
      Scenario tab → *Restart current* (keeps tuned values) → compare graphs →
      *Save tuned values to file* → the diff lands in
      `Assets/StreamingAssets/Tuning/balance.tuning` → commit it.
      A live-tuned run is NOT reproducible until saved.
- [ ] **6.3 Commit the new `.meta` files** Unity generates for
      `Assets/Scripts/Runtime/DebugConsole/`.
- [ ] **6.4 Move `Assets/game.unity` into `Assets/Scenes/`** in the Project
      window (still outstanding from Part 5; never move it in Explorer).

**Known trap — "Multiple Unity instances cannot open the same project" with no
Unity running:** a crashed or force-closed editor leaves a stale
`Temp/UnityLockfile`. Verify no `Unity.exe` process exists (Task-Manager), then
delete that file. `Temp/` is git-ignored; nothing to commit.

## Part 7 — After Milestones 3–6 (the playable slice)

- [ ] **7.1 Let Unity import, then commit the new `.meta` files** for
      `Assets/Scripts/Runtime/Game3D/` and `Assets/Art/` (same routine as 6.3).
      Nothing else needs wiring: press **Play** in any scene and the whole site
      builds itself at runtime.
- [ ] **7.2 The game boots into `00-first-shift`** — the teaching start: two
      racks, one contract paying the bills, empty slots inviting construction,
      and nothing scripted after tick 0. A hands-off year verifiably survives
      (no penalties, town content) — every disaster is player-built. The
      scripted balance-test year is `baseline-year` (Scenario tab in F1).
      **Controls.** Mouse look, **WASD** move, **Shift** run, **Space** jump,
      **Ctrl** duck (standing up checks headroom), **E** interact (hold for
      hold-controls: diesel lever, holding a door shut), **Q** drop what you
      carry, **Esc** frees the cursor, **F1** debug console, **F2** network
      panel, **F5/F9** save/load (host only). The fence gate (amber post caps)
      is at the south-west corner by the car park. Placement: **1–7** choose
      rack/evap/chiller/freecool/solar/battery/diesel, click a highlighted
      slot; **8/9/0** route power/cooling/network runs (left-click waypoints,
      right-click finishes — length becomes electrical loss).
- [ ] **7.3 The 5-player test needs standalone builds** (one project folder
      cannot be opened by five editors, and no third-party clone tool was added
      without asking). FIRST add both game shaders to *Project Settings →
      Graphics → Always Included Shaders* (`GNP/VertexColor` and
      `GNP/VertexColorTransparent`) — the game finds them via `Shader.Find` at
      runtime, and a build strips unreferenced shaders, which would render the
      whole world hot pink. Then *File → Build Profiles → Windows → Build* into
      e.g. `Builds/` (git-ignored). Start the editor as **Host** (F2 → Host),
      start four built players (F2 → address `127.0.0.1` → Join). For remote
      friends, forward UDP **7777** or use a VPN such as Tailscale/Hamachi — no
      relay service is wired up. Host and clients must run the SAME build state
      (the snapshot crosses the wire as raw struct bytes).
- [ ] **7.4 Saves** live at `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Game\gnp-save.json`
      (Unity's `persistentDataPath`) — plain JSON: the balance text, scenario,
      seed and the full command log. Loading replays the log; a save made with
      live-tuned (unsaved) balance values restores THOSE values, so the file is
      self-contained. Two honest limitations: values tuned live MID-run replay
      as if set from tick 0 (for a reproducible run: save tuning to file, then
      restart); and the save references the scenario by NAME — editing or
      deleting the `.scenario` file afterwards changes or blocks the replay
      (a changed file replays cleanly but lands in a different world).
- [ ] **7.5 Player settings worth setting once** (Edit → Project Settings):
      *Player → Resolution* windowed default helps the multi-client test;
      *Company/Product name* changes the save path above if you touch it.
