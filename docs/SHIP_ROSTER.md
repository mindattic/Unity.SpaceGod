# Ship roster and doctrine

The reference every ship ticket builds to: how ships travel, every class with its role, the weapons by tier, the realism rules the game keeps and the two doctrines that decide how fleets are used. It is the owner's roster (2026-10-23), written down once so a new hull or weapon is checked against it instead of against memory. The wider aims are in [docs/VISION.md](VISION.md), and the rules for building on the game are the [README](../README.md#design-principles).

Where the game already differs from this canon, the last section says so and names the ticket that closes the gap.

## Three travel layers

| Layer | Scale | What it is | What it means |
| --- | --- | --- | --- |
| Jump gates | Interstellar | Fixed and expensive | Whoever holds a gate controls all traffic through it, so gates are the main strategic chokepoints |
| Self-contained FTL | Interstellar | Rare and costly, carried by the ship | Only these ships can strike a system without taking a gate |
| Hyper lanes | In a system | Magnetic linear accelerators that launch ships between planets, stations and gates | Ships ride the lane, so they need neither huge engines nor big fuel loads; but routes are predictable, so ambushes and lane sabotage are real threats |

## Classes

### Capital ships

- **Carrier.** FTL-capable. A flight deck and hangars launch and recover piloted fighter and bomber squadrons, and a few corvettes or frigates can be clamped to the outside of the hull. Losing the carrier strands its pilots.
- **Dreadnought / Battleship.** The spinal-mount heavy hitter: a railgun or coilgun, a tachyon lance or a plasma cannon, on a large power plant. It holds the rear.
- **Command Cruiser / Battlecruiser.** Fleet coordination, and the sensor and comms hub. It handles light-lag. In the game (U.SPC-T477, `Core/Combat/FleetCoordination.cs`) it gives up guns for a sensor array, a communications suite and targeting computers inside its hull and carries the fleet's senior officers: ships within its reach in battle find their targets sooner, shoot truer, lay their guns on its shared sensor picture and concentrate on its mark; losing it throws them into confusion for a while, so it is a priority target, and navies build it to lead their groups.
- **Gate Breaker.** A siege capital built to take or destroy jump gates: heavily armoured, with siege weapons, and with boarding and engineering teams to seize a gate intact.

### Strike craft

All piloted. Inertial dampeners are what make a dogfight survivable (see the realism rules below).

- **Interceptor.** Fast and agile with light guns. It hunts fighters and screens bombers.
- **Space Superiority Fighter.** The balanced workhorse.
- **Bomber.** Slow and tough, with torpedoes for anti-capital strikes. It needs an escort.
- **Heavy Fighter / Gunship.** Armoured and multi-crew, with turrets. It brawls and hunts corvettes.
- **Recon / EW Fighter.** Scouts, jams enemy targeting and paints targets for the bombers.
- **Boarding Shuttle / Assault Craft.** Delivers marines to disabled ships and to gates.
- **Breacher.** A whole marine team in an armoured round, fired at high velocity from an Assault Bay on a hull that carries a Marine Habitat (U.SPC-T465, U.SPC-T466).

### Escorts and sub-capitals

- **Cruiser.** Long-endurance patrol. It rides gates and lanes and needs no support fleet; sometimes it carries a small fighter complement.
- **Destroyer.** The anti-fighter and point-defence screen: flak, laser turrets, interceptor missiles and phaser-style beam arrays.
- **Frigate.** A torpedo and missile boat. Its doctrine is the alpha strike: burn in, fire, flip and burn out. Antimatter and photon torpedoes.
- **Corvette.** In-system defence with no FTL. It operates on its home system's hyper lanes.
- **Lane Interdictor.** Pulls ships out of hyper lanes mid-transit. The pirates' and raiders' favourite.
- **Gate Picket.** Small, sensor-heavy early warning at gate exits. In the game (U.SPC-T480, `GatePickets`): every navy posts one at each gate it holds, which warns it of hostiles through the gate or dropping out of FTL nearby, and raiders kill it first. Not the destroyer patrol wing, which flies a beat of its navy's ports.

### Logistics

- **Bulk Freighter.** Truss and pods, built in space, never lands. No armour and no weapons. It rides hyper lanes cheaply.
- **Ice Hauler / Ore Tug.** An engine with claws that pushes asteroids and ice. Large loads may need heavy lanes.
- **Lane Tender.** Unarmed maintenance that keeps lane rings powered and aligned. A top target in war.

## Who has their own FTL

| Class | Own FTL | How it crosses between systems |
| --- | --- | --- |
| Carrier | Yes | Self-jump, with its wings and docked escorts |
| Dreadnought / Battleship | Some | Self-jump if fitted with an FTL drive, otherwise gates |
| Command Cruiser / Battlecruiser | Some | Self-jump if fitted, otherwise gates |
| Gate Breaker | Yes | It needs it |
| Cruiser | No | Gates |
| Destroyer / Frigate | No | Gates, or docked on a carrier's hull |
| Corvette | Never | Never leaves its home system |
| Fighters / Bombers | No | Inside a carrier's hangar |

Battleships are as big as carriers or bigger, so they never land on one.

## Weapons by tier

| Weapon | Range | Character | Carried by |
| --- | --- | --- | --- |
| Railguns / Coilguns | Extreme | Kinetic sniping | Dreadnoughts, cruisers |
| Missiles / Torpedoes | Long | Volley strikes | Frigates, carriers |
| Tachyon Lances | Long | Near-instant; ignore light-speed delay. Power-hungry, and kept rare and expensive so kinetics and missiles stay relevant | Capitals only |
| Lasers | Medium | Point defence and radiator sniping | Everything |
| Phaser-style Beams | Medium | Versatile, tunable energy | Destroyers, cruisers |
| Plasma Cannons | Short | Devastating, bleeds off over distance | Brawlers, gate breakers |
| Antimatter / Photon Torpedoes | Medium | Heavy guided warheads | Frigates, capitals |

Phaser arrays are tuned by beam shape, by the captain in battle, every array on the hull at once: a narrow lance that burns hulls at medium range, or a wide sweep that works like point defence against fighters and missiles close in. Plasma cannons lose damage evenly with the distance the bolt crosses (U.SPC-T488, `Core/Combat/EnergyWeapons.cs`).

## Realism rules, softened

The game keeps the feel of real space and drops what would get in the way of play.

- **Inertial dampeners** are why piloted dogfights work: pilots survive the manoeuvres the fight demands.
- **Bridges** are buried at the centre of the hull with no viewports. The crew sees out through sensors and holo/VR displays.
- **Radiators** are a weak point on big ships, but not a constant worry.
- **Everything fights in full 3D**: fighters, capitals and stations alike, with no battle plane.
- **Shields** exist, so bombers knock the shields down first and then torpedo.

## Construction

Hardpoints are places on the hull's surface. Facilities are things inside the body of the ship. Each hull has limited internal space for facilities (U.SPC-T470).

- **Hardpoints** (`Hardpoint`, `OutfitSlot`, `SlotKind` in `Core/Combat/Construction.cs` and `Outfitting.cs`) are gun and turret mounts, launchers, torpedo tubes, shield emitters and engine mounts. Each is one entry of the design's layout, with a position on the hull and a firing arc, and is never empty.
- **Facilities** (`FacilityKind`, `FacilitySite` in `Core/Combat/Facilities.cs`) have no arc, only a size: the share of internal space they take, in sections. A hull's internal space is its design's own facilities plus its free space (`FacilityLayouts.SpareSpace`: one to six sections by class, as many as it had internal bays before). A design is a trade-off: whatever goes in uses room something else could have had, and a hull with less free space than a facility's size cannot fit it at all.
- **The registry.** Every facility kind is data registered with `FacilityKinds.Register`: its size, mass and bill of materials (scaled with the hull or fixed), the hull categories that may fit it (null for any), whether a yard fits and strips it, the boarding compartment it is (`CompartmentKind`, or none), the module it stands for if any, and an optional `Effect` on the hull's fighting spec, applied by `Loadout.Build` with +1 for each one fitted and -1 for each of the design's own taken out. A new facility (a Marine Habitat, an Assault Bay, an FTL drive room, radiators, a buried bridge, a sensor suite, pods on a truss) is a new registered kind, plus any system that reads `Loadout.CountFacility`. Fitting code, prices, the yard, the outfitting screen and the boarding interior need no change.
- **What is registered today.** Structure that comes with the frame and is not fitted: bridge, engineering, crew quarters, hangar deck, hold, battery magazine, bow section, midships, assault bay. Fittable, each one section on any hull: armour plating, cargo racks, scanner suite, brig. These four were the internal-bay modules before, and they keep the same effects, mass, bill and price. Fittable on hulls big enough for one (freighters, warships, battleships, carriers, stations and service hulls, never fighters, small craft or satellites): the marine habitat (below). Fittable on capitals only (U.SPC-T477): the sensor array (two sections), the communications suite and targeting computers (one each), which the command cruiser is designed with and which make any hull that carries a working suite a command ship.
- **Marine habitats** (`MarineHabitats`, `Core/Combat/MarineHabitats.cs`, U.SPC-T465). Marines live and train in a habitat, one section of internal space whose `Berths` (a facility kind's, fifty a unit of hull scale, or a layout site's own) are the hull's marine complement: `CombatSpec.Marines` is its facilities' berths (`FacilityLayouts.Complement`), not a flat figure per class, and `Loadout.Build` adds a fitted habitat's berths and takes away a stripped one's. Every stock design is drawn with the habitats that hold the marines it always had (carrier 320, battleship 260, cruiser 140, destroyer 50, a freighter's security post 4, a defence platform's garrison 60, an orbital station's 180); a breacher's team of 70 rides in its assault bay, which is no habitat. The habitat is the barracks compartment (`CompartmentKind.Barracks`) the defenders muster from, and the crew work the rest of the ship. Each day out of battle the marines in it drill (`Interior.Drill`, `Interior.Training`, 0 to 1), fully trained in 180 days and 30% better man for man (`Interior.DrillFactor`), repelling boarders and boarding from the hull alike; recruits who make up losses start raw and dilute it. Both are in Settings > Broadsides. What a habitat has ready (`MarineHabitats.Ready`, `TeamsReady`, `Draw`) is what the Assault Bay's teams are drawn from (U.SPC-T466).
- **Layouts.** Each class's facilities (`FacilityLayouts.For`), and a platform's for stations and satellites, list its spaces from bow to stern with their names and places, and say where a branch such as the bridge leaves the keel. `Interior.For` builds the boarding compartments from them, so a hull's interior is its facilities. A facility fitted later that is a space of its own opens off midships, and the interior is rebuilt with its people carried over. A `HullFrame` may bring its own layout.
- **Who reads it.** The yard (`Outfitting.FitFacility`, `StripFacility`, `QuoteFacility`), the construction plan (`ConstructionPlan.FitFacility`, its bill and price), the outfitting screen (F2: the hardpoints, then the facilities, then the free space), the AI's designs (`Outfitting.NavyFacilities` and `RaiderFacilities`, lists of kind ids filled as far as the free space goes) and the boarding interior.

## Two doctrines

- **Gate fleet.** Large, but predictable, because it must come through a chokepoint.
- **Jump strike group.** A carrier, FTL-capable capitals and docked escorts. Smaller, but it can show up anywhere.

## Capital ships fight in full 3D

Settled by the owner (2026-10-23): capital ships fight in full 3D, like the fighters, never on a battle plane. U.SPC-T499 builds the capital flight model that way (it replaces U.SPC-T442, which assumed a plane).

## Where the game is today

| Canon | In the game now | Closed by |
| --- | --- | --- |
| Cruisers and destroyers have no FTL; carriers and gate breakers always do, battleships and command cruisers only when fitted | In: the roster is data (`FtlRoster` in `Core/Systems/CruiseDrive.cs`); carriers and explorers always have a drive, a battleship or command cruiser only while its design fits the FTL drive facility (two sections; each stock design has one, a yard can strip it), cruisers, destroyers and the lane interdictor have none and no berths and cross by the gates; gate breakers and corvettes (which never leave home) slot in as roster entries when they are classes | U.SPC-T472 |
| Carriers clamp a few escorts to the hull; a lost carrier strands its wings | Not modelled | U.SPC-T473 |
| Gate fleets and jump strike groups, used differently | Navies do not yet raise the two kinds of force | U.SPC-T474 |
| The Breacher is a fired marine round | A flown assault craft that rams its drill prow home | U.SPC-T466 |
| Marines live in a Marine Habitat | In: a hull's marines are what its habitats quarter, they muster from its barracks and train in it; the AI's designs do not yet fit extra habitats | U.SPC-T465 |
| Boarding the player takes part in | Boarding only resolves on its own | U.SPC-T468 |
| Frigate, corvette, gate breaker | Not yet classes (the command cruiser landed in U.SPC-T477) | U.SPC-T475, U.SPC-T476, U.SPC-T478 |
| The lane interdictor, a sub-capital, has no FTL of its own | In: no drive of its own; a raider band (a cruiser or destroyer and its escorts) crosses between the stars by the gates, its interdictor with it, and only hulls with a drive go out to lie in wait on FTL routes; the projector as equipment is U.SPC-T507 | U.SPC-T472 |
| Truss-and-pod freighters; ice hauler / ore tug | Not yet | U.SPC-T482, U.SPC-T483 |
| Recon / EW fighter; boarding shuttle | Not yet classes (the interceptor, space superiority fighter and bomber are hulls of their own since U.SPC-T484) | U.SPC-T485, U.SPC-T486 |
| Antimatter / photon torpedoes, flak, interceptor missiles | In: battleships and carriers carry an antimatter torpedo launcher (a frigate takes one in a tube when it is a class); destroyers carry two rotary flak cannons and an interceptor launcher beside their point defence and keep between their capital and the threat; tunables in Settings > Weapons | U.SPC-T489 |
| Dreadnought spinal mounts and rear-line doctrine | Not yet | U.SPC-T490 |
| Radiators and heat on big ships | Not yet | U.SPC-T491 |
| Bridges buried at the centre of warships | Not yet | U.SPC-T492 |
