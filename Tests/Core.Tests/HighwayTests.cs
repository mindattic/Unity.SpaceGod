using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The hyperlanes as an X3 Rebirth highway at the helm (U.SPC-T379): a chain of rings strung along every lane, and a
    /// spur from each system's network on to each of its jump gates, ridden by the same lane ride as any lane, ending in
    /// range of the gate and, through it, picked up again at the far system's own spur.
    /// </summary>
    [TestFixture]
    public class HighwayTests
    {
        const double StartJd = 2461324.0;

        static Galaxy galaxy;

        /// <summary>One galaxy for the read-only checks (building it is the slow part); tests that fly a hull build their own.</summary>
        static Galaxy Shared => galaxy ??= Galaxy.CreateDefault(StartJd);

        static Lane LaneBetween(StarSystem s, string a, string b)
        {
            int ia = s.Catalog[a].Index, ib = s.Catalog[b].Index;
            return s.Lanes.Lanes.First(l => (l.A == ia && l.B == ib) || (l.A == ib && l.B == ia));
        }

        static Ship Pilot(StarSystem s, DVec3 helioKm)
        {
            var w = s.World;
            var ship = w.Fleet.Ships.First(x => x.Spec.Class == ShipClass.Courier && !x.IsEscort && x.Battle == null && !x.Destroyed);
            w.PilotedShip = ship;
            int root = s.Catalog.Root.Index;
            Fleet.PinAt(ship, root, helioKm - s.Ephemeris.HelioAt(root, StartJd), DVec3.UnitX, StartJd);
            return ship;
        }

        // ---------------------------------------------------------------- the ring chain

        [Test]
        public void ALaneIsAChainOfEvenlySpacedRingsBetweenItsRamps()
        {
            Assert.That(Hyperlanes.RingCount(0), Is.EqualTo(Hyperlanes.MinRings), "even a short lane has a chain");
            Assert.That(Hyperlanes.RingCount(1e12), Is.EqualTo(Hyperlanes.MaxRings), "and a long one never more than the most");
            Assert.That(Hyperlanes.RingCount(80e6), Is.EqualTo(40), "one about every two million km");

            int n = 40;
            double step = Hyperlanes.RingFraction(1, n) - Hyperlanes.RingFraction(0, n);
            for (int k = 0; k < n; k++)
            {
                double s = Hyperlanes.RingFraction(k, n);
                Assert.That(s, Is.GreaterThan(0).And.LessThan(1), "strictly between the ramps");
                Assert.That(Hyperlanes.RingFraction(n - 1 - k, n), Is.EqualTo(1 - s).Within(1e-12), "the same chain ridden either way");
                if (k > 0) Assert.That(s - Hyperlanes.RingFraction(k - 1, n), Is.EqualTo(step).Within(1e-12), "evenly spaced");
            }
            Assert.That(Hyperlanes.RingsPassed(0, n), Is.EqualTo(0));
            Assert.That(Hyperlanes.RingsPassed(Hyperlanes.RingFraction(9, n), n), Is.EqualTo(10), "through ring 9 (the tenth) on reaching it");
            Assert.That(Hyperlanes.RingsPassed(1, n), Is.EqualTo(n), "every ring flown through at the far ramp");
        }

        [Test]
        public void ARiderFliesThroughEveryRingSpedUpThenBraked()
        {
            var sol = Galaxy.CreateDefault(StartJd).Systems[0];
            var lanes = sol.Lanes;
            var lane = LaneBetween(sol, "Earth", "Mars");
            int earth = sol.Catalog["Earth"].Index;
            var entry = lanes.RampHelio(lane, earth, StartJd);
            var ship = Pilot(sol, entry);
            sol.World.Fleet.JoinLane(ship, lanes.OnRampAt(entry, StartJd).Value, entry, StartJd);

            int count = Fleet.RideRings(ship, StartJd, out int passed, out bool accelerating);
            Assert.That(count, Is.InRange(20, 200), "a few dozen rings between Earth and Mars");
            Assert.That(passed, Is.EqualTo(0));
            Assert.That(accelerating, Is.True, "the rings push the rider on through the first half");

            int last = 0;
            for (int i = 1; i <= 20; i++)
            {
                double jd = ship.PhaseStartJd + (ship.PhaseEndJd - ship.PhaseStartJd) * i / 20.0;
                Fleet.RideRings(ship, jd, out passed, out accelerating);
                Assert.That(passed, Is.GreaterThanOrEqualTo(last), "never back through a ring");
                last = passed;
            }
            Assert.That(passed, Is.EqualTo(count), "and through them all at the far ramp");
            Assert.That(accelerating, Is.False, "braked for it through the second half");
        }

        // ---------------------------------------------------------------- spurs to the jump gates

        [Test]
        public void EverySystemWithANetworkHasASpurOnToEachOfItsGates()
        {
            int withNetwork = 0;
            foreach (var s in Shared.Systems)
            {
                var lanes = s.Lanes;
                if (lanes.Lanes.Count == 0)
                {
                    Assert.That(lanes.GateSpurs, Is.Empty, s.Name + ": no network to lead on from");
                    continue;
                }
                withNetwork++;
                Assert.That(lanes.GateSpurs.Count, Is.EqualTo(s.Gates.Count), s.Name + ": a spur per gate");
                foreach (var gate in s.Gates)
                {
                    var spur = lanes.GateSpurs.Single(l => l.Gate == gate);
                    Assert.That(spur.IsGateSpur, Is.True);
                    Assert.That(lanes.Lanes.Contains(spur), Is.False, "kept apart from the lanes AI traffic routes over");
                    Assert.That(lanes.Lanes.Any(l => l.A == spur.A || l.B == spur.A), Is.True, s.Name + ": it leaves from a system on the network");
                    var farRamp = lanes.RampHelio(spur, spur.B, StartJd);
                    Assert.That(gate.InRange(farRamp), Is.True, s.Name + ": its far ramp is in range to open the gate");
                    Assert.That(lanes.EndName(spur, spur.B), Is.EqualTo(gate.Name));
                    Assert.That(lanes.EndName(spur, spur.A), Is.EqualTo(s.Catalog[spur.A].Name));
                    Assert.That(lanes.LaneLengthKm(spur, StartJd), Is.GreaterThan(0));
                }
            }
            Assert.That(withNetwork, Is.GreaterThanOrEqualTo(2), "Sol and Alpha Centauri at least");
        }

        [Test]
        public void TheLanesLeadOnToTheGatesAndAGatesSpurToNothingButTheGate()
        {
            var sol = Shared.Systems[0];
            var lanes = sol.Lanes;
            var spur = lanes.GateSpurs[0];
            var cameIn = lanes.Lanes.First(l => l.A == spur.A || l.B == spur.A);
            var onward = lanes.OnwardHops(spur.A, cameIn);
            Assert.That(onward.Any(h => h.Lane == spur && h.From == spur.A && h.To == spur.B), Is.True, "off a lane, on along the spur to the gate");
            Assert.That(lanes.OnwardHops(spur.B, spur), Is.Empty, "at the gate, the way on is the gate itself");
            Assert.That(lanes.OnwardHops(spur.A, spur).Any(h => h.Lane == spur), Is.False, "never straight back up the spur");
            Assert.That(lanes.Route(sol.Catalog["Mercury"].Index, sol.Catalog["Neptune"].Index, StartJd).Any(h => h.Lane.IsGateSpur), Is.False,
                "AI routes keep to the lanes");
        }

        [Test]
        public void ASpurIsJoinedByFlyingThroughItsRingTheWayItRuns()
        {
            var sol = Shared.Systems[0];
            var lanes = sol.Lanes;
            var spur = lanes.GateSpurs[0];
            var centre = lanes.RampHelio(spur, spur.A, StartJd);
            var n = lanes.RampDirection(spur, spur.A, StartJd);
            var side = DVec3.Cross(n, DVec3.UnitZ).Normalized;

            var hop = lanes.GateSpurRingCrossed(centre - n * 50 + side * 400, centre + n * 50 + side * 400, StartJd);
            Assert.That(hop.HasValue, Is.True, "through the ring toward the gate");
            Assert.That(hop.Value.Lane, Is.SameAs(spur));
            Assert.That(hop.Value.From, Is.EqualTo(spur.A));
            Assert.That(hop.Value.To, Is.EqualTo(spur.B));

            Assert.That(lanes.GateSpurRingCrossed(centre + n * 50, centre - n * 50, StartJd).HasValue, Is.False, "not through it the wrong way");
            var wide = side * (Hyperlanes.GateRadiusKm * 1.5);
            Assert.That(lanes.GateSpurRingCrossed(centre - n * 50 + wide, centre + n * 50 + wide, StartJd).HasValue, Is.False, "not past it outside the ring");
            Assert.That(lanes.GateSpurRingCrossed(centre + side * 200, centre + side * 300, StartJd).HasValue, Is.False, "not just inside it, without flying through");
        }

        [Test]
        public void RidingASpurEndsInRangeOfTheGateAndThroughItTheFarSystemsSpurPicksUp()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Systems[0];
            var lanes = sol.Lanes;
            var spur = lanes.GateSpurs.First(l => l.Gate.Destination != null && l.Gate.Destination.Lanes.GateSpurs.Count > 0);
            var gate = spur.Gate;
            var entry = lanes.RampHelio(spur, spur.A, StartJd);
            var ship = Pilot(sol, entry);
            var hop = lanes.GateSpurRingCrossed(entry - lanes.RampDirection(spur, spur.A, StartJd) * 10, entry, StartJd);
            Assert.That(hop.HasValue, Is.True);
            sol.World.Fleet.JoinLane(ship, hop.Value, entry, StartJd);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Lane));
            Assert.That(ship.FinalBody, Is.EqualTo(spur.A), "let go of, its crew would bring it back to the network, not into the star");

            double end = ship.PhaseEndJd;
            Assert.That(end - StartJd, Is.GreaterThan(0).And.LessThan(1), "a spur to the gate is ridden in hours");
            sol.Ephemeris.Update(end);
            var arrival = sol.World.Fleet.StateOf(ship, end).HelioKm;
            sol.World.Fleet.RampReached(ship, null, DVec3.UnitX, end);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Parked));
            var at = sol.Ephemeris.HelioAt(ship.Orbit.Body, end) + ship.Orbit.OffsetAt(end);
            Assert.That((at - arrival).Length, Is.LessThan(1), "brought off right where the ride ended");
            Assert.That(gate.CanActivate(at), Is.True, "and there the gate can be opened");

            Assert.That(GateTransit.TryPerform(gate, at, ship, sol.World.Fleet, end, out var arrived), Is.True);
            var far = gate.Destination;
            var farLanes = far.Lanes;
            var farSpur = farLanes.GateSpurs.Single(l => l.Gate == gate.Partner);
            var outOfGate = arrived.Orbit.Center;
            Assert.That(farLanes.OnRampAt(outOfGate, end).HasValue, Is.False, "coming out of the gate is not flying into a lane");
            var ring = farLanes.RampHelio(farSpur, farSpur.B, end);
            var n = farLanes.RampDirection(farSpur, farSpur.B, end);
            Assert.That(DVec3.Dot(outOfGate - ring, n), Is.LessThan(0), "the hull comes out behind the spur's ring, ready to fly through it");
            var onto = farLanes.GateSpurRingCrossed(ring - n * 20, ring + n * 20, end);
            Assert.That(onto.HasValue, Is.True, "through the ring by the far gate");
            Assert.That(onto.Value.Lane, Is.SameAs(farSpur));
            Assert.That(onto.Value.To, Is.EqualTo(farSpur.A), "on to the far system's own network");
        }
    }
}
