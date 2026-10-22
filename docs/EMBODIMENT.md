# Universal embodiment

> Ticket U.SPC-T18. Owner direction, 2026-10-13: "every thing man made you can 'embody'."

You can take charge of anything man-made: jump into the helm of any ship, a turret on a hull or a
station, a satellite, then hand it back to its AI. Or you can leave it all to play out. This page sets
out the extension point that makes that possible without a separate piloting path for each kind of
thing.

## The extension point

Everything the player can take charge of implements one interface, `IEmbodiable`
(`Assets/UnitySpace/Core/Embodiment/Embodiment.cs`):

| Member | What it is |
| --- | --- |
| `Name`, `Kind`, `Faction` | What it is and whose it is. `Kind` is an `EmbodimentKind`: `Ship`, `Turret`, `Station`, `Satellite`. It is used for display and grouping only. Code never branches on it to decide how control works. |
| `Host` | The embodiable it is mounted on (a turret's ship or station), or null for a free hull. Hand-back and the "move up to the hull" control use it. |
| `CanEmbody` / `Blocker` | Whether the player can take it now, and if not, why ("destroyed", "derelict", "taken as a prize", "not a gun mount"). The only real blockers are a wreck, a hull that has been taken, and a thing that has nothing to control. |
| `PoseAt(jd)` | Where it is and which way it faces: an `EmbodimentPose` (frame body, km offset from that body's centre, forward, up). This is the same frame-body-plus-offset convention the pilot loop and the battles already use. |
| `TakeCharge()` | The player takes it. Its AI lets go: the fleet stops scheduling it, the battle stops working it. |
| `Control(input, dt)` | One frame of player input (`EmbodimentInput`: an aim direction, throttle, strafe, roll and two triggers), already turned into world terms by the Runtime's input layer. Each kind reads the parts it has: a turret reads aim and trigger, a hull reads all of it. |
| `HandBack()` | Its AI picks up from wherever the player left it, with the same pose and the same target picture. Nothing snaps back to a stale schedule. |

`EmbodimentSession` (`Core/Embodiment/EmbodimentSession.cs`) holds the one thing the player is
embodying. `Switch(next)` hands the current thing back before it takes the next, so changing seat is
always a hand-back followed by a take-charge. `Step(input, dt)` sends input on to the current thing,
or hands it back by itself once `CanEmbody` stops being true (shot away, captured). The session never
looks at `Kind`, so a new kind of controllable thing needs a new `IEmbodiable` and nothing else.

`Embodiables` (`Core/Embodiment/Embodiables.cs`) is where the things exist: the mountable guns on a
battle unit, and later the stations and satellites in a system. A picker UI lists from it and never
builds its own list.

## What implements it today

* **`ShipHelm` (Kind `Ship`).** This is the existing ship piloting behind the interface with nothing
  changed. Its eligibility check (`CanEmbody`), its starting frame (`FrameOf`), and its take-charge
  and hand-back bookkeeping (`World.PilotedShip`, an escort cut loose from its formation, the war's
  command handed over, a fighting hull's `BattleUnit` switched between the player and its crew) are
  the code `UnitySpaceApp` already ran. They have been moved into Core, and the Runtime now calls
  them. The hull's flight is still stepped by the Runtime's pilot loop (`PilotFlight.Step` and
  `PilotTuning`) because per-class flight models are being reworked under U.SPC-T17. `ShipHelm.Control`
  hands the trigger and the crosshair to the battle unit, as the pilot loop does now. Moving flight
  behind `Control` is follow-up work, to be done once T17 has landed.
* **`MountedTurret` (Kind `Turret`).** This is new. Any direct-fire gun mount (laser, heavy laser,
  autocannon, railgun, point defence, auto turret) on any `BattleUnit` can be taken: a ship's battery,
  a settlement's defence platform (today's stand-in for a station), or a gate fort. While it is held,
  the battle's `Fire` hands that one mount to `Battle.FireManned`. The mount traverses toward the
  player's aim at its own rate, stays inside its firing arc (a spinal railgun only within its gimbal),
  and fires down its barrel when the trigger is held. Every shot is a real aimed shot that hits only
  what it passes through (`ResolveShot`, the same path the player's nose guns use). Every other mount
  on the hull goes on firing under its crew, and hand-back returns this one to them. Nothing else
  about the host changes. Its own AI (or the player at its helm) keeps flying it.

## What follows (filed as `part-of U.SPC-T18`)

* U.SPC-T445, a Runtime turret seat: pick a mount on the selected or flown hull, a turret camera at the mount, and
  aim and fire from it.
* U.SPC-T446, a picker and switch UI: list embodiables near the camera or on the selected thing (from
  `Embodiables`), and jump between seats. Seamless switching from the strategy view without pausing
  is U.SPC-T422's to build on top of this.
* U.SPC-T447, stations as embodiables: a station hull with its own pose. It does not move, but it can turn its
  guns and launch its craft.
* U.SPC-T448, satellites as embodiables: an orbiting body you can turn and point (sensors, relays), with
  station-keeping thrust.
* U.SPC-T449, every ship role at the helm: freighters, miners, couriers and escorts all flown through `ShipHelm`
  with a sensible control set for their role, and ship flight moved behind `ShipHelm.Control` once
  U.SPC-T17's per-class flight models have landed.

## Rules for a new kind

1. Implement `IEmbodiable`. Keep the AI that holds the thing when the player doesn't inside that
   thing's own system (fleet, battle, station logic). `TakeCharge` and `HandBack` only flip who holds
   it.
2. Register where it can be found in `Embodiables`.
3. Add Core tests: it can be taken, it obeys input while taken, its AI resumes after hand-back, and
   it is dropped when it can no longer be embodied.
4. Never add a `switch` on `EmbodimentKind` to the session, the pilot loop or the picker.
