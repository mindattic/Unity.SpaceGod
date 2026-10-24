# Interactive boarding

> Ticket U.SPC-T468. Owner direction, 2026-10-23: "let the player take part in a boarding fight instead of only
> watching it resolve; whether that is turn-based tactics or a first-person shooter is left open and needs a design
> pass first. Today's weighted-roll fight, compartment by compartment with defending marines mustering against the
> boarders, stays as the resolver behind it and as the auto-resolve choice."

This page is that design pass, and it describes the first layer that has been built from it.

## What there is to work with

The fight aboard is `Interior` (`Assets/UnitySpace/Core/Combat/Boarding.cs`). A hull is a graph of compartments built
from its facilities: bow, holds, quarters, midships, the bridge, engineering, the marines' barracks. Each compartment
holds crew, defending marines and boarders. `Interior.Step` runs the fight in battle seconds and does three things:

1. **Exchange.** In every contested compartment, casualties trade on numbers, marine quality and drill, a defender's
   advantage, and the fortification of the bridge and engineering. This is the weighted roll.
2. **Muster.** Once roused, a delay after the first landing, defending marines who are not engaged move up toward the
   fighting, and toward the bridge or engineering first if they are under attack.
3. **Advance.** Boarders who hold a compartment push on by the shortest way toward the bridge and engineering, leaving
   a rear guard behind.

The ship is taken when the boarders hold both the bridge and engineering. It is held when no boarders are left.

Exchange is resolution. Muster and Advance are decisions, and today the resolver makes them.

## The options

| | Turn-based tactics | First-person shooter |
| --- | --- | --- |
| What it needs | The compartment graph that already exists, plus a panel. | Walkable deck geometry for every hull class, marine avatars and animation, close-quarters weapons and AI, and a separate scene or a streamed interior inside the battle space. |
| Fit with the battle | The battle keeps running, and the fight aboard keeps its own clock. | The battle outside has to pause, run unseen, or be simulated around a player who is walking a corridor. |
| Fit with the resolver | The resolver keeps every exchange. The player makes the decisions it used to make. | The resolver is replaced for the duration, and it is hard to hand back halfway through a fight. |
| Auto-resolve | Free: with no orders, it is today's fight exactly. | Needs a separate path. |
| Scale | The same for a freighter's four spaces and a battleship's dozen. | Content cost grows with every hull class. |

## Decision

**Compartment-level tactics come first.** The player gives standing orders over the compartment graph to the side they
command. The fight keeps running in battle time, and the time warp and pause act on it as they act on the rest of the
battle, so a paused game gives turn-by-turn play. The resolver is untouched:

* Orders change only **where people move**, which is Muster and Advance. Every exchange in a compartment is still the
  weighted roll.
* **Auto** is the resolver's own choice. It is what every side gets by default and what an order hands back to.
  Running with no orders gives a fight identical to the one before this ticket, and a test checks this
  (`BoardingCommandTests.HandedBackToAutoTheFightIsTheResolversExactly`).
* Orders stand only while a player commands that side: the hull's own helm for its defenders, or a hull of the boarding
  faction for its boarders. Once the player leaves, that side goes back to auto.

**A first-person shooter is deferred, but planned for.** When hull interiors have walkable decks, a marine squad
becomes one more `IEmbodiable` seat (see [EMBODIMENT.md](EMBODIMENT.md)) inside a compartment. Exchanges in that
compartment then come from the shooter, every other compartment stays with the resolver, and handing the seat back
returns the compartment to the roll. Nothing in the tactics layer has to change for this.

## What is built (layer one)

* **Orders** (`Core/Combat/BoardingCommand.cs`, `Interior.Command`). `BoardingOrder` is `Auto`, `Push` or `Hold`, and
  is given to a `BoardingRole`, either `Boarders` or `Defenders`.
  * **Boarders, Push.** Go for the chosen compartment instead of the bridge and engineering, for example the barracks to
    catch the marines before they muster. Once they have taken it, they press on as the resolver would.
  * **Boarders, Hold.** Dig in on what they hold, for example to wait for the next pod wave.
  * **Defenders, Push.** Muster on the chosen compartment. This can be to hold the bridge before the boarders reach it,
    or to retake a compartment the boarders have taken.
  * **Defenders, Hold.** Every marine stays at their post.
* **Which fight** (`BoardingCommand.FightFor`). While piloting, the player commands their own hull's defenders if it is
  being boarded. Otherwise they command the boarders of their faction, on the hull they are latched to first, or else on
  any hull in the battle.
* **Controls.** B, while piloting, opens the boarding panel. It lists every compartment with who holds it, the fighting and any breach,
  together with the standing order and the latest line from the fight's log. While the panel is open, the number keys
  go to the boarding: 1 picks the next compartment, 2 pushes for it (boarders) or musters on it (defenders), 3 holds,
  and 0 hands the side back to auto-resolve. A hail still takes the number keys first, and the captain's helm orders
  wait while the panel is open.

## Later layers

* Defender tools that only a commanded side has: venting a compartment the boarders hold (and losing any crew still
  in it), or sealing a bulkhead to cut a link for a time.
* Splitting a party: sending part of the boarders one way and the rest another.
* Commanding from the strategy view, not only while piloting, through the seat picker.
* The shooter layer described above, once hulls have decks.
