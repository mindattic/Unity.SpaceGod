# Sources: people, labour and money in the economy

What is real, where it comes from, and what is assumed or invented. The figures live in `People.cs` (needs, growth, labour, wages, migration), `Industry.cs` (workers per facility) and `Economy.cs` / `War.cs` (navy upkeep).

The settlements, factions, prices and goods themselves are fiction. Credits are the game's own currency, not dollars, so money figures are kept as shares and ratios taken from the real world, never as dollar amounts.

## What a person needs a day

| Need | Figure | Source |
| --- | --- | --- |
| Food | 1.76 kg | NASA's *Life Support Baseline Values and Assumptions Document* (BVAD) ISS planning value of 2.39 kg of packaged food a day, less 0.43 kg of packaging and 0.20 kg (10%) of packages never opened, as worked through in Ewert and Stromgren, *Astronaut Mass Balance for Long Duration Missions*, ICES-2019-126 (2019). |
| Oxygen | 0.89 kg | Ewert and Stromgren 2019, Table 1: an 82 kg astronaut exercising 90 minutes a day. |
| Water drawn from the loop, station or outpost | 5.03 kg | Ewert and Stromgren 2019, Figure 3: the ISS-like regenerative habitat's water budget (drinking, food, hygiene, flush, electrolysis feed). |
| Water drawn from the loop, colony | 50 kg | The UN and WHO figure of 50 litres a person a day for basic domestic needs (drinking, cooking, washing, laundry). Colonies are big enough to have showers and laundries. |
| Water recovered, station | 98% | NASA, June 2023: the ISS reached 98% total water recovery once the Brine Processor Assembly was added. |
| Water recovered, colony and outpost | 93.5% | The ISS figure before the brine processor (about 93 to 94%, NASA). |
| Water recovered, pirate haven | 85% | **Assumed**: patched, poorly kept kit. |
| Water back from exhaled CO2 | 0.47 kg | Ewert and Stromgren 2019: the Sabatier reactor, limited by the hydrogen left over from electrolysis, at 90% efficiency. |
| Air leaked | 0.038 kg | The ISS design allowance for cabin leakage, under 0.23 kg a day, spread over its crew of six. |
| Nitrogen top-up | 0.029 kg | The nitrogen in that leaked air (75.5% of dry air by mass). Nitrogen is not used up or recycled, only lost, so this is all bought in. |
| Medical supplies | 20 g | Between the clinical share (about 7 g) and the whole (about 47 g) of US hospital waste per head: Practice Greenhealth's 33.8 lb of waste per staffed bed a day, over the AHA's 951,045 staffed beds (about 5.9 Mt a year, Slate 2010), for about 310 million people; roughly 15% of it is clinical. |
| Helium-3, machinery, electronics | 2.5 g, 8 g, 1.5 g | **Assumed**: fusion fuel, spare parts and controls for the habitat itself, shared out per head. |

Oxygen is not traded: it is made by electrolysing water (9 kg of water per 8 kg of oxygen), for breathing and to replace the oxygen in leaked air. The Sabatier reactor gets 0.47 kg of that water back. What a person costs the water market a day is then:

- loss from the loop: drawn × (1 − recovery), plus
- oxygen make-up: (0.89 + 0.038 × 0.231) × 9 / 8 − 0.47 = 0.54 kg.

That comes to about 3.79 kg in a colony, 0.64 kg on a station, 0.87 kg at a mining outpost and 1.30 kg at a haven. Farms and industry use water on top of this.

## Growth

- Fed, watered and doctored, a settlement grows 4% a year. **Assumed**: a frontier boom, a little above the fastest natural increase of any country today (about 3.7% a year, UN *World Population Prospects 2024*).
- Without medicine, it declines by about 5% a year (**assumed**).
- Short of food or water (under 98% of need), it shrinks fast: 0.4% a day for each tenth of need unmet (**assumed**).
- It never falls below a quarter of its founding population (a hard core stays) or grows past four times it (housing). **Assumed**.
- *Wellbeing* is what people have actually had, food and water met, smoothed over about 30 days so that a single short tick does not empty a colony. **Assumed**.

## Labour

| Figure | Value | Source |
| --- | --- | --- |
| Share of everyone in the labour force | 45% | ILO: about 3.6 billion people in the global labour force (2023) out of 8.1 billion. |
| Share of the labour force free for facilities | 50% | ILO: services are about half of world employment. The rest run the habitat's services. |
| Farm crew, per level (420 t of food a day) | 1,440 | One US farm worker feeds about 166 people (American Farm Bureau Federation), at 1.76 kg a day each. |
| Smelter crew, per level (800 t of alloys a day) | 930 | About 6 million direct employees in steel (worldsteel) for 1.89 billion tonnes of crude steel in 2023: about 315 t per worker a year. |
| Bulk mining (ice, nickel-iron, hydrocarbons, nitrogen) | 1 worker per 33 t a day | About 12,000 t per employee a year, the productivity of US coal mining (EIA, *Annual Coal Report*). |
| Metal mining (copper, zinc, titanium) | ten times the bulk labour per tonne | **Assumed**: leaner ore, more handling per tonne of product. |
| Trace mining (platinum-group metals, rare earths, helium-3) | a thousand times the bulk labour per tonne | **Assumed**: helium-3 is parts per billion in regolith, and the other two are scarce. |
| Pharmaceutical labs, electronics fabs, machine works, munitions plants, shipyards (per level) | 600, 2,500, 1,200, 900, 4,000 | **Assumed**. |
| Minimum crew | 25% | **Assumed**: below it a facility cannot run at all. Above it, output is in proportion to the crew. |

Life support (farms and water) is crewed first; every other facility shares what labour is left in proportion. A facility short of hands does not expand however well its product sells.

At these figures the founding settlements have far more hands than their industry needs, as real economies do. Labour starts to bind only when a place loses most of its people, through famine, flight or capture.

## Wages, taxes and the treasury

| Figure | Value | Source |
| --- | --- | --- |
| Labour's share of value added | 52% | ILO: the global labour income share is about 52% of GDP (2024). |
| Tax on wages | 35% | OECD *Taxing Wages*: an average tax wedge on labour of about 35% (2023). |
| Service wage | half the system's average facility wage | **Assumed**. |
| Navy upkeep a year | 7.8% of a hull's price | Operating and support costs are about 70% of a US weapon system's life-cycle cost (GAO). Over a 30-year service life, each year costs 0.7 / 0.3 / 30 of the purchase. |

- **Value added** is a facility's output less its inputs, at local market prices, at the share of capacity it actually ran. Wages are labour's share of it, and the owner takes the tax.
- **Havens pay no tax.**
- The owner's treasury also collects the 4% port tax on trade, and its merchants' profits.
- The treasury pays for:
  - new hulls (the yard will not lay one down for an owner who cannot pay),
  - repairs, rearming and replacement fighters and marines (a navy whose treasury is empty gets none),
  - defence platforms, at 30 million credits each (a broke owner leaves its ports bare),
  - the daily upkeep of every warship and platform in service.
- So a starved economy really weakens its navy. Hungry people die or leave, the facilities lose their crews and stall, wage taxes dry up, and the upkeep keeps draining the treasury until the yards and the platforms stop.

## Migration

People move toward food, water and work, following Harris and Todaro, *Migration, Unemployment and Development* (American Economic Review, 1970): migrants weigh the wage on offer by their chance of getting the job.

- **Expected earnings** = (chance of a facility job) × (facility wage) + (otherwise) × (service wage).
- **Pull** = wellbeing × (0.6 + 0.4 × medicine met) × (expected earnings ÷ the system average)^0.25, with the pay factor held to 0.8 to 1.25. Hunger matters more than pay. **Assumed**.
- Every ten days, the people of each settlement move toward every place they can reach whose pull is clearly higher (by more than 0.05, the cost and bother of moving):
  - 0.2% of the population a day for a full unit of difference,
  - weighted by the destination's share of the reachable population, since bigger places draw more.
  - **Assumed**.
- Nobody crosses a war front, pirates move only among their own havens, and nobody leaves a place below its floor or arrives at one at its cap.
- Moving is instant for now. Passenger and colonist transport is a later ticket (U.SPC-T101).
