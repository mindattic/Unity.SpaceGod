# SpaceGod: the vision

SpaceGod is a fully alive, galaxy-spanning space simulation. It is five games at once, all running in the same universe at the same time:

- a **strategy game**, where polities grow, trade, ally, go to war and fall;
- a **dogfighting game**, in the cockpit of a fighter;
- a **capital-ship broadside game**, at the helm of a battleship trading fire with another;
- a **trading game**, buying low in one port and selling high in another;
- a **diplomacy game**, between the powers that hold the stars.

None of these is a mode you pick from a menu. They are views onto one simulation, and the player moves between them by choosing where to stand.

## Why "SpaceGod"

You can change how things turn out by taking charge yourself: drop into a fighter and fly the heroic run down a dreadnought's trench that brings it down, take the bridge of a flagship in the decisive battle, broker the treaty that stops a war, or corner a market. Or you can sit back and let the whole thing play out on its own. The galaxy does not wait for you and does not need you; every war, trade route and alliance runs whether or not anyone is watching. The god in the name is the choice between those two: step in anywhere, or watch.

That means two rules for every system in the game:

1. **It runs without the player.** Navies fight, merchants trade, polities rise and collapse on their own. A feature that only happens when the player is present is not finished.
2. **The player can take it over.** Anything the simulation does, a player should be able to do in person, at the controls of the ship or the seat of the power doing it, and the outcome should depend on how well they do it.

## No end date

This is a universe, not a product. There is no ship date, no feature freeze and no final version. Work is judged by whether it makes the universe more alive, more coherent and more reachable, not by whether it fits a schedule. That is why the design principles in the [README](../README.md#design-principles) put extensibility first: the project will keep growing for as long as it is worked on, so every system has to leave room for the next one.

## The galaxy

- **Alive.** Civilisations are simulated, not scripted: they found colonies, grow, specialise, form and break alliances, fight, and die out. (Civilisation dynamics: U.SPC-T396 to U.SPC-T409.)
- **Continuous and flyable.** Space between stars is real space. A pilot can fly from one end of the galaxy to the other, given long enough. (Flight and galaxy: U.SPC-T412 to U.SPC-T415.)
- **Travel like I-War 2.** Between stars you point at a place, engage the FTL drive and, eventually, arrive. Most traffic uses jump gates, which are fast shortcuts; capital ships and deep-space explorers can cross between stars under their own power. (FTL: U.SPC-T417 to U.SPC-T421.)

## The five games

| Game | What the player does | What happens without the player | Ticket |
| --- | --- | --- | --- |
| Take charge | Step into any ship or seat of power, at any moment, and step out again | The simulation keeps running that ship or power | U.SPC-T422 |
| Strategy | Direct a polity's colonies, fleets, industry and wars | Polities expand, build and fight on their own | U.SPC-T423 |
| Dogfighting | Fly a fighter in a furball, or make the run on a capital ship | Squadrons launch and fight under their carriers | U.SPC-T424 |
| Broadside | Command a capital ship's batteries, shields and manoeuvre in a fleet action | Fleets engage by doctrine | U.SPC-T425 |
| Trading | Buy, haul and sell cargo between ports; run contracts | Merchants trade on supply and demand | U.SPC-T426 |
| Diplomacy | Negotiate treaties, alliances, ceasefires and trade deals | Polities negotiate with each other | U.SPC-T427 |

## Looks

The game runs on URP today. Groundwork is laid for both pipelines: URP stays the fast, broad target, and HDRP is the route to photoreal. Shaders, materials and rendering code are written so either can be swapped in. (Render pipelines: U.SPC-T428 to U.SPC-T430.)

## Settings: the player can reach it

As much as possible is reachable and adjustable in game, from the settings menu: graphics and render quality, controls, simulation speed and scale, difficulty, and the tuning of the simulation itself. A value that only a developer can change by editing code is a missed setting. New settings register themselves in one place, so the menu picks them up without being edited by hand. (Settings registry: U.SPC-T431.) This is design principle 5 in the [README](../README.md#design-principles).
