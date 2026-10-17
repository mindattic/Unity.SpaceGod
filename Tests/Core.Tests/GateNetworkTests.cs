using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class GateNetworkTests
    {
        const double StartJd = 2461324.0;

        static Galaxy Create() => Galaxy.CreateDefault(StartJd);

        /// <summary>Runs the whole galaxy forward in half-day steps until <paramref name="done"/> or <paramref name="days"/> have gone by; returns the date reached.</summary>
        static double RunUntil(Galaxy g, double fromJd, double days, Func<bool> done)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done())
            {
                jd += 0.5;
                g.Update(jd);
            }
            return jd;
        }

        [Test]
        public void TheLatticeIsSparseConnectedAndLinksEachSystemToItsNearest()
        {
            var g = Create();
            var net = g.Network;
            int n = g.Systems.Count;
            Assert.That(n, Is.EqualTo(14));
            // Sparse: far fewer links than every pair, and nobody is a hub to everyone.
            Assert.That(net.Links.Count, Is.LessThan(n * (n - 1) / 2 / 3));
            Assert.That(net.Links.Count, Is.GreaterThanOrEqualTo(n - 1));
            foreach (var s in g.Systems)
            {
                Assert.That(s.Gates.Count, Is.InRange(1, 6), s.Name);
                // Each system's two nearest are a gate away.
                var nearest = g.Systems.Where(o => o != s).OrderBy(o => Galaxy.DistanceLy(s, o)).Take(GateNetwork.Neighbours);
                foreach (var o in nearest) Assert.That(s.GateTo(o), Is.Not.Null, s.Name + " to " + o.Name);
            }
            // Connected: every system can reach every other.
            foreach (var a in g.Systems)
                foreach (var b in g.Systems)
                    Assert.That(net.Route(a, b), Is.Not.Null, a.Name + " to " + b.Name);
            // Sol and Alpha Centauri, each other's nearest, keep their direct pair and its names.
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            Assert.That(sol.GateTo(ac).Name, Is.EqualTo("Centauri Gate"));
            Assert.That(ac.GateTo(sol).Name, Is.EqualTo("Sol Gate"));
            // Some systems are only reachable by way of others.
            Assert.That(g.Systems.Max(s => net.Route(sol, s).Count), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void EveryGateIsPairedAndFacesTheStarItLeadsTo()
        {
            var g = Create();
            foreach (var s in g.Systems)
                foreach (var gate in s.Gates)
                {
                    Assert.That(gate.Home, Is.SameAs(s));
                    Assert.That(gate.Partner.Partner, Is.SameAs(gate));
                    Assert.That(gate.Link.GateIn(s), Is.SameAs(gate));
                    Assert.That(gate.PositionKm.Length, Is.EqualTo(Galaxy.GateDistanceKm(s)).Within(1e-6));
                    Assert.That(DVec3.Dot(gate.PositionKm.Normalized, Galaxy.Direction(s, gate.Destination)), Is.GreaterThan(0.9999), gate.Name);
                    // Owned by the system's leading power.
                    Assert.That(gate.Owner, Is.SameAs(s.World.Sol));
                }
            // No two gates of a system share a spot: a pilot always knows which one it is at.
            foreach (var s in g.Systems)
                foreach (var a in s.Gates)
                    foreach (var b in s.Gates)
                        if (a != b) Assert.That((a.PositionKm - b.PositionKm).Length, Is.GreaterThan(JumpGate.ActivationRangeKm * 2), s.Name);
        }

        [Test]
        public void CrossingTakesLongerTheFartherTheLink()
        {
            Assert.That(GateNetwork.TransitDays(4.34), Is.EqualTo(0.25 + 4.34 / 12).Within(1e-9));
            Assert.That(GateNetwork.TransitDays(10), Is.GreaterThan(GateNetwork.TransitDays(4)));
            var g = Create();
            var sol = g.Find("Sol System");
            var tau = g.Find("Tau Ceti");
            var route = g.Network.Route(sol, tau);
            double days = GateNetwork.RouteDays(route);
            Assert.That(days, Is.EqualTo(route.Sum(r => r.Link.TransitDays) + (route.Count - 1) * GateNetwork.CrossingDays).Within(1e-9));
            // Each step of the route starts where the last one came out.
            var at = sol;
            foreach (var gate in route)
            {
                Assert.That(gate.Home, Is.SameAs(at));
                at = gate.Destination;
            }
            Assert.That(at, Is.SameAs(tau));
        }

        [Test]
        public void RoutesGoRoundABlockadeAndNeverThroughAnEnemysGate()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var w = sol.World;
            var gate = sol.GateTo(ac);
            Assert.That(g.Network.Route(sol, ac, w.Independents, StartJd).Single(), Is.SameAs(gate));

            g.Traffic.Blockade(gate, StartJd, 10, "a test");
            Assert.That(gate.Blockaded(StartJd + 5), Is.True);
            Assert.That(gate.Admits(w.Sol, StartJd + 5), Is.True, "the owner's own ships still pass");
            Assert.That(gate.Admits(w.Independents, StartJd + 5), Is.False);
            var detour = g.Network.Route(sol, ac, w.Independents, StartJd + 5);
            Assert.That(detour, Is.Not.Null);
            Assert.That(detour.Count, Is.GreaterThan(1), "round by another system");
            Assert.That(detour, Does.Not.Contain(gate));
            Assert.That(g.Network.Route(sol, ac, w.Sol, StartJd + 5).Single(), Is.SameAs(gate));
            Assert.That(w.Events.Any(e => e.Text.Contains("closes the Centauri Gate")), Is.True);
            // Lifts on its own.
            Assert.That(g.Network.Route(sol, ac, w.Independents, StartJd + 11).Single(), Is.SameAs(gate));

            // Every gate out of Sol belongs to the Federation, which is at war with the Reach: no way out for the Reach.
            Assert.That(w.Diplomacy.Get(w.Sol, w.Reach), Is.EqualTo(Relation.War));
            Assert.That(g.Network.Route(sol, ac, w.Reach, StartJd), Is.Null);
        }

        [Test]
        public void AFreighterCarriesGoodsAcrossAndSellsThemInTheOtherSystemsMarkets()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            double jd = RunUntil(g, StartJd, 150, () => g.Traffic.Arrivals.TryGetValue(VoyagePurpose.Trade, out int n) && n > 0);
            Assert.That(g.Traffic.Arrivals.GetValueOrDefault(VoyagePurpose.Trade), Is.GreaterThan(0), "no freighter crossed with goods in 150 days");
            Assert.That(g.Traffic.TollsPaid, Is.GreaterThan(0));

            // Find one that has come through with its cargo, and follow it to the sale.
            Ship trader = null;
            StarSystem there = null;
            foreach (var s in new[] { sol, ac })
                foreach (var ship in s.World.Fleet.Ships)
                    if (!ship.Destroyed && ship.Voyage != null && ship.Voyage.Purpose == VoyagePurpose.Trade && ship.Voyage.Destination == s && ship.HasCargo)
                    {
                        trader = ship;
                        there = s;
                    }
            Assume.That(trader, Is.Not.Null, "the arrival had already sold");
            var v = trader.Voyage;
            Assert.That(v.Home, Is.Not.SameAs(there));
            Assert.That(v.Tolls, Is.GreaterThan(0));
            Assert.That(trader.Faction, Is.SameAs(v.Faction));
            Assert.That(there.World.Fleet.Factions, Does.Not.Contain(trader.Faction), "it keeps its own system's colours");
            double credits = trader.Credits;
            // Watched at the market rather than by an empty hold: a trader that sells may load its next cargo in the same step.
            bool sold = false;
            there.World.Economy.Sold += (ship, port, good, tonnes) => sold |= ship == trader;
            RunUntil(g, jd, 60, () => trader.Destroyed || sold);
            Assume.That(trader.Destroyed, Is.False, "lost on the way to market");
            Assert.That(sold, Is.True, "never sold its cargo");
            Assert.That(trader.Credits, Is.Not.EqualTo(credits), "the sale was booked to the ship");
            Assert.That(trader.Cargo == null || !trader.Cargo.Contains(" for " + there.Name), Is.True);
        }

        [Test]
        public void TheBestCargoPaysOnlyWhenThePriceGapBeatsTheDistanceMarkup()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var ship = sol.World.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Freighter && !s.Destroyed);
            var earth = sol.World.Economy.Settlements.First(s => s.Name == "Earth Orbital");
            var route = g.Network.Route(sol, ac, ship.Faction, StartJd);

            // Glut every market at Earth and starve every one at Centauri: something pays.
            foreach (var s in ac.World.Economy.Settlements) Array.Clear(s.Stock, 0, s.Stock.Length);
            for (int i = 0; i < GoodInfo.Count; i++) earth.Stock[i] = earth.Capacity((Good)i);
            Assert.That(InterstellarTraffic.BestCargo(sol, ac, ship, earth.Body, route, StartJd, out var src, out var good, out double t, out double profit), Is.True);
            Assert.That(src, Is.SameAs(earth));
            Assert.That(profit, Is.GreaterThan(0));

            // The same gap over a much longer road earns less: the markup is per light-year.
            var tau = g.Find("Tau Ceti");
            var longRoute = g.Network.Route(sol, tau);
            Assert.That(GateNetwork.RouteLy(longRoute), Is.GreaterThan(GateNetwork.RouteLy(route)));
            double markupNear = 1 + InterstellarTraffic.MarkupPerLy * GateNetwork.RouteLy(route);
            double markupFar = 1 + InterstellarTraffic.MarkupPerLy * GateNetwork.RouteLy(longRoute);
            Assert.That(markupFar, Is.GreaterThan(markupNear));

            // With the two markets level, nothing is worth the trip.
            foreach (var s in ac.World.Economy.Settlements)
                for (int i = 0; i < GoodInfo.Count; i++) s.Stock[i] = s.Capacity((Good)i);
            for (int i = 0; i < GoodInfo.Count; i++) earth.Stock[i] = earth.Target((Good)i) * 0.5;
            Assert.That(InterstellarTraffic.BestCargo(sol, ac, ship, earth.Body, route, StartJd, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void ASurveyShipCrossesSeveralGatesAndComesHome()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var tau = g.Find("Tau Ceti");
            // One well away from any fighting (a courier caught in a raid on a pirate haven never gets to leave).
            var ship = sol.World.Fleet.Ships.First(s => s.Role == ShipRole.Free && s.Spec.Class == ShipClass.Courier && !s.Destroyed
                && s.Battle == null && sol.World.War.Danger(s.Orbit.Body) == 0);
            string name = ship.Name;
            // Wait for it to be in port, then send it.
            double jd = RunUntil(g, StartJd, 20, () => ship.Phase == ShipPhase.Parked && ship.Voyage == null && ship.Battle == null);
            var v = g.Traffic.Send(ship, sol, tau, VoyagePurpose.Survey, jd);
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Route.Count, Is.GreaterThanOrEqualTo(2));
            var waypoints = v.Route.Skip(1).Select(r => r.Home).ToList();

            Ship At(StarSystem s) => s.World.Fleet.Ships.FirstOrDefault(x => x.Name == name && !x.Destroyed);
            jd = RunUntil(g, jd, 120, () => At(tau) != null);
            var there = At(tau);
            Assert.That(there, Is.Not.Null, "never reached Tau Ceti");
            Assert.That(ship.Destroyed && ship.GateDeparted, Is.True, "the hull that left Sol is retired there");
            foreach (var w in waypoints)
                Assert.That(w.World.Fleet.Ships.Any(x => x.Name == name && x.GateDeparted), Is.True, "passed through " + w.Name);
            Assert.That(there.Voyage.Purpose, Is.EqualTo(VoyagePurpose.Survey));
            Assert.That(there.Role, Is.EqualTo(ShipRole.Free));
            Assert.That(g.Traffic.Arrivals[VoyagePurpose.Survey], Is.GreaterThanOrEqualTo(1));

            jd = RunUntil(g, jd, 200, () => At(sol) != null);
            var back = At(sol);
            Assert.That(back, Is.Not.Null, "never came home");
            Assert.That(back.Voyage, Is.Null);
            Assert.That(back.Role, Is.EqualTo(ShipRole.Free));
            Assert.That(back.Ports, Is.Not.Empty);
        }

        [Test]
        public void RaidersThroughAGateGetItShutBehindThemAndANavySentAfterThem()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var raider = sol.World.Fleet.Ships.First(s => s.Faction == sol.World.Pirates && !s.IsEscort && !s.Destroyed);
            string name = raider.Name;
            int escorts = sol.World.Fleet.Ships.Count(s => s.Leader == raider && !s.Destroyed);
            double jd = RunUntil(g, StartJd, 30, () => raider.Phase == ShipPhase.Parked && raider.Battle == null);
            var v = g.Traffic.Send(raider, sol, ac, VoyagePurpose.Raid, jd);
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Route.Single(), Is.SameAs(sol.GateTo(ac)), "raiders slip through an open gate");

            var solGate = ac.GateTo(sol);
            jd = RunUntil(g, jd, 120, () => solGate.Blockaded(jd));
            Assert.That(solGate.Blockaded(jd), Is.True, "the Accord never shut the gate behind the raiders");
            var arrived = ac.World.Fleet.Ships.First(s => s.Name == name && !s.Destroyed);
            Assert.That(ac.World.Fleet.Ships.Count(s => s.Leader == arrived && !s.Destroyed), Is.EqualTo(escorts), "the whole band came through");
            Assert.That(arrived.Voyage.Purpose, Is.EqualTo(VoyagePurpose.Raid));
            // Shut to all but the Accord: a Combine freighter bound for Sol must go round.
            var combine = ac.World.Belt;
            var round = g.Network.Route(ac, sol, combine, jd);
            Assert.That(round, Is.Not.Null);
            Assert.That(round, Does.Not.Contain(solGate));

            // An Accord group goes after them to Sol.
            jd = RunUntil(g, jd, 25, () => ac.World.Fleet.Ships.Any(s => s.Voyage?.Purpose == VoyagePurpose.Expedition) ||
                                           g.Traffic.InGates.Any(p => p.Leader.Voyage.Purpose == VoyagePurpose.Expedition));
            var expedition = ac.World.Fleet.Ships.FirstOrDefault(s => s.Voyage?.Purpose == VoyagePurpose.Expedition)
                             ?? g.Traffic.InGates.Select(p => p.Leader).FirstOrDefault(s => s.Voyage.Purpose == VoyagePurpose.Expedition);
            Assert.That(expedition, Is.Not.Null, "no expedition set out");
            Assert.That(expedition.Faction, Is.SameAs(ac.World.Sol));
            Assert.That(expedition.Voyage.Destination, Is.SameAs(sol));
            Assert.That(expedition.Voyage.Quarry, Is.SameAs(sol.World.Pirates));
            Assert.That(ac.World.War.IsHomeGuard(expedition), Is.False);
        }

        [Test]
        public void AShipHeadingForAGateThatShutsTurnsForAnother()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var ship = sol.World.Fleet.Ships.First(s => s.Role == ShipRole.Courier && !s.Destroyed && s.Faction == sol.World.Independents);
            double jd = RunUntil(g, StartJd, 20, () => ship.Phase == ShipPhase.Parked && ship.Battle == null);
            var v = g.Traffic.Send(ship, sol, ac, VoyagePurpose.Courier, jd);
            Assert.That(v, Is.Not.Null);
            var direct = sol.GateTo(ac);
            Assert.That(v.Route.Single(), Is.SameAs(direct));
            g.Traffic.Blockade(direct, jd, 60);

            jd = RunUntil(g, jd, 60, () => ship.Destroyed || v.NextGate != direct);
            Assert.That(ship.Destroyed && !ship.GateDeparted, Is.False, "lost before it got anywhere");
            Assert.That(v.Route, Does.Not.Contain(direct), "it should have gone round by another gate");
            Assert.That(v.Route.Last().Destination, Is.SameAs(ac));
            Assert.That(sol.World.Events.Any(e => e.Text.Contains("turns for the")), Is.True);

            Ship There() => ac.World.Fleet.Ships.FirstOrDefault(x => x.Name == ship.Name && !x.Destroyed);
            RunUntil(g, jd, 80, () => There() != null);
            Assert.That(There(), Is.Not.Null, "never got to Alpha Centauri the long way");
            Assert.That(g.Traffic.Arrivals[VoyagePurpose.Courier], Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AYearOfTrafficLeavesEveryShipSomewhereReal()
        {
            var g = Create();
            double jd = RunUntil(g, StartJd, 365, () => false);
            Assert.That(g.Traffic.Crossings, Is.GreaterThan(10));
            foreach (var s in g.Systems)
                foreach (var ship in s.World.Fleet.Ships.Where(x => !x.Destroyed))
                {
                    var p = s.World.Fleet.StateOf(ship, jd).HelioKm;
                    Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z), Is.False, ship.Name + " in " + s.Name);
                    Assert.That(ship.Voyage == null || ship.Voyage.Faction == ship.Faction, Is.True, ship.Name);
                }
            // Nobody is left stuck at a gate for good.
            foreach (var s in g.Systems)
                foreach (var ship in s.World.Fleet.Ships.Where(x => !x.Destroyed && x.Voyage != null && x.Voyage.AtGate))
                    Assert.That(jd - ship.Voyage.AtGateJd, Is.LessThan(InterstellarTraffic.MaxWaitDays + 2), ship.Name);
        }
    }
}
