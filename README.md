# UnitySolarSystem

Swim through the real solar system like Quake, select anything and press **F** to focus on it Homeworld 3 style, and flip the whole system between navigable **gameplay** proportions and true **realistic** scale while it keeps moving. Built in Unity 6 (URP).

![Unity 6000.4](https://img.shields.io/badge/Unity-6000.4-000000) ![URP 17](https://img.shields.io/badge/URP-17.4-2b6cb0) ![C# 9](https://img.shields.io/badge/C%23-9-512BD4) ![Tests 112 passing](https://img.shields.io/badge/core%20tests-112%20passing-2ea44f) ![Status prototype](https://img.shields.io/badge/status-prototype-yellow)

![The inner solar system at gameplay scale, with orbit trails, planet labels and faction-coloured ship icons](docs/images/02-inner-system-gameplay.jpg)

A ground-up rebuild of [ExperimentSolarSystem](https://github.com/mindattic/ExperimentSolarSystem) (Babylon.js). Nothing here is a saved position: every planet and moon is propagated from orbital elements, and every ship flies a planned flip-and-burn transit.

## Why

- **Real orbital mechanics.** Planets come from JPL's Keplerian elements (Standish, 1800-2050), solved with Kepler's equation for the current date. Open it today and the planets are where they really are; Venus, for example, is near inferior conjunction in October 2026.
- **Homeworld 3 navigation.** Free swim with WASD and mouselook, then select something and press F: the camera flies to it, orbits it, and rides along as it moves. Any movement key drops you straight back into free swim from where you are.
- **Gameplay and realistic scale, live.** Press G and everything eases between a compressed, playable layout and true proportions (1 unit = 1,000 km) over about 2.5 seconds. Whatever you are looking at stays framed.
- **Billboards when things get small.** Anything under a few pixels, ships and planets alike, turns into a class-shaped icon in its faction colour, so you can always see where everything is, even at true scale where Earth is a speck.
- **A living system.** Eighteen settlements run a supply-and-demand economy that merchants trade on by themselves; three navies and a pirate band fight real battles with lasers, missiles and carrier fighters, losing ships and ports; and traffic between planetary systems rides X3-style hyperlanes. You watch it happen.

## Gameplay vs realistic

The same moment, focused on Earth, before and after pressing G. At gameplay scale the Sun, Venus and the Moon crowd the view. At realistic scale they drop back to their true distances and sizes, while Earth stays put.

| Gameplay | Realistic |
| --- | --- |
| ![Earth focused at gameplay scale, with the Sun, Moon and Venus large nearby](docs/images/07-earth-system-gameplay.jpg) | ![The same view at realistic scale: the Sun is a distant point and the Moon and ships are icons](docs/images/08-earth-system-realistic.jpg) |

![The inner solar system at realistic scale: true orbit spacing with planets reduced to icons](docs/images/09-inner-system-realistic.jpg)

## Features

### Simulation (`Assets/SolarSystem/Core`, no UnityEngine dependency)

- **Ephemeris.** The Sun, 8 planets, Ceres, Pluto, Eris and 14 moons. Earth is offset from the Earth-Moon barycentre by the Moon. The Moon has real nodal regression and apsidal precession. The other moons use JPL's satellite mean elements in their Laplace planes, fitted to JPL Horizons, so each sits within half a degree of its true place on its orbit across 2000-2050 (Phobos and Miranda within 2). Uranus's moons roll with it and Triton goes backwards.
- **Rotation and orientation.** Real IAU poles (RA/Dec converted to the ecliptic) and sidereal rotation periods, with retrograde rotators handled.
- **Scale model.** A radial mapping about each parent that sends every surface to its displayed surface at any blend, so orbits keep their true shape and direction in both modes and nothing in low orbit ends up inside a planet. Intermediate blends interpolate in log space.
- **Fleet.** Around a hundred ships across five factions, growing and shrinking as yards build and battles destroy. Ships park in circular orbits at the real Keplerian rate for the body's GM. They depart when their orbit faces the target, then fly a constant-thrust brachistochrone (accelerate, flip at the midpoint, brake), plus a steady push that takes them from their parking orbit's velocity to the destination's, so they leave and arrive moving with the world. A hop within one planet's system (Earth to the Moon, Io to Europa) is flown in that planet's frame. Each transit is planned once, as an intercept against where the destination will be on arrival, dog-legs around the Sun or planet at the centre of its frame, and is checked against running into the bodies at either end (and steered wide of the planet a moon orbits). Between planetary systems ships take the hyperlanes instead (see below).
- **Sim clock.** Real time up to 1 year per second, starting at the current UTC date.

### Rendering (`Assets/SolarSystem/Runtime`)

- **Floating origin.** The Unity camera never leaves the world origin. Positions are kept in doubles and every renderer is placed at scene position minus camera position, so a 600 m freighter renders cleanly 2.4 million km from Earth. Near and far planes adapt to the nearest surface.
- **Custom URP shaders** lit by the Sun's camera-relative position rather than a range-limited light: day/night terminator, atmosphere rims, procedural surfaces for bodies with no honest colour map, a granulating Sun with corona, Saturn's rings (C, B, Cassini Division, A, Encke Gap) shadowed by the planet, engine plumes, and a starfield with the Milky Way along the real galactic plane.
- **Ships.** Procedurally kit-bashed carriers (flight deck, island, hangar glow), battleships (turrets, superstructure), cruisers and destroyers in faction livery. Warship guns, launchers, point defence, shield emitters, sensor masts and hangar bays are separate parts bolted on at the hardpoints the combat model uses. Freighters, ore haulers and couriers use Kenney Space Kit models. Ships face their thrust vector, so you see them flip at the midpoint.
- **One-pixel orbit trails** that fade behind each body. Moon orbits fade in as you approach their planet.

### Real surfaces, moving weather and the asteroid belt

- **Real maps on 19 worlds.** The Moon (LRO LROC colour), Mercury (MESSENGER), Mars (Viking), the four Galilean moons (Galileo and Voyager), Titan, Enceladus, Phobos, Deimos, Ceres (Dawn), Pluto and Charon (New Horizons), Triton, Miranda, Titania and Oberon (Voyager), with relief shading from real elevation data on the Moon (LOLA), Mars (MOLA) and Mercury (MESSENGER DEM). Every map is turned by its IAU prime meridian, or faces its planet for synchronous moons, so the Moon shows Earth its real near side and Pluto keeps its 0° meridian on Charon. Gaps the spacecraft never imaged are a flat neutral grey, and every caveat (Io's muted false colour, Triton's ultraviolet-based colour reduced to brightness, Titan's haze) is listed in [SOURCES.md](Assets/SolarSystem/Resources/Textures/SOURCES.md).
- **Gas giants that move with their real winds.** Each latitude of Jupiter, Saturn, Uranus and Neptune drifts at its measured zonal wind speed (Cassini, Voyager and Hubble profiles; Jupiter's 23.7°N jet runs 165 m/s, about 13° of longitude a day) through a looping flow map with advected turbulence, on the simulation clock. Jupiter's Great Red Spot sits at its current System III longitude (BAA and JUPOS measurements, drifting 0.31° a day) and turns counter-clockwise, an anticyclone, about once in 6 days. The swirl fades out at time warps where it would strobe.
- **Titan and Venus as eyes see them.** Titan is an orange haze ball with Cassini's 938 nm surface map faint beneath; Venus is its nearly featureless cloud deck, super-rotating in 4 days.
- **The asteroid belt, fully populated.** 111,267 real asteroids from the JPL Small-Body Database (every numbered main-belt asteroid brighter than H 15.5, and every Jupiter Trojan brighter than H 15), each propagated on its own Kepler orbit on the GPU in one draw call, mapped through the same gameplay/realistic scale as the planets. The Kirkwood gaps and the two Trojan clouds come straight from the data. Vesta, Pallas, Hygiea, Juno, Psyche, Interamnia and three big Trojans are labelled.

| The Moon (LROC colour, LOLA relief) | Jupiter and the Great Red Spot | Io |
| --- | --- | --- |
| ![The Moon's far side at a low sun angle, with LOLA relief](docs/images/12-moon-terminator.jpg) | ![Jupiter with the Great Red Spot at its current longitude](docs/images/14-jupiter-great-red-spot.jpg) | ![Io from the Galileo colour mosaic, saturation reduced to approximate natural colour](docs/images/13-io.jpg) |

| Mars (Viking colour, MOLA relief) | The asteroid belt at gameplay scale | The asteroid belt at true scale |
| --- | --- | --- |
| ![Mars with MOLA relief shading](docs/images/16-mars.jpg) | ![111,000 real asteroids around the inner system at gameplay scale](docs/images/17-asteroid-belt-gameplay.jpg) | ![The same belt at realistic scale](docs/images/18-asteroid-belt-realistic.jpg) |

### The living system (game layer: `Assets/SolarSystem/Core/Economy`, `Core/Combat`, `Core/Hyperlanes.cs`)

The orbits are real; everything below is fiction, placed on worlds that plausibly hold what they produce.

- **Economy.** Sixteen goods: food, water and medical supplies; helium-3; nickel-iron, copper, zinc, platinum-group metals, rare earths and titanium; hydrocarbons and nitrogen; refined alloys, electronics, machinery and munitions. Eighteen settlements (Earth's orbital industry, Tycho City, Ares Prime, Ceres Hub, Ganymede Union, Kraken Port on Titan and so on) mine what their world has (ice moons sell water, Titan hydrocarbons, Io copper and zinc from its sulphide flows), turn it into goods through production chains (smelters, electronics fabs, machine works, pharmaceutical labs, munitions plants, shipyards), feed their people and price every good from their stock against what they use. Facilities grow when what they make sells dear and shrink when it is glutted; populations grow when fed and shrink when not.
- **Trade.** Freighters, ore haulers and couriers each pick the cargo and destination that earn the most per day of travel, counting deliveries already on the way, so goods flow from surplus to shortage without a script. Owners post supply contracts for essentials their outposts are short of. Profits go to the ship's faction, ports tax the trade, and traders steer clear of where the fighting has been.
- **War.** The Sol Federation and the Outer Reach Compact are at war (it cools to ceasefires and flares up again); the Belt Consortium trades with both and hunts pirates; the Black Banner raids busy ports from Blackwater on Deimos. Fleet groups take orders (patrol, attack a weak enemy port, defend one under attack, besiege, refit at a yard). A battle starts wherever hostile forces meet: groups, merchants or a settlement's orbital defence platforms. A port with no platforms left, held by a besieging group for 4 to 20 days, changes hands; refits and new hulls are paid for in alloys, electronics and munitions bought from the yard's market.
- **Capital ship combat.** Battles run a tactical simulation in a local space tens of km across: ships manoeuvre to their weapons' range, every hardpoint picks its own target, lasers and missiles hit particular subsystems (guns first, then shields, engines and the bridge; a lost reactor takes the ship with it), point defence shoots missiles and fighters down, and damaged ships break off and run. Turrets traverse onto their targets, shot-away hardpoints leave burning scars, breaches open as the hull falls, and a destroyed ship breaks into bow and stern that tumble apart and burn out.
- **Fighters.** Carriers launch their squadrons (two of interceptors to one of torpedo bombers) as they enter a battle. Each squadron flies attack runs on capital ships (approach from the side, dive in guns firing or torpedoes away, pass close, break off, swing round from a new side) or dogfights enemy fighters. Wingmen fly a loose vee: each pilot has their own wander, reaction lag and discipline, so formations breathe and stretch, and spread out on the run.
- **Boarding.** Every hull has an interior: a chain of compartments (bow, holds, quarters, batteries, midships, the bridge, engineering) with its crew and marines in them. A disabled ship (no engines, a lost bridge on a holed hull, or a hull nearly gone) can be boarded: a warship or raider lines up alongside, matches its drift, latches and sends its marines through the airlock; a pirate **breacher** rams its drill prow into the hull and its assault bay opens straight in; and mid-battle, warships fire drill-tipped **boarding pods** that bounce off shields but punch through bare plating and stick there, each with a squad aboard. A breach vents its compartment and kills crew until the emergency bulkheads seal. The fight inside is resolved over time, compartment by compartment: marines rush to the breach, casualties trade on numbers, training (each faction's marine quality, room for upgrades) and a defender's advantage, and the bridge and engineering are fortified for a last stand. Holding both takes the ship. Pirates keep warships they can crew, strip merchant holds and leave the hull adrift (or sell it to the breakers, or scuttle it); navies take serviceable prizes and scuttle pirates. The interior model keeps everything a future deck-by-deck boarding view needs.
- **Lane ambushes.** Raiders lie in wait just outside a busy hyperlane. When a ship passes, an electromagnetic pulse throws every ship near that point out of the lane, knocking out their drives and shields for a minute or two, and the raiders close in. The fight happens in open space beside the lane; survivors climb back in and carry on. Ambushed lanes look dangerous to route planners for a while, so traffic shifts. Fighters fly back and land on their carriers when the shooting stops, and carriers short of fighters (or groups short of marines) go home to refit.
- **Pilots, not clockwork.**
  - **Avoidance:** every ship watches the others an hour or so ahead. When two would pass closer than a safe distance (a few km plus a few hull lengths), both turn aside, the smaller giving way more, and drift back onto their planned path once clear. Ring queues are a spaced line up the approach path.
  - **Diversion:** merchants still on their way to a port where fighting has just broken out change course mid-flight, keeping their momentum, for the nearest safe port.
  - **Free ships:** yachts, survey ships and prospectors go where they please. They hop out to open space near a world to drift, then wander on to its neighbours.
  - **Route planning** steers round every world in the way, not just the trip's two ends.
- **Hyperlanes.** Twelve lanes link the planetary systems from Mercury to Pluto. Each system has a ramp per lane just outside its moons; a ship flips-and-burns to the ramp, rides the lane at up to 4% of light speed (easing in and out), and flips-and-burns from the far ramp to its destination. Earth to Mars takes a freighter about a day and a half instead of eleven. Lanes bow away from the Sun and route around it.

### Alpha Centauri and the way between the stars (`Core/Systems`)

- **A second star system.** The Sun's nearest neighbour, from published data:
  - **Alpha Centauri A and B** on their real 79.8-year orbit (Akeson et al. 2021), swinging between 11.2 and 35.4 AU apart around their barycentre.
  - **Proxima Centauri**, 12,000 AU out on its half-million-year orbit, placed where its sky position and distance put it today.
  - **Planets:** Proxima d and b (confirmed), Proxima c (**disputed**) and the JWST giant around A, "S1" (**candidate**). Unconfirmed worlds are labelled wherever they are named.
  - **Assumptions**, listed in [Core/Systems/SOURCES.md](Assets/SolarSystem/Core/Systems/SOURCES.md): orbital planes, phases, radii and the stars' poles.
- **Simulation:** every system runs all the time on the shared clock. Only the one you are in is drawn.
- **Lighting:** the two brightest stars where the camera is light the scene, each in the colour of its photosphere (A white, B orange, Proxima red). Proxima flares now and then.
- **Getting there:** click **Alpha Centauri** under **STAR SYSTEMS** in the navigator. The camera:
  1. pulls out of the system until it is a point;
  2. turns toward the destination and glides 4.34 light-years through a view of the 21 nearest real stars;
  3. falls into the other system's overview.

  Esc skips the trip.
- **Seeing the other system:** each system shows the other in its sky. Alpha Centauri is marked from Sol, and the Sun shines in Cassiopeia from Alpha Centauri.
- **Alpha Centauri's game layer**, thinner and younger than Sol's:
  - **Centauri Accord:** governs the Proxima colonies with a small navy.
  - **Toliman Combine:** a corporate concern with no warships, running stations round A and B.
  - **Kentaurus Free Traders:** carry most of the freight.
  - **Long Dark:** raid the long lanes from Blackhollow.
- **Settlements:** New Kentaurus, Ember Station, Far Reach, Rigil Gate, Aurora Skimmer, Toliman Forge and Blackhollow.
- **Crossing:** two trunk hyperlanes cross the 12,000 AU between the A–B pair and Proxima in five days each.

### In orbit: industry, spacecraft and docking rings (`Core/Infrastructure`)

- **Industry in orbit.** Every settlement's industry is now physical, in orbit round its world:
  - its shipyard is an open lattice cradle with the hull it is building framed out rib by rib, then plated as build points accrue, and launched from the yard's orbit;
  - each foundry, fab, works, arsenal and lab is a factory station with radiators;
  - big settlements have a spinning habitat ring.
- **Real spacecraft:**
  - ISS, Tiangong and Hubble;
  - JWST at Sun–Earth L2;
  - five Mars orbiters and the Lunar Reconnaissance Orbiter.

  Orbits are circular at the published altitudes and inclinations; where each craft sits along its orbit is illustrative. Constellation clouds (Starlink shells, GPS, Galileo, GLONASS, BeiDou and the geostationary belt, thousands of points) are off by default; **K** shows them.
- **Docking rings.** Every ship landing on a populous world with an atmosphere goes through one of its docking rings. Each ring is a station hovering directly over a city at stationary-orbit height (42,164 km from Earth's centre), turning with the ground.
  - **Fiction:** away from the equator a ring must keep station by thrust.
  - **Earth's seven rings:** New York, Paris, Beijing, Tokyo, Sydney, Johannesburg and Moscow.
  - **Other ringed worlds:** Mars has two, Venus one, Titan one, and Proxima b two on its terminator.
  - **How many rings:** more people, more rings (⌈log₁₀(population / 10⁵)⌉).
- **Landing.** Merchants and couriers land; warships and pirates hold orbit.
  1. **Approach:** the ship flies to the ring's holding point, velocity-matched to a point turning with the planet.
  2. **Queue:** it waits its turn; each ring clears one ship every 30 minutes.
  3. **Descent:** it drops tail-first down the line to the city, glowing with entry plasma through the atmosphere.
  4. **Trade:** it lands and trades.
  5. **Departure:** it climbs back to the ring and leaves from there.
- **Effect on trade:** route planning counts the queue and the descent.
- **Hull textures:** hulls, stations and structures are plated with a CC0 sci-fi panel texture (ambientCG MetalPlates015A), projected triplanar. Factions are told apart by the shade of their plating as well as their trim.

### Interface

- A navigator with a **STAR SYSTEMS** switcher, then the tree of bodies, fleets, docking rings (with their queues) and everything in orbit: click to select, double-click to focus.
- A details panel with live distance, light time, vis-viva orbital speed, period, eccentricity and gravity for bodies. For ships, it shows status, speed, ETA, cargo or patrol orders.
- Selection brackets, hover highlight, an off-screen pointer to the selected target, and the flight path of the selected ship (lanes included).
- Body panels show each settlement's exports, shortages and defences; ship panels show cargo and trade profit, or hull, shields, lost hardpoints, fighters aboard and orders. **M** opens a market board, **B** jumps to the biggest battle and drops to battle time (real time), and a ticker reports battles, captures, launches and ceasefires.

| Carrier group in formation | Freighter mid-burn | Saturn |
| --- | --- | --- |
| ![SFS Resolute carrier with escorts parked above Earth](docs/images/03-carrier-group.jpg) | ![A Kenney freighter with its engine plume lit, burning toward the Moon](docs/images/05-freighter.jpg) | ![Saturn with ring structure, the planet's shadow on the rings, and Titan](docs/images/06-saturn.jpg) |

## Quick start

Prerequisites: Unity **6000.4.3f1** (via Unity Hub). Optionally the .NET 10 SDK to run the core tests.

1. Clone: `git clone https://github.com/mindattic/UnitySolarSystem.git`
2. Open the folder in Unity Hub. The first import takes a few minutes. The editor opens `Assets/Scenes/SolarSystem.unity` automatically (or use **Solar System > Rebuild Main Scene**).
3. Press Play.

Any scene works, because `Bootstrap` creates the app if the scene does not have one. To build a Windows player, use **Solar System > Build Windows Player**, or run it headless:

```bash
Unity.exe -batchmode -quit -projectPath . -executeMethod SolarSystem.EditorTools.ProjectSetup.SetupAndBuild
```

`-demo` plays the scripted camera tour live and on loop as an unattended showcase, ending each loop by fast-forwarding to the next real fleet action and watching it at battle time; touch any control to stop it and take over.

Screenshots and video come from [MindAttic.Capture](https://github.com/mindattic/MindAttic.Capture) (needs ffmpeg on PATH). Production builds leave it off; build a capture-enabled player with **MindAttic > Capture > Build Windows Player (Capture Enabled)** (`Builds/Capture/UnitySolarSystem.exe`, or headless with `-executeMethod MindAttic.Capture.Unity.Editor.CaptureBuild.BuildWindows`), then:

| Command | Result |
| --- | --- |
| `UnitySolarSystem.exe -capture screen` | runs the tour once, saves a PNG at each stop and quits (how the images above were made) |
| `UnitySolarSystem.exe -capture video screen` | the same, also filmed |
| `UnitySolarSystem.exe -capture video` | films the session from start to exit at native resolution |
| `UnitySolarSystem.exe -demo -capture screen 5` | the live showcase with a PNG every 5 seconds |
| `UnitySolarSystem.exe -demo -capture video` | the live showcase, filmed (close the window to finish the video) |

Everything goes to `R:\MindAttic\Capture\UnitySolarSystem\`, named by time (`2026-10-11T14.38.03.577.png`); each image's `.json` sidecar names the tour stop it shows (`\"tag\": \"03-carrier-group\"`). In the editor, **MindAttic > Capture** toggles the same for Play Mode.

MindAttic.Capture also auto-starts in plain Editor Play sessions and dev builds (not gated by
`-capture`). Currently disabled project-wide: `"enabled": false` in
`Assets/StreamingAssets/capture.json`. Flip it to `true` (or remove the key) to re-enable.

## Controls

| Action | Input |
| --- | --- |
| Swim | W A S D |
| Down / up | Q / E, or C / ] |
| Boost / fine | Shift / Ctrl |
| Look (free) or orbit (focus) | Hold right mouse and drag |
| Swim speed (free) or zoom (focus) | Mouse wheel |
| Select | Left click (double-click to focus) |
| Focus on selection / re-frame | F |
| Leave focus, then deselect | Esc |
| Whole-system view | Home or Backspace |
| Toggle gameplay / realistic scale | G or Backquote |
| Planets: 0 Sun, 1 Mercury to 9 Pluto (in Alpha Centauri: A, B, Proxima, its planets, the stations) | Number keys (press twice to focus) |
| Cycle ships | Tab (Shift+Tab backwards) |
| Time warp slower / faster | , and . (or - and =) |
| Pause: time eases to a stop (x0.99 a step until under 1%), and eases back up when pressed again | Space (or P) |
| Reset to now | T |
| Orbits, labels, help, HUD | O, L, H, U |
| Market board | M |
| Watch the biggest battle (at battle time) | B |
| Hyperlanes on / off | N |
| Satellite constellations on / off | K |
| Level the view to the system's plane (smooth swing: the ecliptic in Sol, the A–B orbit in Alpha Centauri) | R, or the LEVEL button |
| Fly to another star system | Navigator, STAR SYSTEMS (Esc skips the trip) |

## Project layout

```text
Assets/SolarSystem/
  Core/                 engine-free simulation (compiled by Tests/ as plain .NET)
    Kepler.cs             Kepler's equation, perifocal to ecliptic, vis-viva
    OrbitalElements.cs    JPL-style elements with secular rates
    BodyCatalog.cs        physical and orbital data for 26 bodies, with sources
    Ephemeris.cs          positions at any Julian date, orbit sampling
    ScaleModel.cs         realistic / gameplay mapping and its inverse
    Fleet.cs, Ships.cs    ship classes, factions, schedules, brachistochrone transits, lane journeys
    Hyperlanes.cs         lane network, ramps, curves, routing
    World.cs              factions, settlements, ships; advances fleet, markets and wars together
    Systems/              Alpha Centauri's bodies, the Galaxy of star systems and the nearby-star catalogue
    Infrastructure/       orbital yards, factories, habitats, real spacecraft, constellations, docking rings
    Economy/              goods, recipes, settlements and markets, trade planning
    Combat/               hardpoint specs, battles, fighters, war strategy and diplomacy
    SimClock.cs           Julian-date clock and time warp
    BodyOrientation.cs    IAU prime meridians and parent-facing moons, so maps face the right way
    GasGiantWeather.cs    zonal wind profiles and the Great Red Spot's position
    AsteroidField.cs      111,267 SBDB asteroids, Kepler propagation (CPU and GPU-mirror)
  Runtime/
    SolarSystemApp.cs     frame loop, floating origin, picking, ship placement
    Camera/SpaceCamera.cs free swim, focus orbit, fly-to, SOI reference frames
    Camera/StarTravel.cs  the camera's trip between star systems
    Views/                bodies, ships (hardpoints, damage, wrecks), fighters, battle effects, hyperlanes, starfield
    Rendering/            procedural meshes and ship hulls, Kenney loader, shaders
    UI/                   HUD and procedural icon sprites
    Input/InputState.cs   every key binding in one place
  Resources/              shaders, colour maps, Kenney ship models
  Editor/ProjectSetup.cs  scene generation and player build
Tests/                  xUnit tests over Core (dotnet test)
```

## Tests

```bash
cd Tests/Core.Tests
dotnet test
```

158 tests cover the following:

- Kepler solver accuracy up to e = 0.97
- Earth's distance and heliocentric longitude at J2000
- Planet periods
- Lunar perigee and apogee, and the Earth-barycentre offset
- Every moon's position against JPL Horizons, and the Moon (lunar theory), Phobos, Charon, Ceres and Earth checked at five dates across 2000-2050
- Moon spin sense
- Scale-model monotonicity, invertibility and surface continuity
- No overlapping gameplay orbits
- Fleet continuity across every departure and arrival
- Brachistochrone timing, and intercepts meeting the moving target
- Velocity matching at departure and arrival
- Transits at gameplay scale leaving and joining parking slots exactly, never jumping, and staying out of the bodies at their ends
- Body orientation: Greenwich against sidereal time, synchronous moons facing their planet, Pluto's 0° meridian on Charon, map-north conventions
- Great Red Spot longitude against the JUPOS track, and wind profiles (anticyclonic shear around the spot, retrograde rotators)
- Asteroid positions against JPL Horizons (Vesta, Pallas, Hektor), the GPU propagation path, Kirkwood gaps at 3:1, 5:2 and 7:3, both Trojan clouds, and the shader's scale mapping
- Economy: every good produced in roughly the amount used, prices rising with scarcity, two years of trade with nobody going hungry and most traders in profit, traders only docking where welcome
- Combat: the stronger group winning and hardpoints taking the damage, reactor and shield-generator losses, hardpoints sitting on the hull, coarse steps at high warp, and two years of war with battles, losses and a working economy
- Fighters: launch, attack runs, guns and torpedoes, a loose formation that breathes, lead succession, and landing back aboard
- Boarding: fighting through to the bridge and engineering, a small party repelled, vacuum killing crew only until the bulkheads seal, pods bouncing off shields and boarding once they are down, a breacher ramming home, lining up alongside a disabled ship, and a year of lane ambushes and prizes
- Hyperlanes: every system reachable, lanes clear of the Sun, trips taking a day or two instead of weeks, continuity across every leg change, and drawn lanes meeting the burns at the ramps
- Alpha Centauri: the A–B separation (11.2 to 35.4 AU), period, barycentre balance and 2035 periastron; the orbit's 79.2° tilt to the sky; Proxima where the sky puts it and its planets' periods; the Sun in Cassiopeia as seen from there; lanes connecting every port and clearing all three stars; two years of trade, raiding and nobody starving; both systems on one clock
- Avoidance: head-on ships passing wide, ships sharing a spot spreading out, a month of the whole system with no near-collisions, free ships roaming, a mid-flight course change keeping position and momentum, and merchants turning aside from fresh battles
- Infrastructure: rings staying over their cities (latitude and longitude to a millionth of a degree, velocity = ω × r); FIFO queues clearing at most one ship per interval; no position jumps through queue, descent, landing and climb; only merchants landing; yards launching from their slips; constellations at their real altitudes and inclinations; JWST at L2

## Limitations

- **Planet elements are a fit.** The JPL table is accurate to roughly arcminutes across 1800-2050 (measured against Horizons over 2000-2050: Mercury to Neptune within 0.16°), and drifts outside that window.
- **Moons are mean orbits.** The Moon follows the main terms of a real lunar theory (within 0.01°), and Phobos includes its tidal decay (within 0.07°). Every other moon is an ellipse with steady precession, within 0.5° of JPL Horizons over 2000-2050 except Miranda (1.7°), whose neighbours' pulls a mean orbit cannot follow.
- **Ship drives are fiction.** The kinematics are real, but sustained 0.06 to 1.2 g torch drives are not, and transits ignore solar gravity, which is negligible at those accelerations.
- **Gameplay scale bends paths.** Ships fly their true path at both scales, but gameplay scale draws it through the same compression as the planets, eased near each end so the ship stays outside the inflated worlds. In a crowded moon system an inflated neighbouring moon can still be drawn over a passing ship.
- **Alpha Centauri is part guesswork.** The A–B orbit and Proxima's place are measured; the planets' orbital planes and phases, their radii and Proxima's orbital plane are assumed, and two of its worlds may not exist. Ships cannot yet travel between systems (only the camera can), and the trunk lanes across 12,000 AU in days are deep fiction.
- **The game layer is fiction.** Settlements, factions, production rates and prices are invented (on plausible worlds); hyperlanes and torch drives are not physics. Battles are fought at bodies and, by pirate interdiction, beside hyperlanes; ships on flip-and-burn transits cannot be intercepted. Marines are not drawn yet (the interior model is ready for a shipboard view). There is no player control or win condition yet; you observe.
- **Surfaces are wrapped on spheres.** Phobos, Deimos and the other small irregular moons are drawn round. Relief is shading only (exaggerated about 5×), never displacement, and lunar libration is not modelled.
- **Weather is a model on a 2015 snapshot.** Jupiter's map is Hubble's of January 2015, turned so the Great Red Spot is where it is today; the other storms on it are 2015 weather. The flow map reproduces real band speeds and the spot's rotation, not real storm evolution. The Saturn, Uranus and Neptune maps are stylized (CC BY), so only their motion is data.
- **Asteroids ignore Jupiter.** They fly two-body orbits from the 2026 osculating elements, drifting from their true places by roughly 0.01 AU a year. Only numbered objects brighter than H 15.5 (about 2 to 5 km) are included, and asteroids are labelled but not selectable.

## Credits and licences

- Planet colour maps: NASA / USGS (public domain). The Saturn, Uranus and Neptune maps are by [Solar System Scope](https://www.solarsystemscope.com/textures/), **CC BY 4.0**. Details in [Assets/SolarSystem/Resources/Textures/SOURCES.md](Assets/SolarSystem/Resources/Textures/SOURCES.md).
- Moon colour and LOLA elevation: [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/) (Ernie Wright; LRO LROC and LOLA teams). Jupiter: Hubble OPAL map via [NASA SVS](https://svs.gsfc.nasa.gov/12021/) (Simon, Wong, Orton). Moon, planet and satellite mosaics and DEMs: USGS Astrogeology, NASA PDS (MOLA MEGDR), and JPL's Solar System Simulator maps (Caltech/JPL/USGS) for Miranda, Titania and Oberon.
- Asteroids: [JPL Small-Body Database](https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html). Great Red Spot position and drift: British Astronomical Association Jupiter Section and JUPOS. Zonal winds: Porco et al. 2003, Sánchez-Lavega et al. 2000, Sromovsky et al.
- Freighter, ore hauler and courier models: [Kenney Space Kit](https://kenney.nl/assets/space-kit), CC0.
- Hull plating: [ambientCG MetalPlates015A](https://ambientcg.com/a/MetalPlates015A), CC0.
- Alpha Centauri, nearby stars, spacecraft orbits and ring sites: see [Core/Systems/SOURCES.md](Assets/SolarSystem/Core/Systems/SOURCES.md).
- Orbital data: JPL Solar System Dynamics, the IAU WGCCRE report, and NASA planetary fact sheets.
- Code: no LICENSE file yet, so all rights are reserved.

Part of [MindAttic](https://mindattic.com). See more projects at [github.com/mindattic](https://github.com/mindattic).
