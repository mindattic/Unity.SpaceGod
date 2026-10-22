using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// A lived-in system (U.SPC-T375): besides the navies' patrol wings, each navy runs escorted supply convoys round its
    /// ports, and every inhabited port has its ordinary traffic -- orbital buses on a line, taxis on fares, and people's
    /// own 1-, 4- and 6-seat craft -- flying short hops about its own planet's neighbourhood, standing clear of any fight.
    /// </summary>
    [TestFixture]
    public class LocalTrafficTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            w.War.Battles.Clear();
            return w;
        }

        static List<Ship> Commuters(World w) => w.Fleet.Ships.Where(s => !s.Destroyed && s.Role == ShipRole.Commuter).ToList();

        static List<Ship> Convoys(World w) => w.Fleet.Ships.Where(s => !s.Destroyed && s.Convoy && !s.IsEscort).ToList();

        [Test]
        public void SolHasBusesTaxisAndPeoplesOwnOneFourAndSixSeatCraft()
        {
            var w = CreateWorld();
            var traffic = Commuters(w);
            Assert.That(traffic.Count, Is.GreaterThanOrEqualTo(24), "Sol's ports fly too little ordinary traffic to read as lived-in");
            Assert.That(traffic.Count, Is.LessThanOrEqualTo(LocalTraffic.MaxCraftPerSystem));
            Assert.That(traffic.Count(s => s.Civil == CivilCraft.Bus), Is.GreaterThanOrEqualTo(3), "too few buses");
            Assert.That(traffic.Count(s => s.Civil == CivilCraft.Taxi), Is.GreaterThanOrEqualTo(3), "too few taxis");
            foreach (var kind in new[] { CivilCraft.Skiff, CivilCraft.Runabout, CivilCraft.Tourer })
                Assert.That(traffic.Any(s => s.Civil == kind), Is.True, $"nobody flies a {LocalTraffic.DesignOf(kind).DesignName}");
            Assert.That(new[] { CivilCraft.Skiff, CivilCraft.Runabout, CivilCraft.Tourer }.Select(LocalTraffic.Seats), Is.EqualTo(new[] { 1, 4, 6 }));
            foreach (var s in traffic)
            {
                Assert.That(s.Civil, Is.Not.EqualTo(CivilCraft.None), $"{s.Name} is local traffic of no kind");
                Assert.That(s.Combat.Armed, Is.False, $"{s.Name} is armed");
                Assert.That(s.Spec.IsMilitary || s.Faction.IsPirate, Is.False, $"{s.Name} is no civilian");
                Assert.That(s.Combat.Spec, Is.SameAs(LocalTraffic.DesignOf(s.Civil)), $"{s.Name} is not built to its kind's design");
                Assert.That(w.IsTrader(s), Is.False, $"{s.Name} trades");
                Assert.That(w.BurnsPropellant(s), Is.False, $"{s.Name} buys its fuel on the markets");
                if (s.Civil == CivilCraft.Bus) Assert.That(s.Route != null && s.Route.Length >= 2, Is.True, $"{s.Name} has no line to run");
            }
        }

        [Test]
        public void LocalTrafficKeepsToItsOwnPlanetsNeighbourhood()
        {
            var w = CreateWorld();
            var traffic = Commuters(w);
            var home = traffic.ToDictionary(s => s, s => LocalTraffic.NeighbourhoodOf(w.Catalog, s.Ports[0]));
            foreach (var s in traffic)
                Assert.That(s.Ports.All(p => LocalTraffic.NeighbourhoodOf(w.Catalog, p) == home[s]), Is.True, $"{s.Name} may fly out of its neighbourhood");
            var trips = traffic.ToDictionary(s => s, s => s.TripsCompleted);
            var visited = new HashSet<int>();
            for (int day = 1; day <= 10; day++)
            {
                w.Update(StartJd + day);
                foreach (var s in traffic)
                {
                    Assert.That(s.Voyage, Is.Null, $"{s.Name} was sent through a gate");
                    Assert.That(s.Journey == null || s.JourneyIndex >= s.Journey.Count, Is.True, $"{s.Name} rides a hyperlane");
                    Assert.That(s.Battle, Is.Null, $"{s.Name} was swept into a fight");
                    if (s.Orbit == null) continue;
                    Assert.That(LocalTraffic.NeighbourhoodOf(w.Catalog, s.Orbit.Body), Is.EqualTo(home[s]), $"{s.Name} strayed to {w.Catalog[s.Orbit.Body].Name}");
                    if (s.Civil == CivilCraft.Bus) Assert.That(s.Route, Does.Contain(s.Orbit.Body), $"{s.Name} left its line");
                    visited.Add(s.Orbit.Body);
                }
            }
            Assert.That(traffic.Count(s => !s.Destroyed && s.TripsCompleted > trips[s]), Is.GreaterThan(traffic.Count / 2), "the local traffic sits idle");
            Assert.That(visited.Count, Is.GreaterThanOrEqualTo(6), "the local traffic keeps to a handful of places");
        }

        [Test]
        public void ABusRunsItsLineStopByStop()
        {
            var w = CreateWorld();
            var buses = Commuters(w).Where(s => s.Civil == CivilCraft.Bus).ToList();
            var calls = buses.ToDictionary(s => s, s => new List<int>());
            for (double t = 0.05; t <= 6; t += 0.05)
            {
                w.Update(StartJd + t);
                foreach (var bus in buses)
                {
                    if (bus.Phase != ShipPhase.Parked || bus.Orbit == null) continue;
                    var seen = calls[bus];
                    if (seen.Count == 0 || seen[seen.Count - 1] != bus.Orbit.Body) seen.Add(bus.Orbit.Body);
                }
            }
            foreach (var bus in buses)
            {
                var seen = calls[bus];
                Assert.That(seen.All(bus.Route.Contains), Is.True, $"{bus.Name} called off its line");
                Assert.That(seen.Distinct().Count(), Is.EqualTo(bus.Route.Distinct().Count()), $"{bus.Name} did not call at every stop on its line in six days");
                // Stop by stop in the line's order.
                for (int i = 1; i < seen.Count; i++)
                {
                    int at = Array.IndexOf(bus.Route, seen[i - 1]);
                    Assert.That(seen[i], Is.EqualTo(bus.Route[(at + 1) % bus.Route.Length]), $"{bus.Name} skipped a stop");
                }
            }
        }

        [Test]
        public void LocalTrafficStandsClearOfAFight()
        {
            var w = CreateWorld();
            var craft = Commuters(w).First(s => s.Civil == CivilCraft.Taxi);
            int body = craft.Orbit.Body;
            var raider = w.Fleet.AddShip("Gutter Knife", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            raider.Route = new[] { body };
            w.Fleet.Station(raider, body, StartJd);
            w.War.Orders[raider] = new GroupOrder { Kind = OrderKind.Raid, Body = body };
            var b = w.War.TryStartBattle(body, w.Pirates, StartJd);
            Assert.That(b, Is.Not.Null, "the raider found nothing to fight");
            Assert.That(craft.Battle, Is.Null, "a taxi was swept into the fight");
            // And it will not fly into one.
            Assert.That(w.Fleet.Shuns(craft, body), Is.True, "local traffic would fly into a fight");
        }

        [Test]
        public void EachNavyRunsEscortedSupplyConvoysRoundItsOwnPorts()
        {
            var w = CreateWorld();
            var convoys = Convoys(w);
            foreach (var f in w.Fleet.Factions.Where(f => f.IsMilitary && !f.IsPirate))
            {
                var ports = PatrolWings.Ports(w, f);
                var own = convoys.Where(s => s.Faction == f).ToList();
                Assert.That(own.Count, Is.EqualTo(PatrolWings.ConvoysFor(ports.Count)), $"{f.Name} runs the wrong number of convoys");
                foreach (var c in own)
                {
                    Assert.That(c.Spec.Class, Is.EqualTo(ShipClass.Freighter));
                    Assert.That(c.PatrolWing, Is.True);
                    Assert.That(c.Route.Length, Is.GreaterThanOrEqualTo(2));
                    Assert.That(c.Route.All(ports.Contains), Is.True, $"{c.Name}'s run strays off {f.Name}'s ports");
                    var escorts = w.Fleet.Ships.Where(s => !s.Destroyed && s.Leader == c).ToList();
                    Assert.That(escorts.Count, Is.GreaterThanOrEqualTo(2), $"{c.Name} sails without its escort");
                    Assert.That(escorts.All(e => e.Spec.IsMilitary && e.PatrolWing), Is.True);
                }
            }
            Assert.That(convoys.Count, Is.GreaterThanOrEqualTo(6), "Sol's navies run too few supply convoys");
            // Wings and convoys together: dozens of navy hulls flying commanded routes between the worlds.
            Assert.That(w.Fleet.Ships.Count(s => !s.Destroyed && s.PatrolWing), Is.GreaterThanOrEqualTo(60));
            Assert.That(w.Fleet.Ships.Count(s => !s.Destroyed && s.Spec.Class == ShipClass.Freighter), Is.GreaterThanOrEqualTo(30));
        }

        [Test]
        public void AConvoyGoesOnRoundItsRunRatherThanTurnAsideToFight()
        {
            var w = CreateWorld();
            var convoy = Convoys(w).First(s => s.Faction == w.Sol && s.Route.Length >= 2);
            int here = convoy.Route[0], threatened = convoy.Route[1];
            var enemy = w.Fleet.AddShip("Interloper", ShipClass.Destroyer, w.Reach, ShipRole.Patrol);
            enemy.Route = new[] { threatened };
            w.Fleet.Station(enemy, threatened, StartJd);
            int next = w.War.ChooseNext(convoy, here, StartJd);
            var o = w.War.OrderOf(convoy);
            Assert.That(o.Kind, Is.EqualTo(OrderKind.Patrol), "a supply convoy was sent to fight");
            Assert.That(next, Is.EqualTo(PatrolWings.NextOnBeat(convoy, here)));
            // Hurt, it puts in at a yard.
            foreach (var s in w.Fleet.Ships.Where(s => s == convoy || s.Leader == convoy)) s.Combat.Hull = s.Combat.Spec.Hull * 0.2;
            w.War.ChooseNext(convoy, here, StartJd);
            Assert.That(w.War.OrderOf(convoy).Kind, Is.EqualTo(OrderKind.Repair));
        }

        [Test]
        public void TrafficScalesWithAPortsPeople()
        {
            Assert.That(LocalTraffic.BusesFor(5e4), Is.EqualTo(0));
            Assert.That(LocalTraffic.BusesFor(1e6), Is.EqualTo(1));
            Assert.That(LocalTraffic.BusesFor(1e9), Is.EqualTo(2));
            Assert.That(LocalTraffic.TaxisFor(5e4), Is.EqualTo(0));
            Assert.That(LocalTraffic.TaxisFor(1e10), Is.EqualTo(3));
            Assert.That(LocalTraffic.PersonalFor(5e3), Is.EqualTo(0));
            Assert.That(LocalTraffic.PersonalFor(1e4), Is.EqualTo(1));
            Assert.That(LocalTraffic.PersonalFor(1e6), Is.EqualTo(2));
            Assert.That(LocalTraffic.PersonalFor(1e12), Is.EqualTo(4));
            Assert.That(PatrolWings.ConvoysFor(1), Is.EqualTo(0));
            Assert.That(PatrolWings.ConvoysFor(4), Is.EqualTo(2));
            Assert.That(PatrolWings.ConvoysFor(20), Is.EqualTo(PatrolWings.MaxConvoysPerFaction));
        }
    }
}
