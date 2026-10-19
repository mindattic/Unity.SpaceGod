using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// What a war fleet on campaign has left of the three things it burns through (U.SPC-T93), each a share of a full
    /// load (1 as it sets out, 0 empty): propellant for its drives, munitions for its magazines, and the spares it mends
    /// itself with. They drain by the day (marching burns propellant far faster than lying in a system, fighting empties
    /// the magazines, and field repairs eat the spares), and its <see cref="MilitaryLogistics"/> fills them again from a
    /// naval base by supply convoys. Low on any of them the fleet loses <see cref="Readiness"/>, and too low it withdraws.
    /// </summary>
    public sealed class FleetSupply
    {
        public double Propellant = 1, Munitions = 1, Spares = 1;

        /// <summary>No way open over the gates to any naval base of its own (see <see cref="MilitaryLogistics.LineTo"/>), and since when.</summary>
        public bool OutOfSupply;
        public double OutOfSupplySinceJd = double.NaN;

        /// <summary>The naval base it draws on now, or null.</summary>
        public Settlement Base;

        /// <summary>The supply ships on their way to it, or null.</summary>
        public SupplyConvoy Convoy;

        /// <summary>Convoys that reached it, and those that could not get through.</summary>
        public int ConvoysArrived, ConvoysLost;

        /// <summary>Below this share of a full load, a store starts to tell on the fleet's readiness.</summary>
        public const double LowSupply = 0.3;

        /// <summary>A fleet's readiness with a store empty.</summary>
        public const double ReadinessFloor = 0.25;

        /// <summary>A fleet below this readiness, with no convoy on its way, gives up its campaign and goes home.</summary>
        public const double WithdrawReadiness = 0.6;

        /// <summary>A fleet sends for supply ships when its lowest store is below this.</summary>
        public const double ResupplyBelow = 0.75;

        /// <summary>A full load of propellant lasts this many days of marching (under way in a system, or in the gates)...</summary>
        public const double MarchDays = 30;

        /// <summary>...and this many days lying in a system, station-keeping.</summary>
        public const double IdleDays = 150;

        /// <summary>Full magazines last this many days of fighting.</summary>
        public const double BattleDays = 5;

        /// <summary>Its spares wear away over this many days whatever it does (breakdowns, wear).</summary>
        public const double WearDays = 120;

        /// <summary>A day's field repair mends this share of a hull's damage, at full readiness...</summary>
        public const double FieldRepairPerDay = 0.01;

        /// <summary>...and takes this share of the fleet's spares per share mended, per hull.</summary>
        public const double SparesPerRepair = 1.5;

        /// <summary>Readiness for what is left of one store.</summary>
        public static double ReadinessOf(double stock) =>
            stock >= LowSupply ? 1 : ReadinessFloor + (1 - ReadinessFloor) * Math.Max(0, stock) / LowSupply;

        /// <summary>The fleet's readiness: as low as its lowest store makes it (see <see cref="ShipCombat.Readiness"/>).</summary>
        public double Readiness => ReadinessOf(Lowest);

        /// <summary>Its lowest store.</summary>
        public double Lowest => Math.Min(Propellant, Math.Min(Munitions, Spares));

        /// <summary>Too run down to go on, and nothing on its way to it: it must withdraw.</summary>
        public bool MustWithdraw => Readiness < WithdrawReadiness && Convoy == null;

        /// <summary><paramref name="days"/> of campaigning: under way or not, fighting or not.</summary>
        public void Drain(double days, bool moving, bool fighting)
        {
            Propellant = Math.Max(0, Propellant - days / (moving ? MarchDays : IdleDays));
            if (fighting) Munitions = Math.Max(0, Munitions - days / BattleDays);
            Spares = Math.Max(0, Spares - days / WearDays);
        }

        /// <summary>
        /// A day's field repair of one of its <paramref name="hulls"/> carrying <paramref name="damage"/>: the share of it
        /// mended (slower as readiness falls), paid for out of the spares, as far as they go.
        /// </summary>
        public double FieldRepair(double damage, int hulls, double days = 1)
        {
            if (damage <= 0.001 || Spares <= 0) return 0;
            double perShare = SparesPerRepair / Math.Max(1, hulls);
            double share = Math.Min(damage, FieldRepairPerDay * days * Readiness);
            share = Math.Min(share, Spares / perShare);
            Spares = Math.Max(0, Spares - share * perShare);
            return share;
        }

        /// <summary>Takes in what a convoy brings.</summary>
        public void Deliver(SupplyConvoy c)
        {
            Propellant = Math.Min(1, Propellant + c.Propellant);
            Munitions = Math.Min(1, Munitions + c.Munitions);
            Spares = Math.Min(1, Spares + c.Spares);
        }
    }

    /// <summary>
    /// Supply ships a naval base sends after a war fleet (U.SPC-T93): tankers with its propellant, ammunition colliers
    /// with its munitions and spares. They follow the fleet's way over the gates, and the way must still be open when they
    /// come up with it: a gate cut or shut against them behind the fleet, and they cannot get through.
    /// </summary>
    public sealed class SupplyConvoy
    {
        public Campaign Campaign;

        /// <summary>The naval base it set out from, and its system.</summary>
        public Settlement Base;
        public StarSystem From;

        public double SetOutJd, DueJd;
        public int Tankers, Colliers;

        /// <summary>What it carries, as shares of the fleet's full load.</summary>
        public double Propellant, Munitions, Spares;

        /// <summary>What it carries, in tonnes, and what the navy paid for it.</summary>
        public double Tonnes, Cost;

        public override string ToString() => $"{Tankers} tanker{(Tankers == 1 ? "" : "s")} and {Colliers} collier{(Colliers == 1 ? "" : "s")} from {Base?.Name}";
    }

    /// <summary>
    /// Military logistics (U.SPC-T93): the supply of the war fleets out on campaign (see <see cref="InterstellarWars"/>).
    ///
    /// Every fleet burns through its <see cref="FleetSupply"/>: propellant on the march, munitions in battle, spares
    /// mending itself in the field. A navy's naval bases are its shipyard ports; each day a fleet looks for the nearest one
    /// it can reach over gates that will open for it (its own, a neutral's, or enemy gates it has taken), not blockaded or
    /// fought over, and when its stores run down the base sends supply ships after it, bought from the base's own market
    /// (water and hydrocarbons for reaction mass, munitions, alloys and electronics for spares) and paid for from the
    /// navy's treasury: an empty market or an empty treasury sends less, or nothing. So the war draws on the economy, and
    /// the economy's shortages tell on the war.
    ///
    /// A fleet that can reach no base is out of supply. As its stores run low its readiness falls (its guns reload slower,
    /// its crews strike sooner, it mends slower: see <see cref="ShipCombat.Readiness"/>), and below
    /// <see cref="FleetSupply.WithdrawReadiness"/> it gives up the campaign and withdraws. Cutting a gate behind it, or
    /// blockading the base it draws on, starves the fleet.
    /// </summary>
    public sealed class MilitaryLogistics
    {
        readonly Galaxy galaxy;
        readonly List<SupplyConvoy> convoys = new List<SupplyConvoy>();

        /// <summary>Supply convoys on their way.</summary>
        public IReadOnlyList<SupplyConvoy> Convoys => convoys;

        /// <summary>Munitions (t) a hull's magazines hold, full.</summary>
        public const double MunitionsPerHull = 150;

        /// <summary>Alloys and electronics (t) a hull's stores of spares hold, full.</summary>
        public const double AlloysPerHull = 120, ElectronicsPerHull = 20;

        /// <summary>What one tanker and one ammunition collier carry (t).</summary>
        public const double TankerTonnes = 4000, CollierTonnes = 1500;

        /// <summary>Days a base takes to load its supply ships, and to cross a system to a fleet there.</summary>
        public const double LoadingDays = 0.5, CrossingDays = 0.5;

        public MilitaryLogistics(Galaxy galaxy)
        {
            this.galaxy = galaxy;
        }

        Diplomacy Diplomacy => galaxy.Polities.Diplomacy;

        /// <summary>
        /// Whether <paramref name="s"/> serves its owner as a naval base now: a standing shipyard port, not under blockade
        /// and nobody besieging or fighting over it.
        /// </summary>
        public bool BaseOpen(Settlement s, World world)
        {
            if (s == null || s.Fallen || !s.HasShipyard || s.Kind == SettlementKind.Haven) return false;
            var by = Diplomacy.BlockadedBy(s);
            if (by != null && by != s.Owner) return false;
            var besieger = world.War.Besieger(s);
            if (besieger != null && besieger != s.Owner) return false;
            return world.War.BattleAt(s.Body) == null;
        }

        /// <summary>
        /// The naval base of <paramref name="f"/> that can supply a fleet in <paramref name="here"/> soonest, and the way
        /// its supply ships take there (empty if it is in the same system); (null, null) when none can: the fleet is out of supply.
        /// </summary>
        public (Settlement Base, List<JumpGate> Route) LineTo(Faction f, StarSystem here, double jd)
        {
            Settlement best = null;
            List<JumpGate> bestRoute = null;
            double bestDays = double.MaxValue;
            if (f == null || here == null) return (null, null);
            foreach (var s in galaxy.Systems)
            {
                List<JumpGate> route = null;
                bool tried = false;
                foreach (var st in s.World.Economy.Settlements)
                {
                    if (st.Owner != f || !BaseOpen(st, s.World)) continue;
                    if (!tried)
                    {
                        tried = true;
                        route = s == here ? new List<JumpGate>() : galaxy.Network?.Route(s, here, f, jd);
                    }
                    if (route == null) break;
                    double days = GateNetwork.RouteDays(route);
                    if (days < bestDays)
                    {
                        bestDays = days;
                        best = st;
                        bestRoute = route;
                    }
                }
            }
            return (best, bestRoute);
        }

        /// <summary>A campaign's day in the gates: under way, burning propellant.</summary>
        public void InGate(Campaign c, double jd) => c.Supply.Drain(1, moving: true, fighting: false);

        /// <summary>
        /// A day of a campaign's supply, its flagship <paramref name="leader"/> in <paramref name="system"/>: its stores drain
        /// and pay for field repairs, a convoy due comes up with it (or cannot get through), it sends for another when it
        /// runs low and a base can reach it, and its hulls take on its readiness.
        /// </summary>
        public void Day(Campaign c, StarSystem system, Ship leader, double jd)
        {
            var supply = c.Supply;
            var world = system.World;
            var group = Group(world, leader);
            bool fighting = false;
            foreach (var s in group) fighting |= s.Battle != null;
            bool moving = leader.Phase != ShipPhase.Parked || leader.GateKm != null;
            supply.Drain(1, moving, fighting);
            foreach (var s in group)
                if (s.Battle == null)
                {
                    double share = supply.FieldRepair(s.Combat.Damage, group.Count);
                    if (share > 0) s.Combat.Repair(share);
                }

            var (station, route) = LineTo(c.Faction, system, jd);
            supply.Base = station;
            if (station == null && !supply.OutOfSupply)
            {
                supply.OutOfSupply = true;
                supply.OutOfSupplySinceJd = jd;
                world.Log(jd, $"The {c.Faction.Name} war fleet under the {leader.Name} at {system.Name} is cut off from its bases: out of supply", WorldEventKind.Info,
                    system.Catalog.Root.Index);
            }
            else if (station != null && supply.OutOfSupply)
            {
                supply.OutOfSupply = false;
                supply.OutOfSupplySinceJd = double.NaN;
                world.Log(jd, $"The {c.Faction.Name} war fleet under the {leader.Name} is in supply again from {station.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
            }

            var convoy = supply.Convoy;
            if (convoy != null && jd >= convoy.DueJd) Arrive(c, convoy, system, leader, group, jd);
            if (supply.Convoy == null && station != null && supply.Lowest < FleetSupply.ResupplyBelow) Dispatch(c, station, route, system, leader, group, jd);

            double r = supply.Readiness;
            foreach (var s in group) s.Combat.Readiness = r;
        }

        /// <summary>The convoy comes up with the fleet, if the way from its base is still open: it hands over what it carries.</summary>
        void Arrive(Campaign c, SupplyConvoy convoy, StarSystem system, Ship leader, List<Ship> group, double jd)
        {
            var supply = c.Supply;
            supply.Convoy = null;
            convoys.Remove(convoy);
            var world = system.World;
            var way = convoy.From == system ? new List<JumpGate>() : galaxy.Network?.Route(convoy.From, system, c.Faction, jd);
            if (way == null)
            {
                supply.ConvoysLost++;
                world.Log(jd, $"The {c.Faction.Name} supply ships ({convoy}) cannot get through to the war fleet at {system.Name}: the way behind it is cut",
                    WorldEventKind.Loss, system.Catalog.Root.Index);
                return;
            }
            supply.Deliver(convoy);
            supply.ConvoysArrived++;
            // Tanks topped up and magazines filled from what the fleet now has aboard.
            foreach (var s in group)
            {
                s.Propellant = Math.Max(s.Propellant, s.PropellantCapacity * supply.Propellant);
                foreach (var sub in s.Combat.Subsystems)
                    if (sub.Def.Weapon != null && sub.Def.Weapon.Missile && !sub.Destroyed)
                        sub.Ammo = Math.Max(sub.Ammo, (int)Math.Round(sub.Def.Weapon.Ammo * supply.Munitions));
            }
        }

        /// <summary>
        /// A naval base sends supply ships after a fleet: what it needs to fill its stores, as far as the base's market has
        /// it and the navy can pay, on the way <paramref name="route"/>.
        /// </summary>
        void Dispatch(Campaign c, Settlement station, List<JumpGate> route, StarSystem system, Ship leader, List<Ship> group, double jd)
        {
            var f = c.Faction;
            if (f.Treasury <= 0) return;
            var supply = c.Supply;
            int hulls = group.Count;
            double capacity = 0;
            foreach (var s in group) capacity += s.PropellantCapacity;

            double propNeed = (1 - supply.Propellant) * capacity;
            double munNeed = (1 - supply.Munitions) * MunitionsPerHull * hulls;
            double alloysNeed = (1 - supply.Spares) * AlloysPerHull * hulls;
            double elecNeed = (1 - supply.Spares) * ElectronicsPerHull * hulls;
            var stock = station.Stock;
            var price = station.Price;
            // The economy feels what the war asks of it, whether or not it can give it.
            station.ExtraDemand[(int)Good.Water] += propNeed;
            station.ExtraDemand[(int)Good.Munitions] += munNeed;
            station.ExtraDemand[(int)Good.Alloys] += alloysNeed;
            station.ExtraDemand[(int)Good.Electronics] += elecNeed;

            double massHave = stock[(int)Good.Water] + stock[(int)Good.Hydrocarbons];
            double propShare = propNeed > 0 ? Math.Min(1, massHave / propNeed) : 0;
            double munShare = munNeed > 0 ? Math.Min(1, stock[(int)Good.Munitions] / munNeed) : 0;
            double sparesShare = alloysNeed > 0 ? Math.Min(1, Math.Min(stock[(int)Good.Alloys] / alloysNeed, stock[(int)Good.Electronics] / Math.Max(elecNeed, 1e-9))) : 0;

            double cost = 0;
            double prop = propNeed * propShare;
            double fromWater = Math.Min(prop, stock[(int)Good.Water]);
            cost += Take(station, Good.Water, fromWater) + Take(station, Good.Hydrocarbons, prop - fromWater);
            cost += Take(station, Good.HeliumThree, prop * Propulsion.HeliumThreePerTonne);
            double mun = munNeed * munShare;
            cost += Take(station, Good.Munitions, mun);
            double spares = (alloysNeed + elecNeed) * sparesShare;
            cost += Take(station, Good.Alloys, alloysNeed * sparesShare) + Take(station, Good.Electronics, elecNeed * sparesShare);
            if (prop + mun + spares < 1) return;
            f.Treasury -= cost;

            var convoy = new SupplyConvoy
            {
                Campaign = c, Base = station, From = SystemOf(station) ?? system, SetOutJd = jd,
                DueJd = jd + LoadingDays + GateNetwork.RouteDays(route) + CrossingDays,
                Tankers = prop > 0 ? (int)Math.Ceiling(prop / TankerTonnes) : 0,
                Colliers = mun + spares > 0 ? (int)Math.Ceiling((mun + spares) / CollierTonnes) : 0,
                Propellant = (1 - supply.Propellant) * propShare,
                Munitions = (1 - supply.Munitions) * munShare,
                Spares = (1 - supply.Spares) * sparesShare,
                Tonnes = prop + mun + spares, Cost = cost,
            };
            supply.Convoy = convoy;
            convoys.Add(convoy);
            var baseWorld = convoy.From.World;
            baseWorld.Log(jd, $"{station.Name} sends {convoy.Tankers} tanker{(convoy.Tankers == 1 ? "" : "s")} and {convoy.Colliers} ammunition collier{(convoy.Colliers == 1 ? "" : "s")} after the {f.Name} war fleet at {system.Name}",
                WorldEventKind.Launch, station.Body);
        }

        /// <summary>Buys up to <paramref name="tonnes"/> of <paramref name="g"/> off <paramref name="s"/>'s market: what it cost.</summary>
        static double Take(Settlement s, Good g, double tonnes)
        {
            tonnes = Math.Min(Math.Max(0, tonnes), s.Stock[(int)g]);
            s.Stock[(int)g] -= tonnes;
            return tonnes * s.Price[(int)g];
        }

        /// <summary>A campaign is over: whatever is still on its way to it turns back.</summary>
        public void Ended(Campaign c)
        {
            if (c.Supply.Convoy != null) convoys.Remove(c.Supply.Convoy);
            c.Supply.Convoy = null;
        }

        StarSystem SystemOf(Settlement st)
        {
            var world = galaxy.Polities.WorldOf(st);
            foreach (var s in galaxy.Systems)
                if (s.World == world) return s;
            return null;
        }

        /// <summary>A fleet's flagship and the hulls in its group.</summary>
        static List<Ship> Group(World world, Ship leader)
        {
            var list = new List<Ship> { leader };
            foreach (var s in world.Fleet.Ships)
                if (s != leader && s.Leader == leader && !s.Destroyed) list.Add(s);
            return list;
        }
    }
}
