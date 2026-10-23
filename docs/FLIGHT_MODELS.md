# Flight models per class

Scoping for U.SPC-T17: each kind of ship flies by its own rules at the helm, not by one shared model with different numbers per class. Feel comes before physical accuracy. The owner reviews and tunes one family at a time, and each family is a ticket of its own (listed under [Work plan](#work-plan)).

This covers handling: how the stick, throttle and keys become motion. Weapons, lock-on, wingmen and attack runs belong to the dogfighting game (U.SPC-T424). Batteries, firing arcs, bracing, damage control and formations belong to the capital broadside game (U.SPC-T425, `Core/Combat/Broadsides.cs`, landed: see the README's Broadsides). Both games are built on the flight models described here.

## Where it stands

- **Flight is Newtonian** (U.SPC-T59, `Core/Combat/PilotFlight.cs`). Every design has a mass, thrusters on each axis and moments of inertia. Flight assist counter-thrusts toward the velocity the stick asks for, and F turns it off so the hull drifts.
- **Each family already has its own controls** (U.SPC-T269, `PilotAim`, `PilotControl`). A light fighter aims freely and the hull turns after the crosshair. A heavy fighter or hauler turns the nose directly with the mouse. A capital ship is steered on the keys at the helm while the mouse looks around.
- **What was still shared** was the motion itself: every class went through the same `PilotFlight.Step`, so the only differences between classes were the numbers in `PilotTuning`.

## The extension point (landed with T17)

`Core/Combat/FlightModels.cs`:

- **`FlightFamily`**: `LightFighter`, `HeavyFighter`, `Hauler`, `Capital`. A family describes a way of flying, not a particular hull.
- **`IFlightModel`**: one `Step` that takes the same arguments `PilotFlight.Step` always took (state, combat, hull dynamics, class tuning, input, gravity, dt). A model is stateless: anything that carries over between frames lives on `PilotState` or `ShipCombat`, so a single instance flies every hull in its family, and saves need no changes.
- **`FlightModelRegistry`**: two data tables, class to family and family to model. `FlightModelRegistry.Default` is the game's own. A family with no model registered flies `Fallback`, which is `NewtonianFlightModel`: the pre-T17 behaviour, unchanged.
- **`PilotFlight`'s stages** (`Rotate`, `DriveMultiplier`, `AssistThrust`, `RawThrust`, `Integrate`) are public. A new model is assembled from these stages plus rules of its own, so it never needs a branch inside `PilotFlight`.
- **The helm** (`UnitySpaceApp`) flies the piloted hull through `FlightModelRegistry.Default.For(class)`.

To add a way of flying: write an `IFlightModel` and its handling struct, then call `Register` for its family. To add a ship class: call `Assign` to put it in a family. Neither change touches the code that flies the other families.

**Tunables are data.** Each model takes a `readonly struct` of handling numbers (for example `LightFighterHandling`) with a `Default`. When the settings registry (U.SPC-T431) lands, each field becomes a declared setting, and the model reads whatever value the registry holds.

**The light/heavy fighter split** is made at the family level. The heavy fighter is the Gunship (U.SPC-T467), an armoured multi-crew hull with two crewed turrets and a spinal light railgun, built for brawling and anti-corvette work. It holds the heavy-fighter slots in patrol wings, escort wings (at the heavy hire fee) and the scene tour that the Breacher used to fill. The Fighter and Courier are the light fighters. The Breacher is an assault craft, not a fighter. It still flies in the heavy-fighter family until U.SPC-T466 turns it into a boarding round fired from an Assault Bay.

| Class | Family | Model today |
| --- | --- | --- |
| Fighter | Light fighter | `LightFighterFlightModel` (new) |
| Courier | Light fighter | `LightFighterFlightModel` (new; see [Couriers](#couriers)) |
| Gunship | Heavy fighter | Newtonian, until its own model lands |
| Breacher | Heavy fighter (until U.SPC-T466) | Newtonian |
| Freighter, Miner | Hauler | Newtonian, until its own model lands |
| Carrier, Battleship, Cruiser, Destroyer | Capital | Newtonian, until its own model lands |

With flight assist off, every family flies the Newtonian hull. Drifting and turn-and-burn remain available to every pilot. The family models change only what the flight computer does with assist on.

## Light fighters: Star Conflict

**Implemented** (`LightFighterFlightModel`, tests in `Tests/Core.Tests/FlightModelTests.cs`).

The reference is Star Conflict's interceptors and fighters: fast, twitchy, arcade-coupled flight in which the ship goes where it points, sideways dodges matter as much as turning, and the pilot keeps the crosshair on target by carving tight circles rather than by sliding. The aiming half (the crosshair runs ahead and the hull chases it) already shipped in T269. The motion half follows these rules:

1. **The track carves after the nose.** With assist on, the flight computer can push across the nose far harder than the manoeuvre thrusters could. The `Grip` value is scaled so that at 1, the track holds to the nose through the tightest turn at the corner speed. A Newtonian Fighter breaking through 90° at top speed still travels about 80° off its nose a second later. A light fighter at its corner speed is never more than 3° off, and at top speed it slides only briefly.
2. **Corner speed.** The nose swings at the full class turn rate at or below `CornerSpeedFraction` of top speed (0.6). Above that, the rate tapers linearly down to `OverspeedTurnFactor` (0.6) of full at full boost speed. Boost gets you somewhere, and easing off tightens the turn, so throttle becomes a dogfighting decision. This is one reason the grip limit has the scale it does: past the corner speed the hull starts to slide wide.
3. **Hard strafing.** Assist's strafe set point is 0.5 of the forward one, against 0.35 for the shared model, so a light fighter can sidestep fire.
4. **Everything else stays shared.** That includes boost charge and drain, the cruise drive, the gravity near worlds, roll, and assist-off drift.

First-pass numbers (`LightFighterHandling.Default`): grip 1.0, strafe 0.5, corner 0.6, overspeed turn 0.6. They are tuned for feel, and the review ticket is where they get tuned.

Open for the review: whether the boost's turn penalty should apply only while boost is held (as a Star Conflict afterburner) instead of being based on speed alone, and whether AI dogfighters (`Battle`'s dogfight, U.SPC-T274) should fly through the same model so their behaviour matches what the player flies.

## Heavy fighters: Everspace 2

**Proposed** (U.SPC-T441). Applies to the Gunship, the dedicated heavy-fighter hull.

Everspace 2's ships are always coupled and very responsive. The nose is the crosshair (control for this is already in from T269), strafing is a core move, and a heavy ship feels heavy because it carries real heft, not because it is sluggish. Proposed rules:

1. **Stiff, consistent turning.** There is no corner-speed penalty. The nose turns at the same rate at any speed, but that rate is capped lower than a light fighter's (about 70% of it), and it spins up hard: angular acceleration is high relative to the top rate, so the nose starts and stops crisply. That gives Everspace 2's "on rails" aim.
2. **Heft on the throttle.** Assist reaches its set point through a short ease (about 0.4 to 0.6 s to settle) instead of the shared model's thrust-limited push. Changes in speed read as weight without being slow.
3. **Equal-footing strafe.** Strafe and vertical set points are about 0.45 of forward, and ramp up as fast as forward thrust, for Everspace 2-style circle-strafing round a target.
4. **Boost overshoot.** When boost is released, assist doesn't brake hard. It bleeds the extra speed off over a couple of seconds, so a boosted run carries the hull through the target.
5. **No drift penalty.** Assist-off stays Newtonian, like every other family.

Tunables (`HeavyFighterHandling`): turn-rate cap fraction, settle time, strafe fraction, boost bleed time.

## Capital ships: Black Flag or Rebel Galaxy

**Proposed** (follow-up ticket). Applies to the Carrier, Battleship, Cruiser and Destroyer. The helm controls already shipped in T269.

### What each reference does

**Assassin's Creed IV: Black Flag** (the Jackdaw):

- **Sail settings set the speed.** The throttle steps through stopped, half sail and full sail, and the setting holds without keeping a key down. Slowing down tightens the turn. Full sail is for closing distance and ramming, not for fighting.
- **Broadsides fire from the side the camera faces.** Holding fire shows the spread on the water and releasing it fires the volley. A side reloads in a few seconds, so each pass gets one good volley per side and maybe a second. Chase guns cover the bow, fire barrels go off the stern, and swivel guns hit highlighted weak points.
- **The enemy telegraphs its broadside, and the player braces** to take less damage.
- **A pass has a rhythm:** close on a converging course, turn to put the beam on the enemy at medium range, fire while it crosses the beam arc, then come about for the next pass. The fight is decided by the turn circle against the reload, and by keeping off the enemy's beam while getting yours onto it. Slowing to tighten the turn while the enemy is still at speed is how you win the position.

**Rebel Galaxy** (Double Damage):

- **The fight is held to a plane.** Capital ships sit on a 2D battle plane. Smaller craft fly in 3D around it, and turrets cover what is above and below.
- **Broadsides fire port and starboard by looking to that side**, with lead indicators. Turrets can run on their own or be aimed by hand.
- **Stepped throttle, slow turns, and a timed deflector** in place of the brace.
- **It reads clearly in space** because the plane turns 3D positioning into 2D positioning, which is the only kind a broadside game can make readable.

### Recommendation

**Use Black Flag's handling and pass rhythm on Rebel Galaxy's battle plane.** Black Flag is the closer fit for how a capital ship should *fly*. Its stepped sails, its rule that a slower ship turns tighter, and its pass rhythm are what make positioning a skill, and that skill is what the owner asked to study. Rebel Galaxy's main contribution is the plane: without one, getting a broadside to bear in 3D becomes a mess of pitch, and the beam-on geometry a pass depends on breaks down. Rebel Galaxy's deflector timing and Black Flag's brace are the same idea, and that belongs to T425.

Handling rules for `CapitalFlightModel`:

1. **Engine telegraph.** W and S step through a set of speeds instead of being held: Full Astern, Stop, Slow, Half, Full, and Flank, which is the boost and drains the boost charge. The setting holds after the key is let go. The HUD shows the current step, and the hull eases toward each step's speed at the class's own spool rate.
2. **Steerageway.** Turn rate depends on speed. It peaks around Half, which is the fighting speed, drops at Full and Flank, and falls to a slow thruster pivot at Stop (about 25% of peak). This is the Black Flag rule that slowing tightens the turn, plus the naval rule that a ship needs way on to steer.
3. **Keel.** With assist on, slip across the hull is damped strongly, so a turn traces a turning circle instead of a pivot-and-slide. The hull goes where it points, with a little visible slip at Flank.
4. **Battle plane.** Inside a battle the plane is the battle's own plane, and outside one it is the system's plane. Pitch is held within about ±15° of the plane, and the hull levels itself when the keys are released. Q and E change depth slowly, like ballast, instead of pitching, so a ship can step over or under a line without leaving the plane.
5. **Pass timing as data.** Each class gets a target come-about time, meaning a 180° turn at Half: Destroyer about 6 s, Cruiser about 10 s, Battleship about 14 s, Carrier about 18 s. These sit against T425's broadside reload, which should be about half a beam window, so a pass at the fighting speed gives one volley per side and a quick-handed captain gets two. The handling numbers are fitted to the come-about times, not the other way round, and a test checks them.
6. **The Destroyer is the skirmisher** (as in `Battle.BroadsideFacing`). It keeps the telegraph and the plane, but its turning circle is tighter and its Flank faster, so it can cut across a battleship's bow.

Tunables (`CapitalHandling`, with per-class overrides): telegraph speed fractions, spool time, steerage curve, keel damping, plane pitch limit and level-off rate, and come-about target.

## Haulers: freighters and miners

**Proposed** (follow-up ticket). The controls already shipped in T269 (`Hauler`: nose steering with a slow mouse).

A hauler should be satisfying to fly because it is heavy: a run is planned, braking starts early, and a clean docking is an achievement. Proposed rules:

1. **Laden mass.** Cargo and propellant in the hold add to the hull's mass, so a full freighter accelerates, brakes and turns noticeably slower than an empty one. The mass comes from the grid hold and the tanks, which are already modelled.
2. **Weak retro, honestly shown.** The retro thrusters stay weak, and assist stops hiding it: the HUD shows the stop point (where the hull comes to rest at full retro) so the pilot can plan the braking.
3. **Docking mode.** Holding Ctrl (fine) inside a port's approach drops the set point to docking speed and lowers the turn rate for precise placement.
4. **No combat agility.** Strafe is weak (0.25), and boost is a short emergency burn, not a dodge.

Tunables (`HaulerHandling`): mass-per-tonne weighting, docking set point and turn fraction, strafe fraction.

## Couriers

The Courier flies as a light fighter for now: it shares the free-aim controls and the agile airframe. The proposal is a **Runner** family of its own, decided in its own ticket: the highest top speed and longest boost in the game, a wide turning circle (low grip, an early corner speed) and a cruise drive that spools up fastest. It would be a blockade runner, not a dogfighter. If the review prefers the Courier as a light fighter, that ticket closes as won't-do and nothing changes.

## Work plan

Each family is its own ticket, linked part-of U.SPC-T17, approved and worked one at a time:

1. U.SPC-T440: the light-fighter review and tuning pass with the owner, covering the open questions above.
2. U.SPC-T441: the heavy-fighter model (Everspace 2).
3. U.SPC-T442: the capital model (Black Flag handling on Rebel Galaxy's plane).
4. U.SPC-T443: the hauler model.
5. U.SPC-T444: the Courier, as a Runner family or a light fighter.

Every one of these adds a model and a handling struct, registers it, and keeps the logic in Core with tests. None of them edits `PilotFlight` or another family's model.
