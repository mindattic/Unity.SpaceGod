# UnitySolarSystem

The real solar system and Alpha Centauri in Unity 6, on live orbital mechanics, with an economy and a war you can watch or join at the helm of any ship, at playable or true scale.

![Unity 6000.4](https://img.shields.io/badge/Unity-6000.4-000000) ![URP 17](https://img.shields.io/badge/URP-17.4-2b6cb0) ![C# 9](https://img.shields.io/badge/C%23-9-512BD4) ![Tests 725 passing](https://img.shields.io/badge/core%20tests-725%20passing-2ea44f) ![Platform Windows](https://img.shields.io/badge/platform-Windows-0078D6) ![Status prototype](https://img.shields.io/badge/status-prototype-yellow)

![The inner solar system at gameplay scale, with orbit trails, planet labels and faction-coloured ship icons](docs/images/02-inner-system-gameplay.jpg)

A ground-up rebuild of [ExperimentSolarSystem](https://github.com/mindattic/ExperimentSolarSystem) (Babylon.js). Nothing here is a saved position: every planet and moon is propagated from orbital elements, and every ship flies a planned flip-and-burn transit.

## Why

- **Real orbital mechanics.** Planets come from JPL's Keplerian elements (Standish, 1800-2050), solved with Kepler's equation for the current date. Open it today and the planets are where they really are; Venus, for example, is near inferior conjunction in October 2026.
- **Homeworld 3 navigation.** Free swim with WASD and mouselook, then select something and press F: the camera flies to it, orbits it, and rides along as it moves. Any movement key drops you straight back into free swim from where you are.
- **Gameplay and realistic scale, live.** Press G and everything eases between a compressed, playable layout and true proportions (1 unit = 1,000 km) over about 2.5 seconds. Whatever you are looking at stays framed.
- **Billboards when things get small.** Anything under a few pixels, ships and planets alike, turns into a class-shaped icon in its faction colour, so you can always see where everything is, even at true scale where Earth is a speck.
- **A living system.** Twenty-one settlements run a supply-and-demand economy that merchants trade on by themselves; three navies and a pirate band fight real battles with lasers, missiles and carrier fighters, losing ships and ports; and traffic between planetary systems rides X3-style hyperlanes. You watch it happen.
- **Take the helm when you want to.** Press P on any ship to fly it yourself: steer with the mouse, fire its forward guns, cruise across open space, trade crates and fill its tanks at a port, answer a pirate's demand, latch on to a derelict or a wreck and pick it over, turn outlaw and run from the patrols, or fly through a jump gate to another star.

## Gameplay vs realistic

The same moment, focused on Earth, before and after pressing G. At gameplay scale the Sun, Venus and the Moon crowd the view. At realistic scale they drop back to their true distances and sizes, while Earth stays put.

| Gameplay | Realistic |
| --- | --- |
| ![Earth focused at gameplay scale, with the Sun, Moon and Venus large nearby](docs/images/07-earth-system-gameplay.jpg) | ![The same view at realistic scale: the Sun is a distant point and the Moon and ships are icons](docs/images/08-earth-system-realistic.jpg) |

![The inner solar system at realistic scale: true orbit spacing with planets reduced to icons](docs/images/09-inner-system-realistic.jpg)

## Features

### Simulation

- **Ephemeris.** The Sun, 8 planets, Ceres, Pluto, Eris and 14 moons. Earth is offset from the Earth-Moon barycentre by the Moon. The Moon has real nodal regression and apsidal precession. The other moons use JPL's satellite mean elements in their Laplace planes, fitted to JPL Horizons, so each sits within half a degree of its true place on its orbit across 2000-2050 (Phobos and Miranda within 2). Uranus's moons roll with it and Triton goes backwards.
- **Rotation and orientation.** Real IAU poles (RA/Dec converted to the ecliptic) and sidereal rotation periods, with retrograde rotators handled.
- **Scale model.** A radial mapping about each parent that sends every surface to its displayed surface at any blend, so orbits keep their true shape and direction in both modes and nothing in low orbit ends up inside a planet. Intermediate blends interpolate in log space.
- **Fleet.** Five factions in Sol, with yards building merchants toward a target of 100 traders (capped at 170) plus warships, and battles destroying them. Ships park in circular orbits at the real Keplerian rate for the body's GM. They depart when their orbit faces the target, then fly a constant-thrust brachistochrone (accelerate, flip at the midpoint, brake), plus a steady push that takes them from their parking orbit's velocity to the destination's, so they leave and arrive moving with the world. A hop within one planet's system (Earth to the Moon, Io to Europa) is flown in that planet's frame. Each transit is planned once, as an intercept against where the destination will be on arrival, dog-legs around the Sun or planet at the centre of its frame, and is checked against running into the bodies at either end (and steered wide of the planet a moon orbits). Between planetary systems ships take the hyperlanes instead (see below).
- **Sim clock.** Real time up to 1 year per second, starting at the current UTC date.

### Rendering

- **Floating origin.** The Unity camera never leaves the world origin. Positions are kept in doubles and every renderer is placed at scene position minus camera position, so a 600 m freighter renders cleanly 2.4 million km from Earth. Near and far planes adapt to the nearest surface.
- **Custom URP shaders** lit by the Sun's camera-relative position rather than a range-limited light: day/night terminator, atmosphere rims, procedural surfaces for bodies with no honest colour map, a granulating Sun with corona, Saturn's rings (C, B, Cassini Division, A, Encke Gap) shadowed by the planet, engine plumes, and a starfield with the Milky Way along the real galactic plane.
- **Ships.** Procedurally kit-bashed carriers (flight deck, island, hangar glow), battleships (turrets, superstructure), cruisers and destroyers in faction livery. Warship guns, launchers, point defence, shield emitters, sensor masts and hangar bays are separate parts bolted on at the hardpoints the combat model uses. Fighters are their own small twin-gun hull. Freighters, ore haulers and couriers use Kenney Space Kit models. A ship's nose stays prograde through the whole transit, braking half included, and every hull trails engine embers while it burns.
- **One-pixel orbit trails** that fade behind each body. Moon orbits fade in as you approach their planet.

### Real surfaces weather and asteroids

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

### The living system

The orbits are real; everything below is fiction, placed on worlds that plausibly hold what they produce.

- **Economy.** Sixteen goods: food, water and medical supplies; helium-3; nickel-iron, copper, zinc, platinum-group metals, rare earths and titanium; hydrocarbons and nitrogen; refined alloys, electronics, machinery and munitions. Eighteen settlements (Earth's orbital industry, Tycho City, Ares Prime, Ceres Hub, Ganymede Union, Kraken Port on Titan and so on) mine what their world has (ice moons sell water, Titan hydrocarbons, Io copper and zinc from its sulphide flows), turn it into goods through production chains (smelters, electronics fabs, machine works, pharmaceutical labs, munitions plants, shipyards), feed their people and price every good from their stock against what they use. Facilities grow when what they make sells dear and shrink when it is glutted.
- **People and labour.** Each person eats, drinks and breathes by NASA's planning figures: 1.76 kg of food a day, and the water a closed loop loses (98% recovered on a station, as on the ISS since 2023, less in a colony with showers), plus the water split for oxygen and the nitrogen that replaces leaked air. Populations grow when fed, shrink when not, and migrate toward food, water and better expected wages, never across a war front. Every facility needs a crew, life support first; short-handed it makes less, and under a quarter of its crew it stalls. Wages are taxed into the owner's treasury alongside port taxes, and the treasury pays for new hulls, refits, defence platforms and every warship's daily upkeep, so a starving, emptying economy really does weaken its navy. Figures and assumptions are in [Economy/SOURCES.md](Assets/SolarSystem/Core/Economy/SOURCES.md).
- **Trade.** Freighters, ore haulers and couriers each pick the cargo and destination that earn the most per day of travel, counting deliveries already on the way, so goods flow from surplus to shortage without a script. Every run is priced by what it really costs: the propellant its burns throw out with that cargo aboard (at the local price), a toll for every hyperlane it rides (paid to whoever keeps that system's ramps), and the crew's days. A run the tanks cannot cover, with a reserve, is left out; a ship running low with nothing to carry makes for the nearest port that sells propellant. Owners post supply contracts for essentials their outposts are short of. Profits go to the ship's faction, ports tax the trade, and traders steer clear of where the fighting has been.
- **Propellant.** Every hull has a fusion drive with its own exhaust velocity (2,200 km/s for an ore hauler to 4,500 for a courier) and a dry mass and tanks of its own, and the rocket equation sets what each manoeuvre costs: a full hold cuts a freighter's delta-v by nearly two thirds. Civilian ships pay for every transit from their tanks (the flip-and-burn plus matching velocity at each end; hyperlane rides cost no propellant) and fill up in port with water or hydrocarbons, whichever is cheaper there, and the helium-3 that burns them. Those sales are propellant demand the markets stock for. A ship that cannot cover its next trip waits in port for propellant; one that runs dry in flight (now and then a holed tank vents most of what it carries) drifts where its tanks gave out and calls for a tanker, which fills it in a couple of days at a premium, and its call is a distress adventure the player can answer by pumping some of their own reaction mass across.
- **War.** The Sol Federation and the Outer Reach Compact are at war (it cools to ceasefires and flares up again); the Belt Consortium trades with both and hunts pirates; the Black Banner raids busy ports from Blackwater on Deimos and from three hidden bases on real belt asteroids (Hollow Iron on Psyche, Lantern Rock on Lutetia, Dactyl's Rest on Ida). A hidden haven is off the map until a ship's scanners pass within 2 million km of it, a navy follows beaten raiders home, or captured raiders give it away. A navy that finds one sends a strong group to break its two gun platforms and then board it (seizing its stolen goods) or level it; a pilot who has broken one can do the same (1 board, 2 destroy). A fallen haven's raiders lie low and no new bands sail from it until the pirates rebuild it, 180 days on. Fleet groups take orders (patrol, attack a weak enemy port, defend one under attack, besiege, refit at a yard). A battle starts wherever hostile forces meet: groups, merchants or a settlement's orbital defence platforms. A port with no platforms left, held by a besieging group for 4 to 20 days, changes hands; refits and new hulls are paid for in alloys, electronics and munitions bought from the yard's market.
- **Capital ship combat.** Battles run a tactical simulation in a local space tens of km across: ships manoeuvre to their weapons' range, every hardpoint picks its own target, lasers, railguns and missiles hit particular subsystems (guns first, then shields, engines and the bridge; a lost reactor takes the ship with it), point defence shoots missiles and fighters down, and damaged ships break off and run. Turrets traverse onto their targets, shot-away hardpoints leave burning scars, breaches open as the hull falls, and a destroyed ship breaks into bow and stern that tumble apart and burn out.
- **Weapons that travel.** Railgun and gun rounds fly a lead-compensated straight line and land only when they arrive; a miss keeps flying and fades, and the miss rate depends on the target's size. Beams sweep onto their target and leave a scorch, with muzzle flash and recoil. Destroyers carry a fixed spinal railgun.
- **Wrecks.** A ship destroyed in battle leaves a slowly tumbling hull inside a drifting debris field where it died, at a body or beside a hyperlane, carrying its cargo as salvage until it breaks up and is cleared.
- **Surrender.** An outgunned, battered hull can strike its colours instead of fighting to the last: it breaks off, powers down its weapons and hails. How soon depends on its damage, the odds and its faction's temperament (pirates break first, navy crews last). Its captor ransoms the cargo, takes the ship through the boarding path, or lets it go.
- **Escort fighters.** Besides carrier squadrons, each of the six navy patrol groups flies two named fighters as escorts.
- **Fighters.** Carriers launch their squadrons (two of interceptors to one of torpedo bombers) as they enter a battle. Each squadron flies attack runs on capital ships (approach from the side, dive in guns firing or torpedoes away, pass close, break off, swing round from a new side) or dogfights enemy fighters. Wingmen fly a loose vee: each pilot has their own wander, reaction lag and discipline, so formations breathe and stretch, and spread out on the run.
- **Boarding.** Every hull has an interior: a chain of compartments (bow, holds, quarters, batteries, midships, the bridge, engineering) with its crew and marines in them. A disabled ship (no engines, a lost bridge on a holed hull, or a hull nearly gone) can be boarded: a warship or raider lines up alongside, matches its drift, latches and sends its marines through the airlock; a pirate **breacher** rams its drill prow into the hull and its assault bay opens straight in; and mid-battle, warships fire drill-tipped **boarding pods** that bounce off shields but punch through bare plating and stick there, each with a squad aboard. A breach vents its compartment and kills crew until the emergency bulkheads seal. The fight inside is resolved over time, compartment by compartment: marines rush to the breach, casualties trade on numbers, training (each faction's marine quality, room for upgrades) and a defender's advantage, and the bridge and engineering are fortified for a last stand. Holding both takes the ship. Pirates keep warships they can crew, strip merchant holds and leave the hull adrift (or sell it to the breakers, or scuttle it); navies take serviceable prizes and scuttle pirates. The interior model keeps everything a future deck-by-deck boarding view needs.
- **Salvage and boarding derelicts.** A hull the raiders cast adrift drifts where it was taken, with a fifth of its cargo they had no time for and its own stores, and sometimes crew who sealed themselves in or a few raiders still stripping it. Flying a ship, come alongside a derelict or a wreck and match velocity (within about a kilometre and 20 m/s) and press 1 to latch on. Raiders aboard hold the hull until your marines go through the airlock and fight them compartment by compartment, on the same interior model as any boarding; survivors are found once the hulk is clear. Then choose: salvage cargo crate by crate into the grid hold, strip a component (fitted in place of one of your own shot-away hardpoints of the same kind, or broken down into a crate), or claim the hull if it can still fly, with rescued survivors and hands from your ship as its prize crew.
- **Lane ambushes.** Raiders lie in wait just outside a busy hyperlane. When a ship passes, an electromagnetic pulse throws every ship near that point out of the lane, knocking out their drives and shields for a minute or two, and the raiders close in. The fight happens in open space beside the lane; survivors climb back in and carry on. Ambushed lanes look dangerous to route planners for a while, so traffic shifts. Fighters fly back and land on their carriers when the shooting stops, and carriers short of fighters (or groups short of marines) go home to refit.
- **Economic shocks and the news** (`MarketShocks`). Nothing is scripted: each shock comes from what the sim is doing and moves stock and prices through the ordinary markets, so traders reroute and prices jump where they should. Mines worked hard (and short of machinery for upkeep) have accidents that shut most of their extraction for a few weeks, and every ore seam holds a few years of output before it plays out and the mine is cut back (until prospectors find another). Hydroponic farms short of nitrogen catch blights that spoil food and cut harvests. Wages follow the cost of living slowly, and when prices run well ahead of them workers strike: industry stops, life support runs, and the strike ends with a raise. A navy at war whose ports run short of essentials or munitions brings in rationing. A besieging group holding the space over a port declares a blockade through diplomacy, and no ship docks there until it lifts; a navy that catches a neutral's traders running munitions (or electronics and alloys) to its enemy embargoes them, closing its ports to their ships until the embargo runs out or the war stops. All of it is reported as it happens, along with every price spike (a port's price at 2.5 times normal, with its cause, or a good dear across the whole system), battles, captures and surrenders. X opens the news feed, filtered to markets, war or the rest; click a report to select where it happened, and the market board lists what is shaking that port right now.
- **Law, crime and bounties** (`Core/Law`). Each faction's law holds at its worlds and in the planetary systems whose hyperlane ramps it keeps, and on those lanes; a haven's rock out in the belt, and space nobody keeps, is lawless. Attacking a ship you are not at war with, piracy (any attack by raiders, or robbing or taking a lawful hull), smuggling, trespass and running from a scan are crimes, but only if a ship within 1 million km or a station within 3 million saw them: a merchant whose crew all died out on an empty lane tells nobody, and a lawful ship that fights back has done nothing wrong. A crime on record raises the offender's notoriety (halving every 90 days), lowers their standing with that jurisdiction and with the victim's faction, and posts a bounty there (assault 50,000 cr, piracy 150,000), which stands for 180 days unless renewed. Every raider captain gets a record from what their band actually does, and the bounty board adds what is posted on them to the price on their head; a wanted captain killed or taken by those who posted the bounty has paid it. Flying a ship, lock a lawful ship (T) and hold fire on it to attack it: its group, its faction's ships in orbit there and its platforms over that world fight back. Navy patrols hail you in their own space for a 20-second cargo scan (hold position; stolen goods, which is everything a haven sells, are confiscated as smuggling), and if you are wanted, anywhere, they order you to heave to and pay the bounty within 30 seconds (1) or be interdicted. Standing sets what you pay (up to 10% less when trusted, up to 30% more when not), whether a port lets you dock at all (not below -25, nor with a bounty of 100,000 cr or more posted by its owner) and whether a faction offers you distress calls, escorts and bounties. Fines can be paid at any of a faction's ports, welcome or not.
- **Adventures near you** (`Core/Events`). Whatever the sim is doing around you turns into timed opportunities, each with a HUD marker, a short hook, an expiry and a reward (credits, cargo crates for your grid hold, standing with a faction): distress calls from ships really under attack or drifting after an interdiction pulse, derelicts and wrecks to latch on to and pick over, raiders lurking off lanes with a record of ambushes, merchants paying for an escort across a dangerous lane, bounties on the named captains of real raider groups, and the odd rare anomaly. Fly there and see it through; ignore it and it fails when it expires or the situation settles without you.
- **Pilots, not clockwork.**
  - **Avoidance:** every ship watches the others an hour or so ahead. When two would pass closer than a safe distance (a few km plus a few hull lengths), both turn aside, the smaller giving way more, and drift back onto their planned path once clear. Ring queues are a spaced line up the approach path.
  - **Diversion:** merchants still on their way to a port where fighting has just broken out change course mid-flight, keeping their momentum, for the nearest safe port.
  - **Free ships:** yachts, survey ships and prospectors go where they please. They hop out to open space near a world to drift, then wander on to its neighbours.
  - **Route planning** steers round every world in the way, not just the trip's two ends.
- **Hyperlanes.** Twelve lanes link the planetary systems from Mercury to Pluto. Each system has a ramp per lane just outside its moons; a ship flips-and-burns to the ramp, rides the lane at up to 4% of light speed (easing in and out), and flips-and-burns from the far ramp to its destination. Earth to Mars takes a freighter about a day and a half instead of eleven. Lanes bow away from the Sun and route around it. Every ship riding a lane, or lurking off one, shows as a marker in its faction colour.

### Alpha Centauri and the way between the stars

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

  Esc skips the trip. A piloted ship can make the crossing itself through the jump gates (see Piloting below).
- **The gate network.** Jump gates join the fourteen simulated systems in a sparse lattice, not every pair: each system is linked to its two nearest, and any islands that leaves are joined by their shortest links, so every star is reachable, many only by way of others (18 links; Tau Ceti is four gates from Sol). Each gate sits at its system's edge facing the star it leads to and belongs to that system's leading power. A crossing takes six hours plus two hours a light-year (Sol to Alpha Centauri, 4.34 ly, about 15 hours).
- **Traffic between the stars.** AI ships route over the gate graph by crossing time, flying out to a gate, through it and on to the next. Freighters carry a good to another system's market when the price gap there beats the trip, gate tolls included (3% of the cargo's cost a light-year, paid to the gates' owners), trade there a few weeks and bring a backhaul home; couriers carry the mail between Sol and Alpha Centauri for a fee; yachts and survey ships go to look at the unsettled stars; raider groups now and then slip out to raid the other settled system.
- **Blockades.** A gate never opens for its owner's declared enemies, and after raiders come through it, its owner shuts it to everyone but its own ships for 30 days and sends a navy group after them to the system they came from. Traffic then goes round by other gates, or waits at the gate (and gives up after 8 days). The STAR SYSTEMS navigator shows how many gates away each system is, the gates of the one you are in with their crossing times and which are shut, the ships in the gates, and the ships here from beyond them; the view between the stars draws the lattice (red where a gate is shut) with the ships in transit moving along it.
- **Seeing the other system:** each system shows the other in its sky. Alpha Centauri is marked from Sol, and the Sun shines in Cassiopeia from Alpha Centauri.
- **Alpha Centauri's game layer**, thinner and younger than Sol's:
  - **Centauri Accord:** governs the Proxima colonies with a small navy.
  - **Toliman Combine:** a corporate concern with no warships, running stations round A and B.
  - **Kentaurus Free Traders:** carry most of the freight.
  - **Long Dark:** raid the long lanes from Blackhollow.
- **Settlements:** New Kentaurus, Ember Station, Far Reach, Rigil Gate, Aurora Skimmer, Toliman Forge and Blackhollow.
- **Crossing:** two trunk hyperlanes cross the 12,000 AU between the A–B pair and Proxima in five days each.

### The nearest real systems (`Core/Systems/NearbySystems.cs`)

- **Twelve more systems are visitable** from **STAR SYSTEMS**, each at its real place and distance: Barnard's Star, Luhman 16, Wolf 359, Lalande 21185, Sirius, Epsilon Eridani, Ross 128, Epsilon Indi, Tau Ceti, GJ 1061, Luyten's Star and Teegarden's Star.
- **One builder:** every system outside Sol, Alpha Centauri included, is a catalogue entry that one builder (`StarSystemBuilder`) turns into bodies.
- **Stars and binaries:** Sirius A and its white dwarf, and the brown-dwarf pair Luhman 16, move on their measured visual orbits. Epsilon Indi's brown dwarfs Ba and Bb circle each other 1,500 AU from A.
- **Planets:** the confirmed ones come from the NASA Exoplanet Archive, with their real periods, axes, eccentricities and masses ("at least" where only m sin i is known). Candidates (Wolf 359 b, Lalande 21185 d, Luyten d and e) are labelled as such. Each planet is flagged inside or outside its star's habitable zone (Kopparapu et al. 2013).
- **Debris belts:** Epsilon Eridani's three belts and Tau Ceti's disc are drawn in their measured planes.
- **Unsettled:** these systems have no settlements, lanes or ships yet. Anything put there later is game-layer fiction.
- **Sources:** every value is listed in [Core/Systems/SOURCES.md](Assets/SolarSystem/Core/Systems/SOURCES.md).

### Orbital industry and docking rings

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

### Piloting

- **Any ship, anywhere.** Select a ship and press P to take the helm, whether it is parked, mid-transit, on a hyperlane, queued at a ring, landed or in a battle. An escort leaves its formation; a ship already fighting hands you its place in the battle. The clock drops to real time (press . to speed it up), and P again hands the ship back.
- **Flight.** Newtonian: every design has a mass, a main drive, weaker retro thrusters, manoeuvre thrusters on each axis and moments of inertia, so a hull carries its momentum and a carrier's nose is slow to start swinging and slow to stop. The mouse steers, W and S thrust, A, D, Q and E strafe, Z and X roll, Shift lights an afterburner. With flight assist on, the flight computer counter-thrusts toward the velocity the stick asks for, killing drift and holding the hull against gravity as far as its thrusters can. F switches it off: then the throttle is raw thrust and nothing cancels anything, for sliding past a target with the guns on it or flipping end for end and burning to brake. Near a world (within four of its radii) its gravity pulls on the hull; a hull too weak to hold itself up sinks and comes to rest on the surface. The HUD shows a boresight, the prograde and retrograde markers joined by the velocity vector, and how far the track is off the nose. The field of view widens under boost and cruise.
- **Propellant.** The piloted ship flies on its own tanks: every change of speed or direction is paid for in reaction mass by the rocket equation, and a dry hull only coasts. The status bar shows the tanks, the ship panel the tonnes and delta-v left. At a settlement's world the market's Refuel button fills them at that port's price; anywhere else, CALL A TANKER (shown when the tanks are low) brings one alongside at a call-out fee and three times the going rate.
- **Cruise.** Hold J while flying straight at full throttle and the cruise drive spools the main drive up to many times its thrust, and flight assist up to far beyond normal top speed (with assist off it just keeps accelerating). It drops out the moment you take damage, enter a battle, throttle down or turn hard, and weapons cannot fire while cruising.
- **Weapons.** Flying near hostiles starts or joins a battle. Left mouse fires the forward guns along the nose (missiles go to the locked target); side and aft batteries and point defence keep fighting on their own. T locks the camera onto the hovered or selected ship.
- **Hails.** In a battle, the number keys answer: to a pirate demand for your cargo, 1 hands it over and 2 refuses; to an enemy that strikes its colours, 1 demands its cargo, 2 takes the ship and 3 lets it go.
- **Cargo and trade.** I opens the ship's grid cargo hold (a per-class grid; click a crate, then a cell, to move it). Near a settlement's world, the M market board shows Buy and Sell buttons that trade whole crates at the same prices and port tax the AI traders pay.
- **Spawn and pilot.** V opens a screen of every ship class with a rendered preview; click one to spawn it at the camera and fly it.
- **Jump gates.** Every system's gates sit 5,000 km from its centre, each facing the star it leads to (Sol's Centauri Gate, Alpha Centauri's Sol Gate, and so on). Fly within 300 km of one and press Y to come out at its far gate at once, ship, cargo and credits intact, unless it is shut to you.
- **Model viewer.** F9 (or `-modelviewer <name>` on the command line) shows any ship model on its own for visual checks.

### Interface

- A navigator with a **STAR SYSTEMS** switcher, then the tree of bodies, fleets, docking rings (with their queues) and everything in orbit: click to select, double-click to focus.
- A details panel with live distance, light time, vis-viva orbital speed, period, eccentricity and gravity for bodies. For ships, it shows status, speed, ETA, cargo or patrol orders.
- Selection brackets, hover highlight, an off-screen pointer to the selected target, and the flight path of the selected ship (lanes included).
- Body panels show each settlement's exports, shortages and defences; ship panels show cargo and trade profit, or hull, shields, lost hardpoints, fighters aboard and orders. **M** opens a market board, **B** jumps to the biggest battle and drops to battle time (real time), a ticker reports battles, captures, surrenders, launches, ceasefires, economic shocks and price spikes, and **X** opens the full news feed.
- The market board, cargo hold and controls panel collapse to their title bar with their [-] button.

| Carrier group in formation | Freighter mid-burn | Saturn |
| --- | --- | --- |
| ![SFS Resolute carrier with escorts parked above Earth](docs/images/03-carrier-group.jpg) | ![A Kenney freighter with its engine plume lit, burning toward the Moon](docs/images/05-freighter.jpg) | ![Saturn with ring structure, the planet's shadow on the rings, and Titan](docs/images/06-saturn.jpg) |

## Quick start

Prerequisites:

- Windows and Unity **6000.4.3f1** (via Unity Hub).
- [MindAttic.Capture](https://github.com/mindattic/MindAttic.Capture) cloned next to this repo: `Packages/manifest.json` loads its Unity package from `../MindAttic.Capture/unity/com.mindattic.capture`.
- Optionally the .NET 10 SDK, to run the core tests.

```bash
git clone https://github.com/mindattic/UnitySolarSystem.git
git clone https://github.com/mindattic/MindAttic.Capture.git
```

1. Open the `UnitySolarSystem` folder in Unity Hub. The first import takes a few minutes. The editor opens `Assets/Scenes/SolarSystem.unity` automatically (or use **Solar System > Rebuild Main Scene**).
2. Press Play. You start in free swim beside Earth, with Earth selected, gameplay scale on and the clock at the current date. The controls panel is open; H hides it.

Any scene works, because `Bootstrap` creates the app if the scene does not have one.

## Building and deployment

In the editor, **Solar System > Build Windows Player** builds `Builds/Windows/UnitySolarSystem.exe`. Headless, with the editor closed:

```bash
Unity.exe -batchmode -quit -projectPath . -executeMethod SolarSystem.EditorTools.ProjectSetup.SetupAndBuild
```

`tools/deploy.ps1` does a clean build and publishes it: it stops any running copy, deletes `Builds\Windows` (never `Library\`), builds in batch mode with the Unity version from `ProjectSettings/ProjectVersion.txt`, and copies the player to `C:\Apps\UnitySolarSystem\`. Add `-Launch` to start it afterwards.

```powershell
powershell -ExecutionPolicy Bypass -File tools\deploy.ps1
```

`tools\launch.bat` (or `tools\launch.ps1`) always redeploys first, then offers Normal Play, Demo + Capture (`-demo -capture video screen 5`) or Quit.

## Command line

| Flag | Result |
| --- | --- |
| `-demo` | plays the scripted camera tour once (inner system, ships, Saturn, Earth at both scales, Jupiter, docking rings, Alpha Centauri, real surfaces, battles and three staged combat showcases), then quits |
| `-modelviewer <name>` | opens straight into the model viewer on that ship model from `Resources/Ships` |
| `-capture ...` | records the session through MindAttic.Capture (below); independent of `-demo` |

Screenshots and video come from [MindAttic.Capture](https://github.com/mindattic/MindAttic.Capture) (needs ffmpeg on PATH). Capture is compiled into capture-enabled players only: build one with **MindAttic > Capture > Build Windows Player (Capture Enabled)** (`Builds/Capture/UnitySolarSystem.exe`, or headless with `-executeMethod MindAttic.Capture.Unity.Editor.CaptureBuild.BuildWindows`). Capture is off unless `-capture` is on the command line.

| Command | Result |
| --- | --- |
| `UnitySolarSystem.exe -demo -capture screen` | runs the tour once, saves a PNG at each stop and quits (how the images above were made) |
| `UnitySolarSystem.exe -demo -capture video screen 5` | the tour, filmed, with a PNG every 5 seconds as well as at each stop |
| `UnitySolarSystem.exe -capture video` | films the session from start to exit at native resolution |
| `UnitySolarSystem.exe -capture screen 5` | a PNG every 5 seconds until exit |

Everything goes to `R:\MindAttic\Capture\UnitySolarSystem\`; each tour screenshot's `.json` sidecar names the stop it shows (`"tag": "03-carrier-group"`). In the editor, the **MindAttic > Capture** menu records video or screenshots in Play Mode, and F12 takes a screenshot.

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
| Time warp slower / faster | , and . (or - and =, or numpad - and +) |
| Pause: time eases to a stop, and eases back up when pressed again | Space (or Pause) |
| Reset to now | T |
| Orbits, labels, help, HUD | O, L, H (or F1), U |
| Market board | M |
| Watch the biggest battle (at battle time) | B |
| Hyperlanes on / off | N |
| Satellite constellations on / off | K |
| Level the view to the system's plane (smooth swing: the ecliptic in Sol, the A–B orbit in Alpha Centauri) | R, or the LEVEL button |
| Fly to another star system | Navigator, STAR SYSTEMS (Esc skips the trip) |
| Pilot the selected ship / hand it back | P |
| Spawn and pilot any ship class | V |
| Model viewer | F9 |

While piloting:

| Action | Input |
| --- | --- |
| Steer | Mouse |
| Throttle / strafe / rise and sink | W S / A D / E Q |
| Roll | Z X |
| Flight assist on / off | F |
| Boost | Shift |
| Cruise drive | Hold J |
| Fire forward guns | Hold left mouse |
| Lock a target | T |
| Cargo hold | I |
| Buy and sell crates | M, near a settlement's world |
| Refuel | M, near a settlement's world (Refuel), or CALL A TANKER when the tanks are low |
| Activate a jump gate | Y, within 300 km of it |
| Answer a hail in battle | 1, 2, 3 |

## Project layout

```text
Assets/SolarSystem/
  Core/                 engine-free simulation (compiled by Tests/ as plain .NET)
    Kepler.cs             Kepler's equation, perifocal to ecliptic, vis-viva
    OrbitalElements.cs    JPL-style elements with secular rates
    LunarTheory.cs        the main terms of a lunar theory for the Moon
    BodyCatalog.cs        physical and orbital data for 26 bodies, with sources
    Ephemeris.cs          positions at any Julian date, orbit sampling
    ScaleModel.cs         realistic / gameplay mapping and its inverse
    Fleet.cs, Ships.cs    ship classes, factions, schedules, brachistochrone transits, lane journeys
    Hyperlanes.cs         lane network, ramps, curves, routing
    World.cs              factions, settlements, ships; advances fleet, markets and wars together
    Systems/              Alpha Centauri, the Galaxy of star systems, nearby stars, the jump-gate lattice and the traffic across it
    Infrastructure/       orbital yards, factories, habitats, real spacecraft, constellations, docking rings
    Economy/              goods, recipes, settlements and markets, trade planning, propellant (the rocket equation), the player's cargo hold
    Combat/               hardpoints, battles, fighters, boarding, morale and surrender, wrecks, war, diplomacy, piloted flight
    SimClock.cs           Julian-date clock and time warp
    BodyOrientation.cs    IAU prime meridians and parent-facing moons, so maps face the right way
    GasGiantWeather.cs    zonal wind profiles and the Great Red Spot's position
    AsteroidField.cs      111,267 SBDB asteroids, Kepler propagation (CPU and GPU-mirror)
  Runtime/
    Bootstrap.cs          creates the app in any scene; -demo starts the tour
    SolarSystemApp.cs     frame loop, floating origin, picking, ship placement, piloting
    SceneTour.cs          the scripted camera tour; CaptureRunner.cs runs it for -demo
    ModelViewer.cs        the F9 model viewer
    Camera/SpaceCamera.cs free swim, focus orbit, fly-to, SOI reference frames
    Camera/StarTravel.cs  the camera's trip between star systems
    Views/                bodies, ships (hardpoints, damage, wrecks), fighters, battle effects, hyperlanes, starfield
    Rendering/            procedural meshes and ship hulls, ship models and thumbnails, materials
    UI/                   HUD and procedural icon sprites
    Input/InputState.cs   every key binding in one place
  Resources/              shaders, colour maps, ship models
  Editor/ProjectSetup.cs  scene generation and player build
Tests/
  SolarSystem.Core/       builds Core as a plain .NET library
  Core.Tests/             NUnit tests over Core (dotnet test)
tools/                  deploy.ps1, launch.ps1, launch.bat
docs/                   USER_STORIES.md and the README images
```

## Tests

```bash
cd Tests/Core.Tests
dotnet test
```

725 NUnit tests, run in parallel in under two minutes, cover the following:

- Kepler solver accuracy up to e = 0.97
- Earth's distance and heliocentric longitude at J2000
- Planet periods
- Lunar perigee and apogee, and the Earth-barycentre offset
- Every moon's position against JPL Horizons, and the Moon (lunar theory), Phobos, Charon, Ceres and Earth checked at five dates across 2000-2050, and the belt asteroids Psyche, Lutetia and Ida at two dates in 2027
- Moon spin sense
- Math helpers: angle wrapping, sexagesimal conversion, smoothstep and vector operations
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
- Economy internals: market pricing and its clamps, the production tick and limiting inputs, population growth and decline, trader cargo choice and booking
- Combat: the stronger group winning and hardpoints taking the damage, reactor and shield-generator losses, hardpoints sitting on the hull, shield arcs, coarse steps at high warp, and two years of war with battles, losses and a working economy
- Weapons: railgun and gun rounds' travel time, lead and size-dependent misses
- Salvage: latching only alongside with velocities matched, what a derelict is left with (cargo, stores, survivors, raiders aboard), fighting through raiders before anything is taken, the grid hold filling, stripped parts fitted or scrapped, claiming a flyable hull with a prize crew, a live derelict lying where it was taken, and the adventure on a hulk seen through only by working it
- Wrecks and surrender: lasting wrecks with salvage and expiry, hulls striking their colours, ransom, prize-taking and pirate cargo demands
- War and diplomacy: siege length and port capture, symmetric relations, docking rules
- Economic shocks: a mine accident cutting output until it clears, a worked-out seam cutting a mine for good and a new strike after repeated cuts, a blight spoiling food and raising its price, a strike when wages lag prices (industry stops, life support runs, a raise settles it), rationing at war that lifts with a ceasefire, a besieger's blockade shutting a port to every ship, an embargo on a neutral running war goods to the enemy that lifts with the war, price spikes reported once with their cause, and a year of the live system with shocks on their own and nobody starving
- Pirate havens: the hidden belt havens found by scanner range, by trailing beaten raiders or by a parked warship's sweep, the garrison and raiders blocking an assault, boarding (goods seized) or destroying a haven, its raiders lying low until it is rebuilt, a navy assaulting a haven it has found, and new bands sailing only from standing havens
- Fighters: launch, attack runs, guns and torpedoes, a loose formation that breathes, lead succession, and landing back aboard
- Boarding: fighting through to the bridge and engineering, a small party repelled, vacuum killing crew only until the bulkheads seal, pods bouncing off shields and boarding once they are down, a breacher ramming home, lining up alongside a disabled ship, and a year of lane ambushes and prizes
- Hyperlanes: every system reachable, lanes clear of the Sun, trips taking a day or two instead of weeks, continuity across every leg change, and drawn lanes meeting the burns at the ramps
- Alpha Centauri: the A–B separation (11.2 to 35.4 AU), period, barycentre balance and 2035 periastron; the orbit's 79.2° tilt to the sky; Proxima where the sky puts it and its planets' periods; the Sun in Cassiopeia as seen from there; lanes connecting every port and clearing all three stars; two years of trade, raiding and nobody starving; every system on one clock
- Nearby systems: every planet keeps its catalogued period, axis and eccentricity (periapsis and apoapsis traced, the orbit closing after one period); every star its mass, size and light; the binaries balanced on their barycentres, swinging a(1 ± e) apart, at periastron on the catalogued date and tilted as measured; spot checks against the Exoplanet Archive; candidates labelled; habitable-zone flags (Proxima b in, Barnard's planets out, the Sun's zone 0.98–1.69 AU); Epsilon Eridani's belts and Tau Ceti's disc; each system at its parallax distance; a quiet year in every unsettled system
- Avoidance: head-on ships passing wide, ships sharing a spot spreading out, a month of the whole system with no near-collisions, free ships roaming, a mid-flight course change keeping position and momentum, and merchants turning aside from fresh battles
- Infrastructure: rings staying over their cities (latitude and longitude to a millionth of a degree, velocity = ω × r), including a ring over a world that keeps one face to its parent built before the ephemeris has a date; FIFO queues clearing at most one ship per interval; no position jumps through queue, descent, landing and climb; only merchants landing; yards launching from their slips; constellations at their real altitudes and inclinations; JWST at L2
- Piloting: Newtonian flight (momentum conserved with no thrust, a burn changing it by exactly its impulse, drift and turn-and-burn with flight assist off, assist settling on the commanded velocity without overshoot, rolls, gravity close to a world and a hull too weak for it settling on the surface), cruise spooling up and dropping out, forward guns firing only on the trigger, the piloted hull staying where the pilot loop puts it
- Propellant: every design's drive and tanks, delta-v budgets by Tsiolkovsky (a full hold shrinking them), burns throwing out what the rocket equation says, a flip-and-burn costing its acceleration times its flight time plus the velocity match, transits charged their delta-v at the ship's mass, a short ship waiting in port, running dry (or holed), drifting, calling a tanker and carrying on, a change of course refunding the leg not flown, refuelling buying the cheaper reaction mass and its helium-3 and showing up as market demand, trades left out when the tanks cannot cover them, runs priced by their propellant and lane tolls, lane tolls going to the ramps' keeper, two months of the live system with tanks kept up, hand-flown thrust paid for and a dry hull coasting, and dry ships' distress calls answered with a propellant transfer
- Cargo hold and trade: crate footprints and rotation, no overlaps or out-of-bounds placement, buying and selling at the AI's prices and taxes, docking rules
- Law: jurisdictions (worlds, lane keepers, lawless havens), witnesses in sensor range or none, a recorded crime's notoriety, standing and bounty, lawless space falling to the victim's faction, notoriety fading and bounties lapsing, piracy charged to the raider captain with the bounty on their head, nothing recorded with no survivor or witness, war and self-defence exempt but an attack at peace an assault, ransom as piracy, hunting a wanted ship lawful, the patrol enforcing the law exempt and a captured wanted ship's bounty collected, docking refused and fines paid, standing moving prices, contracts withheld, stolen goods from a haven refused at lawful markets, a patrol's scan confiscating contraband, running from a scan, the order to heave to paid off or ending in an interdiction, trespass, and the player opening fire on a lawful ship
- Adventures: each kind offered only from real sim state (a ship actually in a fight or adrift, a derelict, a wreck with salvage, raiders lurking on a lane past the danger threshold, a merchant on a dangerous lane with time left, a raider captain), within range, never twice at once, capped, rested after it ends; ignored ones failing at expiry or when the situation settles without the player; completed ones paying credits, cargo and standing; and twenty days of the live system where every offer traces to something real
- Jump gates: a gate pair per link at each system's outer edge, activation range, arrival inside the far gate, and the ship moving between fleets
- Gate network: a sparse, connected lattice linking each system to its nearest, gates facing their partners and owned by each system's power, crossing time growing with distance, routes going round a blockade and never through an enemy's gate, the distance markup deciding whether a cargo pays, a freighter selling its goods in the other system's markets, a survey ship crossing several gates and coming home, raiders getting a gate shut behind them and a navy sent after them, a ship turning for another gate when its own shuts, and a year of traffic with every ship somewhere real

## Limitations

- **Planet elements are a fit.** The JPL table is accurate to roughly arcminutes across 1800-2050 (measured against Horizons over 2000-2050: Mercury to Neptune within 0.16°), and drifts outside that window.
- **Moons are mean orbits.** The Moon follows the main terms of a real lunar theory (within 0.01°), and Phobos includes its tidal decay (within 0.07°). Every other moon is an ellipse with steady precession, within 0.5° of JPL Horizons over 2000-2050 except Miranda (1.7°), whose neighbours' pulls a mean orbit cannot follow.
- **Ship drives are fiction.** The kinematics are real, but sustained 0.06 to 1.2 g torch drives are not, and transits ignore solar gravity, which is negligible at those accelerations. Piloted flight is Newtonian (thrust over mass, torque over inertia, momentum carried, gravity close to a world), but the masses and thrusts are set for feel, the assist's set speeds are game numbers, gravity is cut off beyond four radii, and spin about each axis is integrated on its own without gyroscopic coupling.
- **Gameplay scale bends paths.** Ships fly their true path at both scales, but gameplay scale draws it through the same compression as the planets, eased near each end so the ship stays outside the inflated worlds. In a crowded moon system an inflated neighbouring moon can still be drawn over a passing ship.
- **Alpha Centauri is part guesswork.** The A–B orbit and Proxima's place are measured; the planets' orbital planes and phases, their radii and Proxima's orbital plane are assumed, and two of its worlds may not exist. The jump gates sit 5,000 km out so a pilot can reach them, not light-years away; AI ships fly the in-system legs to and from them, but the gates themselves, their crossing times and the lattice are fiction. Only Sol and Alpha Centauri are settled, so interstellar trade and raiding run between those two; the other systems are crossed and surveyed. The trunk lanes across 12,000 AU in days are deep fiction.
- **The game layer is fiction.** Settlements, factions, production rates and prices are invented (on plausible worlds); hyperlanes and torch drives are not physics. Battles are fought at bodies and, by pirate interdiction, beside hyperlanes; patrols hail and interdict only the player's ship (NPC raiders are fought by the war as before), and a crime's witness is whoever was in range, without line of sight or reporting delays; ships on flip-and-burn transits cannot be intercepted. Marines are not drawn yet (the interior model is ready for a shipboard view). There is no campaign or win condition; you observe, or fly one ship at a time.
- **Navies do not burn propellant yet.** Civilian ships fly on real propellant; warships' tanks are carried for when they are piloted, but their fleets do not draw on the markets until they have a supply chain of their own. Nor does a ship on an interstellar voyage while it runs in to a jump gate (a crossing of the whole system that no tank would cover), and the tank masses are an economic scale, apart from the flight model's much heavier hull masses.
- **Piloting is partial.** A piloted ship cannot ride hyperlanes yet and gets no warning of a lane ambush. Only ships can be piloted. Stripping a part takes the soundest (or the one that fits your hull); there is no picking from a list yet.
- **Surfaces are wrapped on spheres.** Phobos, Deimos and the other small irregular moons are drawn round. Relief is shading only (exaggerated about 5×), never displacement, and lunar libration is not modelled.
- **Weather is a model on a 2015 snapshot.** Jupiter's map is Hubble's of January 2015, turned so the Great Red Spot is where it is today; the other storms on it are 2015 weather. The flow map reproduces real band speeds and the spot's rotation, not real storm evolution. The Saturn, Uranus and Neptune maps are stylized (CC BY), so only their motion is data.
- **Asteroids ignore Jupiter.** They fly two-body orbits from the 2026 osculating elements, drifting from their true places by roughly 0.01 AU a year. Only numbered objects brighter than H 15.5 (about 2 to 5 km) are included, and asteroids are labelled but not selectable.

## Roadmap

Planned, from the open tickets in [docs/USER_STORIES.md](docs/USER_STORIES.md):

- A distinct flight model per ship class, tuned one class at a time.
- Piloting anything man-made: satellites, stations and single turrets, not just ships.
- Shield bubbles that ripple where a hit lands.
- The piloted ship riding hyperlanes, with warning of and a response to lane ambushes.
- Hidden pirate bases on real belt asteroids.

## Documentation

- [docs/USER_STORIES.md](docs/USER_STORIES.md): every story and ticket, with the test that proves each one. It is generated from the MindAttic.Workflow database; change tickets with the workflow CLI, not by hand.
- [Assets/SolarSystem/Resources/Textures/SOURCES.md](Assets/SolarSystem/Resources/Textures/SOURCES.md): where every surface map comes from and its caveats.
- [Assets/SolarSystem/Core/Systems/SOURCES.md](Assets/SolarSystem/Core/Systems/SOURCES.md): Alpha Centauri, nearby stars, spacecraft orbits and ring sites, with the assumptions made.
- [AGENTS.md](AGENTS.md): instructions for coding agents working in this repo.

## License

The code has no LICENSE file yet, so all rights are reserved. Third-party assets keep their own licences:

- Planet colour maps: NASA / USGS (public domain). The Saturn, Uranus and Neptune maps are by [Solar System Scope](https://www.solarsystemscope.com/textures/), **CC BY 4.0**. Details in [Assets/SolarSystem/Resources/Textures/SOURCES.md](Assets/SolarSystem/Resources/Textures/SOURCES.md).
- Moon colour and LOLA elevation: [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/) (Ernie Wright; LRO LROC and LOLA teams). Jupiter: Hubble OPAL map via [NASA SVS](https://svs.gsfc.nasa.gov/12021/) (Simon, Wong, Orton). Moon, planet and satellite mosaics and DEMs: USGS Astrogeology, NASA PDS (MOLA MEGDR), and JPL's Solar System Simulator maps (Caltech/JPL/USGS) for Miranda, Titania and Oberon.
- Asteroids: [JPL Small-Body Database](https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html). Great Red Spot position and drift: British Astronomical Association Jupiter Section and JUPOS. Zonal winds: Porco et al. 2003, Sánchez-Lavega et al. 2000, Sromovsky et al.
- Freighter, ore hauler and courier models: [Kenney Space Kit](https://kenney.nl/assets/space-kit), CC0.
- Hull plating: [ambientCG MetalPlates015A](https://ambientcg.com/a/MetalPlates015A), CC0.
- Alpha Centauri, nearby stars, spacecraft orbits and ring sites: see [Core/Systems/SOURCES.md](Assets/SolarSystem/Core/Systems/SOURCES.md).
- Orbital data: JPL Solar System Dynamics, the IAU WGCCRE report, and NASA planetary fact sheets.

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [ExperimentSolarSystem](https://github.com/mindattic/ExperimentSolarSystem), the Babylon.js predecessor, and [MindAttic.Capture](https://github.com/mindattic/MindAttic.Capture), which records it.
