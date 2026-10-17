using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolarSystem.Core
{
    /// <summary>Why a ship is crossing the gates.</summary>
    public enum VoyagePurpose
    {
        /// <summary>A freighter carrying goods to another system's markets (and a backhaul home).</summary>
        Trade,

        /// <summary>A courier carrying mail and data cores between the settled systems, for a fee.</summary>
        Courier,

        /// <summary>A yacht or survey ship off to look at an unsettled system.</summary>
        Survey,

        /// <summary>A raider group off to prey on another system's traffic.</summary>
        Raid,

        /// <summary>A navy group sent after raiders to the system they came from.</summary>
        Expedition,
    }

    /// <summary>
    /// One ship's (or group's) trip across the gate network: where it belongs, where it is bound,
    /// the gates to take, and what it does when it gets there. Kept on the flagship
    /// (<see cref="Ship.Voyage"/>), and carried over to each copy the gates make of it.
    /// </summary>
    public sealed class Voyage
    {
        public VoyagePurpose Purpose;

        /// <summary>The system the ship belongs to, and comes back to.</summary>
        public StarSystem Home;

        /// <summary>Where this leg ends: the far system outbound, <see cref="Home"/> on the way back.</summary>
        public StarSystem Destination;

        /// <summary>The gates still to take, in order (see <see cref="GateNetwork.Route"/>), and the next one's index.</summary>
        public List<JumpGate> Route = new List<JumpGate>();
        public int Leg;

        /// <summary>Held at a gate: waiting to go through (it is about to, or the way is shut).</summary>
        public bool AtGate;
        public double AtGateJd;

        /// <summary>When a ship held at a shut gate started waiting; NaN when not waiting.</summary>
        public double WaitingSinceJd = double.NaN;

        /// <summary>Abroad, when it turns for home.</summary>
        public double StayUntilJd;

        /// <summary>When it left home.</summary>
        public double SetOutJd;

        /// <summary>The colours it set out under: a ship taken by another faction on the way is off the voyage.</summary>
        public Faction Faction;

        /// <summary>An expedition's quarry: the raiders it was sent after.</summary>
        public Faction Quarry;

        /// <summary>Light-years along the outbound route (what tolls and fees scale with).</summary>
        public double Ly;

        /// <summary>Gate tolls paid so far, credits.</summary>
        public double Tolls;

        /// <summary>What it was doing at home, to take up again when it gets back (body indices of the home catalogue).</summary>
        public ShipRole HomeRole;
        public int[] HomeRoute;
        public int[] HomePorts;

        /// <summary>A trade run's plan, on the news it was planned on (U.SPC-T100): the market it was bound for, what that
        /// market's board said the cargo would fetch there, and how old that board was when the ship set out.</summary>
        public string PlannedMarket;
        public double ExpectedSale, PlannedOnNewsJd = double.NaN;

        public JumpGate NextGate => Route != null && Leg < Route.Count ? Route[Leg] : null;
        public bool Homeward => Destination == Home;
    }

    /// <summary>A ship (and its escorts) inside a gate, between leaving one system and coming out in the next.</summary>
    public sealed class Passage
    {
        /// <summary>Flagship first, then its escorts (the copies that will come out at the far end).</summary>
        public readonly List<Ship> Ships = new List<Ship>();
        public JumpGate From, To;
        public double EnterJd, ExitJd;

        /// <summary>The news it carries: what the system it left knew when it went in (U.SPC-T100, <see cref="Newswire.Bundle"/>).</summary>
        public List<SystemReport> News;

        public Ship Leader => Ships[0];

        /// <summary>How far through (0 at the near gate, 1 at the far one).</summary>
        public double Progress(double jd) => ExitJd > EnterJd ? Math.Max(0, Math.Min(1, (jd - EnterJd) / (ExitJd - EnterJd))) : 1;
    }

    /// <summary>
    /// The traffic between the star systems. Every system runs its own world and nothing else is
    /// shared, so a ship crosses by flying to one of its system's gates (a burn to the gate's fixed
    /// point, see <see cref="Ship.GateKm"/>), going in (the hull is retired where it was, as the
    /// piloted crossing does; see <see cref="GateTransit"/>), spending <see cref="GateNetwork.TransitDays"/>
    /// in the gate (a <see cref="Passage"/>), and coming out as a copy in the next system's fleet, at
    /// the far gate. A route of several gates crosses each system between them gate to gate.
    ///
    /// Each day it sends out what the settled systems would: freighters carrying a good to another
    /// system's market when the price gap there beats the cost of the trip, gate tolls included
    /// (<see cref="MarkupPerLy"/> of the cargo's cost a light-year, paid to the gates' owners);
    /// couriers with the mail; now and then a raider group bound for another system's lanes and a
    /// yacht off to look at an unsettled star; and, after raiders come through, a navy group sent to
    /// hunt them in the system they came from. Abroad, each does its work for a few weeks (the
    /// freighter trades in the local markets, the raiders raid) and then comes home the quickest open
    /// way. A gate's owner closes it to all but its own ships for <see cref="BlockadeDays"/> after
    /// raiders come through it; traffic then goes round by other gates where it can, or waits.
    /// </summary>
    public sealed class InterstellarTraffic
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<Passage> inGates = new List<Passage>();
        readonly List<(StarSystem system, Ship ship)> atGates = new List<(StarSystem, Ship)>();
        readonly List<(StarSystem from, StarSystem to, Faction quarry, double jd)> expeditionsWanted = new List<(StarSystem, StarSystem, Faction, double)>();
        double nextDailyJd;

        /// <summary>Ships inside a gate right now.</summary>
        public IReadOnlyList<Passage> InGates => inGates;

        /// <summary>What each system knows of the others, and how old it is (U.SPC-T100).</summary>
        public Newswire News { get; }

        /// <summary>Voyages that have reached their far end so far, by purpose (homecomings not counted).</summary>
        public readonly Dictionary<VoyagePurpose, int> Arrivals = new Dictionary<VoyagePurpose, int>();

        /// <summary>Gate crossings so far (a group counts once), and the tolls paid on cargo, credits.</summary>
        public int Crossings { get; private set; }
        public double TollsPaid { get; private set; }

        /// <summary>Gate tolls and freight, as a share of a cargo's cost per light-year crossed.</summary>
        public const double MarkupPerLy = 0.03;

        /// <summary>A freighter goes only for a margin of at least this share of what the cargo cost landed.</summary>
        public const double MinMargin = 0.12;

        /// <summary>The most freighters out on one system's runs to another at once.</summary>
        public const int MaxTradersPerRoute = 4;

        /// <summary>What a mail run pays a courier a light-year, credits (the receiving system's government pays).</summary>
        public const double MailFeePerLy = 60000;

        /// <summary>How long a gate stays closed after raiders come through it (days).</summary>
        public const double BlockadeDays = 30;

        /// <summary>A ship held at a shut gate gives up after this long (days) and goes about its business where it is.</summary>
        public const double MaxWaitDays = 8;

        /// <summary>Daily chance a settled system's raiders set out for another system, when none are out.</summary>
        public const double RaidChancePerDay = 0.02;

        /// <summary>Daily chance a yacht or survey ship sets out for an unsettled system.</summary>
        public const double SurveyChancePerDay = 0.03;

        public InterstellarTraffic(Galaxy galaxy, double jd, int seed = 1601)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            nextDailyJd = jd + 0.5;
            News = new Newswire(galaxy, jd);
            foreach (var system in galaxy.Systems)
            {
                var s = system;
                s.World.ReachedGate += (ship, when) => OnReachedGate(s, ship, when);
                s.World.VoyageOrders = (ship, here, when) => Orders(s, ship, here, when);
            }
        }

        GateNetwork Network => galaxy.Network;

        /// <summary>Gate arrivals, emergences and blocked ships to <paramref name="jd"/>; each day, new voyages.</summary>
        public void Update(double jd)
        {
            // Ships that have reached a gate go in (or wait, or turn for another), in date order.
            atGates.Sort((a, b) => (a.ship.Voyage?.AtGateJd ?? 0).CompareTo(b.ship.Voyage?.AtGateJd ?? 0));
            foreach (var (system, ship) in new List<(StarSystem, Ship)>(atGates))
            {
                var v = ship.Voyage;
                // The first try is when it got there; a ship already waiting tries again now.
                double when = v != null && double.IsNaN(v.WaitingSinceJd) ? Math.Min(v.AtGateJd, jd) : jd;
                TryEnter(system, ship, when, jd);
            }

            for (int i = inGates.Count - 1; i >= 0; i--)
                if (inGates[i].ExitJd <= jd)
                {
                    var p = inGates[i];
                    inGates.RemoveAt(i);
                    Emerge(p);
                }

            // The news: relays sent and reports arrived, before anyone plans on it.
            News.Update(jd);

            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- at the gate

        void OnReachedGate(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            if (v == null)
            {
                // Bound for a gate with nowhere to go: back to work.
                ship.GateKm = null;
                return;
            }
            v.AtGate = true;
            v.AtGateJd = jd;
            atGates.Add((system, ship));
        }

        /// <summary>Takes a held ship through its next gate if it opens for it; else round by another, or waits.</summary>
        void TryEnter(StarSystem system, Ship ship, double when, double now)
        {
            var v = ship.Voyage;
            if (ship.Destroyed || v == null || ship == system.World.PilotedShip || ship.Faction != v.Faction)
            {
                atGates.RemoveAll(e => e.ship == ship);
                if (!ship.Destroyed) CancelVoyage(system, ship, now, v != null && ship.Faction == v.Faction);
                return;
            }
            var gate = v.NextGate;
            bool here = gate != null && gate.Home == system && gate.InRange(ship.Orbit.OffsetAt(when));
            if (gate == null || gate.Home != system || !gate.Link.Open(system, ship.Faction, when) || !here)
            {
                var route = Network.Route(system, v.Destination, ship.Faction, now);
                if (route != null && route.Count > 0 && route[0] != gate)
                {
                    // Round by another gate.
                    atGates.RemoveAll(e => e.ship == ship);
                    v.Route = route;
                    v.Leg = 0;
                    v.AtGate = false;
                    v.WaitingSinceJd = double.NaN;
                    ship.GateKm = route[0].PositionKm;
                    system.World.Fleet.ScheduleNext(ship, now);
                    if (gate != null)
                        system.World.Log(now, $"{ship.Name} turns for the {route[0].Name}: the {gate.Name} is shut to it", WorldEventKind.Info, system.Catalog.Root.Index);
                    return;
                }
                if (route != null && route.Count > 0 && !here)
                {
                    // The right gate, but not at it (it was knocked off course): fly the last of the way.
                    atGates.RemoveAll(e => e.ship == ship);
                    v.AtGate = false;
                    ship.GateKm = route[0].PositionKm;
                    system.World.Fleet.ScheduleNext(ship, now);
                    return;
                }
                if (double.IsNaN(v.WaitingSinceJd))
                {
                    v.WaitingSinceJd = now;
                    if (gate != null) system.World.Log(now, $"{ship.Name} waits at the {gate.Name}: it is shut to {ship.Faction.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
                }
                if (now - v.WaitingSinceJd > MaxWaitDays)
                {
                    atGates.RemoveAll(e => e.ship == ship);
                    GiveUp(system, ship, now);
                }
                return;
            }

            atGates.RemoveAll(e => e.ship == ship);
            Enter(system, ship, gate, when);
        }

        /// <summary>Retires the group here and puts copies of it in the gate, bound for the far end.</summary>
        void Enter(StarSystem system, Ship leader, JumpGate gate, double jd)
        {
            var v = leader.Voyage;
            var world = system.World;
            var link = gate.Link;
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            v.Leg++;

            // The gate's toll on a cargo, split between its two owners: the distance-scaled markup on interstellar freight.
            if (leader.HasCargo && leader.CargoCost > 0)
            {
                double toll = leader.CargoCost * MarkupPerLy * link.Ly;
                leader.CargoCost += toll;
                leader.Faction.Treasury -= toll;
                v.Tolls += toll;
                TollsPaid += toll;
                if (gate.Owner != null) gate.Owner.Treasury += toll * 0.5;
                if (gate.Partner.Owner != null) gate.Partner.Owner.Treasury += toll * 0.5;
            }
            world.Economy.Release(leader);

            var p = new Passage { From = gate, To = gate.Partner, EnterJd = jd, ExitJd = jd + link.TransitDays, News = News.Bundle(system, jd) };
            var copy = CopyOf(leader);
            copy.Voyage = v;
            copy.Role = leader.Role;
            p.Ships.Add(copy);
            foreach (var e in world.Fleet.Ships)
            {
                if (e.Leader != leader || e.Destroyed) continue;
                var ec = CopyOf(e);
                ec.Role = ShipRole.Escort;
                ec.Leader = copy;
                ec.FormationSlot = e.FormationSlot;
                p.Ships.Add(ec);
                Retire(e);
            }
            Retire(leader);
            inGates.Add(p);
            Crossings++;
            string group = p.Ships.Count > 1 ? $" and {p.Ships.Count - 1} more" : "";
            world.Log(jd, $"{leader.Name}{group} enter{(p.Ships.Count > 1 ? "" : "s")} the {gate.Name} for {gate.Destination.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
        }

        static Ship CopyOf(Ship s)
        {
            var c = s.CloneForGateArrival();
            c.Role = s.Role;
            return c;
        }

        static void Retire(Ship s)
        {
            s.Destroyed = true;
            s.GateDeparted = true;
            s.Voyage = null;
            s.GateKm = null;
        }

        /// <summary>Out of the far gate: into that system's fleet, then on to the next gate or to the voyage's business there.</summary>
        void Emerge(Passage p)
        {
            var system = p.To.Home;
            var world = system.World;
            var fleet = world.Fleet;
            var leader = p.Leader;
            var v = leader.Voyage;
            double jd = p.ExitJd;
            var forward = p.From.ArrivalForward;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            ParkingOrbit AtGate() => new ParkingOrbit
            {
                Body = system.Catalog.Root.Index, RadiusKm = 0, Center = p.From.ArrivalOffsetKm, U = right, V = forward,
                EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
            };
            foreach (var s in p.Ships)
            {
                s.Orbit = AtGate();
                s.Phase = ShipPhase.Parked;
                s.PhaseStartJd = jd;
                s.PhaseEndJd = jd;
                s.DestinationBody = s.Orbit.Body;
                s.Ports = world.FreeShipPorts();
                fleet.Receive(s);
            }

            if (v.NextGate != null)
            {
                // Passing through: across to the next gate.
                leader.GateKm = v.NextGate.PositionKm;
                fleet.ScheduleNext(leader, jd);
            }
            else if (v.Homeward) Home(system, leader, jd);
            else Arrive(system, leader, p.To, jd);
            foreach (var s in p.Ships) world.NotifyAdded(s);
            News.Carried(system, p.News, leader.Name, jd);
        }

        /// <summary>Back where it belongs: takes up its old role, route and ports.</summary>
        void Home(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            ship.Voyage = null;
            ship.GateKm = null;
            ship.Role = v.HomeRole;
            ship.Route = v.HomeRoute;
            ship.RouteIndex = 0;
            ship.Ports = v.HomePorts ?? ship.Ports;
            system.World.Fleet.ScheduleNext(ship, jd);
            system.World.Log(jd, $"{ship.Name} is home from {Describe(v)}", WorldEventKind.Info, system.Catalog.Root.Index);
        }

        static string Describe(Voyage v) => v.Purpose switch
        {
            VoyagePurpose.Raid => "a raid beyond the gates",
            VoyagePurpose.Expedition => "an expedition beyond the gates",
            VoyagePurpose.Survey => "a survey beyond the gates",
            VoyagePurpose.Courier => "a mail run beyond the gates",
            _ => "a run beyond the gates",
        };

        /// <summary>At the voyage's far end: to work, by what it came for.</summary>
        void Arrive(StarSystem system, Ship ship, JumpGate cameIn, double jd)
        {
            var v = ship.Voyage;
            var world = system.World;
            ship.GateKm = null;
            Arrivals[v.Purpose] = (Arrivals.TryGetValue(v.Purpose, out int n) ? n : 0) + 1;
            switch (v.Purpose)
            {
                case VoyagePurpose.Trade:
                    ship.Ports = world.PortBodies();
                    v.StayUntilJd = jd + 25 + rng.NextDouble() * 20;
                    break;
                case VoyagePurpose.Courier:
                {
                    double fee = MailFeePerLy * v.Ly;
                    ship.Faction.Treasury += fee;
                    ship.Credits += fee;
                    if (world.Sol != null) world.Sol.Treasury -= fee;
                    ship.Cargo = null;
                    ship.Ports = world.PortBodies();
                    v.StayUntilJd = jd + 5 + rng.NextDouble() * 7;
                    break;
                }
                case VoyagePurpose.Survey:
                    ship.Ports = world.FreeShipPorts();
                    v.StayUntilJd = jd + 15 + rng.NextDouble() * 15;
                    break;
                case VoyagePurpose.Raid:
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = jd + 20 + rng.NextDouble() * 15;
                    // The gate's owner shuts it behind them, and sends a navy after them to where they came from.
                    if (cameIn.Owner != null && cameIn.Owner.IsMilitary)
                    {
                        Blockade(cameIn, jd, BlockadeDays, $"{ship.Faction.Name} raiders come through");
                        expeditionsWanted.Add((system, v.Home, ship.Faction, jd));
                    }
                    break;
                case VoyagePurpose.Expedition:
                    ship.Route = null;
                    ship.Ports = null;
                    v.StayUntilJd = jd + 20 + rng.NextDouble() * 10;
                    break;
            }
            ship.Role = v.Purpose == VoyagePurpose.Survey ? ShipRole.Free : v.HomeRole;
            if (ship.Role == ShipRole.Free || v.Purpose == VoyagePurpose.Raid || v.Purpose == VoyagePurpose.Expedition)
                ship.Ports ??= world.FreeShipPorts();
            world.Log(jd, $"{ship.Name} of {ship.Faction.Name} comes through the {cameIn.Partner.Name} from {v.Home.Name}", WorldEventKind.Info, system.Catalog.Root.Index);
            world.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>
        /// Shuts <paramref name="gate"/> to everyone but its owner for <paramref name="days"/> (or
        /// longer, if already shut longer).
        /// </summary>
        public void Blockade(JumpGate gate, double jd, double days, string why = null)
        {
            bool already = gate.Blockaded(jd);
            gate.BlockadeUntilJd = Math.Max(gate.BlockadeUntilJd, jd + days);
            if (!already && gate.Owner != null)
                gate.Home.World.Log(jd, $"{gate.Owner.Name} closes the {gate.Name}" + (why != null ? " after " + why : ""), WorldEventKind.Diplomacy, gate.Home.Catalog.Root.Index);
        }

        /// <summary>Opens a blockaded gate again.</summary>
        public void Lift(JumpGate gate, double jd)
        {
            if (!gate.Blockaded(jd)) return;
            gate.BlockadeUntilJd = jd;
            if (gate.Owner != null) gate.Home.World.Log(jd, $"{gate.Owner.Name} opens the {gate.Name} again", WorldEventKind.Diplomacy, gate.Home.Catalog.Root.Index);
        }

        /// <summary>The way is shut and stays shut: it gives up and goes about its business where it is.</summary>
        void GiveUp(StarSystem system, Ship ship, double jd)
        {
            var v = ship.Voyage;
            v.AtGate = false;
            v.WaitingSinceJd = double.NaN;
            ship.GateKm = null;
            if (system == v.Home)
            {
                CancelVoyage(system, ship, jd);
                return;
            }
            // Stranded abroad (or between): it stays a while and tries again for home.
            v.Destination = system;
            v.Route = new List<JumpGate>();
            v.Leg = 0;
            v.StayUntilJd = jd + 3;
            ship.Ports = ship.Role == ShipRole.Free || ship.Spec.IsMilitary || ship.Faction.IsPirate ? system.World.FreeShipPorts() : system.World.PortBodies();
            system.World.Fleet.ScheduleNext(ship, jd);
        }

        /// <summary>
        /// Calls a voyage off where the ship is (it was taken over by the pilot or by captors, or never
        /// got away); <paramref name="restore"/> gives it back its home role if it is at home.
        /// </summary>
        void CancelVoyage(StarSystem system, Ship ship, double jd, bool restore = true)
        {
            var v = ship.Voyage;
            ship.GateKm = null;
            if (v == null) return;
            v.AtGate = false;
            ship.Voyage = null;
            if (restore && system == v.Home)
            {
                ship.Role = v.HomeRole;
                ship.Route = v.HomeRoute;
                ship.Ports = v.HomePorts ?? ship.Ports;
            }
            else if (restore || ship.Ports == null) ship.Ports = system.World.FreeShipPorts();
            if (ship != system.World.PilotedShip && !ship.Destroyed) system.World.Fleet.ScheduleNext(ship, jd);
        }

        // ---------------------------------------------------------------- orders abroad

        /// <summary>World hook: where a raider group or an expedition abroad goes next (-1 for anything else).</summary>
        int Orders(StarSystem system, Ship ship, int here, double jd)
        {
            var v = ship.Voyage;
            if (v == null || ship.GateKm != null || system == v.Home) return -1;
            var economy = system.World.Economy;
            var war = system.World.War;
            if (v.Purpose == VoyagePurpose.Raid)
            {
                // The busiest port with guns it can face; it hangs about a port that is paying off.
                int best = -1;
                double bestScore = 0;
                foreach (var s in economy.Settlements)
                {
                    if (s.Kind == SettlementKind.Haven || !s.Discovered) continue;
                    double traffic = 1;
                    foreach (var t in system.World.Fleet.Ships)
                        if (!t.Destroyed && system.World.IsTrader(t) && (t.DestinationBody == s.Body || (t.Orbit != null && t.Orbit.Body == s.Body))) traffic++;
                    double defence = war.DefenceAt(s.Body, ship.Faction);
                    double score = traffic / (defence + 100) * (0.7 + rng.NextDouble() * 0.6);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s.Body;
                    }
                }
                return best >= 0 ? best : here;
            }
            if (v.Purpose == VoyagePurpose.Expedition)
            {
                // The quarry's haven, if it is known; else wherever its raiders are; else the busiest port.
                foreach (var s in economy.Settlements)
                    if (s.Kind == SettlementKind.Haven && !s.Fallen && !s.Hidden && (v.Quarry == null || s.Owner == v.Quarry || s.Owner.IsPirate))
                        return s.Body;
                foreach (var t in system.World.Fleet.Ships)
                    if (!t.Destroyed && !t.IsEscort && t.Faction.IsPirate && t.Orbit != null && t.Phase == ShipPhase.Parked) return t.Orbit.Body;
                var ports = system.World.PortBodies();
                return ports.Length > 0 ? ports[rng.Next(ports.Length)] : here;
            }
            return -1;
        }

        // ---------------------------------------------------------------- sending ships out

        void Daily(double jd)
        {
            var settled = new List<StarSystem>();
            foreach (var s in galaxy.Systems)
                if (s.Settled) settled.Add(s);

            foreach (var s in galaxy.Systems) SendHomeWhenDone(s, jd);
            foreach (var from in settled)
                foreach (var to in settled)
                {
                    if (from == to) continue;
                    DispatchTrade(from, to, jd);
                    DispatchCourier(from, to, jd);
                }
            foreach (var from in settled)
            {
                DispatchRaid(from, settled, jd);
                DispatchSurvey(from, jd);
            }
            for (int i = expeditionsWanted.Count - 1; i >= 0; i--)
            {
                var (from, to, quarry, asked) = expeditionsWanted[i];
                if (DispatchExpedition(from, to, quarry, jd) || jd - asked > 20) expeditionsWanted.RemoveAt(i);
            }
        }

        /// <summary>A ship that can be sent: its own master (not an escort, not the player's), idle in port, not fighting.</summary>
        static bool Idle(World world, Ship s) =>
            !s.Destroyed && !s.IsEscort && !s.Derelict && s.Battle == null && s != world.PilotedShip && s.Voyage == null && s.GateKm == null &&
            (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Landed) && s.Orbit != null;

        /// <summary>The body a ship in port is at (its ring's world when landed).</summary>
        static int PortOf(Ship s) => s.Phase == ShipPhase.Landed && s.Ring != null ? s.Ring.Body : s.Orbit.Body;

        /// <summary>How many of <paramref name="home"/>'s ships with <paramref name="purpose"/> are out (on the way, abroad or coming back).</summary>
        int Out(StarSystem home, VoyagePurpose purpose, StarSystem bound = null)
        {
            int n = 0;
            bool Counts(Voyage v) => v != null && v.Home == home && v.Purpose == purpose && (bound == null || v.Destination == bound || (v.Homeward && v.Route.Count > 0 && v.Route[0].Home == bound) || v.Destination == bound);
            foreach (var p in inGates)
                if (Counts(p.Leader.Voyage)) n++;
            foreach (var s in galaxy.Systems)
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && Counts(ship.Voyage)) n++;
            return n;
        }

        /// <summary>
        /// Sends <paramref name="ship"/> (a flagship or a ship on its own, in <paramref name="from"/>)
        /// to <paramref name="to"/> on <paramref name="purpose"/>, by the quickest way open to it.
        /// Returns the voyage, or null if there is no way through or the ship is not free to go.
        /// </summary>
        public Voyage Send(Ship ship, StarSystem from, StarSystem to, VoyagePurpose purpose, double jd)
        {
            if (from == to || !Idle(from.World, ship)) return null;
            var route = Network.Route(from, to, ship.Faction, jd);
            if (route == null || route.Count == 0) return null;
            return SetOut(from, to, ship, purpose, route, jd);
        }

        /// <summary>Starts <paramref name="ship"/> on a voyage from <paramref name="from"/> to <paramref name="to"/> by <paramref name="route"/>.</summary>
        Voyage SetOut(StarSystem from, StarSystem to, Ship ship, VoyagePurpose purpose, List<JumpGate> route, double jd)
        {
            var v = new Voyage
            {
                Purpose = purpose, Home = from, Destination = to, Route = route, SetOutJd = jd, Ly = GateNetwork.RouteLy(route), Faction = ship.Faction,
                HomeRole = ship.Role, HomeRoute = ship.Route, HomePorts = ship.Ports,
            };
            ship.Voyage = v;
            ship.GateKm = route[0].PositionKm;
            from.World.Fleet.ScheduleNext(ship, jd);
            return v;
        }

        /// <summary>Sends an idle freighter with the most profitable cargo for <paramref name="to"/>'s markets, if one pays.</summary>
        void DispatchTrade(StarSystem from, StarSystem to, double jd)
        {
            if (Out(from, VoyagePurpose.Trade, to) >= MaxTradersPerRoute) return;
            // Traders here know the far markets only by the last news from them (U.SPC-T100): none, no run.
            var news = News.Known(from, to);
            if (news == null) return;
            var world = from.World;
            Ship bestShip = null;
            Settlement bestFrom = null;
            Good bestGood = default;
            double bestTonnes = 0, bestProfit = 0;
            List<JumpGate> bestRoute = null;
            Settlement bestTo = null;
            double bestSale = 0;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Role != ShipRole.Trader || ship.Spec.Class != ShipClass.Freighter || ship.HasCargo) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                if (BestCargo(from, to, ship, PortOf(ship), route, jd, news.Markets, out var src, out var good, out double tonnes, out double profit, out var dst, out double sale)
                    && profit > bestProfit)
                {
                    bestTo = dst;
                    bestSale = sale;
                    bestProfit = profit;
                    bestShip = ship;
                    bestFrom = src;
                    bestGood = good;
                    bestTonnes = tonnes;
                    bestRoute = route;
                }
            }
            if (bestShip == null) return;
            world.Economy.LoadUnbooked(bestShip, bestFrom, bestGood, bestTonnes, to.Name);
            var voyage = SetOut(from, to, bestShip, VoyagePurpose.Trade, bestRoute, jd);
            if (voyage != null)
            {
                voyage.PlannedMarket = bestTo.Name;
                voyage.ExpectedSale = bestSale;
                voyage.PlannedOnNewsJd = news.AsOfJd;
            }
            world.Log(jd, $"{bestShip.Name} loads {Tonnes(bestTonnes)} {GoodInfo.Of(bestGood).Name} at {bestFrom.Name} for {bestTo.Name} at {to.Name}, by the {bestRoute[0].Name}, on news {LightSpeed.Describe(news.AgeDays(jd))} old", WorldEventKind.Info, bestFrom.Body);
        }

        static string Tonnes(double t) => t.ToString("N0", CultureInfo.InvariantCulture) + " t";

        /// <summary>
        /// The best good to carry from the ship's port to <paramref name="to"/>'s markets: bought here,
        /// marked up by the tolls for the route's light-years (<see cref="MarkupPerLy"/>) and the days
        /// on the way, against what the dearest market there would pay. True if one clears <see cref="MinMargin"/>.
        /// </summary>
        public static bool BestCargo(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route, double jd,
            out Settlement source, out Good good, out double tonnes, out double profit) =>
            BestCargo(from, to, ship, port, route, jd, to.World.Economy.Settlements, out source, out good, out tonnes, out profit, out _, out _);

        /// <summary>
        /// As <see cref="BestCargo(StarSystem, StarSystem, Ship, int, List{JumpGate}, double, out Settlement, out Good, out double, out double)"/>,
        /// but against <paramref name="markets"/>: the far system's boards as the news here has them (U.SPC-T100), not as
        /// they are. <paramref name="market"/> is the board the run is planned on and <paramref name="sale"/> what it says
        /// the cargo will fetch there, after port tax.
        /// </summary>
        public static bool BestCargo(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route, double jd, IEnumerable<Settlement> markets,
            out Settlement source, out Good good, out double tonnes, out double profit, out Settlement market, out double sale)
        {
            market = null;
            sale = 0;
            source = null;
            good = default;
            tonnes = 0;
            profit = 0;
            double ly = GateNetwork.RouteLy(route);
            double days = VoyageDays(from, to, ship, port, route);
            double markup = 1 + MarkupPerLy * ly;
            foreach (var src in from.World.Economy.At(port))
            {
                if (!from.World.Economy.CanTrade(ship, src)) continue;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    var gd = (Good)g;
                    if (!Economy.Carries(ship.Spec.Class, gd)) continue;
                    double available = src.Available(gd);
                    double minimum = Math.Min(ship.CapacityTonnes * 0.1, 200);
                    if (available < minimum) continue;
                    foreach (var dst in markets)
                    {
                        if (dst.Kind == SettlementKind.Haven || !to.World.Diplomacy.CanDockAt(ship.Faction, dst)) continue;
                        double t = Math.Min(Math.Min(ship.CapacityTonnes, available), dst.Absorb(gd));
                        if (t < minimum) continue;
                        double landed = src.PurchaseCost(gd, t) * (1 + Economy.PortTax) * markup;
                        double value = dst.SaleValue(gd, t) * (1 - Economy.PortTax);
                        double p = value - landed - days * RunningCostPerDay;
                        if (p > landed * MinMargin && p > profit)
                        {
                            profit = p;
                            market = dst;
                            sale = value;
                            source = src;
                            good = gd;
                            tonnes = t;
                        }
                    }
                }
            }
            return source != null;
        }

        /// <summary>A freighter's running costs a day (as at home).</summary>
        const double RunningCostPerDay = 1500;

        /// <summary>Rough door-to-door days: out to the gate, through the gates, and in from the far gate to a port.</summary>
        public static double VoyageDays(StarSystem from, StarSystem to, Ship ship, int port, List<JumpGate> route)
        {
            double accel = ship.Spec.AccelG * Astro.GravityKmPerDay2;
            double Burn(double km) => 2 * Math.Sqrt(Math.Max(km, 1) / accel);
            double outbound = Burn(from.Ephemeris.Helio(port).Length);
            // In from the far gate: to a typical port, about as far out as the one it left.
            double inbound = Burn(Math.Max(from.Ephemeris.Helio(port).Length * 0.5, 0.05 * Astro.AuKm));
            return outbound + GateNetwork.RouteDays(route) + inbound + 1;
        }

        /// <summary>A courier with the mail, every so often, when none is out on the run.</summary>
        void DispatchCourier(StarSystem from, StarSystem to, double jd)
        {
            if (Out(from, VoyagePurpose.Courier, to) > 0 || rng.NextDouble() > 0.2) return;
            var world = from.World;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Role != ShipRole.Courier || ship.HasCargo) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) return;
                SetOut(from, to, ship, VoyagePurpose.Courier, route, jd);
                ship.Cargo = "Interstellar mail and data cores for " + to.Name;
                world.Log(jd, $"{ship.Name} takes the mail for {to.Name} by the {route[0].Name}", WorldEventKind.Info, PortOf(ship));
                return;
            }
        }

        /// <summary>Now and then a raider group slips out for another settled system's lanes.</summary>
        void DispatchRaid(StarSystem from, List<StarSystem> settled, double jd)
        {
            var world = from.World;
            if (world.Pirates == null || Out(from, VoyagePurpose.Raid) > 0 || rng.NextDouble() > RaidChancePerDay) return;
            var targets = new List<StarSystem>();
            foreach (var s in settled)
                if (s != from) targets.Add(s);
            if (targets.Count == 0) return;
            var to = targets[rng.Next(targets.Count)];
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Faction != world.Pirates || ship.Phase != ShipPhase.Parked || ship.Combat.Damage > 0.2 || !ship.Combat.Armed) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                SetOut(from, to, ship, VoyagePurpose.Raid, route, jd);
                world.Log(jd, $"The {world.Pirates.Name} raiders under {ship.Name} slip away for {to.Name}", WorldEventKind.Info, -1);
                return;
            }
        }

        /// <summary>Now and then a yacht or survey ship sets off to look at an unsettled star, often several gates out.</summary>
        void DispatchSurvey(StarSystem from, double jd)
        {
            if (Out(from, VoyagePurpose.Survey) >= 2 || rng.NextDouble() > SurveyChancePerDay) return;
            var world = from.World;
            var wild = new List<StarSystem>();
            foreach (var s in galaxy.Systems)
                if (s != from && !s.Settled) wild.Add(s);
            if (wild.Count == 0) return;
            var to = wild[rng.Next(wild.Count)];
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Role != ShipRole.Free || ship.Spec.Class != ShipClass.Courier) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) return;
                SetOut(from, to, ship, VoyagePurpose.Survey, route, jd);
                ship.Cargo = "Survey instruments, bound for " + to.Name;
                world.Log(jd, $"{ship.Name} sets out to survey {to.Name}, {route.Count} gate{(route.Count == 1 ? "" : "s")} out", WorldEventKind.Info, PortOf(ship));
                return;
            }
        }

        /// <summary>A navy group sent after raiders to the system they came from; false if none could go yet.</summary>
        bool DispatchExpedition(StarSystem from, StarSystem to, Faction quarry, double jd)
        {
            var world = from.World;
            var navy = world.Sol;
            if (navy == null || !navy.IsMilitary || Out(from, VoyagePurpose.Expedition) > 0) return Out(from, VoyagePurpose.Expedition) > 0;
            foreach (var ship in world.Fleet.Ships)
            {
                if (!Idle(world, ship) || ship.Faction != navy || !ship.Spec.IsMilitary || ship.Phase != ShipPhase.Parked) continue;
                if (world.War.IsHomeGuard(ship) || ship.Combat.Damage > 0.2 || !ship.Combat.Armed) continue;
                var route = Network.Route(from, to, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                var v = SetOut(from, to, ship, VoyagePurpose.Expedition, route, jd);
                v.Quarry = quarry;
                world.Log(jd, $"{ship.Name} leads an expedition through the {route[0].Name} after the {quarry.Name} at {to.Name}", WorldEventKind.Info, PortOf(ship));
                return true;
            }
            return false;
        }

        /// <summary>Ships abroad whose stay is over turn for home, a freighter with a backhaul if one pays.</summary>
        void SendHomeWhenDone(StarSystem system, double jd)
        {
            var world = system.World;
            foreach (var ship in world.Fleet.Ships)
            {
                var v = ship.Voyage;
                if (v == null || ship.Destroyed || ship.IsEscort || v.Homeward || v.AtGate || ship.GateKm != null || system != v.Destination) continue;
                if (ship == world.PilotedShip || ship.Faction != v.Faction)
                {
                    CancelVoyage(system, ship, jd, ship.Faction == v.Faction);
                    continue;
                }
                bool hurt = (ship.Spec.IsMilitary || ship.Faction.IsPirate) && ship.Combat.Damage > 0.45;
                if (jd < v.StayUntilJd && !hurt) continue;
                if (ship.Battle != null || (ship.Phase != ShipPhase.Parked && ship.Phase != ShipPhase.Landed)) continue;
                // A freighter sells what it carries before it goes (unless it has been at it far too long).
                if (ship.HasCargo && v.Purpose == VoyagePurpose.Trade && jd < v.StayUntilJd + 30) continue;
                var route = Network.Route(system, v.Home, ship.Faction, jd);
                if (route == null || route.Count == 0) continue;
                // It knows home's markets only by the news that has reached it here (U.SPC-T100).
                var homeNews = News.Known(system, v.Home);
                if (v.Purpose == VoyagePurpose.Trade && !ship.HasCargo && homeNews != null &&
                    BestCargo(system, v.Home, ship, PortOf(ship), route, jd, homeNews.Markets, out var src, out var good, out double tonnes, out _, out _, out _))
                {
                    world.Economy.LoadUnbooked(ship, src, good, tonnes, v.Home.Name);
                    world.Log(jd, $"{ship.Name} loads {Tonnes(tonnes)} {GoodInfo.Of(good).Name} at {src.Name} to take home to {v.Home.Name}", WorldEventKind.Info, src.Body);
                }
                v.Destination = v.Home;
                v.Route = route;
                v.Leg = 0;
                ship.GateKm = route[0].PositionKm;
                world.Fleet.ScheduleNext(ship, jd);
            }
        }
    }
}
