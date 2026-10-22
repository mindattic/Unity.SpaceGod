# Civilization dynamics spike (U.SPC-T397)

How real civilizations rise and fall, and how that maps onto the SpaceGod simulation. Feeds the umbrella U.SPC-T396 (empire decline and isolation). The per-mechanic tickets U.SPC-T398 to T409 already exist and each requires this spike; this document is the model they build.

Held to README.md "Design principles": mechanics plug in through an extension point and data (principle 1), nothing assumes space between systems is empty or that a gate is the only way out (principle 2), and every rate is a declared setting (U.SPC-T431).

## 1. Findings from history

The cases differ, but the same few drivers recur, nearly always in combination. Collapse is rarely one cause: a shock lands on a system already strained, and the strain was usually self-inflicted (overextension, debasement, a hollowed-out army).

| Case | What happened | Drivers (in rough order of weight) | Timescale |
| --- | --- | --- | --- |
| Rome (West) | Third-century crisis (235-284) then the 5th-century loss of the provinces | Overextension of frontier garrisons; debasement of the denarius (silver content fell from ~90% to ~5%); succession crises (about 26 emperors in 50 years); Antonine and Cyprian plagues; tax-base loss as provinces seceded (Gallic and Palmyrene empires) | Decline over 200 years; fall of the West 476 |
| Late Bronze Age collapse | Palaces and trade networks failed across the eastern Mediterranean around 1200 BCE | Interlocking dependence (tin from far away, grain from Egypt and Anatolia); drought and famine; earthquakes; raiders (the "Sea Peoples") breaking sea lanes; a failed node cascading to its partners | Two to three generations; a "dark age" of 300 years, then recovery with new states |
| Egypt, New Kingdom to Third Intermediate Period | Ramesses II's empire (the "Ozymandias" ruin) fragmented | Cost of wars and temple endowments draining the treasury; unpaid workers striking (Deir el-Medina, c. 1155 BCE); loss of Levantine holdings; priesthood of Amun as a rival power; Libyan settlement; drought | About 150 years |
| Mongols and khanates | Empire split after Mongke (1259) into Yuan, Ilkhanate, Golden Horde and Chagatai | Succession disputes (kurultai); distance: orders took months to cross; each khanate adopted local religion (Islam in three of them), which severed the ideological bond; Black Death and trade disruption in the 1340s; fiscal failure of paper money in Yuan and Ilkhanate | Split within 30 years; collapse 1330-1370 |
| Byzantium | Shrank from the Mediterranean to one city and was taken in 1453 | Loss of the grain provinces (Egypt, 7th C) and tax base (Anatolia, 1071); Fourth Crusade sack (1204) as a deliberate decapitation; debased coinage (the solidus lost its standing); mercenary dependence; plague of Justinian; civil wars between claimants | 800 years of slow shrinking, with real recoveries (Macedonian dynasty) |
| Spanish and British empires | Overseas empires lost their colonies | Distance and slow communication gave colonial elites autonomy; fiscal strain from wars and debt (Spanish bankruptcies, British post-1763 taxation); the metropole distracted or occupied (Napoleon in Spain, 1808); local identity hardened; gold inflation and the cost of defending sea lanes | Spanish Americas lost 1808-1826 (about 20 years once triggered); British 1776 and 1945-1970 |
| Soviet breakup | USSR dissolved in 1991 | Imperial overreach (Afghanistan, arms race); economic stagnation and shortages; ideological collapse (glasnost let the belief dissolve before the state); nationalism in the peripheral republics; a failed coup showing the centre could not enforce; an oil-price shock | Six years from reform to breakup; the end itself took months |
| Great Schism and Reformation | Christendom split in 1054 and again from 1517 | A shared belief fractures along distance and political lines (East/West; German princes); a new doctrine gives distant rulers a legitimate reason to defy the centre and keep tax revenue; printing as a fast information channel; followed by holy war (Thirty Years' War killed an estimated 20% of Germany) | Decades to split; a century of war |
| Black Death | 1347-1351, about a third of Europe dead | Spread along trade routes, ports first (Caffa to Messina to everywhere in about 4 years); isolation invented (Ragusa 1377, 30 days, later extended to 40); labour scarcity raised wages and loosened feudalism (a long-run benefit); persecution of scapegoats; recurrent waves for 300 years with fading mortality as immunity built | Peak in 4 years; population recovery took about 150-200 years |

### Cross-cutting lessons

1. **Collapse is a cascade, not an event.** A shock (plague, drought, a lost fleet) only topples a polity that already has low reserves: low treasury, thin garrison, long supply lines. Model reserves; do not model shocks as direct kill switches.
2. **Distance and time out of contact are the master variable.** Every case with a remote province (the Mongol khanates, the Spanish Americas, Rome's Britain) ends in drift. Slow information means local officials act alone, then act for themselves.
3. **Networks fail at hubs.** The Bronze Age and Byzantium both show dependence on a few trade nodes; losing one starves its partners.
4. **Belief is the glue, and it can be cut separately from the army.** The Soviet Union and the Mongol khanates lost their cohesion by ideology before force.
5. **Money debasement and unpaid soldiers are the proximate cause of military failure.** Armies that are not paid desert, mutiny or become the next claimant.
6. **Recovery is real but slow and uneven.** After the Bronze Age collapse and the plague, survivors were often better off per head, and successor states rose from the ruins. Decline is not monotone; Byzantium recovered several times.
7. **Timescale mismatch with a game.** Real collapses run decades to centuries. SpaceGod's sim clock runs up to a year per second, so a real-time collapse over 20 sim years is about 20 seconds at top speed or a long session at 1x. Rates should be expressed in sim days and tuned with the harness (U.SPC-T439), and mechanics must also be visible at lower speed (U.SPC-T409).

## 2. What the simulation already has

| Core piece | Relevant state and behaviour | Gap |
| --- | --- | --- |
| `Economy/People.cs` | `GrowthRate(sustenance, health)`, `FaminePerDay` (0.004) shrinks a short-fed settlement, `MinShare` 0.25 hard-core floor, `Settlement.Wellbeing` and `HealthWellbeing` smoothed over 30 days | A famine shrinks a population but never changes ownership, order, or the polity; there is no import-dependence signal, and the floor prevents extinction |
| `Economy/Settlement.cs` | `Population`, `Stock`, `Inbound`, `Satisfaction`, `Garrison`, `Unrest`, `OccupiedFrom`, `Owner`, `Fallen`, `DaysSinceCapture` | `Unrest` exists but is only set by occupation (`InterstellarWars`) and is read only by `Economy.cs` (halves disruption). Nothing raises it from hunger or neglect, and nothing acts on it except a recapture roll |
| `Systems/Polities.cs` | One shared `Polities` registry: `Factions`, `Worlds`, `HolderOf`, `SystemsHeldBy`, `CapitalOf`, `Navy`, `Diplomacy` | No runtime founding (`Found` is internal and used at world build), no "polity dies" path, no polity-level state beyond `Treasury`, `CrewMorale`, `Manpower` |
| `Ships.cs` `Faction` | `Treasury`, `Capital`, `Manpower`, `CrewMorale`, `Kind` | No loyalty, legitimacy, belief; colours and name fixed at construction (fine for a new faction, which is a new object) |
| `Systems/GateNetwork.cs`, `JumpGate.cs` | `GateLink.Working`, `Open(from, faction, jd)` (both rings whole, both ends allow it), `Forcible`; Void Bases relay long gaps | Gates can fail (integrity) but nothing computes "this system is cut off", for how long, or what that does |
| `Systems/GateConstruction.cs` | Survey/build/rebuild of gates by constructor ships, materials, build days, `GateJob`, `GateStage` | Rebuilds are triggered by normal expansion logic, not by a severed system's need; no "can this isolated system afford to rebuild" check |
| `Systems/InterstellarTraffic.cs`, `Economy/Contracts.cs` | Voyages and gate crossings carry people and goods; contracts move supplies; `Contracts.Update` | No notion of a system depending on imports, and traffic is not a disease vector |
| `Systems/MilitarySupply.cs` | `FleetSupply`, `SupplyConvoy`, `MilitaryLogistics`: fleet supply lines | Military only; civilian supply is not tracked |
| `Systems/InterstellarWars.cs` | Campaigns, landings, occupation, `OccupationUnrest = 0.8`, revolt/recapture roll at `Unrest > 0.5` | The only existing rebellion, tied to the occupier/previous owner; no secession to a new faction |
| `Economy/MarketShocks.cs` | `ShockKind`: MineAccident, Depletion, Blight, Strike, Rationing, Blockade, Embargo | The existing shock framework is the nearest thing to an extension point; resource shocks (T407) belong here |
| `Law/Law.cs`, `Law/Outlaws.cs`, `Systems/PirateClans.cs` | Crimes, patrol hails, outlaws, bounty hunters; pirate clans keep havens, hostile to all | No path from "unpaid or defected fleet" to outlaw/pirate; no raider power rising from a collapsed polity |
| `Combat/Morale.cs`, `Combat/Crews.cs` | Crew morale, reserves of spacers, surrender terms | Morale is not tied to pay or polity legitimacy |
| `Information/LightSpeed.cs`, `News.cs`, `GalaxyMap.cs` | Light-speed delays within a system, news, map | No interstellar information delay; the news feed is the place to surface events |
| `Events/EventDirector.cs` | Adventures from real sim state | Can later offer relief runs and escort missions for starving systems |
| `Persistence/SaveGame.cs`, `ObjectGraph.cs` | Saved state | New state must be added here |

World update (`World.cs`, around lines 150-165) calls `Fleet`, `Companies`, `War`, `Sensors`, `Shocks`, `Law`, `Outlaws`, `Contracts`, `Escorts`, `Holdings` in order. A new subsystem that is edited into that list one by one is exactly what principle 1 forbids; the framework below adds one hook once.

## 3. The proposed model

### 3.1 State variables

Four layers, each a scalar in a defined range unless noted. Every variable is stored in the registry (section 4), not as new fields on `Settlement` or `Faction`.

**Per system (keyed by the star system or its `World`):**

| Variable | Range | Meaning | Rises with | Falls with |
| --- | --- | --- | --- | --- |
| `Supply` | 0-1 | Share of essential needs (food, water, medicine, upkeep goods) met, smoothed over about 30 days. Derived from existing `Settlement.Satisfaction` and `Wellbeing`; the dynamic adds the *import dependence* | Local production, inbound cargo, stockpile | Blockade, lane loss, hub failure |
| `ImportDependence` | 0-1 | Share of its essentials it must import (derived from `Demand` against local `Supply` at the settlements) | n/a (a structural measure) | Local farms and water built |
| `Reserve` | days | Stockpile cover: days of essentials at current population | Stockpiling, local surplus | Consumption exceeding input |
| `Contact` | 0-1 (and `DaysSinceContact`) | Whether a working, open route exists to the capital (graph reachability over `GateLink.Open`, plus any ship flying through) | Open route, traffic | Gates broken, embargo, quarantine |
| `Loyalty` | 0-1 | Allegiance to the holding polity | Contact, protection (garrison, navy presence), investment, prosperity, shared belief | Distance, `DaysSinceContact`, hunger, taxation above tolerance, unpaid garrison, ideological difference, recent conquest |
| `Order` | 0-1 | Internal stability: a read-only view of `1 - Settlement.Unrest`. Dynamics change it only by writing `Unrest` through its normal member, so there is one source of truth | Garrison, supply, loyalty | Hunger, plague, low loyalty |
| `Belief` | a small vector of weights over defined creeds (see 3.2) | The mix of ideologies held | Missionary traffic, state sponsorship, isolation (drift to a local variant) | Contact with rival belief, schism |
| `Disease` | per disease: prevalence 0-1 and immunity 0-1 | Epidemic burden | Contact with infected systems, crowding, poor medicine | Quarantine, medicine, time and immunity |
| `Tech` | 0-1 industry/maintenance level | What it can build and maintain (gates, hulls) | Contact, investment | Dark age, lost specialists |
| `Resource` | 0-1 | Remaining extractable base for the system | Discovery | Depletion |

**Per polity (keyed by `Faction`):**

| Variable | Range | Meaning |
| --- | --- | --- |
| `Cohesion` | 0-1 | Weighted mean of its systems' `Loyalty`, weighted by population, penalised by overextension (section 5.7). The headline number the UI and the transitions read |
| `Legitimacy` | 0-1 | The ruler's right to rule: rises with victories, prosperity and orderly succession; falls with defeats, hunger, scandal, debasement, contested succession. Distinct from loyalty (a system can be loyal to the polity but not its ruler) |
| `Solvency` | days, unbounded (negative when in debt) | Treasury divided by daily recurring costs (pay, maintenance, garrisons): days of runway. Built on `Faction.Treasury`. `Solvency < 0` means in debt |
| `Reach` | systems | The count and span of what it can administer (a polity-level capacity; see overextension) |
| `Succession` | state | Heir clarity: Clear, Contested, Civil war (see transitions) |
| `Creed` | creed id | The state belief, against which each system's `Belief` is compared |

All of the above are derived or stored by a dynamic's own state object; the existing fields (`Treasury`, `Unrest`, `Population`, `Wellbeing`) stay the source of truth for their own domain and are read and nudged through the same public API the existing systems use.

### 3.2 Belief

A creed is data: id, name, a "distance" matrix to the others (how far apart they are), a missionary strength, and a flag for state-sponsored or not. A system's `Belief` is a small vector. Isolation causes drift: with no contact the system's belief decays toward a locally generated variant (a new creed id created at runtime with a small distance from the parent). This reproduces both the Mongol khanates and the Reformation: a different creed in a distant province is a ready-made justification to leave. The default galaxy ships with two or three creeds and one deliberately latent schism.

### 3.3 Transitions

Each transition has a trigger (a condition on the state) and a roll or threshold. Most have hysteresis: a state must stay beyond the threshold for N days before it flips, so a brief shock does not collapse a system and a rebound can occur.

| Transition | Trigger | Effect | Timescale (sim days) |
| --- | --- | --- | --- |
| Shortage | `Supply < 0.98` (existing line) | Existing `FaminePerDay` shrinks population; the new part raises `Order` risk and lowers `Loyalty` | Weeks |
| Famine crisis | `Supply < 0.6` for 60 days | Mass death and emigration; `Loyalty` falls fast; neighbours who relied on it as a hub are short next | 2-6 months |
| Unrest | `Order < 0.5` | Output falls (existing disruption factor), garrison pulled in, conditions for revolt | Months |
| Severance | `GateSevered` (no open gate route to the capital) | Starts `DaysSinceContact`; the system keeps simulating and may rebuild gates | Immediate; consequences over years |
| Drift | `Loyalty` falls steadily when `DaysSinceContact` grows | `Loyalty` decays at a rate set by distance and neglect | Years (target 5 to 30 sim years to secede, matching the Spanish Americas' 20 years) |
| Revolt | `Loyalty < revolt threshold` and `Order` low | Garrison and local fleet fight or defect; war-like local battle; may be crushed | Weeks to months |
| Secession | `Loyalty < secession threshold` for a sustained time, with no garrison able to crush it | A new polity (U.SPC-T438) holds the system, with its own name and colours, hostile or neutral to its parent | One event |
| Schism | Two creeds in one polity with `Belief` distance above a threshold and a trigger (isolation, a missionary, a dispute) | A fraction of systems and fleets adopt the new creed; loyalty to the creed's centre exceeds loyalty to the polity; may found a new polity | Years |
| Outbreak | Seeded by a trigger (a rare random roll, scaled by crowding and trade) or arriving via traffic | Prevalence rises; mortality and lowered productivity; spreads along routes | Weeks to a few years |
| Quarantine | Prevalence above a threshold in a neighbour | Port or gate closed; supply of the quarantiner and the quarantined falls, which can start famine | Months |
| Fiscal crisis | `Solvency < 0` for 30 days | Fleets and garrisons unpaid: `CrewMorale` and `Loyalty` fall; ships desert, mutiny, or turn outlaw; gate maintenance lapses | Months |
| Succession | Ruler's turnover event (random with a hazard rising with age or low legitimacy) | `Contested` splits the polity into claimants; systems and fleets choose by `Loyalty` and `Legitimacy` | Months to years |
| Dark age | `Tech` falls after severance and loss of specialists | Cannot build or repair gates, yards, hulls above a level | Years to decades |
| Recovery | Contact restored, or supply restored and `Order` high | Variables drift back toward their baseline; rejoin, be reconquered, or stand as a successor | Years to decades |
| Death | A polity holds no standing system | Removed from the active list, leaves ruins and derelicts; may be remembered in the history log | One event |

### 3.4 Feedback loops

Reinforcing (spirals; these cause collapse):
1. **Hunger spiral.** Low `Supply` lowers `Order` and `Loyalty`, which lowers output and raises revolt, which breaks supply further.
2. **Fiscal spiral.** Lost trade lowers the tax base, lowering `Solvency`, which lowers the navy's pay and presence, which lets raiders cut more trade.
3. **Isolation spiral.** No contact lowers `Loyalty`, which lowers investment and `Tech`, which prevents rebuilding the gate, which prolongs no contact.
4. **Hub cascade.** A failed hub starves the systems that import through it, and they stop buying, starving their own suppliers.
5. **Overextension.** More systems raise cost and lower `Cohesion` per system; a war to retake the lost systems costs more.
6. **Epidemic and quarantine.** Disease triggers quarantine, which cuts supply, which weakens health, which feeds disease.

Balancing (these produce recovery and keep the model stable):
1. **Survivor surplus.** After population falls, the per-head resource and supply improve, so `Supply` recovers.
2. **Immunity.** Prevalence falls as immunity builds.
3. **Consolidation.** A smaller polity has a lower upkeep and a higher `Cohesion`, which can stabilise it.
4. **Opportunity.** Collapse removes rivals; a successor or a neighbour rises (the rebound).
5. **Existing floors.** `People.MinShare` keeps a hard core, so a system never reaches zero, which is the "dark age" seed.

The stability rule: every decay is exponential with a floor, every transition has hysteresis, and only the polity-level transitions (secession, death) are discrete events. The harness (U.SPC-T439) enforces that over many seeds.

## 4. The shared extension point (build first)

Proposed: **U.SPC-T437 (new)**.

```csharp
// Core/CivDynamics/ (new folder), namespace Unity.SpaceGod.Core
public interface ICivDynamic
{
    string Id { get; }                       // "logistics", "disease", ...
    int RunOrder { get; }                    // run order within the step (not the Order state variable)
    void Declare(CivSettings s);             // declares tunables (T431 registry): default, range, group
    void Step(CivContext ctx, double dtDays);// one dynamics tick
}

public sealed class CivState                 // the registry
{
    public T System<T>(StarSystem s, string key) where T : new();   // per-system state bag
    public T Polity<T>(Faction f, string key) where T : new();      // per-polity state bag
    // serialisable: each bag type registers a codec so SaveGame can persist it
}

public sealed class CivDynamics              // the host, owned by Polities
{
    public void Register(ICivDynamic d);
    public void Update(double jd);           // runs once per game day, over all systems, not once per World
    public event Action<CivEvent> Occurred;  // secession, outbreak, famine...: news, map and the history log subscribe
}
```

Rules:

1. **One hook.** `Polities` owns `CivDynamics`; the first world to update in a given day runs it (as `KeptBy` does for recruiting). That is the only edit to the core loop: one line. A new mechanic is a new `ICivDynamic` registered at startup (data and code, in its own file), never an edit to `Fleet`, `War` or `Economy`.
2. **State lives in the registry**, keyed by system or faction, not as new fields on `Settlement`. Existing fields (`Unrest`, `Treasury`, `Population`) are read and written through their normal members.
3. **Cross-mechanic effects go through a modifier list,** not direct calls: a dynamic publishes named modifiers (`loyalty.delta`, `supply.multiplier`, `gate.maintenance`) and other dynamics or the core read the aggregate. That lets the disease and logistics dynamics both lower `Supply` without knowing about each other.
4. **Events as data.** A `CivEvent` (kind, subject, day, text, severity) feeds `News`, the galaxy map and a history log (U.SPC-T409).
5. **All rates are settings.** `Declare` registers each constant (e.g. `civ.loyalty.driftPerDay`, `civ.disease.spreadPerContact`) with default, min, max and group "Civilization", so the T431 menu picks it up. Until T431 lands, `CivSettings` is a plain dictionary with defaults in code, so the mechanics are tunable and testable.
6. **Neutral defaults.** Every registry variable declares a neutral value used when its owning dynamic is absent or disabled (`Legitimacy = 1`, belief distance 0, `Tech = 1`, `Solvency` solvent, `Contact` as the gate graph says). Terms read from a missing dynamic then contribute nothing, so T401 can ship before T403/T406 and T400 before T405/T408. The tax-tolerance term in the loyalty equation is owned by T405 and is zero until it exists.
6a. **Determinism and stepping.** `CivDynamics` owns its own seeded RNG, saved in the registry and advanced only by `Step`, so a save replays identically whatever else draws random numbers. The host runs fixed 1-day substeps, catching up `floor(jd - lastDay)` days with a per-frame cap that carries the remainder, so dynamics always see `dtDays == 1` at 1x or at a year per second; hazards are day-counted (`1 - (1-p)^dt` if a coarse step is ever used for unobserved systems) and hysteresis counts days. The harness (T439) checks the same outcome at 1x and max speed.
6b. **Persistence.** The registry serialises through the existing `ObjectGraph` and `SaveGame`.
7. **Ability to be switched off.** A master setting and a per-dynamic enabled flag so a game can run with only the mechanics under test, and the existing 1199 core tests keep their behaviour with the host empty.
8. **Performance (principle 3).** The step runs once a day over per-system state (a few dozen systems), not per ship, and unobserved systems are simulated coarsely (state updates, no entities), which is the "render only what can be seen" idea applied to simulation.

Companion extension point: **U.SPC-T438 (new)**, a runtime polity founding and splitting API. Secession, schism, succession and dark-age successor states all need to create a faction (name, prefix, colours, capital), transfer settlements, ships, treasury share and diplomacy, and fire an event. Today `Polities.Found` is internal and used at world build. A single `Polities.Split(parent, systems, spec)` keeps four mechanics from each writing their own version.

## 5. The drivers, one by one

For each: mechanism, triggers and feedback, timescale, mapping to Core, owning ticket.

### 5.1 Logistics collapse (U.SPC-T398)

- **Mechanism.** A system that imports essentials runs short when lanes break (piracy, war, a lost gate, blockade). Shortage becomes famine, unrest and death; the shortfall passes to systems that relied on it as a hub. Rome's grain supply, the Bronze Age tin and grain network, Byzantium's lost Egypt.
- **Triggers and feedback.** Blockade, gate integrity lost, hubs destroyed, hulls lost. Hunger spiral and hub cascade (above).
- **Timescale.** Famine in weeks after the reserve is gone; collapse in months. `Reserve` days is the buffer, which gives players and AIs time to react.
- **Maps to.** `People.GrowthRate` and `FaminePerDay` (the death mechanism already exists); `Settlement.Satisfaction`, `Stock`, `Inbound`; `Contracts` (supply contracts move food); `MarketShocks` Blockade and Embargo; `MilitarySupply` as the pattern.
- **Build.** State `Supply`, `ImportDependence`, `Reserve`; dynamic `logistics`: reads settlement satisfaction, computes dependence, lowers `Loyalty` and `Order` when `Supply` is low; hub cascade is natural because starving systems stop paying for imports (existing price and `Inbound` model). It does not alter `People`; it adds the consequences `People` lacks.

### 5.2 Disease (U.SPC-T399)

- **Mechanism.** An outbreak starts at one system and spreads along trade and passenger traffic. It kills and weakens, triggers quarantines that break supply, and fades with immunity. Black Death, Antonine plague, Justinian.
- **Triggers and feedback.** Crowding, poor `HealthWellbeing`, contact with infected systems. Spreads on `GateCrossing` and `Voyage` counts; the epidemic and quarantine spiral.
- **Timescale.** Peak in weeks to months, waves for years, immunity over decades.
- **Maps to.** `HealthWellbeing`, `People.SicknessPerDay`, medical supplies as `Good.MedicalSupplies`; `InterstellarTraffic` (`Passage`, `GateCrossing`) as the vector; `JumpGate`/`GateLink.Open` and `Diplomacy` Embargo for quarantine; `Crews` (crew sickness, ships as carriers).
- **Build.** State `Disease` (prevalence, immunity per disease, a disease is data: lethality, spread rate, incubation, duration); dynamic `disease`: spread proportional to traffic between systems per day; mortality through a population multiplier; quarantine as an embargo modifier. The first disease is data-only.

### 5.3 Severed systems (U.SPC-T400)

- **Mechanism.** A system is cut off when no working, open route reaches the capital. It simulates in isolation, must rebuild gates and fleets, and its time out of contact is tracked.
- **Maps to.** `GateNetwork` graph, `GateLink.Working`/`Open`, `GateConstruction`/`GateProject` (rebuild); Void Bases (a single destroyed relay severs everything beyond it, an excellent hub failure); `Polities.CapitalOf`.
- **Build.** State `Contact`, `DaysSinceContact`. Dynamic `contact`: once a day, a flood fill over open links from each polity's capital system, marking unreachable systems; recompute only when a gate's `Working` flips (an event) instead of every day. Under principle 2, "cut off" means cut off from the gate network; a ship can still fly there under its own power, which counts as partial contact: `Contact` stays a 0-1 weight and an arriving ship from the capital reduces `DaysSinceContact` by its weight, while severance itself is the boolean `GateSevered`, so isolation still fires in a flyable universe. A severed system gets a rebuild goal for `GateConstruction`, gated on `Tech` and `Solvency`.

### 5.4 Loyalty and cohesion drift (U.SPC-T401)

- **Mechanism.** Loyalty erodes with distance from the capital, time out of contact, neglect, heavy taxation and misrule; it recovers with contact, investment and protection. The Mongol khanates, Spanish America.
- **Equation (daily).**
  `dLoyalty = +recovery (contact, garrison, prosperity) - drift (distance_ly x k_d + DaysSinceContact^0.5 x k_c + tax over tolerance x k_t + (1 - Supply) x k_s + (1 - polity Legitimacy) x k_l + belief distance x k_b)`, clamped to 0-1 with exponential approach so that nothing is a cliff.
- **Maps to.** `GateNetwork` link lengths for distance (light-years, `GateLink.Ly`), the `contact` dynamic, `Settlement.Garrison`, `Economy` tax and `WageTaxRate`, `Settlement.Wellbeing`.
- **Build.** State `Loyalty`, polity `Cohesion`; dynamic `loyalty`. Everything is a setting. Loyalty is the number every other mechanic reads or writes, so it comes before them.

### 5.5 Rebellion and secession (U.SPC-T402)

- **Mechanism.** Low-loyalty systems revolt; a colony left alone long enough becomes a new polity.
- **Maps to.** The existing revolt roll in `InterstellarWars` (about line 850) is the pattern to generalise, not duplicate; `Garrison`; `Polities.Split` (T438); `Diplomacy` for the new faction's relations; `Law`/`Outlaws` if a revolt fails and its ships flee as outlaws.
- **Build.** Transitions Revolt and Secession; the new faction inherits the system's settlements, the local fleet, and a share of treasury; its relation to the parent is a function of how it left (peaceful, violent). Requires T401 (loyalty), T437, T438.

### 5.6 Religious and ideological schisms (U.SPC-T403)

- **Mechanism.** Beliefs drift and split; schisms divide a polity or cross polity lines, change alliances, spark persecution, migration and holy war.
- **Maps to.** `Belief` state, creeds as data; `Diplomacy` (alliances change by creed distance); `People` migration (refugees go where their creed is held; `MigrationPerDay`); `War` casus belli; `News`.
- **Build.** Creed data, drift and missionary dynamics, the schism transition; persecution lowers `Loyalty` and drives migration. Requires T437, T438 and T401.

### 5.7 Conquest and overextension (U.SPC-T404)

- **Mechanism.** Conquest grows a polity, but each held system adds cost (garrison, administration), stretches supply, and lowers cohesion past its reach.
- **Equation.** `Reach` = f(capital's population, tech, transport speed); `Overextension` = max(0, systems held weighted by distance / `Reach` - 1); it adds a drift term to every system's loyalty and a cost term to `Solvency`, and thins garrisons.
- **Maps to.** `Polities.SystemsHeldBy`, `GateNetwork` hops and light-years, `InterstellarWars` (occupation, `OccupationUnrest`), `MilitaryLogistics`.
- **Build.** Dynamic `overextension`; no change to `InterstellarWars`, only extra drift read by the loyalty dynamic and a cost line read by the fiscal dynamic. This is how a winning empire stops itself.

### 5.8 Fiscal crisis (U.SPC-T405)

- **Mechanism.** The treasury fails (war costs, lost trade, debasement) and fleets and garrisons go unpaid: desertion, mutiny, pirates, lapsed maintenance, falling loyalty. Rome's debasement, Egypt's strikes, the Mongol paper money.
- **Triggers.** `Solvency < 0` for N days. Debasement is the AI's desperate option: print money (raise `Treasury` now, raise a hidden inflation that raises prices and lowers `Legitimacy` and the value of wages later).
- **Maps to.** `Faction.Treasury`, `Economy` taxes, `Crews`/`Morale` (`CrewMorale`), `Holdings` and crews' wages, `Law`/`Outlaws` (deserting hulls become outlaws), `PirateClans` (a mutinous squadron founds or joins a clan), `GateConstruction` (maintenance cut).
- **Build.** Dynamic `fiscal`: costs from navy size and garrisons, income from the existing taxes; unpaid effects as modifiers on `CrewMorale` and `Loyalty`; desertion hooks into the existing outlaw path.

### 5.9 Succession and leadership crises (U.SPC-T406)

- **Mechanism.** The leadership falls and a polity splits into rival claimants and civil war until one wins or the polity splits for good. Rome, the Mongol khanates, Byzantium.
- **Maps to.** `Polities.Split` (T438), `Diplomacy` (the claimants are at war), fleet allegiance by `Loyalty` and `Legitimacy`, `War`.
- **Build.** Polity state `Succession` and `Legitimacy`; the hazard rises when legitimacy is low or the polity is large; a contested succession splits the navy and systems; the winner restores legitimacy at a cost. Requires T437, T438, T401.

### 5.10 Resource and stellar shocks (U.SPC-T407)

- **Mechanism.** Resource depletion and disasters (flares, impacts, habitat failure) cripple an economy and set off the other mechanics. Climate shocks in the Bronze Age and the Little Ice Age.
- **Maps to.** The `MarketShocks` framework (`Depletion` and `Blight` already exist) is the host: add new `ShockKind`s through it rather than creating a second shock system; `Resource` state; `Settlement.Facilities`, `Industry`; stellar flares by star type in `StarSystemSpec`.
- **Build.** Add shock kinds and a stellar activity model. A regional shock (a star flaring) hits a system's food or habitat, producing the `Supply` drop which the other dynamics turn into collapse. Needs only T437 plus the existing shocks.

### 5.11 Death, dark ages and recovery (U.SPC-T408)

- **Mechanism.** A polity can die, leaving ruins; isolated or collapsed systems sink into a dark age and later recover, reconnect, and rejoin, be reconquered or rise as successors.
- **Maps to.** `Wreck`/`Salvage` and `Ship.Derelict` for ruins; `Tech`; `GateConstruction` (a dark-age system cannot build, then can); `Polities.Split` for successors; `Settlement.Fallen`.
- **Build.** Transition Death, `Tech` loss and recovery, a "ruin" flag on fallen settlements that the event director can offer as exploration. Recovery uses the balancing loops, so it is mostly parameters.

### 5.12 Visibility (U.SPC-T409)

Galaxy map overlays (loyalty, supply, plague, severed), news and hails, and a history log, all fed by the `CivEvent` stream. It reads state through the registry, never from a mechanic directly, so a new mechanic gets an overlay by registering a layer.

## 6. Mapping state variables and transitions to tickets

| Piece | Ticket | Builds |
| --- | --- | --- |
| Registry, `ICivDynamic` step, settings, events, persistence | **T437 (new)** | The framework and the master/per-dynamic switches |
| `Polities.Split` and runtime faction founding | **T438 (new)** | Shared by secession, schism, succession, successor states |
| Long-run harness | **T439 (new)** | Balance and regression across seeds |
| `Contact`, `DaysSinceContact`, severance, rebuild goal | T400 | Reachability dynamic |
| `Supply`, `ImportDependence`, `Reserve`, hub cascade | T398 | Logistics dynamic |
| `Loyalty`, `Cohesion`, `Order` | T401 | Loyalty dynamic (the central variable) |
| `Disease`, quarantine | T399 | Disease dynamic |
| `Solvency`, debasement, desertion | T405 | Fiscal dynamic |
| `Reach`, `Overextension` | T404 | Overextension dynamic |
| `Resource`, shock kinds, stellar activity | T407 | Shocks via `MarketShocks` |
| Revolt, secession | T402 | Transitions on top of loyalty and Split |
| `Belief`, creeds, schism | T403 | Belief dynamic |
| `Legitimacy`, `Succession`, civil war | T406 | Leadership dynamic |
| `Tech`, dark age, Death, recovery | T408 | Collapse and recovery |
| Overlays, news, history | T409 | Presentation |

## 7. Build order

Each step is independently testable and leaves the game playable.

1. **T437** framework (and the T431 settings hook when it lands). Zero behaviour change with no dynamics registered. Also **T439** harness skeleton, so every later step is measured.
2. **T400** severed systems: produces `Contact`, the cheapest signal and the one the owner asked about first, and needs only the existing gate graph.
3. **T401** loyalty: reads contact, supply (from existing `Satisfaction`), garrison. Nothing changes hands yet, but the number is visible.
4. **T398** logistics collapse: turns the existing famine into consequences (loyalty, order, hub cascade). Together with 2-3 this gives the "cut off and starve" story.
5. **T438** polity splitting, then **T402** rebellion and secession: the headline payoff (a colony returns as a different faction).
6. **T409** visibility in step with each of the above (at least the map overlay and news before T402), because invisible simulation reads as nothing happening.
7. **T405** fiscal crisis and **T404** overextension (they interact: the cost of holding raises fiscal strain; build together).
8. **T399** disease (uses traffic data; its tuning needs the harness and the supply dynamic).
9. **T407** shocks (new `MarketShocks` kinds, small once the rest exists).
10. **T406** succession, **T403** schisms (both need Split and loyalty; the most narrative and the most tuning).
11. **T408** dark ages and recovery, last because it depends on the shape of everything above; its balancing loops need the harness data.

## 8. Tuning and risks

- **Timescales.** Targets in sim time: famine within 1-6 months of severance (once reserve ends), secession 5-30 sim years after severance, plague peak under a year, dark age decades, succession crisis hazard of a few percent a year. All are settings.
- **Runaway collapse.** The biggest risk is that the default galaxy collapses into nothing in 50 sim years. Mitigation: hysteresis, floors (`MinShare`), a protected "core" polity setting, and the harness asserting at least N polities survive in 100 years for default settings.
- **Stagnation** is the opposite risk (no event for centuries). The harness reports events per decade.
- **Player and AI agency.** Every spiral has a counter the player or AI can apply (relief convoys, rebuilding a gate, paying the fleet, a diplomatic fix). A mechanic that cannot be answered is not shipped.
- **Determinism.** All rolls use the framework's own seeded RNG (rule 6a), so a save replays the same collapse.
- **Existing tests.** The framework must be a no-op unless dynamics are registered, and each dynamic is off in tests that predate it.
- **New tickets filed.** U.SPC-T437, T438 and T439 (linked part-of U.SPC-T396, requires this spike); T398-T408 and T409 now also require T437, and T402, T403, T406, T408 require T438. T402 requires T401 and T401 requires T400, giving the order above. No duplicates of T398-T409 were filed.
