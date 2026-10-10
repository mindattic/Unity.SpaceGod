# Quicksave — crash-recovery backfill after the 2026-10-10 computer crash
_Printed: 2026-10-10_

## Current task
There is no in-flight work to resume. The computer crashed, but this repo was already fully
committed and pushed to `origin/main` at the time (`git status` clean, nothing ahead/behind). This
quicksave is a manual backfill of the crash-recovery convention (`quicksave`/`quickload`/the
`do`/`recover` hook), not a resume of lost work — nothing was lost. Treat **Next concrete steps**
below as the reasoned continuation of the project from its last real commit, not as something
already underway.

## Decisions locked this session
- The crash-recovery convention (`.claude\commands\quicksave.md`, `.claude\commands\quickload.md`,
  `.claude\hooks\quickload-on-do.ps1`, `.claude\settings.json` registering the `UserPromptSubmit`
  hook) was added to this repo, mirroring the convention already used elsewhere in MindAttic
  (template: `D:\Projects\MindAttic\ChiMesh\.claude\`). Typing a bare `do`, `do it`, or `recover`
  now restores this file as working memory.
- Project conventions per `AGENTS.md`/`CLAUDE.md`: follow the shared protocol at
  `..\mindattic-agent-standard\AGENTS.md`, then this repo's own documentation (this project has no
  separate BIBLE.md/USER_STORIES.md of its own — the README is the canonical design/status doc).
  `Assets/SolarSystem/Core` is engine-free C# compiled standalone by `Tests/Core.Tests`
  (`dotnet test`, 41 passing); floating-origin rendering and the scale-model blend live in
  `Assets/SolarSystem/Runtime`. These architectural boundaries are not up for relitigation.

## State / where we are
- Last two commits: `0722599` — "Add missing CLAUDE.md provider-compatibility forwarder", and
  before that `8d7bb20` — "Unity solar system: Kepler ephemeris, live gameplay/realistic scale,
  Homeworld-style camera, patrolling fleets" (the substantive PoC commit). `origin/main` matches
  local `main` exactly.
- The simulation core (Kepler ephemeris for Sun/8 planets/Ceres/Pluto/Eris/14 moons, real IAU
  poles/sidereal rotation, gameplay<->realistic scale blending, a 50-ship 4-faction fleet with
  planned brachistochrone transits) is built and covered by 41 passing core tests
  (`Tests/Core.Tests`). Rendering (floating origin, custom URP shaders, billboarding, orbit
  trails) and the navigator/details-panel UI are built per the README's feature list.
- README's own "Limitations" section is the closest thing to a backlog this project has (there is
  no separate BIBLE/USER_STORIES doc here, unlike UnityEve/GridGameGaiden):
  - Moon phases are placeholder for every moon except Earth's (position along orbit is fixed,
    flagged in the details panel).
  - Planetary elements are a JPL fit accurate ~1800-2050, drifting outside that window.
  - Ship drives are kinematically real but the accelerations (0.06-1.2 g sustained) are fictional;
    transits ignore solar gravity (negligible at those accelerations, by design).
  - At gameplay scale, transiting ships follow a drawn path between compressed positions rather
    than the true inertial line (realistic scale uses the true line).
  - **"Not a game yet."** No combat, economy, or win condition — explicitly called out as the
    biggest gap.

## Open questions / pending
- No open questions were raised in this session (it is a backfill, not a live discussion). The
  owner has not said whether the next phase is "make it a game" (combat/economy/win condition) or
  continued simulation fidelity work (moon phases, ship paths at gameplay scale) — the steps below
  are inferred from the README's own stated limitations, not confirmed instructions.

## Next concrete steps
1. Confirm the project still builds/tests cleanly post-crash: `cd Tests/Core.Tests && dotnet test`
   (expect 41 passing), then open in Unity Hub and confirm `Assets/Scenes/SolarSystem.unity` still
   loads and plays.
2. Decide with the owner which limitation to close first. The README's ordering and the project's
   own framing ("Not a game yet") suggest this is the largest open gap: no combat, economy, or win
   condition exists at all, so this is likely the next big design conversation rather than a small
   fix.
3. If simulation fidelity is prioritized instead: give the non-Earth moons real orbital-position
   calculation (replacing the fixed placeholder angle) would be the next concrete, scoped,
   test-covered piece of work — it's explicitly flagged and isolated to `Ephemeris.cs`/
   `BodyCatalog.cs`.
4. Smaller, well-scoped fix: make gameplay-scale ship transit paths match the true inertial line
   the way realistic-scale already does, removing the "drawn, not flown" caveat.

## Anchors
- Files: `AGENTS.md`, `CLAUDE.md`, `README.md`, `Assets/SolarSystem/Core/Ephemeris.cs`,
  `Assets/SolarSystem/Core/BodyCatalog.cs`, `Assets/SolarSystem/Core/ScaleModel.cs`,
  `Assets/SolarSystem/Core/Fleet.cs`, `Assets/SolarSystem/Runtime/SolarSystemApp.cs`,
  `Assets/SolarSystem/Runtime/Camera/SpaceCamera.cs`, `Tests/Core.Tests`
- Names / ids: `Kepler`, `OrbitalElements`, `Ephemeris`, `ScaleModel`, `Fleet`, `SimClock`,
  `SpaceCamera`
- Commands to re-run:
  - `cd Tests/Core.Tests && dotnet test`
  - `Unity.exe -batchmode -quit -projectPath . -executeMethod SolarSystem.EditorTools.ProjectSetup.SetupAndBuild`
  - `Builds/Windows/UnitySolarSystem.exe -capture <folder>` (scripted camera tour + screenshots)
