using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class TravelCourseTests
    {
        static Galaxy Create() => Galaxy.CreateDefault(2461324.0);

        /// <summary>The farthest pair of systems by gates, so the course has several legs.</summary>
        static (StarSystem from, StarSystem to) FarPair(Galaxy g)
        {
            var from = g.Find("Sol System");
            var to = g.Systems.OrderByDescending(s => g.Network.Route(from, s)?.Count ?? 0).First();
            return (from, to);
        }

        [Test]
        public void ACourseIsOneGatePerLegEachStartingWhereTheLastEnded()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            Assert.That(course.Legs, Is.GreaterThanOrEqualTo(3));
            Assert.That(course.Gates[0].Home, Is.SameAs(from));
            for (int i = 1; i < course.Legs; i++) Assert.That(course.Gates[i].Home, Is.SameAs(course.Gates[i - 1].Partner.Home));
            Assert.That(course.Gates[course.Legs - 1].Partner.Home, Is.SameAs(to));
        }

        [Test]
        public void PassingEachGateInOrderWalksTheCourseToItsEnd()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            var gates = course.Gates.ToList();
            for (int i = 0; i < gates.Count; i++)
            {
                Assert.That(course.Next, Is.SameAs(gates[i]));
                Assert.That(course.Passed(gates[i], g.Network, null, double.NegativeInfinity), Is.EqualTo(i < gates.Count - 1));
            }
            Assert.That(course.Done, Is.True);
        }

        [Test]
        public void AnotherGateReplansFromWhereTheShipComesOut()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            var wrong = from.Gates.First(x => x != course.Next);
            Assert.That(course.Passed(wrong, g.Network, null, double.NegativeInfinity), Is.True);
            Assert.That(course.Next.Home, Is.SameAs(wrong.Partner.Home));
        }

        [Test]
        public void TheViewTakesTheSameGatesOneAtATime()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            Assert.That(GateJump.TryPlan(g.Network, from, to, out var route), Is.True);
            Assert.That(route.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(route[0].Home, Is.SameAs(from));
            Assert.That(route.Last().Partner.Home, Is.SameAs(to));
        }
    
        const double StartJd = 2461324.0;

        static double RunUntil(Galaxy g, double fromJd, double days, System.Func<bool> done)
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
        public void AHullOnAPlottedCourseTakesEveryGateAndCrossesEverySystemOnTheWay()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var tau = g.Find("Tau Ceti");
            var ship = sol.World.Fleet.Ships.First(s => s.Role == ShipRole.Free && s.Spec.Class == ShipClass.Courier && !s.Destroyed
                && s.Battle == null && sol.World.War.Danger(s.Orbit.Body) == 0);
            string name = ship.Name;
            double jd = RunUntil(g, StartJd, 20, () => ship.Phase == ShipPhase.Parked && ship.Voyage == null && ship.Battle == null && !ship.OutOfPropellant);
            var v = g.Traffic.SendPlotted(ship, sol, tau, jd);
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Purpose, Is.EqualTo(VoyagePurpose.Plotted));
            Assert.That(v.Route.Count, Is.GreaterThanOrEqualTo(2), "the trip is not several gates");
            var waypoints = v.Route.Skip(1).Select(r => r.Home).ToList();
            var gates = v.Route.ToList();

            // Count the spurs (the hyperlanes' way to a gate) the hull rides: across each system it takes a lane to the gate where one runs.
            int spurRides = 0;
            foreach (var sys in g.Systems)
            {
                var fleet = sys.World.Fleet;
                var old = fleet.LaneJoined;
                fleet.LaneJoined = (s, hop, when) =>
                {
                    if (s == ship && hop.Lane.IsGateSpur) spurRides++;
                    old?.Invoke(s, hop, when);
                };
            }
            var visited = new System.Collections.Generic.HashSet<StarSystem>();
            jd = RunUntil(g, jd, 150, () =>
            {
                foreach (var sys in g.Systems)
                    if (sys.World.Fleet.Ships.Any(x => x == ship && !x.Destroyed)) visited.Add(sys);
                return ship.Voyage == null && sys0(g, ship) == tau;
            });
            Assert.That(sys0(g, ship), Is.SameAs(tau), "never reached Tau Ceti");
            foreach (var w in waypoints) Assert.That(w.World.Fleet.Ships.Any(x => x.Name == name && x.GateDeparted), Is.True, "passed through " + w.Name);
            Assert.That(ship.Voyage, Is.Null, "the voyage ends on arrival");
            Assert.That(g.Traffic.Arrivals[VoyagePurpose.Plotted], Is.GreaterThanOrEqualTo(1));
            // Every gate taken, each in the system it stands in.
            foreach (var gate in gates) Assert.That(gate.Traffic, Is.GreaterThanOrEqualTo(1), gate.Name);
            Assert.That(spurRides, Is.GreaterThanOrEqualTo(gates.Count), "the lanes were not used to reach the gates");
        }

        static StarSystem sys0(Galaxy g, Ship ship) =>
            g.Systems.FirstOrDefault(s => s.World.Fleet.Ships.Any(x => x == ship && !x.Destroyed));

        [Test]
        public void AFleetOnAPlottedCourseCrossesEverySystemTogether()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var tau = g.Find("Tau Ceti");
            var w = sol.World;
            var flag = new Faction("Hauliers Guild", "HG", new Rgb(0.5f, 0.5f, 0.5f)) { Treasury = 1e9 };
            var lead = w.Fleet.AddShip("Plotter", ShipClass.Freighter, flag, ShipRole.Trader);
            w.Fleet.Station(lead, w.Catalog["Earth"].Index, StartJd);
            lead.HasCargo = true;
            lead.CargoGood = Good.Electronics;
            lead.CargoTonnes = 1e6 / System.Math.Max(1, GoodInfo.Of(Good.Electronics).BasePrice) * 100;
            w.Escorts.Update(StartJd + 1);
            var wing = w.Fleet.Ships.Where(s => s.Leader == lead && !s.Destroyed).ToList();
            Assert.That(wing.Count, Is.GreaterThanOrEqualTo(1), "no wing took on");
            string leadName = lead.Name;
            var v = g.Traffic.SendPlotted(lead, sol, tau, StartJd + 2);
            Assert.That(v, Is.Not.Null);
            var waypoints = v.Route.Skip(1).Select(r => r.Home).ToList();
            RunUntil(g, StartJd + 2, 200, () => sys0(g, lead) == tau && lead.Voyage == null);
            Assert.That(sys0(g, lead), Is.SameAs(tau), "the leader never reached Tau Ceti");
            foreach (var wp in waypoints)
                Assert.That(wp.World.Fleet.Ships.Any(x => x.Name == leadName && x.GateDeparted), Is.True, "the fleet skipped " + wp.Name);
            Assert.That(wing.Any(e => sys0(g, e) == tau || e.Destroyed), Is.True, "the wing did not cross with its leader");
        }
    }
}
