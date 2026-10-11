# UnitySolarSystem

Swim through the real solar system like Quake, select anything and press **F** to focus on it Homeworld 3 style, and flip the whole system between navigable **gameplay** proportions and true **realistic** scale while it keeps moving. Built in Unity 6 (URP).

![Unity 6000.4](https://img.shields.io/badge/Unity-6000.4-000000) ![URP 17](https://img.shields.io/badge/URP-17.4-2b6cb0) ![C# 9](https://img.shields.io/badge/C%23-9-512BD4) ![Tests 87 passing](https://img.shields.io/badge/core%20tests-87%20passing-2ea44f) ![Status prototype](https://img.shields.io/badge/status-prototype-yellow)

![The inner solar system at gameplay scale, with orbit trails, planet labels and faction-coloured ship icons](docs/images/02-inner-system-gameplay.jpg)

A ground-up rebuild of [ExperimentSolarSystem](https://github.com/mindattic/ExperimentSolarSystem) (Babylon.js). Nothing here is a saved position: every planet and moon is propagated from orbital elements, and every ship flies a planned flip-and-burn transit.

## Why

- **Real orbital mechanics.** Planets come from JPL's Keplerian elements (Standish, 1800-2050), solved with Kepler's equation for the current date. Open it today and the planets are where they really are; Venus, for example, is near inferior conjunction in October 2026.
- **Homeworld 3 navigation.** Free swim with WASD and mouselook, then select something and press F: the camera flies to it, orbits it, and rides along as it moves. Any movement key drops you straight back into free swim from where you are.
- **Gameplay and realistic scale, live.** Press G and everything eases between a compressed, playable layout and true proportions (1 unit = 1,000 km) over about 2.5 seconds. Whatever you are looking at stays framed.
- **Billboards when things get small.** Anything under a few pixels, ships and planets alike, turns into a class-shaped icon in its faction colour, so you can always see where everything is, even at true scale where Earth is a speck.
- **A living traffic layer.** Three navies run carrier and battleship groups on patrol routes, while freighters, ore haulers and couriers haul goods between eleven ports.

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
- **Fleet.** 50 ships across 4 factions. Ships park in circular orbits at the real Keplerian rate for the body's GM. They depart when their orbit faces the target, then fly a constant-thrust brachistochrone (accelerate, flip at the midpoint, brake), plus a steady push that takes them from their parking orbit's velocity to the destination's, so they leave and arrive moving with the world. A hop within one planet's system (Earth to the Moon, Io to Europa) is flown in that planet's frame. Each transit is planned once, as an intercept against where the destination will be on arrival, dog-legs around the Sun or planet at the centre of its frame, and is checked against running into the bodies at either end.
- **Sim clock.** Real time up to 1 year per second, starting at the current UTC date.

### Rendering (`Assets/SolarSystem/Runtime`)

- **Floating origin.** The Unity camera never leaves the world origin. Positions are kept in doubles and every renderer is placed at scene position minus camera position, so a 600 m freighter renders cleanly 2.4 million km from Earth. Near and far planes adapt to the nearest surface.
- **Custom URP shaders** lit by the Sun's camera-relative position rather than a range-limited light: day/night terminator, atmosphere rims, procedural surfaces for bodies with no honest colour map, a granulating Sun with corona, Saturn's rings (C, B, Cassini Division, A, Encke Gap) shadowed by the planet, engine plumes, and a starfield with the Milky Way along the real galactic plane.
- **Ships.** Procedurally kit-bashed carriers (flight deck, island, hangar glow), battleships (turrets, superstructure), cruisers and destroyers in faction livery. Freighters, ore haulers and couriers use Kenney Space Kit models. Ships face their thrust vector, so you see them flip at the midpoint.
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

### Interface

- A navigator tree of bodies and fleets: click to select, double-click to focus.
- A details panel with live distance, light time, vis-viva orbital speed, period, eccentricity and gravity for bodies. For ships, it shows status, speed, ETA, cargo or patrol orders.
- Selection brackets, hover highlight, an off-screen pointer to the selected target, and the flight path of the selected ship.

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

`Builds/Windows/UnitySolarSystem.exe -capture <folder>` runs a scripted camera tour, saves screenshots and quits. That is how the images above were made.

## Controls

| Action | Input |
| --- | --- |
| Swim | W A S D |
| Down / up | Q / E, or C / Space |
| Boost / fine | Shift / Ctrl |
| Look (free) or orbit (focus) | Hold right mouse and drag |
| Swim speed (free) or zoom (focus) | Mouse wheel |
| Select | Left click (double-click to focus) |
| Focus on selection / re-frame | F |
| Leave focus, then deselect | Esc |
| Whole-system view | Home or Backspace |
| Toggle gameplay / realistic scale | G or Backquote |
| Planets: 0 Sun, 1 Mercury to 9 Pluto | Number keys (press twice to focus) |
| Cycle ships | Tab (Shift+Tab backwards) |
| Time warp slower / faster | , and . (or - and =) |
| Pause / reset to now | P / T |
| Orbits, labels, help, HUD | O, L, H, U |

## Project layout

```text
Assets/SolarSystem/
  Core/                 engine-free simulation (compiled by Tests/ as plain .NET)
    Kepler.cs             Kepler's equation, perifocal to ecliptic, vis-viva
    OrbitalElements.cs    JPL-style elements with secular rates
    BodyCatalog.cs        physical and orbital data for 26 bodies, with sources
    Ephemeris.cs          positions at any Julian date, orbit sampling
    ScaleModel.cs         realistic / gameplay mapping and its inverse
    Fleet.cs, Ships.cs    ship classes, factions, schedules, brachistochrone transits
    SimClock.cs           Julian-date clock and time warp
    BodyOrientation.cs    IAU prime meridians and parent-facing moons, so maps face the right way
    GasGiantWeather.cs    zonal wind profiles and the Great Red Spot's position
    AsteroidField.cs      111,267 SBDB asteroids, Kepler propagation (CPU and GPU-mirror)
  Runtime/
    SolarSystemApp.cs     frame loop, floating origin, picking, ship placement
    Camera/SpaceCamera.cs free swim, focus orbit, fly-to, SOI reference frames
    Views/                bodies, ships, line strips, starfield
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

87 tests cover the following:

- Kepler solver accuracy up to e = 0.97
- Earth's distance and heliocentric longitude at J2000
- Planet periods
- Lunar perigee and apogee, and the Earth-barycentre offset
- Every moon's position against JPL Horizons
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

## Limitations

- **Planet elements are a fit.** The JPL table is accurate to roughly arcminutes across 1800-2050, and drifts outside that window.
- **Ship drives are fiction.** The kinematics are real, but sustained 0.06 to 1.2 g torch drives are not, and transits ignore solar gravity, which is negligible at those accelerations.
- **Gameplay scale bends paths.** Ships fly their true path at both scales, but gameplay scale draws it through the same compression as the planets, eased near each end so the ship stays outside the inflated worlds. In a crowded moon system an inflated neighbouring moon can still be drawn over a passing ship.
- **Not a game yet.** There is no combat, economy or win condition.
- **Surfaces are wrapped on spheres.** Phobos, Deimos and the other small irregular moons are drawn round. Relief is shading only (exaggerated about 5×), never displacement, and lunar libration is not modelled.
- **Weather is a model on a 2015 snapshot.** Jupiter's map is Hubble's of January 2015, turned so the Great Red Spot is where it is today; the other storms on it are 2015 weather. The flow map reproduces real band speeds and the spot's rotation, not real storm evolution. The Saturn, Uranus and Neptune maps are stylized (CC BY), so only their motion is data.
- **Asteroids ignore Jupiter.** They fly two-body orbits from the 2026 osculating elements, drifting from their true places by roughly 0.01 AU a year. Only numbered objects brighter than H 15.5 (about 2 to 5 km) are included, and asteroids are labelled but not selectable.

## Credits and licences

- Planet colour maps: NASA / USGS (public domain). The Saturn, Uranus and Neptune maps are by [Solar System Scope](https://www.solarsystemscope.com/textures/), **CC BY 4.0**. Details in [Assets/SolarSystem/Resources/Textures/SOURCES.md](Assets/SolarSystem/Resources/Textures/SOURCES.md).
- Moon colour and LOLA elevation: [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/) (Ernie Wright; LRO LROC and LOLA teams). Jupiter: Hubble OPAL map via [NASA SVS](https://svs.gsfc.nasa.gov/12021/) (Simon, Wong, Orton). Moon, planet and satellite mosaics and DEMs: USGS Astrogeology, NASA PDS (MOLA MEGDR), and JPL's Solar System Simulator maps (Caltech/JPL/USGS) for Miranda, Titania and Oberon.
- Asteroids: [JPL Small-Body Database](https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html). Great Red Spot position and drift: British Astronomical Association Jupiter Section and JUPOS. Zonal winds: Porco et al. 2003, Sánchez-Lavega et al. 2000, Sromovsky et al.
- Freighter, ore hauler and courier models: [Kenney Space Kit](https://kenney.nl/assets/space-kit), CC0.
- Orbital data: JPL Solar System Dynamics, the IAU WGCCRE report, and NASA planetary fact sheets.
- Code: no LICENSE file yet, so all rights are reserved.

Part of [MindAttic](https://mindattic.com). See more projects at [github.com/mindattic](https://github.com/mindattic).
